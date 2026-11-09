using System.Globalization;
using Chess;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Services.Engines;

/// <summary>Engine-Ergebnisse des Zugvergleichs lesen und zeigen (aus <see cref="MoveComparisonService"/> herausgezogen,
/// 2026-11-09): Linien (<see cref="Pv"/>), Bewertung (<see cref="Ev"/>), Matt/Patt auf dem Brett, Anzeige-Texte aus Sicht des Lesers.</summary>
internal static class MoveComparisonEval
{
    private const double MateScore = 100_000;

    /// <summary>Eine Linie der Engine: erster Zug (UCI + SAN), Bewertung aus WEISS-Sicht, Linie als SAN.</summary>
    internal sealed record Pv(string Uci, string San, int? CpWhite, int? MateWhite, IReadOnlyList<string> Line)
    {
        public string MoverEval(bool moverWhite) => GameMistakes.EvalText(CpWhite, MateWhite, moverWhite);
    }

    /// <summary>Bewertung einer Stellung aus Weiß-Sicht: Rangwert (Matt ± <see cref="MateScore"/>) und die Rohwerte;
    /// angezeigt wird sie über <see cref="ShownText(Ev, bool)"/> aus Sicht der Seite am Zug.</summary>
    internal sealed record Ev(double Score, int? CpWhite, int? MateWhite, bool Checkmate);

    internal static List<Pv> Pvs(string fen, string? resultJson)
    {
        var result = new List<Pv>();
        if (string.IsNullOrEmpty(fen) || BrokerCandidates.Parse(resultJson, fen) is not { } cands) return result;
        var sign = FenFields.WhiteToMove(fen) ? 1 : -1;   // Parse liefert die Sicht der Seite am Zug — zurück nach Weiß
        foreach (var cand in cands)
        {
            var line = GameMistakes.LineSans(fen, cand.Pv is { Count: > 0 } pv ? pv : [cand.Uci], MoveComparisonService.LinePlies);
            if (line.Count == 0) continue;
            result.Add(new Pv(cand.Uci, line[0], cand.Cp * sign, cand.Mate * sign, line));
        }
        return result;
    }

    internal static Ev? EvalOf(string fen, string? resultJson)
    {
        if (Terminal(fen) is { } t) return t;
        var pv = Pvs(fen, resultJson).FirstOrDefault();
        if (pv is null) return null;
        if (pv.MateWhite is int m)
            return new Ev(m > 0 ? MateScore - m : -MateScore - m, null, m, false);
        var cp = pv.CpWhite ?? 0;
        return new Ev(cp, cp, null, false);
    }

    /// <summary>Matt oder Patt auf dem Brett (keine legalen Züge) — dann gibt es nichts zu rechnen.</summary>
    internal static Ev? Terminal(string fen)
    {
        if (string.IsNullOrEmpty(fen)) return null;
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            if (board.Moves().Length > 0) return null;
            var white = FenFields.WhiteToMove(fen);
            var mated = white ? board.WhiteKingChecked : board.BlackKingChecked;
            if (!mated) return new Ev(0, 0, null, false);
            return white ? new Ev(-MateScore, null, null, true) : new Ev(MateScore, null, null, true);
        }
        catch { return null; }
    }

    /// <summary>Aus Sicht des Lesers, wie „Warum war das ein Fehler?": „+1.35", „mate in 3", „gets mated in 2", „mate".</summary>
    internal static string MoverText(Ev e, bool moverWhite)
    {
        if (e.Checkmate) return (e.Score > 0) == moverWhite ? "mate" : "gets mated in 0";
        return GameMistakes.EvalText(e.CpWhite, e.MateWhite, moverWhite);
    }

    // d3-rk-vergleich-vorzeichen-widerspruch: die ganze Seite zeigt die Bewertung aus Sicht der Seite am Zug der
    // Ausgangsstellung — dieselbe Sicht wie die Begründung des Sprachmodells (MoverText). Vorher stand dort Weiß-Sicht,
    // mit Schwarz am Zug also „-4.08" am besten Zug neben „+4,1" im Erklärtext.

    /// <summary>Anzeige-Text aus Sicht des Lesers (Seite am Zug): „+4.08", „#3" (er setzt matt), „#-2" (er wird matt).</summary>
    internal static string? ShownText(Ev? e, bool moverWhite)
    {
        if (e is null) return null;
        if (e.Checkmate) return (e.Score > 0) == moverWhite ? "#0" : "#-0";
        return Plain(e.CpWhite, e.MateWhite, moverWhite);
    }

    internal static string PvText(Pv p, bool moverWhite) => Plain(p.CpWhite, p.MateWhite, moverWhite);

    private static string Plain(int? cpWhite, int? mateWhite, bool moverWhite)
    {
        var sign = moverWhite ? 1 : -1;
        if (mateWhite is int m) return "#" + (m * sign).ToString(CultureInfo.InvariantCulture);
        var v = (cpWhite ?? 0) * sign / 100.0;
        return (v > 0 ? "+" : "") + v.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
