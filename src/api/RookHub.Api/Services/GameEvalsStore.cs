using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Liest, was die Partieseiten aus einer <see cref="GameAnalysis"/> brauchen: die Bewertungen für Kurve, Genauigkeit und
/// Zug-Klassen (<see cref="GameEvalsDto"/>) und den Stand für eine Partienliste (<see cref="SavedGameAnalysisDto"/>).
/// Geteilt von den gespeicherten Partien (<see cref="SavedGameService"/>) und den Vereinspartien von LeagueHub
/// (<c>LeagueClubService</c>, 0.593.0) — WELCHE Analyse zu einer Partie gehört und wer sie sehen darf, entscheidet der
/// Aufrufer; hier steht nur, wie sie gelesen wird.
/// </summary>
public static class GameEvalsStore
{
    /// <summary>Kopf einer Analyse ohne Stellungen — genug, um die Bewertungen zu lesen.</summary>
    public sealed record Head(int Id, GameAnalysisStatus Status, int PlyCount, int TargetDepth,
        int? RefineDepth, DateTime? RefinedAt);

    /// <summary>Projektion auf <see cref="Head"/> (übersetzt sich nach SQL, lädt weder PGN noch Stellungen).</summary>
    public static IQueryable<Head> Heads(IQueryable<GameAnalysis> analyses) =>
        analyses.Select(a => new Head(a.Id, a.Status, a.PlyCount, a.TargetDepth, a.RefineDepth, a.RefinedAt));

    /// <summary>
    /// Die Bewertungen einer Analyse, alles in WEISS-Sicht. <paramref name="bookPlies"/> bekommt die Stellungen NACH
    /// jedem Halbzug (Zeile p+1 = nach Halbzug p) und liefert die Buchzüge des Betrachters; <c>null</c> = keine (anonym,
    /// oder ein Aufrufer, der keine Repertoires kennt).
    /// </summary>
    public static async Task<GameEvalsDto> ReadAsync(AppDbContext db, Head analysis,
        Func<List<string>, Task<List<int>>>? bookPlies, CancellationToken ct = default)
    {
        var rows = await db.GameAnalysisPositions.AsNoTracking()
            .Where(p => p.GameAnalysisId == analysis.Id && p.CandidatesJson != null)
            .OrderBy(p => p.Ply)
            .Select(p => new { p.Ply, p.Fen, p.GameMoveUci, p.CandidatesJson, p.Depth, p.AnalyzedAt, p.Refined, p.NodeStepsJson })
            .ToListAsync(ct);
        var plies = rows
            .Select(r =>
            {
                var ply = GameEvals.PlyOf(r.Ply, r.Fen, r.GameMoveUci, r.CandidatesJson, r.Depth);
                // Knotenanalyse (Lc0): wie weit gerechnet wurde — die letzte Stufe trägt die erreichten Knoten (0.684.0).
                if (ply is not null && r.NodeStepsJson is not null && NodeSteps.Parse(r.NodeStepsJson) is { Count: > 0 } steps)
                    ply.Nodes = steps[^1].Nodes;
                return ply;
            })
            .OfType<GameEvalPlyDto>()
            .ToList();
        var running = analysis.Status is GameAnalysisStatus.Pending or GameAnalysisStatus.Running;

        var book = new List<int>();
        if (bookPlies is not null)
        {
            var fens = await db.GameAnalysisPositions.AsNoTracking()
                .Where(p => p.GameAnalysisId == analysis.Id)
                .OrderBy(p => p.Ply)
                .Select(p => p.Fen)
                .ToListAsync(ct);
            // Zeile p+1 ist die Stellung NACH Halbzug p; für den letzten Halbzug gibt es keine (Buch endet vorher).
            book = await bookPlies(fens.Skip(1).ToList());
        }

        return new GameEvalsDto
        {
            Status = analysis.Status.ToString().ToLowerInvariant(),
            Analyzed = rows.Count,
            Total = analysis.PlyCount,
            TargetDepth = analysis.TargetDepth,
            AnalysisId = analysis.Id,
            Plies = plies,
            Final = GameEvals.FinalOf(plies.LastOrDefault(), analysis.PlyCount),
            BookPlies = book,
            Refining = analysis.Status == GameAnalysisStatus.Done && analysis.RefineDepth != null && analysis.RefinedAt == null,
            Refined = analysis.RefineDepth != null ? rows.Count(r => r.Refined) : 0,
            EtaMinutes = running
                ? GameEvals.EtaMinutes(
                    rows.Where(r => r.AnalyzedAt != null).Select(r => r.AnalyzedAt!.Value),
                    analysis.PlyCount - rows.Count, DateTime.UtcNow)
                : null,
        };
    }

    /// <summary>
    /// Stand mehrerer Analysen für eine Liste, nach <see cref="GameAnalysis.Id"/>: Status, Fortschritt (EINE gruppierte
    /// Zählung über alle Ids statt einer Abfrage je Partie) und die abgelegte Genauigkeit. Eine FERTIGE Analyse ohne
    /// Genauigkeit (von vor 0.515.0) wird hier nachgerechnet und geschrieben — höchstens <paramref name="backfillPerCall"/>
    /// je Aufruf, best-effort, die Anzeige stimmt auch ohne den Nachtrag. Unbekannte Ids fehlen im Ergebnis.
    /// </summary>
    public static async Task<Dictionary<int, SavedGameAnalysisDto>> StatesAsync(AppDbContext db, List<int> ids,
        int backfillPerCall, CancellationToken ct = default)
    {
        var result = new Dictionary<int, SavedGameAnalysisDto>();
        if (ids.Count == 0) return result;

        var heads = await db.GameAnalyses.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.Status, a.PlyCount, a.AccuracyWhite, a.AccuracyBlack })
            .ToListAsync(ct);
        var analyzed = await db.GameAnalysisPositions.AsNoTracking()
            .Where(p => ids.Contains(p.GameAnalysisId) && p.CandidatesJson != null)
            .GroupBy(p => p.GameAnalysisId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var backfilled = 0;
        foreach (var h in heads)
        {
            var state = new SavedGameAnalysisDto
            {
                Status = h.Status.ToString().ToLowerInvariant(),
                Analyzed = analyzed.TryGetValue(h.Id, out var n) ? n : 0,
                Total = h.PlyCount,
                AccuracyWhite = h.AccuracyWhite,
                AccuracyBlack = h.AccuracyBlack,
            };
            if (h.Status == GameAnalysisStatus.Done && h.AccuracyWhite is null && h.AccuracyBlack is null
                && backfilled < backfillPerCall)
            {
                backfilled++;
                var positions = await db.GameAnalysisPositions.AsNoTracking()
                    .Where(p => p.GameAnalysisId == h.Id).ToListAsync(ct);
                var accuracy = GameAccuracy.FromPositions(positions, h.PlyCount);
                state.AccuracyWhite = accuracy.White;
                state.AccuracyBlack = accuracy.Black;
                try
                {
                    var tracked = await db.GameAnalyses.FirstOrDefaultAsync(a => a.Id == h.Id, ct);
                    if (tracked is not null)
                    {
                        tracked.AccuracyWhite = accuracy.White;
                        tracked.AccuracyBlack = accuracy.Black;
                        await db.SaveChangesAsync(ct);
                    }
                }
                catch (DbUpdateException) { /* Anzeige stimmt auch ohne den Nachtrag */ }
            }
            result[h.Id] = state;
        }
        return result;
    }
}
