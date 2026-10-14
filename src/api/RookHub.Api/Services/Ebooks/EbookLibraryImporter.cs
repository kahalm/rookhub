using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Liest die Bibliothek auf dem Server (<see cref="EbookOptions.LibraryRoot"/>) in die Datenbank ein — die Dateien
/// bleiben, wo sie sind. Gleichnamige Dateien eines Ordners (<c>Buch.pdf</c>, <c>Buch.djvu</c>, <c>Buch.epub</c>)
/// werden EIN Buch mit mehreren Formaten; ein Beiblatt <c>Buch.json</c> (Titel, Autor, Jahr, Schlagworte, Elo — so
/// liegt die Bibliothek des Nutzers vor) liefert die Metadaten, sonst Ordnername („Nachname, Vorname") und Dateiname.
///
/// <para><b>Idempotent:</b> schon bekannte Pfade werden übersprungen; taucht neben einem bekannten Buch ein neues
/// Format auf, wird es dem Buch angehängt. Eingelesene Bücher gehören dem einlesenden Admin und sind zunächst nur
/// für Verwalter sichtbar — die Freigabe setzt man danach in der Übersicht (auch für viele auf einmal).</para>
/// </summary>
public class EbookLibraryImporter
{
    private readonly AppDbContext _db;
    private readonly EbookStorage _storage;
    private readonly ILogger<EbookLibraryImporter> _logger;

