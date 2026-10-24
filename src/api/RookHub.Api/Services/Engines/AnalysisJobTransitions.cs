using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Engines;

/// <summary>Schreibt einen Statusübergang eines Hintergrund-Auftrags in die Datenbank (aus <see cref="AnalysisJobWorker"/>
/// gezogen, Zerlegung Z11). Welcher Übergang gilt, entscheiden <see cref="AnalysisJobRunEnd"/> und
/// <see cref="BrokerFailurePolicy"/>.</summary>
internal static class AnalysisJobTransitions
{
    /// <summary>Einen Übergang (<see cref="RunOutcome"/>) anwenden: erst der Fehlversuchs-Zähler, dann der Status —
    /// fertig (laufender Stand auf 0), pausiert (<see cref="PauseAsync"/>) oder gescheitert (<see cref="FailAsync"/>).</summary>
    internal static async Task ApplyAsync(AppDbContext db, AnalysisJob job, RunOutcome outcome)
    {
        if (outcome.Fruitless == FruitlessChange.Reset) job.FruitlessAttempts = 0;
        else if (outcome.Fruitless == FruitlessChange.Increment) job.FruitlessAttempts++;
        switch (outcome.Kind)
        {
            case RunEndKind.Done:
                job.Status = AnalysisJobStatus.Done; job.FinishedAt = DateTime.UtcNow; job.UpdatedAt = job.FinishedAt.Value;
                job.CurrentDepth = 0; job.CurrentNps = 0;   // es rechnet nichts mehr
                await db.SaveChangesAsync(CancellationToken.None);
                break;
            case RunEndKind.Paused:
                await PauseAsync(db, job, outcome.Error, outcome.Backoff);
                break;
            default:
                await FailAsync(db, job, outcome.Error!);
                break;
        }
    }

    internal static async Task PauseAsync(AppDbContext db, AnalysisJob job, string? error, TimeSpan? backoff = null)
    {
        job.Status = AnalysisJobStatus.Paused;
        job.CurrentDepth = 0; job.CurrentNps = 0;   // der laufende Stand gehört zum Lauf, nicht zum Auftrag
        job.LastError = error;
        job.NextAttemptAt = backoff is null ? null : DateTime.UtcNow + backoff;
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    internal static async Task FailAsync(AppDbContext db, AnalysisJob job, string error)
    {
        job.Status = AnalysisJobStatus.Failed;
        job.LastError = error;
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
