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

        return Cleanup(text.ToLowerInvariant()
            .Replace("ä", "a").Replace("ö", "o").Replace("ü", "u")
            .Replace("ß", "ss").Replace("æ", "ae").Replace("ø", "o").Replace("å", "a")
            .Replace("đ", "d").Replace("ł", "l"));
    }

    /// <summary>
    /// Die ZWEITE Schreibweise desselben Namens: Umlaute als ASCII-UMSCHRIFT (ü → ue) und
    /// nichtlateinische Schriften umgeschrieben (Київ → kyiv, Αθήνα → athina).
    ///
    /// <para><b>Warum es zwei braucht.</b> <see cref="Normalize"/> faltet „München" zu
    /// <c>munchen</c>, die ausgeschriebene Form „Muenchen" aber zu <c>muenchen</c> — die beiden
    /// treffen sich nie, und chess-results schreibt regelmaessig die zweite. Am 2026-09-09 auf Dev
    /// gemessen: 53 der 278 unverorteten deutschen Eintraege scheiterten daran (die uebrigen 225
    /// sind mehrdeutige Staedtenamen, eine andere Frage). Beim SUCHEN zusaetzlich <c>ue → u</c> zu
    /// falten waere falsch — aus „Quedlinburg" wuerde <c>qudlinburg</c>. Deshalb eine zweite Spalte
    /// im Lexikon, gefuellt beim Import, und beide Formen auf beiden Seiten.</para>
    ///
    /// <para><b>Und warum sie auch die nichtlateinischen Schriften erledigt.</b> Der Aufraeumteil
    /// ersetzt alles ausser <c>[a-z0-9]</c> durch Leerzeichen — Kyrillisch fiel damit RESTLOS weg:
    /// „Київ" wurde zu einer leeren Zeichenkette. 29 547 der 29 571 importierten ukrainischen
    /// Postleitzahl-Zeilen trugen deshalb einen LEEREN normalisierten Namen, und weil der
    /// Postleitzahl-Weg eine Bestaetigung durch den Ortsnamen verlangt, konnte diese Bestaetigung
    /// dort NIE gelingen. Die Postleitzahlen waren eingespielt und wirkungslos. Eine Umschrift
    /// loest beide Richtungen: kyrillischer Text gegen kyrillisches Lexikon (beide Seiten ergeben
    /// <c>kyiv</c>) und lateinischer Text gegen kyrillisches Lexikon.</para>
    ///
    /// <para>Die Tabelle folgt fuer Kyrillisch der ukrainischen Umschrift (г → h, и → y, і → i) —
    /// die Ukraine ist der Fall, der uns betrifft. Fuer russische Namen faellt sie damit etwas
    /// anders aus als die dortige Konvention; das ist unschaedlich, solange BEIDE Seiten dieselbe
    /// Tabelle benutzen, und genau das ist hier der Fall.</para>
    /// </summary>
    public static string NormalizeTranscribed(string? text, string? iso2 = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var expanded = text.ToLowerInvariant()
            .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue")
            .Replace("ß", "ss").Replace("æ", "ae").Replace("ø", "oe").Replace("å", "aa")
            .Replace("đ", "dj").Replace("ł", "l");

        var table = TableFor(iso2);
        var sb = new StringBuilder(expanded.Length);
        foreach (var ch in expanded)
            sb.Append(table.TryGetValue(ch, out var latin) ? latin : ch.ToString());

        return Cleanup(sb.ToString());
    }

    /// <summary>
    /// Welche Umschrift gilt fuer dieses Land?
    ///
    /// <para><b>Warum das nicht EINE Tabelle sein kann.</b> Derselbe Buchstabe wird verschieden
    /// umgeschrieben: <c>и</c> ist im Ukrainischen ein <c>y</c> („Київ" -> <c>kyiv</c>, genau wie
    /// GeoNames schreibt), im Russischen ein <c>i</c> („Истра" -> <c>istra</c>, ebenfalls wie
    /// GeoNames). Dasselbe bei <c>г</c>: ukrainisch <c>h</c>, russisch und bulgarisch <c>g</c>.
    /// Eine Tabelle fuer beide waere fuer eines der Laender falsch — und es haengt daran, ob der
    /// kyrillische Ortstext den LATEINISCHEN GeoNames-Namen findet. Auf Dev gemessen: die
    /// russischen Ortszeilen sind zu 100 % lateinisch (1 108 von 1 108), die ukrainischen zu 99 %
    /// (398 von 400). Mit der falschen Tabelle findet dort kein einziger Text seinen Ort.</para>
    ///
    /// <para>Beide Seiten benutzen dieselbe Auswahl — der Lexikon-Eintrag ueber
    /// <c>GeoPlace.Country</c>, der Suchtext ueber das Land des Turniers. Ohne Land gilt die
    /// allgemeine Tabelle.</para>
    /// </summary>
    private static Dictionary<char, string> TableFor(string? iso2) =>
        string.Equals(iso2, "UA", StringComparison.OrdinalIgnoreCase) ? UkrainianTable : GeneralTable;

    /// <summary>Der gemeinsame Aufraeumteil: Diakritika weg, alles ausser Buchstabe/Ziffer zu Leerzeichen.</summary>
    private static string Cleanup(string lowered)
    {
        // Restliche Diakritika ueber die Unicode-Zerlegung entfernen (é → e, č → c, ...).
        var decomposed = lowered.Normalize(NormalizationForm.FormD);
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
    /// Kyrillisch und Griechisch zu Latein, Buchstabe fuer Buchstabe. Was hier fehlt (Georgisch,
    /// Armenisch, Hebraeisch) faellt weiterhin weg — dort greift die Ausnahme fuer Treffer OHNE
    /// vergleichbaren Namen in <c>GeocodingService</c>.
    /// </summary>
    private static readonly Dictionary<char, string> UkrainianTable = new()
    {
        // Ukrainische Umschrift: г -> h, и -> y, і -> i (so schreibt auch GeoNames).
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "h", ['ґ'] = "g", ['д'] = "d",
        ['е'] = "e", ['є'] = "ie", ['ж'] = "zh", ['з'] = "z", ['и'] = "y", ['і'] = "i",
        ['ї'] = "i", ['й'] = "i", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n",
        ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u",
        ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "shch",
        ['ь'] = "", ['ъ'] = "", ['ю'] = "iu", ['я'] = "ia", ['ы'] = "y", ['э'] = "e",
        ['ё'] = "e", ['ў'] = "u",
        // Serbisch, Makedonisch, Bulgarisch
        ['ђ'] = "dj", ['ј'] = "j", ['љ'] = "lj", ['њ'] = "nj", ['ћ'] = "c", ['џ'] = "dz",
        ['ѕ'] = "dz", ['ѓ'] = "g", ['ќ'] = "k",
        // Griechisch
        ['α'] = "a", ['β'] = "v", ['γ'] = "g", ['δ'] = "d", ['ε'] = "e", ['ζ'] = "z",
        ['η'] = "i", ['θ'] = "th", ['ι'] = "i", ['κ'] = "k", ['λ'] = "l", ['μ'] = "m",
        ['ν'] = "n", ['ξ'] = "x", ['ο'] = "o", ['π'] = "p", ['ρ'] = "r", ['σ'] = "s",
        ['ς'] = "s", ['τ'] = "t", ['υ'] = "y", ['φ'] = "f", ['χ'] = "ch", ['ψ'] = "ps",
        ['ω'] = "o", ['ά'] = "a", ['έ'] = "e", ['ή'] = "i", ['ί'] = "i", ['ό'] = "o",
        ['ύ'] = "y", ['ώ'] = "o", ['ϊ'] = "i", ['ϋ'] = "y", ['ΐ'] = "i", ['ΰ'] = "y",
    };

    /// <summary>
    /// Die allgemeine Tabelle — russisch, bulgarisch, serbisch, makedonisch und alles Griechische.
    /// Unterschied zur ukrainischen: <c>г -> g</c> und <c>и -> i</c>.
    /// </summary>
    private static readonly Dictionary<char, string> GeneralTable = BuildGeneralTable();

    private static Dictionary<char, string> BuildGeneralTable()
    {
        var table = new Dictionary<char, string>(UkrainianTable)
        {
            ['г'] = "g",
            ['и'] = "i",
            ['й'] = "y",
            ['є'] = "e",
            ['ї'] = "yi",
        };
        return table;
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
    public static List<string> PlaceCandidates(string? text, int maxWords = 3) =>
        [.. PlaceCandidatePairs(text, null, maxWords).Select(p => p.Normalized).Where(n => n.Length > 0)];

    /// <summary>
    /// Dieselben Kandidaten in BEIDEN Schreibweisen, paarweise. Getrennt zu bilden waere falsch:
    /// die Wortfolgen muessen einander entsprechen, und schon die Dopplungs-Pruefung verschiebt
    /// sonst die Reihenfolge der beiden Listen gegeneinander.
    ///
    /// <para>Ein Paar kann in der ersten Form LEER sein — genau der kyrillische Fall, in dem nur
    /// die Umschrift etwas hergibt. Beide Formen leer heisst „nichts Vergleichbares".</para>
    /// </summary>
    public static List<(string Normalized, string Transcribed)> PlaceCandidatePairs(
        string? text, string? iso2 = null, int maxWords = 3)
    {
        var words = Words(Normalize(text));
        var transcribedWords = Words(NormalizeTranscribed(text, iso2));

        // Die Wortpaare. Die Umschrift aendert keine Wortgrenzen, also passen die Listen im
        // Normalfall Wort fuer Wort zusammen. Zwei Sonderfaelle: eine nichtlateinische Schrift
        // faellt in der ERSTEN Form restlos weg (dann traegt nur die Umschrift — genau der
        // kyrillische Fall), und verschieben sich die Grenzen doch, wird nur die erste Form
        // benutzt: lieber ein Kandidat weniger als ein falsch zusammengesetzter.
        List<(string Normalized, string Transcribed)> wordPairs;
        if (words.Length == transcribedWords.Length)
            wordPairs = [.. words.Zip(transcribedWords, (n, tr) => (n, tr))];
        else if (words.Length == 0)
            wordPairs = [.. transcribedWords.Select(tr => ("", tr))];
        else
            wordPairs = [.. words.Select(n => (n, ""))];

        var pairs = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var length = Math.Min(maxWords, wordPairs.Count); length >= 1; length--)
        {
            for (var start = 0; start + length <= wordPairs.Count; start++)
            {
                var window = wordPairs.Skip(start).Take(length).ToList();
                var normalized = string.Join(' ', window.Select(w => w.Normalized)).Trim();
                var transcribed = string.Join(' ', window.Select(w => w.Transcribed)).Trim();
                if (normalized.Length < 3 && transcribed.Length < 3) continue;
                if (!seen.Add(normalized + "|" + transcribed)) continue;
                pairs.Add((normalized, transcribed));
            }
        }
        return pairs;
    }

    private static string[] Words(string normalized) =>
        [.. normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            // Reine Ziffernfolgen sind PLZ/Hausnummern, keine Ortsnamen.
            .Where(w => !w.All(char.IsDigit))];

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