    public EbookLibraryImporter(AppDbContext db, EbookStorage storage, ILogger<EbookLibraryImporter> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>Ordner, die keine Autoren sind: Eingangskörbe und Sync-Reste.</summary>
    internal static bool IsAuthorFolder(string name) =>
        !name.StartsWith('.') && !name.StartsWith("AAA", StringComparison.Ordinal)
        && !name.Equals("Unknown", StringComparison.OrdinalIgnoreCase);

    /// <summary>„Nachname, Vorname" → „Vorname Nachname"; alles andere unverändert.</summary>
    internal static string AuthorFromFolder(string folder)
    {
        if (!IsAuthorFolder(folder)) return string.Empty;
        var comma = folder.IndexOf(',');
        if (comma <= 0) return folder.Trim();
        var last = folder[..comma].Trim();
        var first = folder[(comma + 1)..].Trim();
        return first.Length == 0 ? last : $"{first} {last}";
    }

    /// <summary>Metadaten aus dem Beiblatt — jedes Feld optional, Autor als Liste oder Text.</summary>
    internal sealed record Sidecar(string? Title, string? Author, int? Year, List<string> Tags, int? TargetMinElo);

    internal static Sidecar? ParseSidecar(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string? title = root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            string? author = null;
            if (root.TryGetProperty("author", out var a))
            {
                if (a.ValueKind == JsonValueKind.String) author = a.GetString();
                else if (a.ValueKind == JsonValueKind.Array)
                    author = string.Join(", ", a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim()).Where(x => x.Length > 0));
            }
            int? year = root.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number && y.TryGetInt32(out var yi) ? yi : null;
            var tags = new List<string>();
            if (root.TryGetProperty("tags", out var tg) && tg.ValueKind == JsonValueKind.Array)
                tags.AddRange(tg.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!));
            int? elo = root.TryGetProperty("targetMinElo", out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var ei) ? ei : null;
            return new Sidecar(string.IsNullOrWhiteSpace(title) ? null : title.Trim(), string.IsNullOrWhiteSpace(author) ? null : author.Trim(), year, tags, elo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<EbookLibraryImportResultDto> ImportAsync(int adminUserId, CancellationToken ct = default)
    {
        var result = new EbookLibraryImportResultDto { Configured = _storage.LibraryEnabled };
        if (!_storage.LibraryEnabled) return result;
        var root = _storage.LibraryRoot!;

        // Alle zugelassenen Dateien, gruppiert nach Ordner + Name ohne Endung.
        var groups = new Dictionary<string, List<FileInfo>>(StringComparer.Ordinal);
        foreach (var path in EnumerateFiles(root))
        {
            ct.ThrowIfCancellationRequested();
            var format = EbookFormats.FormatOf(path);
            if (format == null) continue;
            result.FilesSeen++;
            var key = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, Path.GetFileNameWithoutExtension(path));
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<FileInfo>();
            list.Add(new FileInfo(path));
        }

        var known = (await _db.EbookFiles.Where(f => f.Ebook.Source == EbookSource.Library)
                .Select(f => new { f.RelativePath, f.EbookId }).ToListAsync(ct))
            .ToDictionary(x => x.RelativePath, x => x.EbookId, StringComparer.Ordinal);

        var pending = 0;
        foreach (var (key, files) in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var relPaths = files.Select(f => (File: f, Rel: _storage.LibraryRelativePath(f.FullName))).Where(x => x.Rel != null).ToList();
            if (relPaths.Count == 0) { result.Unreadable++; continue; }

            var existingId = relPaths.Select(x => known.TryGetValue(x.Rel!, out var id) ? id : (int?)null).FirstOrDefault(id => id != null);
            var newFiles = relPaths.Where(x => !known.ContainsKey(x.Rel!)).ToList();
            if (newFiles.Count == 0) { result.Skipped++; continue; }

            Ebook ebook;
            if (existingId != null)
            {
                ebook = await _db.Ebooks.Include(b => b.Files).FirstAsync(b => b.Id == existingId, ct);
            }
            else
            {
                var sidecar = ReadSidecar(key + ".json");
                var folder = Path.GetRelativePath(root, Path.GetDirectoryName(key) ?? root).Split(Path.DirectorySeparatorChar)[0];
                var fileName = Path.GetFileName(key);
                ebook = new Ebook
                {
                    Title = Clip(sidecar?.Title ?? EbookFormats.TitleFromFileName(fileName), 300),
                    Author = Clip(sidecar?.Author ?? (folder == "." ? string.Empty : AuthorFromFolder(folder)), 300),
                    Year = sidecar?.Year is >= 1400 and <= 2200 ? sidecar.Year : null,
                    Tags = EbookFormats.JoinTags(EbookFormats.NormalizeTags(sidecar?.Tags)),
                    TargetMinElo = sidecar?.TargetMinElo is >= 100 and <= 3500 ? sidecar.TargetMinElo : null,
                    OwnerUserId = adminUserId,
                    Source = EbookSource.Library,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                _db.Ebooks.Add(ebook);
                result.Added++;
            }

            foreach (var (file, rel) in newFiles)
            {
                var format = EbookFormats.FormatOf(file.Name)!;
                if (ebook.Files.Any(f => f.Format == format)) continue;   // zwei Dateien gleichen Formats (Buch.PDF + Buch.pdf) — die erste gewinnt
                ebook.Files.Add(new EbookFile
                {
                    Format = format,
                    RelativePath = rel!,
                    FileName = Clip(file.Name, 300),
                    SizeBytes = SafeLength(file),
                });
                known[rel!] = ebook.Id;
                result.FilesAdded++;
            }
            if (EbookFormats.NeedsPdfConversion(ebook.Files) && ebook.ConversionState == EbookConversionState.NotNeeded)
                ebook.ConversionState = EbookConversionState.Pending;
            ebook.CoverState = ebook.CoverState == EbookCoverState.Done ? EbookCoverState.Done
                : ebook.Files.Any(f => EbookFormats.WithCover.Contains(f.Format)) ? EbookCoverState.Pending : EbookCoverState.Unsupported;

            if (++pending >= 100)
            {
                await _db.SaveChangesAsync(ct);
                pending = 0;
            }
        }
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("E-Book-Bibliothek eingelesen: {Added} neue Bücher, {FilesAdded} Dateien, {Skipped} schon bekannt, {Seen} Dateien gesehen",
            result.Added, result.FilesAdded, result.Skipped, result.FilesSeen);
        return result;
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> subDirs, files;
            try
            {
                subDirs = Directory.EnumerateDirectories(dir);
                files = Directory.EnumerateFiles(dir);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }
            foreach (var sub in subDirs)
                if (!Path.GetFileName(sub).StartsWith('.')) stack.Push(sub);
            foreach (var f in files) yield return f;
        }
    }

    private static Sidecar? ReadSidecar(string path)
    {
        try
        {
            return File.Exists(path) ? ParseSidecar(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long SafeLength(FileInfo f)
    {
        try { return f.Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}
