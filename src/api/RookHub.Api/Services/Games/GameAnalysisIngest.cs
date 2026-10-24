using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.EngineBroker;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Library;
using RookHub.Api.DTOs.Games;
using RookHub.Api.DTOs.Engines;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Übernimmt fertige Stellungs-Aufträge in die Stellungen einer Partie-Analyse — erster Durchgang und Vertiefung
/// (Teil der Pumpe von <see cref="GameAnalysisService"/>, Zerlegung Z5). Übernommene Aufträge werden gelöscht,
/// gescheiterte zählen gegen <see cref="GameAnalysisDefaults.MaxPositionAttempts"/>.
/// </summary>
internal sealed class GameAnalysisIngest
{
    private readonly AppDbContext _db;
    private readonly AnalysisJobService _jobs;
    private readonly ILogger _logger;

    internal GameAnalysisIngest(AppDbContext db, AnalysisJobService jobs, ILogger logger)
    {
        _db = db;
        _jobs = jobs;
        _logger = logger;
    }

    /// <summary>Fertige Aufträge in die Stellungen kopieren.</summary>
    internal async Task<bool> IngestFinishedAsync(GameAnalysis analysis, CancellationToken ct)
    {
        // Offene Auftraege BEIDER Durchgaenge: eine Stellung mit Kandidaten UND Auftrag wird gerade vertieft.
        var pending = analysis.Positions.Where(p => p.AnalysisJobId != null).ToList();
        if (pending.Count == 0) return false;

        var jobIds = pending.Select(p => p.AnalysisJobId!.Value).ToList();
        // Getrackt laden: uebernommene Auftraege werden gleich mitgeloescht (siehe unten).
        var jobs = await _db.AnalysisJobs
            .Where(j => jobIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, ct);

        // Ein uebernommener Auftrag hat seinen Zweck erfuellt: das Ergebnis liegt kopiert bei der
        // Stellung. Bliebe er stehen, fuellte eine einzige Partie 80 der 200 Zeilen, die
        // AnalysisJobService.MaxJobsPerUser je Nutzer haelt — nach drei Partien haette der Trimmer
        // die von Hand eingereihten Auftraege des Nutzers verdraengt und die Auftragsliste
        // bestuende nur noch aus Partie-Halbzuegen.
        var consumed = new List<AnalysisJob>();

        var changed = false;
        foreach (var pos in pending)
        {
            if (!jobs.TryGetValue(pos.AnalysisJobId!.Value, out var job))
            {
                // Auftrag ist weg (gelöscht/getrimmt) → Stellung erneut einreihen.
                pos.AnalysisJobId = null;
                changed = true;
                continue;
            }

            changed |= pos.CandidatesJson != null
                ? TakeRefinement(analysis, pos, job, consumed)
                : TakeFirstPass(analysis, pos, job, consumed);
        }
        if (consumed.Count > 0) _db.AnalysisJobs.RemoveRange(consumed);
        return changed;
    }

    /// <summary>Zweiter Durchgang (Vertiefung) einer Stellung, die schon Kandidaten hat. → ob sich etwas geändert hat.</summary>
    private bool TakeRefinement(GameAnalysis analysis, GameAnalysisPosition pos, AnalysisJob job, List<AnalysisJob> consumed)
    {
        var changed = false;
        // Zweiter Durchgang: das Ergebnis ERSETZT das des ersten — ist es unbrauchbar oder scheitert der
        // Auftrag endgueltig, bleibt das erste stehen (eine schwaechere Bewertung ist besser als keine).
        if (job.Status == AnalysisJobStatus.Done)
        {
            var refined = BrokerCandidates.Parse(job.ResultJson, pos.Fen);
            if (refined is { Count: > 0 })
            {
                pos.CandidatesJson = BrokerCandidates.ToJson(refined);
                pos.EvalText = BrokerCandidates.EvalTextOf(refined);
                pos.Depth = job.ReachedDepth;
                pos.AnalyzedAt = DateTime.UtcNow;
                if (job.NodeStepsJson is not null) pos.NodeStepsJson = job.NodeStepsJson;
            }
            pos.Refined = true;
            pos.AnalysisJobId = null;
            consumed.Add(job);
            changed = true;
        }
        else if (job.Status == AnalysisJobStatus.Failed)
        {
            pos.FailedAttempts++;
            pos.AnalysisJobId = null;
            if (pos.FailedAttempts >= GameAnalysisDefaults.MaxPositionAttempts) pos.Refined = true;
            changed = true;
        }
        else if (job.SecondsSpent >= GameAnalysisDefaults.RefineMaxSecondsPerPosition)
        {
            // Zeitgrenze (0.647.0): was erreicht ist, gilt — tiefer als der erste Durchgang ersetzt es ihn, sonst
            // bleibt der erste stehen. Der Lauf wird angehalten und der Auftrag entfernt (wie beim Loeschen).
            var partial = job.ReachedDepth > pos.Depth ? BrokerCandidates.Parse(job.ResultJson, pos.Fen) : null;
            if (partial is { Count: > 0 })
            {
                pos.CandidatesJson = BrokerCandidates.ToJson(partial);
                pos.EvalText = BrokerCandidates.EvalTextOf(partial);
                pos.Depth = job.ReachedDepth;
                pos.AnalyzedAt = DateTime.UtcNow;
            }
            pos.Refined = true;
            pos.AnalysisJobId = null;
            _jobs.Interrupt(job.Id);
            consumed.Add(job);
            changed = true;
            _logger.LogInformation(
                "GameAnalysis {Id}: Vertiefung von Stellung {Ply} nach {Seconds} s bei Tiefe {Depth}/{Target} beendet",
                analysis.Id, pos.Ply, job.SecondsSpent, job.ReachedDepth, job.TargetDepth);
        }
        return changed;
    }

