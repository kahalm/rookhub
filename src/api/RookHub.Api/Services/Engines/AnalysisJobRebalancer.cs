using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Services.Engines;

/// <summary>
/// Freie Engines übernehmen wartende Aufträge (0.920.0, Wunsch 2026-11-09). Ein Auftrag bekommt seine Engine beim
/// Anlegen fest zugeteilt (<see cref="AnalysisJobService.CreateAsync"/>); kamen danach Engines dazu oder wurden welche
/// frei, saßen sie leer, während auf drei anderen 20 Aufträge je Engine warteten (09.11.: vier neue Server-Engines,
/// 67 Aufträge auf „Gross Hintergrund 1–3").
///
/// <list type="bullet">
/// <item><b>Wer gibt ab</b>: ein wartender Auftrag (Queued/Paused, fällig), dessen Engine ihn jetzt nicht rechnen kann —
/// sie rechnet gerade etwas anderes, ist aus oder hat für diese Art Arbeit gerade Sperrzeit.</item>
/// <item><b>Wer nimmt</b>: eine Engine aus derselben Hintergrund-Liste des Besitzers, die frei ist, nichts Eigenes
/// wartend hat und diese Art Arbeit jetzt nehmen darf (<see cref="EngineAvailability.UsableAsync"/>).</item>
/// <item><b>Was zuerst</b>: dieselbe Rangfolge wie <see cref="AnalysisJobService.PickNextForEngineAsync"/>
/// (<see cref="AnalysisJob.Priority"/>, Hintergrund, Stapel, Alter).</item>
/// <item><b>Was nie</b>: Aufträge auf einer Engine, die NICHT in der Liste steht (von Hand gewählt), und
/// Nur-auf-Anforderung-Engines (Lc0) — weder als Geber noch als Nehmer.</item>
/// </list>
/// </summary>
public static class AnalysisJobRebalancer
{
    /// <summary>Hängt Aufträge auf freie Engines um und speichert. → Zahl umgehängt.</summary>
    public static async Task<int> RebalanceAsync(AppDbContext db, DateTime now, Func<string, bool> isBusy,
        IReadOnlySet<string> explicitOnly, QuietHours? quiet, ILogger? logger = null, CancellationToken ct = default)
    {
        var waiting = await db.AnalysisJobs
            .Where(j => (j.Status == AnalysisJobStatus.Queued || j.Status == AnalysisJobStatus.Paused)
                && (j.NextAttemptAt == null || j.NextAttemptAt <= now))
            .OrderBy(j => j.Priority).ThenBy(j => j.Background).ThenBy(j => j.Batch).ThenBy(j => j.CreatedAt)
            .ToListAsync(ct);
        if (waiting.Count == 0) return 0;

        var nowUtc = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc));
        var moved = 0;
        foreach (var group in waiting.GroupBy(j => j.EngineOwnerUserId ?? j.UserId))
        {
            var cred = await db.LichessEngineCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == group.Key, ct);
            if (cred is null) continue;
            var list = ExplicitOnlyEngines.Automatic(cred.BackgroundEngines, explicitOnly);
            if (list.Count < 2) continue;
            var inList = list.ToHashSet(StringComparer.Ordinal);
            var forAll = (await EngineAvailability.UsableAsync(db, group.Key, list, batch: false, quiet, nowUtc, ct)).ToHashSet(StringComparer.Ordinal);
            var forBatch = (await EngineAvailability.UsableAsync(db, group.Key, list, batch: true, quiet, nowUtc, ct)).ToHashSet(StringComparer.Ordinal);
            bool CanRun(string engine, AnalysisJob j) => j.Batch ? forBatch.Contains(engine) : forAll.Contains(engine);

            var jobs = group.ToList();
            var withWork = jobs.Select(j => j.EngineId).ToHashSet(StringComparer.Ordinal);
            foreach (var engine in list.Where(e => forAll.Contains(e) && !isBusy(e) && !withWork.Contains(e)))
            {
                var job = jobs.FirstOrDefault(j => j.EngineId != engine && inList.Contains(j.EngineId)
                    && (isBusy(j.EngineId) || !CanRun(j.EngineId, j)) && CanRun(engine, j));
                if (job is null) continue;
                logger?.LogInformation("AnalysisJob {Id}: von {From} auf freie Engine {To} umverteilt", job.Id, job.EngineId, engine);
                job.EngineId = engine;
                job.UpdatedAt = now;
                jobs.Remove(job);
                withWork.Add(engine);
                moved++;
            }
        }
        if (moved > 0) await db.SaveChangesAsync(ct);
        return moved;
    }
}
