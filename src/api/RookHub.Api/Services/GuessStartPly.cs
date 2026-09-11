using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Ab welchem Halbzug lohnt es sich, eine Partie raten zu lassen?
///
/// <para>Bis 0.467.0 war die Antwort eine KONSTANTE (<c>GuessSessionService.DefaultSkipPlies</c> = 8,
/// im Code als „grober Platzhalter" vermerkt). Acht Halbzuege sind bei einem scharfen Gambit schon
/// mitten im Gefecht und bei einer geschlossenen Eroeffnung noch reines Buchwissen — geraten wurde
/// also mal zu frueh, mal zu spaet, und nie aus einem Grund.</para>
///
/// <para><b>Zwei Hinweise, der FRUEHERE gewinnt.</b> Beide sagen etwas anderes, und beide koennen
/// allein danebenliegen:</para>
/// <list type="number">
/// <item><b>Die Eroeffnung verlaesst das Buch.</b> Der Rohbestand (<see cref="Models.LibraryGame"/>)
/// ist eine Eroeffnungsstatistik aus 130 000 Meisterpartien: der erste Halbzug, dessen Zugfolge in
/// weniger als <see cref="RareBelowGames"/> dieser Partien vorkommt, ist der, ab dem nicht mehr
/// nachgeschlagen, sondern gespielt wird. Genau das Verfahren, das der alte Kommentar als Ziel
/// nannte („chessgames startet dort, wo eine Stellung unter 1000 Datenbankpartien faellt").</item>
/// <item><b>Der Kommentator faengt an zu reden.</b> Wo er den ersten Kommentar setzt, hielt er die
/// Partie fuer erklaerungsbeduerftig. Bei einer kommentierten Meisterpartie ist das die ehrlichste
/// Auskunft, die es gibt — und sie steht schon im PGN.</item>
/// </list>
///
/// <para><b>Warum eine UNTERGRENZE noetig ist:</b> Sammlungen setzen den ersten Kommentar oft an den
/// ERSTEN Zug, und dort steht dann keine Erklaerung, sondern eine Quellenangabe („{1)Skinner:
/// Alexander Alekhines Chess Games 1902-1946. p.14}"). Ohne <see cref="Earliest"/> begaenne die
/// Punktepartie bei solchen Partien mit „rate 1. e4" — kein Test von Spielstaerke, sondern von
/// Geduld.</para>
/// </summary>
public class GuessStartPly
{
    private readonly AppDbContext _db;

    public GuessStartPly(AppDbContext db) => _db = db;

    /// <summary>Frueher als nach drei vollen Zuegen faengt keine Punktepartie an.</summary>
    public const int Earliest = 6;

    /// <summary>Und spaeter als nach zwanzig vollen Zuegen auch nicht — sonst ueberspringt die
    /// Uebung die halbe Partie, nur weil eine Eroeffnung lange im Buch bleibt.</summary>
    public const int Latest = 40;

    /// <summary>Unter so vielen Bestands-Partien gilt eine Zugfolge als verlassen. Zwanzig von
    /// 130 000: haeufiger heisst, dass mehrere Kommentatoren sie fuer erwaehnenswert hielten.</summary>
    public const int RareBelowGames = 20;

    /// <summary>So viele Partien muss der Bestand mindestens haben, damit seine Haeufigkeiten etwas
    /// bedeuten. Auf einer frischen Installation ist er LEER — dort waere jede Zugfolge „selten",
    /// und der Hinweis zeigte immer auf Halbzug 1.</summary>
    public const int MinLibrarySize = 1000;