    /// <summary>Erster Durchgang einer Stellung ohne Kandidaten. → ob sich etwas geändert hat.</summary>
    private bool TakeFirstPass(GameAnalysis analysis, GameAnalysisPosition pos, AnalysisJob job, List<AnalysisJob> consumed)
    {
        var changed = false;
        if (job.Status == AnalysisJobStatus.Done && !string.IsNullOrEmpty(job.ResultJson))
        {
            var candidates = BrokerCandidates.Parse(job.ResultJson, pos.Fen);
            if (candidates is { Count: > 0 })
            {
                pos.CandidatesJson = BrokerCandidates.ToJson(candidates);
                pos.EvalText = BrokerCandidates.EvalTextOf(candidates);
                pos.Depth = job.ReachedDepth;
                pos.AnalyzedAt = DateTime.UtcNow;
                pos.NodeStepsJson = job.NodeStepsJson;   // Zwischenstände der Knotenanalyse, bevor der Auftrag gelöscht wird
                changed = true;
            }
            else
            {
                // Fertig, aber unlesbar → als analysiert MIT LEERER Liste ablegen, sonst hinge die
                // Partie ewig an dieser Stellung. Die Wertung überspringt solche Stellungen.
                pos.CandidatesJson = "[]";
                pos.AnalyzedAt = DateTime.UtcNow;
                changed = true;
                _logger.LogWarning("GameAnalysis {Id}: Ergebnis von Auftrag {JobId} unlesbar (Ply {Ply})",
                    analysis.Id, job.Id, pos.Ply);
            }
            pos.AnalysisJobId = null;
            consumed.Add(job);
        }
        else if (job.Status == AnalysisJobStatus.Failed)
        {
            // Der Auftrag gibt auf. Das heisst fast immer „die Engine war gerade nicht zu
            // gebrauchen" und NICHT „diese Stellung geht nicht": eine Stellung der Partie hat
            // immer einen legalen Zug, Matt oder Patt kann sie nicht sein. Frueher stand hier
            // sofort die leere Liste — eine tote Engine loeschte damit stillschweigend
            // Stellungen aus der Partie, die nie wieder gerechnet wurden. Also zaehlen und
            // erneut einreihen; erst nach MaxPositionAttempts ist endgueltig Schluss, damit
            // eine wirklich unloesbare Stellung nicht ewig im Kreis laeuft.
            pos.FailedAttempts++;
            pos.AnalysisJobId = null;
            if (pos.FailedAttempts >= GameAnalysisDefaults.MaxPositionAttempts)
            {
                pos.CandidatesJson = "[]";
                pos.AnalyzedAt = DateTime.UtcNow;
                _logger.LogWarning("GameAnalysis {Id}: Stellung {Ply} nach {Tries} Anlaeufen aufgegeben",
                    analysis.Id, pos.Ply, pos.FailedAttempts);
            }
            // Der gescheiterte Auftrag bleibt stehen — seine Fehlermeldung ist die einzige
            // Erklaerung, warum es an dieser Stellung hakt.
            changed = true;
        }
        return changed;
    }
}
