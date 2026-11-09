using RookHub.Api.DTOs.Engines;
using RookHub.Api.Models;
using static RookHub.Api.Services.Engines.MoveComparisonBoard;
using static RookHub.Api.Services.Engines.MoveComparisonEval;

namespace RookHub.Api.Services.Engines;

/// <summary>Ein Zugvergleich als DTO für die Seite (aus <see cref="MoveComparisonService"/> herausgezogen, 2026-11-09):
/// Kandidaten mit Bewertung, besten Antworten und Tests, geordnet nach Stärke; Status-Texte.</summary>
internal static class MoveComparisonView
{
    /// <param name="jobs">Zwischenstand der noch laufenden Aufträge (Id → Ergebnis, erreichte Tiefe) — solange gerechnet
    /// wird, zeigt die Seite ihn statt des gespeicherten Ergebnisses.</param>
    internal static MoveComparisonDto Build(MoveComparison c, IReadOnlyDictionary<int, (string? Json, int Depth)> jobs, bool explanationsAvailable)
    {
        (string? Json, int Depth) Current(MoveComparisonLine l)
            => l.AnalysisJobId is int id && jobs.TryGetValue(id, out var j) ? j : (l.ResultJson, l.ReachedDepth);

        var moverWhite = FenFields.WhiteToMove(c.Fen);
        var best = c.Lines.FirstOrDefault(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == c.BestUci);
        var candidates = c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Candidate)
            .Select(l =>
            {
                var (json, depth) = Current(l);
                var ev = EvalOf(l.Fen, json);
                var replies = Pvs(l.Fen, json);
                var tests = c.Lines.Where(r => r.Kind == MoveComparisonLineKind.Reply && r.CandidateUci == l.CandidateUci
                        && r.State != MoveComparisonLineState.Skipped)   // ein anderer Zug — nicht zeigen (ReplyTransfers)
                    .OrderBy(r => r.Ordinal)
                    .Select(r =>
                    {
                        var (rj, rd) = Current(r);
                        var san = replies.FirstOrDefault(p => p.Uci == r.ReplyUci)?.San ?? r.ReplyUci ?? "?";
                        var answer = r.State == MoveComparisonLineState.Illegal ? [] : Pvs(r.Fen, rj).FirstOrDefault()?.Line ?? [];
                        return new MoveComparisonTestDto(r.ReplyUci ?? "", san, StateText(r.State), rd,
                            r.State == MoveComparisonLineState.Illegal ? null : ShownText(EvalOf(r.Fen, rj), moverWhite), answer);
                    })
                    .ToList();
                var explanation = l.Explanation is null ? null : PieceLetters.Convert(l.Explanation, "en", c.Language);
                return (Dto: new MoveComparisonCandidateDto(l.CandidateUci, SanOf(c.Fen, l.CandidateUci) ?? l.CandidateUci,
                        StateText(l.State), depth, ShownText(ev, moverWhite), l.CandidateUci == c.BestUci,
                        replies.Select(p => new MoveComparisonReplyDto(p.Uci, p.San, PvText(p, moverWhite), p.Line)).ToList(), tests, explanation),
                    Score: ev is null ? double.MinValue : moverWhite ? ev.Score : -ev.Score, l.Ordinal, Ranked: l.State == MoveComparisonLineState.Done);
            })
            .ToList();
        // Erst gerechnet, dann nach Stärke — solange noch gerechnet wird, bleibt die Reihenfolge der Auswahl (sonst
        // sprängen die Zeilen mit jedem Zwischenstand).
        var ordered = c.BestUci is null
            ? candidates.OrderBy(x => x.Ordinal)
            : candidates.OrderByDescending(x => x.Ranked).ThenByDescending(x => x.Score).ThenBy(x => x.Ordinal);
        return new MoveComparisonDto(c.Id, c.Fen, c.Title, c.Depth, StatusText(c.Status), c.Error, moverWhite, c.BestUci,
            c.Language, explanationsAvailable, c.Lines.Count(l => l.State == MoveComparisonLineState.Pending), c.Lines.Count,
            c.CreatedAt, c.FinishedAt, ordered.Select(x => x.Dto).ToList(), SourceText(c.Source));
    }

    internal static string StatusText(MoveComparisonStatus s) => s switch
    {
        MoveComparisonStatus.Candidates => "candidates",
        MoveComparisonStatus.Replies => "replies",
        MoveComparisonStatus.Explaining => "explaining",
        MoveComparisonStatus.Done => "done",
        MoveComparisonStatus.Waiting => "waiting",
        _ => "failed",
    };

    internal static string SourceText(MoveComparisonSource? s) => s switch
    {
        MoveComparisonSource.Analysis => "analysis",
        MoveComparisonSource.Mixed => "mixed",
        _ => "engine",
    };

    internal static string StateText(MoveComparisonLineState s) => s switch
    {
        MoveComparisonLineState.Pending => "pending",
        MoveComparisonLineState.Done => "done",
        MoveComparisonLineState.Illegal => "illegal",
        _ => "failed",
    };
}
