namespace RookHub.Api.Services.Ebooks;

/// <summary>Formate, Inhaltstypen und die Schlagwort-Regeln der E-Book-Bibliothek — EINE Stelle für Dienst,
/// Importer, Controller und Tests.</summary>
public static class EbookFormats
{
    /// <summary>Zugelassene Endungen (klein, ohne Punkt). Mehr nimmt der Upload nicht an, mehr liest der Import nicht ein.</summary>
    public static readonly IReadOnlyList<string> Allowed = ["pdf", "epub", "djvu", "cbz", "cbr", "mobi", "azw3"];

    /// <summary>Formate, aus denen sich ein Titelbild rendern lässt (pdftoppm, ddjvu, Bild im Zip).</summary>
    public static readonly IReadOnlySet<string> WithCover = new HashSet<string> { "pdf", "epub", "djvu", "cbz" };

    /// <summary>Reihenfolge, in der eine Datei als „Hauptdatei" gilt: PDF liest der Browser, der Rest ist Download.</summary>
    public static readonly string[] Preference = ["pdf", "epub", "djvu", "cbz", "cbr", "mobi", "azw3"];

    /// <summary>Braucht dieses Buch eine PDF-Fassung? (DJVU vorhanden, PDF nicht.)</summary>
    public static bool NeedsPdfConversion(IEnumerable<Models.EbookFile> files) =>
        files.Any(f => f.Format == "djvu") && !files.Any(f => f.Format == "pdf");

    /// <summary>Ab so vielen Bytes je Seite gilt ein PDF als übergroßer Scan (0.775.4): 600-dpi-Farb-JPEGs mit ~30 Megapixeln
    /// je Seite, an denen der Browser-Leser (pdf.js) scheitert. Solche Bücher bekommen eine Lesefassung (150 dpi).</summary>
    public const long HeavyPdfBytesPerPage = 1_500_000;

    /// <summary>Ist diese (Original-)PDF ein übergroßer Scan?</summary>
    public static bool IsHeavyPdf(Models.EbookFile f, int? pageCount) =>
        f.Format == "pdf" && !f.Derived && pageCount is > 0 && f.SizeBytes / pageCount.Value > HeavyPdfBytesPerPage;

    /// <summary>Braucht das Buch eine (Lese-)Fassung als PDF: nur DJVU, oder ein übergroßes PDF ohne Lesefassung.</summary>
    public static bool NeedsReadingPdf(IEnumerable<Models.EbookFile> files, int? pageCount) =>
        NeedsPdfConversion(files) || (files.Any(f => IsHeavyPdf(f, pageCount)) && !files.Any(f => f.Format == "pdf" && f.Derived));

    /// <summary>Das PDF zum Lesen/Verarbeiten: die abgeleitete Lesefassung, wenn es eine gibt, sonst das Original.</summary>
    public static Models.EbookFile? ReadingPdf(IEnumerable<Models.EbookFile> files) =>
        files.Where(f => f.Format == "pdf").OrderByDescending(f => f.Derived).FirstOrDefault();

    /// <summary>Brett-Modi im Leser (0.781.0) und der Standard, wenn das Buch keinen festlegt.</summary>
    public static readonly IReadOnlyList<string> BoardModes = ["view", "free", "calc", "solve", "maia"];
    public const string DefaultBoardMode = "solve";

    /// <summary>Gültiger Modus oder <c>null</c> (leer/unbekannt = Standard).</summary>
    public static string? NormalizeBoardMode(string? mode)
    {
        var m = mode?.Trim().ToLowerInvariant();
        return m != null && BoardModes.Contains(m) ? m : null;
    }

    public const int MaxTags = 20;
    public const int MaxTagLength = 40;

    public static bool IsAllowed(string? format) =>
        format != null && Allowed.Contains(format, StringComparer.Ordinal);

    /// <summary>Rang eines Formats in <see cref="Preference"/> (unbekannt → hinten).</summary>
    public static int Rank(string format) => Array.IndexOf(Preference, format) is var r && r >= 0 ? r : int.MaxValue;

    /// <summary>Endung einer Datei als Format (klein, ohne Punkt); <c>null</c>, wenn nicht zugelassen.</summary>
    public static string? FormatOf(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext)) return null;
        var f = ext.TrimStart('.').ToLowerInvariant();
        return IsAllowed(f) ? f : null;
    }

    public static string ContentType(string format) => format switch
    {
        "pdf" => "application/pdf",
        "epub" => "application/epub+zip",
        "djvu" => "image/vnd.djvu",
        "cbz" => "application/vnd.comicbook+zip",
        "cbr" => "application/vnd.comicbook-rar",
        "mobi" => "application/x-mobipocket-ebook",
        "azw3" => "application/vnd.amazon.mobi8-ebook",
        _ => "application/octet-stream",
    };

    /// <summary>Die Hauptdatei eines Buchs nach <see cref="Preference"/>; <c>null</c> ohne Dateien.</summary>
    public static Models.EbookFile? Primary(IEnumerable<Models.EbookFile> files)
    {
        Models.EbookFile? best = null;
        var bestRank = int.MaxValue;
        foreach (var f in files)
        {
            var rank = Array.IndexOf(Preference, f.Format);
            if (rank < 0) rank = int.MaxValue - 1;
            // Gleiches Format: die Lesefassung (Derived) vor dem Original — sie ist bei übergroßen Scans die lesbare.
            if (rank < bestRank || (rank == bestRank && f.Derived && best is { Derived: false })) { best = f; bestRank = rank; }
        }
        return best;
    }

    /// <summary>Schlagworte bereinigen: getrimmt, leer raus, auf <see cref="MaxTagLength"/> gekürzt, ohne Dubletten
    /// (Groß-/Kleinschreibung egal, die erste Schreibweise bleibt), höchstens <see cref="MaxTags"/>.</summary>
    public static List<string> NormalizeTags(IEnumerable<string>? tags)
    {
        var result = new List<string>();
        if (tags == null) return result;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in tags)
        {
            var t = (raw ?? string.Empty).Trim();
            if (t.Length == 0) continue;
            if (t.Length > MaxTagLength) t = t[..MaxTagLength].TrimEnd();
            if (!seen.Add(t)) continue;
            result.Add(t);
            if (result.Count >= MaxTags) break;
        }
        return result;
    }

    /// <summary>CSV (Komma oder Semikolon) → Schlagworte, bereinigt.</summary>
    public static List<string> ParseTagCsv(string? csv) =>
        NormalizeTags(string.IsNullOrWhiteSpace(csv) ? null : csv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries));

    public static string JoinTags(IEnumerable<string> tags) => string.Join(",", tags);

    /// <summary>Titel aus einem Dateinamen: ohne Endung, Unterstriche als Leerzeichen, mehrfache Leerzeichen zusammengezogen.</summary>
    public static string TitleFromFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Trim();
        while (name.Contains("  ")) name = name.Replace("  ", " ");
        return name.Length == 0 ? fileName : name;
    }
}
