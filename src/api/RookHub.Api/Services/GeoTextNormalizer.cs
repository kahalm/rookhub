using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

/// <summary>
/// Textaufbereitung fuers Geocoding. Die Spielorte sind roher Freitext in wechselnder Schreibweise
/// ("Rifer Hauptstrasse 37 5400 Hallein (RIF)", "Graz, Flann O'Brien, Paradeisgasse 1", "Wien"),
/// also wird gefaltet statt geparst: Umlaute aufloesen, Diakritika weg, alles klein.
/// </summary>
public static class GeoTextNormalizer
{
    private static readonly Regex PostalTokenPattern = new(
        @"\b\d{3}[ -]?\d{2,3}\b|\b\d{3,6}\b", RegexOptions.Compiled);

    private static readonly Regex NonWordPattern = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    /// <summary>
    /// "Grosse Muerzgasse" und "Große Mürzgasse" muessen denselben Schluessel ergeben, sonst
    /// findet die Gazetteer-Suche den Ort nur bei exakt passender Schreibweise.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var expanded = text.ToLowerInvariant()
            .Replace("ä", "a").Replace("ö", "o").Replace("ü", "u")
            .Replace("ß", "ss").Replace("æ", "ae").Replace("ø", "o").Replace("å", "a")
            .Replace("đ", "d").Replace("ł", "l");

        // Restliche Diakritika ueber die Unicode-Zerlegung entfernen (é → e, č → c, ...).
        var decomposed = expanded.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        var collapsed = NonWordPattern.Replace(sb.ToString().Normalize(NormalizationForm.FormC), " ");
        return collapsed.Trim();
    }

    /// <summary>
    /// Postleitzahl-Kandidaten aus einem Adresstext. Bewusst grob: welche Ziffernfolge wirklich eine
    /// PLZ ist, entscheidet der Gazetteer-Treffer, nicht ein Laenderregex - eine Hausnummer findet
    /// dort schlicht nichts. Reihenfolge = Auftreten im Text (Hausnummer steht meist vorn).
    /// </summary>
    public static List<string> PostalCandidates(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (Match m in PostalTokenPattern.Matches(text))
        {
            var token = m.Value.Trim();
            if (!result.Contains(token)) result.Add(token);
            // "1090 Wien" und "SE-114 35": die Variante ohne Trenner mit aufnehmen.
            var compact = token.Replace(" ", "").Replace("-", "");
            if (compact != token && !result.Contains(compact)) result.Add(compact);
        }
        return result;
    }

    /// <summary>
    /// Ortsnamen-Kandidaten: alle zusammenhaengenden Wortfolgen bis <paramref name="maxWords"/>
    /// Laenge aus dem normalisierten Text, laengste zuerst. "bad ischl" muss vor "bad" und "ischl"
    /// probiert werden, sonst gewinnt der falsche, groessere Ort.
    /// </summary>
    public static List<string> PlaceCandidates(string? text, int maxWords = 3)
    {
        var normalized = Normalize(text);
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            // Reine Ziffernfolgen sind PLZ/Hausnummern, keine Ortsnamen.
            .Where(w => !w.All(char.IsDigit))
            .ToArray();

        var candidates = new List<string>();
        for (var length = Math.Min(maxWords, words.Length); length >= 1; length--)
        {
            for (var start = 0; start + length <= words.Length; start++)
            {
                var candidate = string.Join(' ', words.Skip(start).Take(length));
                if (candidate.Length < 3) continue;
                if (!candidates.Contains(candidate)) candidates.Add(candidate);
            }
        }
        return candidates;
    }
}
