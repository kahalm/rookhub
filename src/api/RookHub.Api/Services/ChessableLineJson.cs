using System.Text.Json;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

/// <summary>
/// Liest aus den ROHEN getGame-Antworten eines Browser-Imports, was die Extension nicht verlaesslich
/// liefern kann: den Kursnamen. Chessable schreibt ihn in JEDE Linie (<c>game.name</c>; <c>game.title</c>
/// ist der Linientitel), waehrend die getCourse-Antwort nur Kapitel-Ids traegt und piratechess den Namen
/// aus dem ersten Kapitel ableitet. Die Extension las ihn bis 1.60.0 bei Kursen ausserhalb des eigenen
/// Kontos aus dem Seitentext — am 2026-09-19 hiess ein Repertoire deshalb
/// „Short &amp; Sweet0%Priority0/15variations✓ 0/15" (die Kurskachel klebt Titel und Fortschrittsbadges in
/// EINEN Link). Der Name aus der Linie ist Chessables eigene Angabe und geht deshalb vor.
/// </summary>
public static partial class ChessableLineJson
{
    public const int MaxNameLength = 200;

    /// <summary>Kursname der ersten Linie, die einen traegt; <c>null</c>, wenn keine (auch bei Cache-Platzhaltern <c>null</c>).</summary>
    public static string? FirstCourseNameOf(IEnumerable<string?>? lines)
    {
        if (lines is null) return null;
        foreach (var line in lines)
            if (CourseNameOf(line) is { } name) return name;
        return null;
    }

    /// <summary>Kursname EINER getGame-Antwort (<c>game.name</c>, Gross-/Kleinschreibung egal), normalisiert und gekappt.</summary>
    public static string? CourseNameOf(string? gameJson)
    {
        if (string.IsNullOrWhiteSpace(gameJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(gameJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!TryGetIgnoreCase(doc.RootElement, "game", out var game) || game.ValueKind != JsonValueKind.Object) return null;
            if (!TryGetIgnoreCase(game, "name", out var name) || name.ValueKind != JsonValueKind.String) return null;
            var text = Whitespace().Replace(name.GetString() ?? string.Empty, " ").Trim();
            if (text.Length == 0) return null;
            return text.Length > MaxNameLength ? text[..MaxNameLength] : text;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Reihenfolge = Verlaesslichkeit: Name aus den Linien &gt; Name der Extension (Kursliste per Token oder
    /// Seitentext) &gt; Rueckfall (piratechess: erstes Kapitel).
    /// </summary>
    public static string? ResolveCourseName(string? fromExtension, IEnumerable<string?>? lines, string? fallback = null)
        => FirstCourseNameOf(lines)
           ?? (string.IsNullOrWhiteSpace(fromExtension) ? null : fromExtension)
           ?? (string.IsNullOrWhiteSpace(fallback) ? null : fallback);

    private static bool TryGetIgnoreCase(JsonElement obj, string key, out JsonElement value)
    {
        foreach (var p in obj.EnumerateObject())
            if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
        value = default;
        return false;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
