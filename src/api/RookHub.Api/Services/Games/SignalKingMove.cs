using Chess;
using RookHub.Api.Services.Scoresheet;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Der „Signal-Königszug“ am Ende einer übertragenen Partie (1.022.0, Wunsch 2026-11-26). Am DGT-Brett stellt der Sieger nach
/// der Partie oft seinen König in die Mitte — das Zeichen an Brett und Schiedsrichter, dass die Partie aus ist. Die Übertragung
/// zeichnet das als letzten Zug auf. Befund an Prod-Vereinspartie 268 (Raja Rithvik R – Binder, Moriz, 1-0): nach 44...Dc3+ steht
/// 45.Ke4 — in Wahrheit Matt in 4 für Schwarz (45...Te1+ 46.Kf4 De5+ 47.Kg4 Df5+ 48.Kh4 Dh5#), die Analyse markierte ihn mit ??,
/// die Zusammenfassung schrieb darüber.
///
/// <para><b>Regel</b> (bewusst eng, entfernt wird nur, was dem Ergebnis klar widerspricht — und nie mehr als dieser EINE
/// Halbzug):</para>
/// <list type="number">
/// <item>Das Ergebnis ist entschieden (<c>1-0</c> / <c>0-1</c>).</item>
/// <item>Der letzte Halbzug ist ein Königszug des SIEGERS laut Ergebnis.</item>
/// <item>Er geht auf eines der vier Mittelfelder d4, e4, d5, e5 (<see cref="Centre"/>) — dort steht das Signal. Königszüge
/// woandershin bleiben immer stehen (Prod-Bestand 26.11.: zwei Kopien in „Meine Partien“ enden mit Kh2/Ke8 ins Matt in 2 —
/// nicht in der Mitte, also kein Signal, eher ein Abschreibfehler; das entscheidet der Mensch).</item>
/// <item>Er widerspricht dem Ergebnis, ohne Engine nachgewiesen: danach hat der VERLIERER ein erzwungenes Matt in höchstens
/// <see cref="MaxMateMoves"/> Zügen, nur mit Schachgeboten (Mattsuche <see cref="ForcedMateByChecks"/>, gedeckelt auf
/// <see cref="NodeBudget"/> Stellungen — wird der Deckel erreicht, gilt „kein Matt“ und der Zug bleibt), oder die Stellung ist
/// patt (dann wäre die Partie remis).</item>
/// </list>
/// <para>Warum keine Engine-Bewertung: Die Prüfung läuft beim Einlesen, bevor irgendeine Analyse da ist, und muss für dieselben
/// Züge immer gleich ausgehen. Ein König in der Mitte im Endspiel ist sonst oft ein guter Zug (Prod 26.11.: 24 Kopien enden mit
/// einem Königszug des Siegers in die Mitte, fast alle normale Endspielzüge) — ein „Engine sagt schlecht“ allein reicht daher
/// nicht. Das Matt durch Schachgebote ist der eindeutige Fall; ein Signalzug, der „nur“ Material einstellt, bleibt stehen.</para>
/// Rein, wirft nie.
/// </summary>
public static class SignalKingMove
{
    /// <summary>Die Felder des Signals.</summary>
    public static readonly IReadOnlySet<string> Centre = new HashSet<string>(StringComparer.Ordinal) { "d4", "e4", "d5", "e5" };

    /// <summary>Höchste Mattlänge (Züge des Verlierers) der Suche.</summary>
    public const int MaxMateMoves = 5;

    /// <summary>Deckel der Mattsuche (erzeugte Stellungen); darüber gilt „kein Matt“.</summary>
    public const int NodeBudget = 300_000;

    /// <param name="Ply">Der Halbzug (1-basiert) — immer der letzte.</param>
    /// <param name="San">Der entfernte Zug, z. B. <c>Ke4</c>.</param>
    /// <param name="MoveNumber">Zugnummer für die Anzeige („45.Ke4“ / „45...Ke4“).</param>
    /// <param name="MateIn">Matt in n für den Verlierer danach; <c>null</c> = patt.</param>
    public sealed record Finding(int Ply, string San, int MoveNumber, bool WhiteMoved, int? MateIn)
    {
        /// <summary>„45.Ke4“ / „45...Ke4“.</summary>
        public string Label => $"{MoveNumber}{(WhiteMoved ? "." : "...")}{San}";

        /// <summary>Kurzer Grund für Log und Bericht.</summary>
        public string Reason => MateIn is { } n ? $"danach Matt in {n} für den Verlierer" : "danach patt";
    }

