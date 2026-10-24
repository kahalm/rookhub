using Microsoft.EntityFrameworkCore;

namespace RookHub.Api.Services.Accounts.Erasure;

/// <summary>Kontolöschung, Bereich Analyse: Aufträge, Verlauf, Zugvergleiche, Kalk-Bäume, Punktepartien, Partie-Analysen.</summary>
internal static class AnalysisDataEraser
{
    /// <summary>Hintergrund-Aufträge, Analyse-Verlauf, Zugvergleiche.</summary>
    internal static async Task JobsAndHistoryAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // Hintergrund-Analyseaufträge tragen Stellungen samt selbst gewählten Titeln (PII-nah) und
        // würden nach der Löschung weiter Rechenzeit auf der Engine des Kontos verbrauchen.
        db.AnalysisJobs.RemoveRange(await db.AnalysisJobs.Where(x => x.UserId == userId).ToListAsync());
        // Analyse-Verlauf (0.603.0): Stellungen und Zugfolgen des Nutzers.
        db.AnalysisHistoryEntries.RemoveRange(await db.AnalysisHistoryEntries.Where(h => h.UserId == userId).ToListAsync());
        // Zugvergleiche (0.602.0): die Zeilen cascaden am Kopf; ihre Aufträge stehen in AnalysisJobs und gehen oben mit.
        // Unter InMemory cascadet nichts — die Zeilen deshalb ausdrücklich.
        var comparisons = await db.MoveComparisons.Include(c => c.Lines).Where(c => c.UserId == userId).ToListAsync();
        db.MoveComparisonLines.RemoveRange(comparisons.SelectMany(c => c.Lines));
        db.MoveComparisons.RemoveRange(comparisons);
    }

    /// <summary>Kalk-Bäume, Punktepartien (vor den Analysen — Restrict), Partie-Analysen.</summary>
    internal static async Task CalculationAndAnalysesAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // Eigene Analysebäume des Kalkulations-Modus: Nutzerarbeit mit Freitext, kein Aggregat.
        db.CalculationTrees.RemoveRange(await db.CalculationTrees.Where(x => x.UserId == userId).ToListAsync());
        // Punktepartie-Durchläufe samt geratenen Zügen: reine Nutzerarbeit an konkreten Partien.
        // MUSS vor den Partie-Analysen stehen — die Sitzung zeigt per Restrict auf die Analyse.
        var guessSessions = await db.GuessSessions.Where(g => g.UserId == userId).Select(g => g.Id).ToListAsync();
        if (guessSessions.Count > 0)
        {
            db.GuessMoves.RemoveRange(
                await db.GuessMoves.Where(m => guessSessions.Contains(m.GuessSessionId)).ToListAsync());
            db.GuessSessions.RemoveRange(await db.GuessSessions.Where(g => g.UserId == userId).ToListAsync());
        }
        // Partie-Analysen samt ihrer Stellungen: PGN und Kandidatenlisten sind Nutzerarbeit an
        // konkreten Partien (oft eigenen) — keine anonyme Statistik. Die Stellungen hängen per
        // Cascade daran, werden hier aber explizit mitgenommen (InMemory cascadet nicht).
        var gameAnalysisIds = await db.GameAnalyses.Where(g => g.UserId == userId).Select(g => g.Id).ToListAsync();
        if (gameAnalysisIds.Count > 0)
        {
            db.GameAnalysisPositions.RemoveRange(
                await db.GameAnalysisPositions.Where(p => gameAnalysisIds.Contains(p.GameAnalysisId)).ToListAsync());
            db.GameAnalyses.RemoveRange(await db.GameAnalyses.Where(g => g.UserId == userId).ToListAsync());
        }
    }
}
