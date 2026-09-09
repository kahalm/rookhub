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
    /// <summary>
    /// Woran eine Postleitzahl im Freitext zu erkennen ist. Der erste Teil deckt die ZIFFERN-Form
    /// ab (fast ganz Europa), der zweite die alphanumerische der britischen Inseln.
    ///
    /// <para><b>Warum der zweite Teil dazugekommen ist.</b> Bis dahin war der Ausdruck rein
    /// numerisch — und damit war „CF31 3NR" (Wales) oder „D02 XY45" (Irland) keine Postleitzahl,
    /// sondern gar nichts. Aufgefallen beim Bau der walisischen Quelle: sie liefert bei 30 von 38
    /// Turnieren eine vollstaendige Postleitzahl mit, und keine einzige waere je gefunden worden.
    /// Dasselbe gilt fuer England, Schottland und Irland — vier der Quellen der dritten
    /// Runde.</para>
    ///
    /// <para>Falsche Treffer sind dabei harmlos und bewusst in Kauf genommen: was eine
    /// Postleitzahl IST, entscheidet weiterhin der Gazetteer-Treffer und nicht dieser Ausdruck.
    /// Ein Kandidat, den es im Lexikon nicht gibt, findet dort schlicht nichts.</para>
    /// </summary>
    private static readonly Regex PostalTokenPattern = new(
        @"\b\d{3}[ -]?\d{2,3}\b|\b\d{3,6}\b" +
        @"|\b[A-Za-z]{1,2}\d[A-Za-z\d]?[ -]?\d[A-Za-z]{2}\b" +
        @"|\b[A-Za-z]\d{2}[ -]?[A-Za-z\d]{4}\b",
        RegexOptions.Compiled);

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

            // Und der Weg zurueck: die walisische Quelle schreibt „SA714LA" ohne Leerzeichen,
            // GeoNames speichert „SA71 4LA" mit. Ohne diese Variante findet der zusammengezogene
            // Wert im Lexikon nichts — der haeufigere Fall bei den britischen Inseln, wo die
            // Quellen die Postleitzahl gern an die Anschrift kleben.
            foreach (var spaced in Spaced(compact))
                if (!result.Contains(spaced)) result.Add(spaced);
        }
        return result;
    }

    /// <summary>
    /// Aus einem zusammengezogenen Wert die Schreibweise MIT Trennstelle. Britische
    /// Postleitzahlen trennen vor den letzten drei Zeichen („SA714LA" → „SA71 4LA"), irische
    /// Eircodes nach den ersten dreien („D02XY45" → „D02 XY45"). Beide Varianten werden angeboten;
    /// welche stimmt, entscheidet das Lexikon.
    /// </summary>
    private static IEnumerable<string> Spaced(string compact)
    {
        if (compact.Length is < 5 or > 8) yield break;
        if (!compact.All(char.IsLetterOrDigit)) yield break;
        if (!compact.Any(char.IsAsciiLetter)) yield break;

        yield return compact[..^3] + " " + compact[^3..];
        if (compact.Length > 3) yield return compact[..3] + " " + compact[3..];
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

    /// <summary>
    /// Trennzeichen, die im Ortstext MEHRERE Spielorte voneinander abgrenzen: Komma,
    /// Schraegstrich, Semikolon, „und", „&amp;". Kein Ortsname enthaelt eines davon.
    /// </summary>
    /// <summary>
    /// Trennzeichen zwischen SPIELORTEN. Der Schraegstrich steht bewusst NICHT dabei — er ist
    /// zweideutig: „Schwaz/Jenbach/Kufstein" sind drei Orte, „St. Veit/Glan" und „Frankfurt/M"
    /// sind einer. Aufgeloest wird das ueber <see cref="SlashParts"/>: der Geocoder probiert den
    /// Abschnitt ZUERST als EINEN Namen und zerlegt erst, wenn das nichts findet.
    /// </summary>
    private static readonly Regex VenueSeparators =
        new(@"[,;]|\bund\b|&", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SlashSeparator = new("/", RegexOptions.Compiled);

    /// <summary>
    /// Bindewoerter in Ortsnamen. „St. Veit an der Glan" und „St. Veit/Glan" bezeichnen dasselbe,
    /// „Frankfurt am Main" und „Frankfurt/M" ebenso — chess-results kuerzt genau diese Woerter
    /// weg. Ohne sie ist der abgekuerzte Name nicht auf den amtlichen abzubilden.
    ///
    /// <para>Bewusst NUR Bindewoerter: „sankt", „bad" oder „neu" gehoeren zum Namen und
    /// unterscheiden Orte („Bad Ischl" gegen „Ischl"). Die Liste im VenueDisambiguationService
    /// ist laenger, weil sie eine andere Frage beantwortet — welches Wort in einem VEREINSnamen
    /// unterscheidend ist.</para>
    /// </summary>
    private static readonly HashSet<string> NameConnectives = new(StringComparer.Ordinal)
    {
        "an", "am", "im", "in", "auf", "ob", "bei", "vor", "zu", "zur", "zum",
        "der", "die", "das", "dem", "den",
    };

    /// <summary>
    /// Zerlegt den Ortstext in ABSCHNITTE — einen je moeglichem Spielort.
    ///
    /// <para>Der Grund steht in „Mayrhofen, St.Veit": ohne Zerlegung bildet die
    /// Kandidatenerzeugung die Wortfolge „st veit" ueber das Komma hinweg, und weil laengere
    /// Wortfolgen kuerzere schlagen (damit „Bad Ischl" nicht als „Ischl" landet), gewinnt sie
    /// gegen „mayrhofen" — der Pin sass 250 km entfernt in Tirol, obwohl der erste Ort im Text
    /// Mayrhofen ist. Eine Wortfolge darf einen Trenner also nicht ueberspringen.</para>
    ///
    /// <para>Adressen benutzen dieselben Trenner („Halle 1, Eichetstrasse 29, 5020 Salzburg") —
    /// dort liefert der PLZ-Weg die Antwort und wird VOR dieser Zerlegung versucht.</para>
    /// </summary>
    public static List<string> VenueSegments(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return VenueSeparators.Split(text)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Ein Abschnitt am SCHRAEGSTRICH zerlegt. Nur zu benutzen, NACHDEM der ganze Abschnitt als
    /// EIN Ortsname erfolglos probiert wurde — sonst wird aus „Vereinstreff St. Veit/Glan" ein
    /// „St. Veit" (das es in Tirol gibt) und ein „Glan", und der Pin sitzt 250 km entfernt im
    /// falschen Bundesland. Genau so gemeldet fuer tnr1351833.
    /// </summary>
    public static List<string> SlashParts(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return [];
        return SlashSeparator.Split(segment)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Beschreibt der (normalisierte) Text denselben Ort wie ein Gazetteer-Name, nur abgekuerzt?
    ///
    /// <para>Verglichen wird Wort fuer Wort ohne Bindewoerter, und jedes Textwort muss ein
    /// WORTANFANG des zugehoerigen Gazetteer-Wortes sein. „st veit glan" trifft damit „st veit an
    /// der glan", „frankfurt m" trifft „frankfurt am main", „spittal drau" trifft „spittal an der
    /// drau".</para>
    ///
    /// <para>„sorocaba sp" trifft „sorocaba" ausdruecklich NICHT — die Wortzahl passt nicht. Das
    /// ist die Bedingung, die die brasilianische Schreibweise Ort/Bundesstaat weiterhin durch die
    /// Zerlegung laufen laesst (83 der 105 Schraegstrich-Faelle am Dev-Stand).</para>
    /// </summary>
    public static bool DescribesSamePlace(string? normalizedText, string? normalizedPlaceName)
    {
        var text = Distinctive(normalizedText);
        var place = Distinctive(normalizedPlaceName);
        if (text.Count == 0 || text.Count != place.Count) return false;

        for (var i = 0; i < text.Count; i++)
        {
            if (!place[i].StartsWith(text[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static List<string> Distinctive(string? normalized) =>
        (normalized ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !NameConnectives.Contains(w))
            .ToList();
}
