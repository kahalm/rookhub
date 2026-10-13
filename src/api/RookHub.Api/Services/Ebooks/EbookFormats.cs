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
            if (rank < bestRank) { best = f; bestRank = rank; }
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