    /// <summary>
    /// Der Vorschlag fuer diese Partie, oder <c>null</c>, wenn kein Hinweis greift (dann gilt die
    /// Vorgabe des Aufrufers).
    /// </summary>
    /// <param name="pgn">Das PGN der Partie (Kopfzeilen duerfen drinstehen).</param>
    /// <param name="plyCount">Laenge der Partie — der Vorschlag liegt nie hinter ihrem Ende.</param>
    public async Task<int?> SuggestAsync(string? pgn, int plyCount, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pgn) || plyCount <= 0) return null;

        var stats = LibraryGameReader.Analyse(MoveTextOf(pgn));
        if (stats.PlyCount == 0) return null;

        // Beide Hinweise zaehlen 1-basiert („der siebte Halbzug"), `StartPly` ist der 0-BASIERTE
        // Index des ersten zu ratenden Zuges. Die beiden werden deshalb VERSCHIEDEN umgerechnet:
        //
        // • Eroeffnung: der Zug, an dem das Buch endet, ist der erste eigene — also minus eins.
        // • Kommentar: der KOMMENTIERTE Zug wird noch vorgespielt, geraten wird der danach.
        //   Sonst gehoerte der Kommentar zu dem Zug, der gerade gesucht ist, und duerfte nicht
        //   gezeigt werden („die Fortsetzung verlaesst den Server nicht") — der Hinweis, der den
        //   Einstieg bestimmt hat, waere ausgerechnet der einzige unsichtbare.
        var byOpening = await OpeningExitAsync(stats.OpeningLine, ct) - 1;
        var byComment = stats.FirstCommentedPly > 0 ? stats.FirstCommentedPly : (int?)null;

        var suggested = Earlier(byOpening, byComment);
        if (suggested is null) return null;

        // Nie hinter das Partieende: mindestens ein Zug muss zu raten bleiben, sonst ist die
        // Sitzung keine. Bei einer Miniatur gewinnt dieser Deckel gegen die Untergrenze.
        var latest = Math.Min(Latest, plyCount - 2);
        if (latest < 0) return null;

        var value = Math.Min(suggested.Value, latest);
        return Math.Max(value, Math.Min(Earliest, latest));
    }

    private static int? Earlier(int? a, int? b) =>
        a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);

    /// <summary>
    /// Der erste Halbzug, dessen Zugfolge im Bestand seltener als <see cref="RareBelowGames"/> ist.
    ///
    /// <para>Gesucht wird BINAER: die Zahl der Partien mit gemeinsamem Anfang faellt mit jedem
    /// Halbzug monoton, also genuegen fuenf Abfragen statt dreissig. Jede ist eine Praefix-Suche
    /// auf dem Index ueber <c>OpeningLine</c>.</para>
    /// </summary>
    private async Task<int?> OpeningExitAsync(string openingLine, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(openingLine)) return null;
        if (await _db.LibraryGames.CountAsync(ct) < MinLibrarySize) return null;

        var moves = openingLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (moves.Length == 0) return null;

        int lo = 1, hi = moves.Length, found = 0;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (await CountWithPrefixAsync(string.Join(' ', moves.Take(mid)), ct) < RareBelowGames)
            {
                found = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        // Nichts gefunden heisst: die Zugfolge ist bis ans Ende des Fensters gebraeuchlich. Dann
        // ist das Fenster-Ende der frueheste Punkt, den dieser Hinweis stuetzen kann.
        return found > 0 ? found : moves.Length;
    }

    /// <summary>Wie viele Bestands-Partien beginnen mit genau dieser Zugfolge? Die Wortgrenze ist
    /// entscheidend: ohne sie zaehlte „e4 e" auch „e4 e6" mit.</summary>
    private Task<int> CountWithPrefixAsync(string prefix, CancellationToken ct)
    {
        var withSeparator = prefix + " ";
        return _db.LibraryGames.CountAsync(
            g => g.OpeningLine != null
                 && (g.OpeningLine == prefix || g.OpeningLine.StartsWith(withSeparator)), ct);
    }

    /// <summary>Der Zugteil eines PGN — Kopfzeilen raus. Ein Tag-Wert kann alles enthalten, auch
    /// etwas, das wie ein Zug aussieht („[Opening \"e4 e5\"]"); ihn mitzulesen verschoebe die
    /// Zaehlung.</summary>
    public static string MoveTextOf(string pgn)
    {
        var lines = pgn.Split('\n');
        var moves = new System.Text.StringBuilder(pgn.Length);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) continue;
            moves.Append(line).Append(' ');
        }
        return moves.ToString();
    }
}