    /// <summary>Ist der letzte Halbzug von <paramref name="sans"/> ein Signal-Königszug (Regel der Klassenbeschreibung)?
    /// <c>null</c> = nein (oder nicht nachspielbar).</summary>
    public static Finding? Detect(IReadOnlyList<string> sans, string? result, string? startFen = null)
    {
        if (sans.Count == 0 || result?.Trim() is not ("1-0" or "0-1") || sans[^1].TrimStart().FirstOrDefault() != 'K') return null;
        var winnerWhite = result.Trim() == "1-0";
        try
        {
            var board = string.IsNullOrWhiteSpace(startFen) ? new ChessBoard() : ChessBoard.LoadFromFen(startFen);
            for (var i = 0; i < sans.Count - 1; i++)
                if (!Play(board, sans[i])) return null;
            var moverWhite = board.Turn == PieceColor.White;
            if (moverWhite != winnerWhite) return null;
            var key = Key(sans[^1]);
            var last = board.Moves(generateSan: true).FirstOrDefault(m => Key(m.San) == key);
            if (last is null || last.Piece.Type != PieceType.King) return null;
            var uci = PgnParser.ToUci(last);
            if (!Centre.Contains(uci[2..4])) return null;
            var moveNumber = board.ToFen().Split(' ') is { Length: >= 6 } f && int.TryParse(f[5], out var n) ? n : (sans.Count + 1) / 2;
            var san = last.San?.TrimEnd('$') ?? sans[^1];
            if (!board.Move(last)) return null;
            if (TacticsBoard.TryParse(board.ToFen()) is not { } after) return null;
            var replies = after.LegalMoves();
            if (replies.Count == 0)
                return after.InCheck() ? null : new Finding(sans.Count, san, moveNumber, moverWhite, null);
            return ForcedMateByChecks(after, MaxMateMoves) is { } mate ? new Finding(sans.Count, san, moveNumber, moverWhite, mate) : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Die Züge ohne einen Signal-Königszug am Ende (<see cref="Detect"/>); sonst dieselbe Liste. Entfernt höchstens
    /// EINEN Halbzug.</summary>
    public static IReadOnlyList<string> Strip(IReadOnlyList<string> sans, string? result, out Finding? dropped, string? startFen = null)
    {
        dropped = Detect(sans, result, startFen);
        return dropped is null ? sans : sans.Take(sans.Count - 1).ToList();
    }

    /// <summary>Kürzestes erzwungenes Matt der Seite am Zug in höchstens <paramref name="maxMoves"/> Zügen, bei dem JEDER ihrer
    /// Züge Schach bietet (iterativ vertieft: erst Matt in 1, dann in 2 …). <c>null</c> = keins gefunden oder Deckel
    /// <see cref="NodeBudget"/> erreicht. Rochaden kennt das Brett nicht — ein Matt mit Rochade bleibt unentdeckt (sicher: dann
    /// bleibt der Zug stehen).</summary>
    public static int? ForcedMateByChecks(TacticsBoard board, int maxMoves)
    {
        var nodes = 0;
        for (var depth = 1; depth <= maxMoves; depth++)
        {
            var found = MatesIn(board, depth, ref nodes);
            if (found == true) return depth;
            if (found == null) return null;   // Deckel erreicht
        }
        return null;
    }

    /// <summary><c>true</c> = Matt in ≤ <paramref name="depth"/> mit Schachgeboten, <c>false</c> = nein, <c>null</c> = Deckel.</summary>
    private static bool? MatesIn(TacticsBoard b, int depth, ref int nodes)
    {
        foreach (var m in b.LegalMoves())
        {
            if (++nodes > NodeBudget) return null;
            var a = b.Apply(m);
            if (!a.InCheck()) continue;
            var replies = a.LegalMoves();
            if (replies.Count == 0) return true;
            if (depth == 1) continue;
            var all = true;
            foreach (var r in replies)
            {
                if (++nodes > NodeBudget) return null;
                var sub = MatesIn(a.Apply(r), depth - 1, ref nodes);
                if (sub == null) return null;
                if (sub == false) { all = false; break; }
            }
            if (all) return true;
        }
        return false;
    }

    /// <summary>Zug-Schlüssel; Gera.Chess hängt an einen Patt-Zug ein <c>$</c> an.</summary>
    private static string Key(string? san) => ScoresheetNotation.Key((san ?? string.Empty).TrimEnd('$'));

    private static bool Play(ChessBoard board, string san)
    {
        var key = Key(san);
        var move = board.Moves(generateSan: true).FirstOrDefault(m => Key(m.San) == key);
        return move != null && board.Move(move);
    }
}
