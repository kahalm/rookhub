using RookHub.Api.Services.Games;

namespace RookHub.Api.Services.Puzzles;

/// <summary>
/// Matt als DROHUNG hinter einem Zug (0.976.0, Empfohlene Übungen; Befund an Vereinspartie 251: 37.Sd6 gabelt die Türme, „auf
/// 37...Tg8 folgt 38.Sf7#“ — das Mattbild stand in keiner Hauptlinie, also gab es dazu keine Übung). Rein, ohne Engine, über
/// <see cref="TacticsBoard"/>; gesucht wird nur Matt in EINEM Zug (tiefere Mattdrohungen liefern die gerechneten Zusatzlinien der
/// Fehler-Vergleiche, <see cref="GameExercises.SideLinesOf"/>).
///
/// <para>Zwei Fälle, in dieser Reihenfolge:</para>
/// <list type="number">
/// <item><b>Der Zug droht Matt</b>: er schlägt nichts und gibt kein Schach, und dürfte die Seite noch einmal ziehen
/// (<see cref="TacticsBoard.Pass"/>), setzte sie in einem Zug matt. Ein Schlagzug zählt hier nicht: „zurückschlagen, sonst Matt“
/// ist ein Abtausch, keine Drohung.</item>
/// <item><b>Eine naheliegende Antwort läuft ins Matt</b> (nur, wenn der Zug selbst nichts droht — sonst wäre jede beliebige
/// Antwort „naheliegend“): steht der Gegner im Schach, jede legale Antwort außer der der Engine-Linie; sonst das Schlagen der
/// gezogenen Figur und die Züge der Figuren, die sie angreift und die das etwas angeht (wertvoller als sie oder ungedeckt) —
/// wegziehen, sich dazwischenstellen, sich einmauern wie 37...Tg8.</item>
/// </list>
/// </summary>
public static class MateThreats
{
    /// <param name="ReplyUci">Die Antwort, die ins Matt läuft; <c>null</c> = der Zug droht das Matt von sich aus.</param>
    /// <param name="MateUci">Der Mattzug.</param>
    /// <param name="End">Die Mattstellung (der Mattgesetzte am Zug) — Grundlage des Mattbilds.</param>
    public sealed record Threat(string? ReplyUci, string MateUci, TacticsBoard End);

    /// <summary>Die Mattdrohung hinter <paramref name="uci"/> in <paramref name="fen"/>; <paramref name="lineReply"/> = die Antwort
    /// der Engine-Linie (sie läuft nicht ins Matt, sonst stünde die Linie auf Matt). <c>null</c> = keine. Wirft nie.</summary>
    public static Threat? After(string fen, string uci, string? lineReply = null)
    {
        try { return TacticsBoard.TryParse(fen) is { } b ? Core(b, uci, lineReply) : null; }
        catch (Exception) { return null; }
    }

    private static Threat? Core(TacticsBoard b0, string uci, string? lineReply)
    {
        if (uci.Length < 4 || TacticsBoard.SquareIndex(uci[..2]) is not int from || TacticsBoard.SquareIndex(uci[2..4]) is not int to) return null;
        if (b0.At(from) is not char moved || TacticsBoard.IsWhite(moved) != b0.WhiteToMove) return null;
        var captures = b0.At(to) is char target && TacticsBoard.IsWhite(target) != b0.WhiteToMove;
        if (b0.Apply(uci) is not { } b1 || MatePattern.IsMate(b1)) return null;
        var check = b1.InCheck();

        if (!check && FirstMate(b1.Pass()) is { } threat)
            return captures ? null : new Threat(null, threat.Uci, threat.End);

        // Feste Reihenfolge: erst das Schlagen der gezogenen Figur, dann nach Feldern.
        foreach (var reply in Natural(b0, b1, to, check).Where(m => Uci(m) != lineReply)
                     .OrderBy(m => m.To == to ? 0 : 1).ThenBy(m => m.From).ThenBy(m => m.To).ThenBy(m => m.Promotion ?? ' '))
            if (FirstMate(b1.Apply(reply)) is { } mate)
                return new Threat(Uci(reply), mate.Uci, mate.End);
        return null;
    }

    /// <summary>Die naheliegenden Antworten auf den Zug nach <paramref name="to"/> (Fall 2 der Klassenbeschreibung).</summary>
    private static IEnumerable<TacticsBoard.Move> Natural(TacticsBoard b0, TacticsBoard b1, int to, bool check)
    {
        var legal = b1.LegalMoves();
        if (check) return legal;
        // Die gezogene Figur steht auf `to` (bei einer Rochade nicht — dann gibt es hier nichts „Naheliegendes“).
        if (b1.At(to) is not char piece || TacticsBoard.IsWhite(piece) != b0.WhiteToMove) return [];
        var g1 = TacticMotifs.Grid(b1);
        var hit = TacticMotifs.AttackedBy(g1, to).Where(t => g1[t] != '\0' && char.IsUpper(g1[t]) != b0.WhiteToMove
            && char.ToUpperInvariant(g1[t]) != 'K'
            && (TacticMotifs.Value(g1[t]) > TacticMotifs.Value(piece) || MatePattern.Attackers(g1, t, !b0.WhiteToMove).Count == 0)).ToHashSet();
        return legal.Where(m => m.To == to || hit.Contains(m.From));
    }

    /// <summary>Ist <paramref name="replyUci"/> eine naheliegende Antwort auf <paramref name="uci"/> in <paramref name="fen"/>
    /// (Fall 2 der Klassenbeschreibung)? Für die Antwort-Tests der Fehler-Vergleiche: dort steht die Antwort auf den FEHLZUG, gespielt
    /// nach dem besten Zug — das kann auch ein Zug sein, den nach dem besten Zug niemand erwägt. Wirft nie.</summary>
    public static bool IsNaturalReply(string fen, string uci, string? replyUci)
    {
        try
        {
            if (string.IsNullOrEmpty(replyUci) || uci.Length < 4 || TacticsBoard.SquareIndex(uci[2..4]) is not int to
                || TacticsBoard.TryParse(fen) is not { } b0 || b0.Apply(uci) is not { } b1) return false;
            return Natural(b0, b1, to, b1.InCheck()).Any(m => Uci(m) == replyUci.ToLowerInvariant());
        }
        catch (Exception) { return false; }
    }

    /// <summary>Der erste Mattzug der Seite am Zug (feste Reihenfolge: Startfeld, Zielfeld, Umwandlungsfigur) samt Mattstellung.</summary>
    private static (string Uci, TacticsBoard End)? FirstMate(TacticsBoard b)
    {
        foreach (var m in b.LegalMoves().OrderBy(m => m.From).ThenBy(m => m.To).ThenBy(m => m.Promotion ?? ' '))
        {
            var after = b.Apply(m);
            if (MatePattern.IsMate(after)) return (Uci(m), after);
        }
        return null;
    }

    private static string Uci(TacticsBoard.Move m) =>
        TacticsBoard.SquareName(m.From) + TacticsBoard.SquareName(m.To) + (m.Promotion is char p ? char.ToLowerInvariant(p).ToString() : "");
}
