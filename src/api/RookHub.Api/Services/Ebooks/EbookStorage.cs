using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Die Platte hinter der E-Book-Bibliothek: löst relative Pfade gegen die beiden Wurzeln auf (<see cref="EbookOptions"/>),
/// schreibt Uploads unter <c>files/JJJJ/MM/&lt;guid&gt;.&lt;ext&gt;</c> und kennt den Ort der Titelbilder (<c>covers/</c>).
///
/// <para><b>Pfadregel:</b> jeder gespeicherte Pfad ist relativ und wird beim Auflösen gegen die Wurzel geprüft — ein
/// Eintrag, der aus ihr hinausführt („..“, absolut), wird nie geöffnet. Dateien der BIBLIOTHEK löscht dieser Dienst
/// nie (nur lesend eingehängt); gelöscht werden ausschließlich Uploads und Titelbilder.</para>
/// </summary>
public class EbookStorage
{
    private readonly EbookOptions _options;
    private readonly ILogger<EbookStorage> _logger;

    public EbookStorage(IOptions<EbookOptions> options, ILogger<EbookStorage> logger)
    {
        _options = options.Value;
        _logger = logger;
        Root = Normalize(_options.Root);
        LibraryRoot = Normalize(_options.LibraryRoot);
    }

    /// <summary>Beschreibbare Wurzel (Uploads, Titelbilder) oder <c>null</c> = nicht konfiguriert.</summary>
    public string? Root { get; }

    /// <summary>Bibliothek auf dem Server (nur lesend) oder <c>null</c>.</summary>
    public string? LibraryRoot { get; }

    public long MaxFileBytes => _options.MaxFileBytes;
    public long UserQuotaBytes => _options.UserQuotaBytes;

    /// <summary>Hochladen möglich: Wurzel gesetzt UND anlegbar/beschreibbar (einmal wirklich probiert — wie beim
    /// DataProtection-Verzeichnis in <c>Program.cs</c>: ein Bind-Mount, der root gehört, meldet sonst erst beim ersten
    /// Upload einen Fehler).</summary>
    public bool UploadsEnabled
    {
        get
        {
            if (Root == null) return false;
            try
            {
                Directory.CreateDirectory(Path.Combine(Root, "files"));
                Directory.CreateDirectory(Path.Combine(Root, "covers"));
                var probe = Path.Combine(Root, ".write-probe");
                File.WriteAllText(probe, DateTime.UtcNow.ToString("O"));
                File.Delete(probe);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "E-Books: Wurzel {Root} ist nicht beschreibbar — Hochladen bleibt aus", Root);
                return false;
            }
        }
    }

    public bool LibraryEnabled => LibraryRoot != null && Directory.Exists(LibraryRoot);

    private static string? Normalize(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar);

    private string RootFor(EbookSource source) =>
        (source == EbookSource.Library ? LibraryRoot : Root)
        ?? throw new DomainValidationException("The e-book storage is not configured on this server.") { Code = ApiErrorCodes.EbooksNotConfigured };

    /// <summary>Voller Pfad einer Datei; wirft, wenn der relative Pfad die Wurzel verlässt.</summary>
    public string ResolvePath(EbookSource source, string relativePath)
    {
        var root = RootFor(source);
        var full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"E-book path escapes its root: {relativePath}");
        return full;
    }

    /// <summary>Voller Pfad einer Datei eines Buchs: erzeugte Dateien liegen immer unter der beschreibbaren Wurzel.</summary>
    public string ResolveFile(Ebook ebook, EbookFile file) =>
        ResolvePath(file.Derived ? EbookSource.Upload : ebook.Source, file.RelativePath);

    /// <summary>Relativer Zielpfad der PDF-Fassung eines DJVU-Buchs.</summary>
    public static string ConvertedPdfPath(int ebookId) => $"converted/{ebookId}.pdf";

    /// <summary>Relativer Pfad (mit „/“) einer Datei unter der Bibliothek; <c>null</c>, wenn sie nicht darunter liegt.</summary>
    public string? LibraryRelativePath(string fullPath)
    {
        if (LibraryRoot == null) return null;
        var full = Path.GetFullPath(fullPath);
        if (!full.StartsWith(LibraryRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;
        return Path.GetRelativePath(LibraryRoot, full).Replace(Path.DirectorySeparatorChar, '/');
    }

    public string CoverPath(int ebookId) => Path.Combine(RootFor(EbookSource.Upload), "covers", ebookId + ".cover");

    public bool CoverExists(int ebookId) => Root != null && File.Exists(CoverPath(ebookId));

    /// <summary>Ergebnis eines gespeicherten Uploads.</summary>
    public sealed record SavedUpload(string RelativePath, long SizeBytes, string Sha256);

    /// <summary>
    /// Schreibt den Strom unter <c>files/JJJJ/MM/&lt;guid&gt;.&lt;ext&gt;</c> und bildet dabei den SHA-256. Mehr als
    /// <paramref name="maxBytes"/> → Datei weg, 400 — der Hinweis auf die Grenze kommt schon vorher aus der
    /// Content-Length, hier ist nur die Sicherung gegen einen Client, der sie unterschlägt.
    /// </summary>
    public async Task<SavedUpload> SaveUploadAsync(Stream input, string format, long maxBytes, CancellationToken ct)
    {
        var root = RootFor(EbookSource.Upload);
        var now = DateTime.UtcNow;
        var dir = Path.Combine("files", now.ToString("yyyy"), now.ToString("MM"));
        Directory.CreateDirectory(Path.Combine(root, dir));
        var relative = $"{dir.Replace(Path.DirectorySeparatorChar, '/')}/{Guid.NewGuid():N}.{format}";
        var full = ResolvePath(EbookSource.Upload, relative);
        var tmp = full + ".part";

        long total = 0;
        using var sha = SHA256.Create();
        try
        {
            await using (var output = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                        throw new DomainValidationException($"The file is larger than {maxBytes / (1024 * 1024)} MB.") { Code = ApiErrorCodes.EbookFileTooLarge };
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            if (total == 0)
                throw new DomainValidationException("The file is empty.") { Code = ApiErrorCodes.EbookFileEmpty };
            sha.TransformFinalBlock([], 0, 0);
            File.Move(tmp, full, overwrite: false);
            return new SavedUpload(relative, total, Convert.ToHexStringLower(sha.Hash!));
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    /// <summary>Löscht eine HOCHGELADENE Datei (Bibliotheksdateien bleiben immer).</summary>
    public void DeleteUpload(EbookFile file, EbookSource source)
    {
        if ((source != EbookSource.Upload && !file.Derived) || Root == null) return;
        try { TryDelete(ResolvePath(EbookSource.Upload, file.RelativePath)); }
        catch (InvalidOperationException ex) { _logger.LogWarning(ex, "E-Books: Pfad nicht löschbar {Path}", file.RelativePath); }
    }

    public void DeleteCover(int ebookId)
    {
        if (Root == null) return;
        TryDelete(CoverPath(ebookId));
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _logger.LogWarning(ex, "E-Books: Datei nicht gelöscht {Path}", path); }
    }
}
