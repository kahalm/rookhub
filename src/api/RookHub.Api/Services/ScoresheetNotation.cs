using System.Text;

namespace RookHub.Api.Services;

/// <summary>
/// Schachnotation in den Sprachen, in denen Partieformulare geschrieben werden, und ihre Übersetzung in
/// englische SAN (<c>K Q R B N</c>). Reine Funktionen, keine Abhängigkeiten.
///
/// <para>Auf einem deutschen Formular steht <c>Sf3</c>, auf einem französischen <c>Cf3</c>, auf einem
/// polnischen <c>Sf3</c> mit <c>H</c> für die Dame, auf einem ungarischen <c>Hf3</c> — derselbe Buchstabe
/// heißt je nach Sprache Dame, Springer oder gar nichts. Deshalb liefert <see cref="Candidates"/> keine
/// Antwort, sondern KANDIDATEN: welche das sind, entscheidet erst die Stellung (nur legale Züge zählen,
/// <see cref="ScoresheetResolver"/>). Groß-/Kleinschreibung ist auf einem handgeschriebenen Formular
/// nicht verlässlich; <c>b3</c> kann deshalb auch ein Läuferzug sein, <c>Cf3</c> auch ein c-Bauer — die
/// Legalität trennt das fast immer, und wo nicht, entscheidet der Blick auf die nächsten Züge.</para>
/// </summary>
public static class ScoresheetNotation
{
    /// <summary>Figurenbuchstaben einer Sprache in der Reihenfolge König, Dame, Turm, Läufer, Springer.</summary>
    public sealed record Language(string Code, string Name, string King, string Queen, string Rook, string Bishop, string Knight);

    /// <summary>Die unterstützten Notationssprachen (Codes wie in der Oberfläche). Skandinavien, Kroatien &amp; Co.
    /// teilen sich die deutschen Buchstaben und stehen der Übersicht halber trotzdem einzeln da.</summary>
    public static readonly IReadOnlyList<Language> Languages = new[]
    {
        new Language("en", "English", "K", "Q", "R", "B", "N"),
        new Language("de", "Deutsch", "K", "D", "T", "L", "S"),
        new Language("fr", "Français", "R", "D", "T", "F", "C"),
        new Language("es", "Español", "R", "D", "T", "A", "C"),
        new Language("it", "Italiano", "R", "D", "T", "A", "C"),
        new Language("pt", "Português", "R", "D", "T", "B", "C"),
        new Language("nl", "Nederlands", "K", "D", "T", "L", "P"),
        new Language("hr", "Hrvatski / Srpski / Slovenščina", "K", "D", "T", "L", "S"),
        new Language("pl", "Polski", "K", "H", "W", "G", "S"),
        new Language("cs", "Čeština / Slovenčina", "K", "D", "V", "S", "J"),
        new Language("hu", "Magyar", "K", "V", "B", "F", "H"),
        new Language("sv", "Svenska / Dansk / Norsk", "K", "D", "T", "L", "S"),
        new Language("fi", "Suomi", "K", "D", "T", "L", "R"),
        new Language("ro", "Română", "R", "D", "T", "N", "C"),
        new Language("tr", "Türkçe", "Ş", "V", "K", "F", "A"),
        new Language("ru", "Русский", "Кр", "Ф", "Л", "С", "К"),
        new Language("uk", "Українська", "Кр", "Ф", "Т", "С", "К"),
        new Language("bg", "Български", "Ц", "Д", "Т", "О", "К"),
    };

