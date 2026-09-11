using System.Text;

namespace RookHub.Api.Services;

/// <summary>
/// Loest die FIGURENZEICHEN der ChessBase-Sammlung in Buchstaben auf.
///
/// <para><b>Der Fund:</b> im PGN stehen die Figuren INNERHALB der Kommentare nicht als Buchstaben,
/// sondern als Zeichen aus dem privaten Unicode-Bereich (U+E024 bis U+E029) — die Codepunkte der
/// ChessBase-Figurenschrift. Ohne diese Schrift sind sie unsichtbar, und der Kommentar liest sich
/// als „I can't win the pawn due to the h7+ trick" oder „Black's  structure is ruined". Am
/// Bestand gemessen (2026-09-11): <b>45 von 101</b> Partien einer Stichprobe enthalten solche
/// Zeichen, in 4000 Kommentarzeilen ueber 1100 Vorkommen.</para>
///
/// <para><b>Die Zuordnung ist am Text belegt</b>, nicht geraten: <c>U+E024</c> steht in „I would
/// prefer …b1" (Koenig), <c>U+E025</c> in „Black has played ...…a5" (Dame), <c>U+E026</c> in „a
/// more ambitious …dg1!?" (Turm), <c>U+E027</c> im „…h7+ trick" (Laeufer — das griechische
/// Geschenk), <c>U+E028</c> in „long castle (after ...…c6)" (Springer) und <c>U+E029</c> in
/// „Black's … structure" (Bauer).</para>
///
/// <para><b>Die Sprache entscheidet den Buchstaben</b> — ein Springer heisst englisch N und deutsch
/// S. Deshalb wird hier aufgeloest, wo die Sprache feststeht: beim Ablegen eines
/// <see cref="Models.CommentSet"/>. Der BAUER bekommt als einziger ein Wort statt eines Buchstabens
/// („pawn", „Bauer"): in der Notation traegt er keinen, und „Black's P structure" waere weder
/// Deutsch noch Englisch.</para>
/// </summary>
public static class Figurines
{
    /// <summary>Der erste Codepunkt der Reihe (Koenig); die sechs folgen aufeinander.</summary>
    public const char First = '';
    public const char Last = '';

    /// <summary>Koenig, Dame, Turm, Laeufer, Springer — in der Reihenfolge der Codepunkte.</summary>
    private static readonly Dictionary<string, string> Letters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "KQRBN",
        ["de"] = "KDTLS",
        ["fr"] = "RDTFC",
        ["es"] = "RDTAC",
        ["it"] = "RDTAC",
        ["pt"] = "RDTBC",
        ["nl"] = "KDTLP",
        ["pl"] = "KHWGS",
        ["cs"] = "KDVSJ",
        ["hu"] = "KVBFH",
        ["sv"] = "KDTLS",
        ["hr"] = "KDTLS",
    };

    /// <summary>Das Wort fuer den Bauern; ohne Eintrag gilt das englische.</summary>
    private static readonly Dictionary<string, string> Pawn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "pawn", ["de"] = "Bauer", ["fr"] = "pion", ["es"] = "peón", ["it"] = "pedone",
        ["pt"] = "peão", ["nl"] = "pion", ["pl"] = "pionek", ["cs"] = "pěšec", ["hu"] = "gyalog",
        ["sv"] = "bonde", ["hr"] = "pješak",
    };

    /// <summary>Ob der Text ueberhaupt eines dieser Zeichen enthaelt — bei mehr als der Haelfte der
    /// Partien ist die Antwort nein, und dann soll nichts kopiert werden.</summary>
    public static bool Contains(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var c in text)
            if (c is >= First and <= Last) return true;
        return false;
    }

    /// <summary>
    /// Die Figurenzeichen durch die Buchstaben der Sprache ersetzen. Eine unbekannte Sprache (und
    /// <c>und</c>, „nicht bestimmbar") bekommt die englischen — sie sind die verbreitetsten und
    /// stehen ohnehin im Quelltext der meisten Partien.
    /// </summary>
    public static string Apply(string? text, string? language)
    {
        if (!Contains(text)) return text ?? string.Empty;

        var letters = Letters.GetValueOrDefault(language ?? "", Letters["en"]);
        var pawn = Pawn.GetValueOrDefault(language ?? "", Pawn["en"]);
        var sb = new StringBuilder(text!.Length);
        foreach (var c in text)
        {
            if (c is >= First and < Last) sb.Append(letters[c - First]);
            else if (c == Last) sb.Append(pawn);
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
