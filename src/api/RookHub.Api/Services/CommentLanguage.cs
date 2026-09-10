using System.Text;

namespace RookHub.Api.Services;

/// <summary>
/// In welcher Sprache sind die Kommentare einer Partie geschrieben?
///
/// <para>Fuer den Rohbestand ist das ein Auswahlkriterium und keine Nebensache: eine glaenzend
/// kommentierte Partie nuetzt nichts, wenn der Text niemandem etwas sagt, der ihn lesen soll.</para>
///
/// <para><b>Warum eine eigene Handvoll Zeilen und keine Bibliothek:</b> die Aufgabe ist hier viel
/// kleiner als „erkenne eine beliebige Sprache". Es geht um ein knappes Dutzend europaeischer
/// Sprachen, der Text ist reichlich vorhanden (Hunderte Woerter je Partie), und die Antwort muss
/// nur gut genug sein, um zu sortieren. Haeufigkeiten von FUNKTIONSWOERTERN reichen dafuer: die
/// stehen in jedem Prosatext und in keiner Schachnotation.</para>
///
/// <para>Die Schachnotation selbst ist dabei kein Stoerfaktor, sondern faellt einfach durch — „Nf3",
/// „Rxe4" und „1-0" stehen in keiner der Wortlisten.</para>
/// </summary>
public static class CommentLanguage
{
    /// <summary>Die Funktionswoerter, an denen eine Sprache haengt. Bewusst kurz gehalten: je
    /// laenger die Listen, desto mehr Woerter tauchen in mehreren Sprachen auf (span. „la" =
    /// franz. „la"), und desto unschaerfer wird die Entscheidung.</summary>
    private static readonly Dictionary<string, string[]> Markers = new()
    {
        ["de"] = ["der", "die", "das", "und", "ist", "nicht", "aber", "auch", "mit", "sich",
                  "weiss", "schwarz", "zug", "stellung", "muss", "waere", "kann", "noch", "schon", "sehr"],
        ["en"] = ["the", "and", "is", "not", "but", "with", "this", "that", "white", "black",
                  "move", "position", "would", "should", "after", "better", "now", "very", "has", "was"],
        ["es"] = ["el", "los", "las", "una", "que", "por", "con", "pero", "blancas", "negras",
                  "jugada", "posicion", "mejor", "despues", "muy", "esta", "para", "como", "hay", "tambien"],
        ["fr"] = ["le", "les", "une", "des", "que", "pour", "avec", "mais", "blancs", "noirs",
                  "coup", "position", "meilleur", "apres", "tres", "cette", "dans", "sur", "est", "pas"],
        ["it"] = ["il", "lo", "gli", "una", "che", "per", "con", "ma", "bianco", "nero",
                  "mossa", "posizione", "migliore", "dopo", "molto", "questa", "nel", "non", "anche", "sono"],
        ["nl"] = ["de", "het", "een", "en", "niet", "maar", "met", "deze", "wit", "zwart",
                  "zet", "stelling", "beter", "daarna", "zeer", "ook", "voor", "naar", "kan", "moet"],
        ["pt"] = ["os", "as", "uma", "que", "por", "com", "mas", "brancas", "pretas", "jogada",
                  "posicao", "melhor", "depois", "muito", "esta", "para", "nao", "tambem", "sao", "seu"],
        ["pl"] = ["nie", "jest", "sie", "tego", "ale", "przez", "bialy", "czarny", "ruch", "pozycja",
                  "lepiej", "potem", "bardzo", "tez", "moze", "musi", "juz", "tylko", "jak", "co"],
        ["cs"] = ["je", "se", "na", "ale", "pro", "bily", "cerny", "tah", "pozice", "lepsi",
                  "potom", "velmi", "take", "muze", "musi", "jen", "jak", "co", "tento", "neni"],
        ["hu"] = ["nem", "hogy", "egy", "meg", "csak", "vilagos", "sotet", "lepes", "allas", "jobb",
                  "utan", "nagyon", "is", "mar", "kell", "lehet", "mint", "ez", "de", "van"],
        ["tr"] = ["bir", "ve", "bu", "icin", "ama", "beyaz", "siyah", "hamle", "konum", "daha",
                  "sonra", "cok", "de", "da", "ile", "olan", "var", "yok", "gibi", "ise"],
        ["sv"] = ["och", "att", "inte", "men", "med", "detta", "vit", "svart", "drag", "stallning",
                  "battre", "efter", "mycket", "ocksa", "kan", "maste", "har", "var", "som", "for"],
    };

