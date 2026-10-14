using System.IO.Compression;

namespace RookHub.Api.Services.Ebooks;

/// <summary>Ein Eintrag aus dem EPUB-ZIP: Inhalt + Content-Type.</summary>
public sealed record EpubEntry(byte[] Bytes, string ContentType);

/// <summary>
/// Einzelne Einträge eines EPUB-ZIPs ausliefern: der EPUB-Leser öffnet das Buch „als Ordner“ und holt nur
/// <c>container.xml</c>, OPF, Navigation, das gezeigte Kapitel und dessen Bilder — statt das ganze Archiv (Dvoretsky:
/// 84 MB) vor der ersten Seite. Der Pfad wird NUR als ZIP-Eintrag aufgelöst (nie im Dateisystem); <c>..</c>, absolute
/// Pfade, Backslashes und Steuerzeichen werden abgewiesen. Groß-/Kleinschreibung wie im ZIP. Das Archiv wird je Anfrage
/// geöffnet — gelesen wird dabei nur das zentrale Verzeichnis am Dateiende, nicht das ganze Buch.
/// </summary>
public static class EbookEpubEntries
{
    /// <summary>Größter Eintrag, der ausgeliefert wird (Kapitel/Bilder sind weit darunter).</summary>
    public const long MaxEntryBytes = 64L * 1024 * 1024;

    /// <summary>Pfad im ZIP bereinigen; <c>null</c> = ungültig (leer, <c>..</c>, absolut, Backslash, Steuerzeichen).</summary>
    public static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024) return null;
        if (path.StartsWith('/') || path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl)) return null;
        var parts = new List<string>();
        foreach (var p in path.Split('/'))
        {
            if (p is "" or ".") continue;
            if (p == "..") return null;
            parts.Add(p);
        }
        return parts.Count == 0 ? null : string.Join('/', parts);
    }

    /// <summary>Content-Type nach Endung (EPUB-Bestandteile, Bilder, Schriften); unbekannt = octet-stream.</summary>
    public static string ContentType(string path)
    {
        var dot = path.LastIndexOf('.');
        var ext = dot < 0 || dot < path.LastIndexOf('/') ? "" : path[(dot + 1)..].ToLowerInvariant();
        return ext switch
        {
            "xhtml" or "xht" => "application/xhtml+xml",
            "html" or "htm" => "text/html; charset=utf-8",
            "css" => "text/css; charset=utf-8",
            "opf" => "application/oebps-package+xml",
            "ncx" => "application/x-dtbncx+xml",
            "xml" => "application/xml",
            "smil" => "application/smil+xml",
            "js" => "text/javascript",
            "json" => "application/json",
            "txt" => "text/plain; charset=utf-8",
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "svg" => "image/svg+xml",
            "webp" => "image/webp",
            "bmp" => "image/bmp",
            "ttf" => "font/ttf",
            "otf" => "font/otf",
            "woff" => "font/woff",
            "woff2" => "font/woff2",
            "mp3" => "audio/mpeg",
            "mp4" => "video/mp4",
            _ => "application/octet-stream",
        };
    }

    /// <summary>Einen Eintrag lesen; <c>null</c>, wenn der Pfad ungültig ist, fehlt, ein Ordner oder zu groß ist, oder
    /// die Datei kein lesbares ZIP ist.</summary>
    public static async Task<EpubEntry?> ReadAsync(string zipFile, string? entryPath, CancellationToken ct = default)
    {
        var path = NormalizePath(entryPath);
        if (path == null) return null;
        try
        {
            await using var fs = new FileStream(zipFile, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.RandomAccess | FileOptions.Asynchronous);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = zip.GetEntry(path);
            if (entry == null || entry.FullName.EndsWith('/') || entry.Length > MaxEntryBytes) return null;
            await using var s = entry.Open();
            using var ms = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
            await s.CopyToAsync(ms, ct);
            return new EpubEntry(ms.ToArray(), ContentType(path));
        }
        catch (InvalidDataException) { return null; }
    }
}
