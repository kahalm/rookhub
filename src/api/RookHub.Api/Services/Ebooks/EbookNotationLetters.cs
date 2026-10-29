using System.Text;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Figurenbuchstaben der Notation in die Sprache der Übersetzung bringen (0.876.1, User 29.10.: ein deutsches Buch auf
/// Englisch zeigt „Nf3“ statt „Sf3“). Gilt für das ganze Kapitel — Zuglisten gehen nie ans Modell (reine Notation) und
/// müssen trotzdem umgestellt werden. Nur, wenn das Buch wirklich die Buchstaben seiner Sprache setzt: viele spanische
/// oder deutsche Verlagsbücher drucken englische Notation, und „R“ heißt im Spanischen König, im Englischen Turm —
/// umgestellt wird darum nur, wenn landestypische Buchstaben die englischen klar überwiegen.
/// </summary>
public static class EbookNotationLetters
{
    /// <summary>Figurenbuchstabe der Quellsprache → englischer (K Q R B N). Nur Sprachen mit eigenen Buchstaben.</summary>
    private static readonly Dictionary<string, Dictionary<char, char>> Maps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = new() { ['K'] = 'K', ['D'] = 'Q', ['T'] = 'R', ['L'] = 'B', ['S'] = 'N' },
        ["hr"] = new() { ['K'] = 'K', ['D'] = 'Q', ['T'] = 'R', ['L'] = 'B', ['S'] = 'N' },
        ["sr"] = new() { ['K'] = 'K', ['D'] = 'Q', ['T'] = 'R', ['L'] = 'B', ['S'] = 'N' },
        ["nl"] = new() { ['K'] = 'K', ['D'] = 'Q', ['T'] = 'R', ['L'] = 'B', ['P'] = 'N' },
        ["es"] = new() { ['R'] = 'K', ['D'] = 'Q', ['T'] = 'R', ['A'] = 'B', ['C'] = 'N' },
        ["it"] = new() { ['R'] = 'K', ['D'] = 'Q', ['T'] = 'R', ['A'] = 'B', ['C'] = 'N' },
        ["pt"] = new() { ['R'] = 'K', ['D'] = 'Q', ['T'] = 'R', ['B'] = 'B', ['C'] = 'N' },
        ["fr"] = new() { ['R'] = 'K', ['D'] = 'Q', ['T'] = 'R', ['F'] = 'B', ['C'] = 'N' },
        ["cs"] = new() { ['K'] = 'K', ['D'] = 'Q', ['V'] = 'R', ['S'] = 'B', ['J'] = 'N' },
        ["hu"] = new() { ['K'] = 'K', ['V'] = 'Q', ['B'] = 'R', ['F'] = 'B', ['H'] = 'N' },
        ["pl"] = new() { ['K'] = 'K', ['H'] = 'Q', ['W'] = 'R', ['G'] = 'B', ['S'] = 'N' },
    };

    // Figurenzug: Buchstabe, optional Ausgangslinie/-reihe, optional Schlagzeichen/Strich, Zielfeld. Davor kein Buchstabe
    // und keine Ziffer außer der Zugnummer mit Punkt („12.Sf3“), danach kein Buchstabe („Sa3“, nicht „Sand“).
    private static readonly Regex PieceMove = new(
        @"(?<=^|[^\p{L}\d]|\d\.|\.\.\.|…)([A-Z])(?=[a-h]?[1-8]?[x:×\-–]?[a-h][1-8](?![\p{L}\d]))", RegexOptions.Compiled);
    private static readonly Regex Promotion = new(@"(?<=[a-h][18]=?)([A-Z])(?![\p{L}\d])", RegexOptions.Compiled);
    private static readonly Regex Tag = new(@"<[^>]*>", RegexOptions.Compiled);

    /// <summary>
    /// Umsteller für ein Buch oder <c>null</c> (Sprache ohne eigene Buchstaben, Ziel nicht Englisch, oder das Buch setzt
    /// schon englische Notation). <paramref name="texts"/>: der Text des Buchs (HTML genügt).
    /// </summary>
    public static Func<string, string>? For(string? sourceLanguage, string targetLanguage, IEnumerable<string> texts)
    {
        if (sourceLanguage == null || !targetLanguage.Equals("en", StringComparison.OrdinalIgnoreCase)) return null;
        if (!Maps.TryGetValue(sourceLanguage, out var map)) return null;
        // Landestypisch = Buchstabe, der im Englischen keine Figur ist oder dort eine andere bedeutet.
        var native = map.Where(kv => kv.Key != kv.Value).Select(kv => kv.Key).ToHashSet();
        var english = "QRBN".Where(c => !map.ContainsKey(c)).ToHashSet();
        int n = 0, e = 0;
        foreach (var t in texts)
            foreach (Match m in PieceMove.Matches(Tag.Replace(t, " ")))
            {
                var c = m.Groups[1].Value[0];
                if (native.Contains(c) && !"QRBN".Contains(c)) n++;
                else if (english.Contains(c)) e++;
            }
        if (n < 5 || n < 3 * e) return null;
        return html => Convert(html, map);
    }

    /// <summary>Nur im Text zwischen den Tags umstellen; Attribute (Bildpfade, ids) bleiben unberührt.</summary>
    internal static string Convert(string html, IReadOnlyDictionary<char, char> map)
    {
        var sb = new StringBuilder(html.Length);
        var pos = 0;
        foreach (Match tag in Tag.Matches(html))
        {
            sb.Append(ConvertText(html[pos..tag.Index], map));
            sb.Append(tag.Value);
            pos = tag.Index + tag.Length;
        }
        sb.Append(ConvertText(html[pos..], map));
        return sb.ToString();
    }

    private static string ConvertText(string text, IReadOnlyDictionary<char, char> map)
    {
        if (text.Length == 0) return text;
        string Swap(Match m) => map.TryGetValue(m.Groups[1].Value[0], out var en) ? en.ToString() : m.Value;
        return Promotion.Replace(PieceMove.Replace(text, Swap), Swap);
    }
}
