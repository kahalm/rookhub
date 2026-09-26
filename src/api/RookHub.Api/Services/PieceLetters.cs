using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

/// <summary>
/// Die Figurenbuchstaben der Zuege einer Uebersetzung auf die Zielsprache umstellen: aus „Nf3" wird
/// „Sf3", aus dem franzoesischen „Cf3" ebenso.
///
/// <para><b>Warum nicht dem Modell ueberlassen.</b> Die Regel steht im Auftrag
/// (<c>CommentTranslationService.FigurineNote</c>), und ein starkes Modell haelt sie ueberwiegend
/// ein — im Bestand 91 Prozent der Zeilen. Das Modell auf eigener Hardware stellt die Buchstaben
/// dagegen fast nie um (gemessen 2026-09-26: 12 von 54 Zeilen). Welcher Buchstabe zu welcher Figur
/// gehoert, steht aber fest und laesst sich ausrechnen; was feststeht, sollte nicht an einem
/// Modelldurchgang haengen.</para>
///
/// <para><b>Warum es auch auf schon umgestellte Zuege gefahrlos laeuft.</b> Ersetzt wird nur, was ein
/// Buchstabe der QUELLSPRACHE ist. Steht in der Uebersetzung schon „Sf3", kommt „S" im englischen
/// Alphabet nicht vor und bleibt stehen. Und wo ein Buchstabe in beiden Sprachen steht, zeigt er auf
/// dieselbe Figur: franzoesisch „Td1" (tour) wird deutsch „Td1" (Turm).</para>
///
/// <para><b>Was nicht angefasst wird:</b> Prosa. Ein Treffer braucht ein vollstaendiges Zugmuster mit
/// Zielfeld — „Bad Wiessee" und „Damit" sehen deshalb keinen Zug, „Bxe4" und „Nbd7" schon.</para>
/// </summary>
public static class PieceLetters
{
    /// <summary>Koenig, Dame, Turm, Laeufer, Springer — in dieser Reihenfolge. Hier steht nur, was
    /// belegt ist: eine geratene Zeile schriebe Zuege kaputt, und eine fehlende laesst sie bloss
    /// unveraendert.</summary>
    private static readonly Dictionary<string, string> Alphabets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "KQRBN",   // king queen rook bishop knight
        ["de"] = "KDTLS",   // Koenig Dame Turm Laeufer Springer
        ["fr"] = "RDTFC",   // roi dame tour fou cavalier
        ["es"] = "RDTAC",   // rey dama torre alfil caballo
        ["it"] = "RDTAC",   // re donna torre alfiere cavallo
        ["pt"] = "RDTBC",   // rei dama torre bispo cavalo
        ["nl"] = "KDTLP",   // koning dame toren loper paard
        ["sv"] = "KDTLS",   // kung dam torn loepare springare
        ["da"] = "KDTLS",   // konge dronning taarn loeber springer
        ["hr"] = "KDTLS",   // kralj dama top lovac skakac
        ["hu"] = "KVBFH",   // kiraly vezer bastya futo huszar
        ["cs"] = "KDVSJ",   // kral dama vez strelec jezdec
        ["pl"] = "KHWGS",   // krol hetman wieza goniec skoczek
    };

    private static readonly Dictionary<string, Regex> MoveCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Regex> PromotionCache = new(StringComparer.Ordinal);
    private static readonly Lock Gate = new();

    /// <summary>
    /// Die Zuege in <paramref name="text"/> von den Figurenbuchstaben der Sprache
    /// <paramref name="from"/> auf die der Sprache <paramref name="to"/> umstellen. Ist eine der
    /// beiden Sprachen unbekannt (etwa „und") oder tragen beide dieselben Buchstaben, bleibt der Text
    /// unveraendert — im Zweifel lieber ein englischer Zug als ein zerschriebener.
    /// </summary>
    public static string Convert(string text, string? from, string? to)
    {
        if (string.IsNullOrEmpty(text) || from is null || to is null) return text;
        if (!Alphabets.TryGetValue(from.Trim(), out var source)) return text;
        if (!Alphabets.TryGetValue(to.Trim(), out var target)) return text;
        if (string.Equals(source, target, StringComparison.Ordinal)) return text;

        string Letter(string found) => target[source.IndexOf(found[0], StringComparison.Ordinal)].ToString();

        // „Nf3", „Nbd7", „N1f3", „Bxe4", „Qh4+" — ein Zielfeld gehoert dazu, sonst waere jedes Wort
        // mit grossem Anfangsbuchstaben ein Kandidat.
        text = MoveRegex(source).Replace(text, m => Letter(m.Groups["p"].Value) + m.Groups["rest"].Value);
        // Die Umwandlung: „e8=Q" wird „e8=D".
        return PromotionRegex(source).Replace(text, m => Letter(m.Value));
    }

    private static Regex MoveRegex(string letters) => Cached(MoveCache, letters,
        $@"(?<![\p{{L}}\p{{N}}])(?<p>[{letters}])(?<rest>[a-h]?[1-8]?x?[a-h][1-8])(?![\p{{L}}\p{{N}}])");

    private static Regex PromotionRegex(string letters) => Cached(PromotionCache, letters,
        $@"(?<=[a-h][1-8]=)[{letters}](?![\p{{L}}\p{{N}}])");

    private static Regex Cached(Dictionary<string, Regex> cache, string letters, string pattern)
    {
        lock (Gate)
        {
            if (!cache.TryGetValue(letters, out var rx))
                cache[letters] = rx = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
            return rx;
        }
    }
}