    /// <summary>Code → Sprache; unbekannt/„auto" → <c>null</c>.</summary>
    public static Language? Find(string? code)
        => Languages.FirstOrDefault(l => string.Equals(l.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Ist das ein bekannter Code oder „auto"?</summary>
    public static bool IsKnown(string? code)
        => string.Equals(code?.Trim(), "auto", StringComparison.OrdinalIgnoreCase) || Find(code) != null;

    /// <summary>Eine Lesart eines Formular-Eintrags in englischer SAN (normalisiert, siehe <see cref="Key"/>)
    /// samt Aufschlag: 0 = so steht es da, mehr = weiter hergeholt (Fremdsprache, anderer Fall).</summary>
    public readonly record struct Candidate(string Key, double Cost);

    /// <summary>
    /// Alle Lesarten eines Eintrags als englische SAN-Schlüssel.
    /// </summary>
    /// <param name="written">Was auf dem Formular steht (oder was das Modell als SAN geliefert hat).</param>
    /// <param name="primary">Sprache, in der der Eintrag vermutlich geschrieben ist; <c>null</c> = Englisch.</param>
    /// <param name="others">Weitere Sprachen, die mit Aufschlag <paramref name="otherCost"/> mitlaufen
    /// (bei „auto" alle, sonst nur Englisch — manche schreiben englisch, obwohl das Formular deutsch ist).</param>
    public static List<Candidate> Candidates(string? written, Language? primary, IEnumerable<Language>? others = null,
        double otherCost = 0.6)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        var cleaned = Clean(written);
        if (cleaned.Length == 0) return new();

        void Add(string key, double cost)
        {
            if (key.Length == 0) return;
            if (!result.TryGetValue(key, out var old) || cost < old) result[key] = cost;
        }

        if (cleaned is "O-O" or "O-O-O") { Add(cleaned, 0); return ToList(result); }

        var langs = new List<(Language Lang, double Cost)> { (primary ?? Languages[0], 0) };
        foreach (var o in others ?? Array.Empty<Language>())
            if (langs.All(l => l.Lang.Code != o.Code)) langs.Add((o, otherCost));

        // Ein KLEINER Buchstabe a–h vorn ist eher eine Linie als eine Figur („bxa4" ist ein Bauer, kein Läufer):
        // die Figur-Lesart bleibt, kostet aber etwas mehr — bei einem Gleichstand gewinnt der Bauer.
        var lowerFile = cleaned[0] is >= 'a' and <= 'h';
        foreach (var (lang, baseCost) in langs)
        {
            // Figurenzug: führender Figurenbuchstabe dieser Sprache (Groß/klein egal — Handschrift).
            if (TryStripPiece(cleaned, lang, out var piece, out var rest))
                Add(piece + NormalizeSquares(rest, lang), baseCost + (lowerFile ? 0.3 : 0));
        }
        // Bauernzug: beginnt mit einer Linie a–h. Handschriftlich groß geschrieben („B3") kostet es etwas.
        if (cleaned[0] is >= 'a' and <= 'h')
            Add(NormalizeSquares(cleaned, primary), 0);
        else if (cleaned[0] is >= 'A' and <= 'H')
            Add(NormalizeSquares(char.ToLowerInvariant(cleaned[0]) + cleaned[1..], primary), 0.4);

        return ToList(result);
    }

    private static List<Candidate> ToList(Dictionary<string, double> d)
        => d.Select(kv => new Candidate(kv.Key, kv.Value)).OrderBy(c => c.Cost).ToList();

    /// <summary>
    /// Vergleichsschlüssel eines SAN-Zuges: ohne Schach-/Matt-/Bewertungszeichen, ohne <c>x</c> und
    /// <c>=</c>, Rochaden als <c>O-O</c>/<c>O-O-O</c>. <c>exd5</c> → <c>ed5</c>, <c>e8=Q+</c> → <c>e8Q</c>,
    /// <c>Nbd2</c> bleibt <c>Nbd2</c>.
    /// </summary>
    public static string Key(string san)
    {
        var c = Clean(san);
        if (c is "O-O" or "O-O-O") return c;
        // Hinter der Figur stehen nur noch Linien/Reihen (Figuren wandeln nicht um) — also klein.
        return c.Length > 0 && "KQRBN".Contains(c[0]) ? c[0] + c[1..].ToLowerInvariant() : c;
    }

    /// <summary>Wie <see cref="Key"/>, aber ohne Unterscheidung der Ausgangsfigur (<c>Nbd2</c> → <c>Nd2</c>,
    /// <c>R1e2</c> → <c>Re2</c>). Für Formulare, auf denen der Zusatz fehlt — dann entscheidet die Stellung.</summary>
    public static string LooseKey(string key)
    {
        if (key.Length > 3 && "KQRBN".Contains(key[0]))
        {
            // Figur + Zielfeld (+ Umwandlung gibt es bei Figuren nicht).
            return key[0] + key[^2..];
        }
        return key;
    }

    /// <summary>
    /// Säubert einen Eintrag: Figurinen → englische Buchstaben, Rochaden vereinheitlicht, Schach-/Schlag-/
    /// Bewertungszeichen, Leerzeichen, „e.p." und Bindestriche (Langschrift „e2-e4") entfernt.
    /// Ergebnis ist entweder <c>O-O</c>/<c>O-O-O</c> oder eine Folge aus Buchstaben und Ziffern.
    /// </summary>
    public static string Clean(string? written)
    {
        if (string.IsNullOrWhiteSpace(written)) return string.Empty;
        var s = written.Trim();
        var castle = s.Replace(" ", "").Replace("–", "-").Replace("—", "-").ToUpperInvariant()
            .TrimEnd('+', '#', '!', '?');
        if (castle is "0-0-0" or "O-O-O" or "000" or "OOO") return "O-O-O";
        if (castle is "0-0" or "O-O" or "00" or "OO") return "O-O";

        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '♔': case '♚': sb.Append('K'); break;
                case '♕': case '♛': sb.Append('Q'); break;
                case '♖': case '♜': sb.Append('R'); break;
                case '♗': case '♝': sb.Append('B'); break;
                case '♘': case '♞': sb.Append('N'); break;
                case '+': case '#': case '!': case '?': case ':': case '×': case '=': case '-': case '–': case '—':
                case '(': case ')': case ',': case '.': case '/': case '\'':
                    break;
                default:
                    if (!char.IsWhiteSpace(ch)) sb.Append(ch);
                    break;
            }
        }
        var t = sb.ToString();
        // „ep"/„e.p." am Ende (Punkte sind oben schon weg) — nur wenn davor ein Feld steht.
        if (t.Length > 4 && t.EndsWith("ep", StringComparison.OrdinalIgnoreCase) && char.IsDigit(t[^3])) t = t[..^2];
        // Schlagzeichen „x" (nicht die Linie!): ein x steht nie für eine Linie, Linien gehen nur bis h.
        t = t.Replace("x", "").Replace("X", "");
        return t;
    }

    /// <summary>Führenden Figurenbuchstaben einer Sprache abtrennen (längster Code zuerst, „Кр" vor „К").</summary>
    private static bool TryStripPiece(string s, Language lang, out char piece, out string rest)
    {
        var codes = new (string Code, char En)[]
        {
            (lang.King, 'K'), (lang.Queen, 'Q'), (lang.Rook, 'R'), (lang.Bishop, 'B'), (lang.Knight, 'N'),
        }.OrderByDescending(c => c.Code.Length);
        foreach (var (code, en) in codes)
        {
            if (s.Length > code.Length && s.StartsWith(code, StringComparison.OrdinalIgnoreCase))
            {
                // Ein kleiner Buchstabe a–h ist in Sprachen, deren Figurencode selbst eine Linie ist (b, c, f, h …),
                // genauso gut ein Bauer — beide Lesarten laufen mit, die Stellung entscheidet.
                piece = en;
                rest = s[code.Length..];
                return rest.Length >= 2;
            }
        }
        piece = default;
        rest = string.Empty;
        return false;
    }

    /// <summary>Linien klein, Umwandlungsfigur am Ende (<c>e8D</c>) in die englische übersetzen.</summary>
    private static string NormalizeSquares(string rest, Language? lang)
    {
        var sb = new StringBuilder(rest.Length);
        for (var i = 0; i < rest.Length; i++)
        {
            var ch = rest[i];
            var last = i == rest.Length - 1;
            if (last && i > 0 && (rest[i - 1] == '8' || rest[i - 1] == '1') && char.IsLetter(ch) && !(ch is >= 'a' and <= 'h'))
            {
                sb.Append(PromotionPiece(ch.ToString(), lang));
                continue;
            }
            sb.Append(ch is >= 'A' and <= 'H' ? char.ToLowerInvariant(ch) : ch);
        }
        return sb.ToString();
    }

    private static string PromotionPiece(string code, Language? lang)
    {
        foreach (var l in lang == null ? Languages : new[] { lang }.Concat(Languages))
        {
            if (string.Equals(code, l.Queen, StringComparison.OrdinalIgnoreCase)) return "Q";
            if (string.Equals(code, l.Rook, StringComparison.OrdinalIgnoreCase)) return "R";
            if (string.Equals(code, l.Bishop, StringComparison.OrdinalIgnoreCase)) return "B";
            if (string.Equals(code, l.Knight, StringComparison.OrdinalIgnoreCase)) return "N";
        }
        return code.ToUpperInvariant();
    }

    /// <summary>Zeichenpaare, die in Handschrift leicht verwechselt werden (Ziffern der Reihen, Buchstaben der Linien).</summary>
    private static readonly HashSet<(char, char)> Confusable = BuildConfusable(
        "16", "17", "38", "68", "56", "49", "06", "08", "23", "27", "35", "ad", "bh", "ce", "gq", "ef", "hk", "bd");

    private static HashSet<(char, char)> BuildConfusable(params string[] pairs)
    {
        var set = new HashSet<(char, char)>();
        foreach (var p in pairs) { set.Add((p[0], p[1])); set.Add((p[1], p[0])); }
        return set;
    }

    /// <summary>Unterscheiden sich zwei gleich lange Schlüssel in genau EINEM Zeichen, und ist das ein leicht zu
    /// verwechselndes Paar (6/8, 1/7, a/d …)?</summary>
    public static bool IsConfusable(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = -1;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i]) continue;
            if (diff >= 0) return false;
            diff = i;
        }
        return diff >= 0 && Confusable.Contains((char.ToLowerInvariant(a[diff]), char.ToLowerInvariant(b[diff])));
    }

    /// <summary>
    /// Gewichteter Abstand für Lesefehler: ein VERLESENES Zeichen kostet 1, ein fehlendes oder dazugedichtetes 1,2.
    /// In Handschrift wird ein Zeichen viel öfter falsch gelesen als ganz übersehen — am HCS-Beleg 01 gewann sonst
    /// „bxa4" → „a4" (Zeichen gestrichen) gegen das richtige „bxa4" → „bxc4" (Zeichen verlesen).
    /// </summary>
    public static double WeightedDistance(string a, string b)
    {
        const double indel = 1.2;
        if (a == b) return 0;
        var prev = new double[b.Length + 1];
        var cur = new double[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j * indel;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i * indel;
            for (var j = 1; j <= b.Length; j++)
            {
                var sub = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + indel, prev[j] + indel), prev[j - 1] + sub);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>Levenshtein-Abstand (klein, für Tippfehler/Lesefehler in einem Zug).</summary>
    public static int Distance(string a, string b)
    {
        if (a == b) return 0;
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