    /// <summary>Ab so vielen erkannten Woertern trauen wir dem Ergebnis. Darunter ist es Zufall:
    /// „e4 e5" plus zwei Ausrufezeichen sagt ueber die Sprache nichts.</summary>
    private const int MinHits = 5;

    /// <summary>Liegt die zweitbeste Sprache so nah an der besten, stehen vermutlich beide im Text
    /// (Sammlungen mischen das) — dann werden beide genannt.</summary>
    private const double SecondShare = 0.6;

    /// <summary>
    /// Die Sprache(n) des Textes als CSV von ISO-Kuerzeln, oder <c>null</c>, wenn der Text zu wenig
    /// hergibt. Kyrillisch wird an der SCHRIFT erkannt und nicht an Woertern — dort ist die Frage
    /// schon mit dem ersten Buchstaben beantwortet.
    /// </summary>
    public static string? Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var cyrillic = 0;
        var letters = 0;
        foreach (var c in text)
        {
            if (!char.IsLetter(c)) continue;
            letters++;
            if (c is >= 'Ѐ' and <= 'ӿ') cyrillic++;
        }
        if (letters > 40 && cyrillic > letters / 5) return "ru";

        var hits = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var word in Words(text))
        {
            foreach (var (lang, markers) in Markers)
                if (Array.IndexOf(markers, word) >= 0)
                    hits[lang] = hits.GetValueOrDefault(lang) + 1;
        }
        if (hits.Count == 0) return null;

        var ranked = hits.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        if (ranked[0].Value < MinHits) return null;

        var result = ranked[0].Key;
        if (ranked.Count > 1 && ranked[1].Value >= ranked[0].Value * SecondShare && ranked[1].Value >= MinHits)
            result += "," + ranked[1].Key;
        return result;
    }

    /// <summary>
    /// Der reine Kommentartext einer Partie: alles, was in geschweiften Klammern steht.
    /// </summary>
    /// <param name="pgn">Das PGN der Partie (Kopfzeilen stoeren nicht, sie tragen keine Klammern).</param>
    /// <param name="maxChars">Deckel. Fuer die Frage nach der Sprache reichen die ersten paar
    /// tausend Zeichen; eine Partie mit 60 KB Analyse deswegen ganz durchzugehen waere verschwendet.</param>
    public static string CommentText(string? pgn, int maxChars = 4000)
    {
        if (string.IsNullOrEmpty(pgn)) return string.Empty;

        var sb = new StringBuilder(Math.Min(maxChars, 1024));
        for (var i = 0; i < pgn.Length && sb.Length < maxChars;)
        {
            var open = pgn.IndexOf('{', i);
            if (open < 0) break;
            var close = pgn.IndexOf('}', open + 1);
            if (close < 0) break;
            var take = Math.Min(close - open - 1, maxChars - sb.Length);
            if (take > 0) sb.Append(pgn, open + 1, take).Append(' ');
            i = close + 1;
        }
        return sb.ToString();
    }

    /// <summary>Woerter in Kleinschreibung und ohne Akzente — die Wortlisten stehen ebenfalls ohne,
    /// damit „posición" und „posicion" dasselbe treffen.</summary>
    private static IEnumerable<string> Words(string text)
    {
        var sb = new StringBuilder(24);
        foreach (var raw in text)
        {
            var c = Fold(raw);
            if (c is >= 'a' and <= 'z')
            {
                sb.Append(c);
                continue;
            }
            if (sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    /// <summary>Buchstabe auf a-z zurueckgefuehrt; alles andere wird zum Trenner.</summary>
    private static char Fold(char c)
    {
        c = char.ToLowerInvariant(c);
        return c switch
        {
            >= 'a' and <= 'z' => c,
            'á' or 'à' or 'â' or 'ä' or 'ã' or 'å' or 'ą' => 'a',
            'é' or 'è' or 'ê' or 'ë' or 'ę' => 'e',
            'í' or 'ì' or 'î' or 'ï' => 'i',
            'ó' or 'ò' or 'ô' or 'ö' or 'õ' or 'ø' => 'o',
            'ú' or 'ù' or 'û' or 'ü' or 'ů' => 'u',
            'ç' or 'č' or 'ć' => 'c',
            'ñ' or 'ň' => 'n',
            'š' or 'ś' => 's',
            'ž' or 'ź' or 'ż' => 'z',
            'ř' => 'r',
            'ť' => 't',
            'ď' => 'd',
            'ý' => 'y',
            'ł' => 'l',
            'ß' => 's',
            _ => ' ',
        };
    }
}
