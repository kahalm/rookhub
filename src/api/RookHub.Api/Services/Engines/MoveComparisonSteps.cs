using Chess;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using static RookHub.Api.Services.Engines.MoveComparisonBoard;
using static RookHub.Api.Services.Engines.MoveComparisonEval;

namespace RookHub.Api.Services.Engines;

/// <summary>Reine Schritte der Zugvergleichs-Pumpe (aus <see cref="MoveComparisonService"/> herausgezogen, 2026-11-09):
/// Kandidaten nach Stärke ordnen, die schwächeren bestimmen und die Antwort-Zeilen gegen den besten planen.</summary>
internal static class MoveComparisonSteps
{
    /// <summary>Gerechnete Kandidaten, der beste (für die Seite am Zug) zuerst.</summary>
    internal static List<(MoveComparisonLine Line, double Score)> Ranked(MoveComparison c)
    {
        var moverWhite = FenFields.WhiteToMove(c.Fen);
        return c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Candidate && l.State == MoveComparisonLineState.Done)
            .Select(l => (Line: l, Ev: EvalOf(l.Fen, l.ResultJson)))
            .Where(x => x.Ev is not null)
            .Select(x => (x.Line, Score: moverWhite ? x.Ev!.Score : -x.Ev!.Score))
            .OrderByDescending(x => x.Score).ThenBy(x => x.Line.Ordinal)
            .ToList();
    }

    internal static IEnumerable<MoveComparisonLine> WeakerCandidates(MoveComparison c)
        => c.BestUci is null ? [] : c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Candidate
            && l.State == MoveComparisonLineState.Done && l.CandidateUci != c.BestUci);

    /// <summary>Kandidaten fertig: den besten bestimmen (<see cref="MoveComparison.BestUci"/>) und je schwächerem Kandidaten
    /// dessen beste Antworten als Zeilen nach dem besten Zug anlegen (offen, ohne Auftrag; unmöglich/anderer Zug/Matt gleich
    /// erledigt). <c>false</c> = kein Kandidat bewertbar, der Vergleich ist <see cref="MoveComparisonStatus.Failed"/>.</summary>
    internal static bool AddReplyLines(MoveComparison c)
    {
        var ranked = Ranked(c);
        if (ranked.Count == 0)
        {
            c.Status = MoveComparisonStatus.Failed;
            c.Error = "No candidate move could be calculated";
            return false;
        }
        var best = ranked[0].Line;
        c.BestUci = best.CandidateUci;
        foreach (var line in PlanReplies(best, ranked.Skip(1).Select(x => x.Line))) c.Lines.Add(line);
        return true;
    }

    /// <summary>Gibt es etwas zu begründen (einen gerechneten schwächeren Kandidaten)?</summary>
    internal static bool HasWeakerCandidates(MoveComparison c) => WeakerCandidates(c).Any();

    /// <summary>Die Auswahl prüfen (FEN, legale Züge, Rochade als König-schlägt-Turm, doppelte zählen einmal, Anzahl) und als
    /// UCI liefern; Absagen als <see cref="MoveComparisonException"/> mit Code.</summary>
    internal static List<string> CandidateUcis(string fen, IEnumerable<string>? moves)
    {
        if (fen.Length is 0 or > 120 || !AnalysisJobService.IsLegalFen(fen))
            throw new MoveComparisonException("invalid-fen", "Invalid FEN");
        var legal = ChessBoard.LoadFromFen(fen).Moves();
        if (legal.Length == 0) throw new MoveComparisonException("game-over", "No legal moves in this position");

        var ucis = new List<string>();
        foreach (var raw in moves ?? [])
        {
            var move = FindMove(legal, raw) ?? throw new MoveComparisonException("invalid-move", $"Not a legal move: {raw}");
            var uci = GamePlies.ToUci(move);
            if (!ucis.Contains(uci)) ucis.Add(uci);
        }
        if (ucis.Count < MoveComparisonService.MinCandidates) throw new MoveComparisonException("too-few-moves", $"At least {MoveComparisonService.MinCandidates} moves");
        if (ucis.Count > MoveComparisonService.MaxCandidates) throw new MoveComparisonException("too-many-moves", $"At most {MoveComparisonService.MaxCandidates} moves");
        return ucis;
    }

    /// <summary>Je Kandidat die Zeile mit der Stellung danach; setzt er matt/patt, ist nichts zu rechnen (<c>Done</c>).</summary>
    internal static List<MoveComparisonLine> PlanCandidates(string fen, IReadOnlyList<string> ucis)
    {
        var lines = new List<MoveComparisonLine>();
        for (var i = 0; i < ucis.Count; i++)
        {
            var after = PlayUci(fen, ucis[i])!;
            var line = new MoveComparisonLine { Kind = MoveComparisonLineKind.Candidate, CandidateUci = ucis[i], Ordinal = i, Fen = after };
            if (Terminal(after) is not null) line.State = MoveComparisonLineState.Done;   // setzt matt/patt — nichts zu rechnen
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>Je schwächerem Kandidaten dessen <see cref="MoveComparisonService.RepliesTested"/> beste Antworten als Zeilen
    /// nach dem besten Zug: <c>Illegal</c>, wenn sie dort nicht geht, <c>Skipped</c>, wenn es nicht derselbe Zug ist
    /// (<see cref="MoveComparisonBoard.ReplyTransfers"/>), <c>Done</c>, wenn die Stellung danach schon zu Ende ist.</summary>
    internal static List<MoveComparisonLine> PlanReplies(MoveComparisonLine best, IEnumerable<MoveComparisonLine> weaker)
    {
        var lines = new List<MoveComparisonLine>();
        var bestOver = Terminal(best.Fen) is not null;
        foreach (var weak in weaker)
        {
            var replies = Pvs(weak.Fen, weak.ResultJson).Take(MoveComparisonService.RepliesTested).ToList();
            for (var k = 0; k < replies.Count; k++)
            {
                var line = new MoveComparisonLine
                {
                    Kind = MoveComparisonLineKind.Reply, CandidateUci = weak.CandidateUci, ReplyUci = replies[k].Uci, Ordinal = k,
                };
                var after = bestOver ? null : PlayUci(best.Fen, replies[k].Uci);
                if (after is null) line.State = MoveComparisonLineState.Illegal;
                else if (!ReplyTransfers(weak.Fen, best.Fen, replies[k].Uci)) line.State = MoveComparisonLineState.Skipped;
                else
                {
                    line.Fen = after;
                    if (Terminal(after) is not null) line.State = MoveComparisonLineState.Done;
                }
                lines.Add(line);
            }
        }
        return lines;
    }
}
