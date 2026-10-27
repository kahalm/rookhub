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
/// Die Pumpe der Partie-Analysen (Teil von <see cref="GameAnalysisService"/>, Zerlegung Z5): fertige Aufträge einsammeln
/// (<see cref="GameAnalysisIngest"/>), nachlegen, wenn die Partie dran ist (<see cref="GameAnalysisTurns"/>), Vertiefung
/// und Neustart. Den Takt gibt <see cref="GameAnalysisPumpService"/> vor.
/// </summary>
internal sealed class GameAnalysisPump
{
    private readonly AppDbContext _db;
    private readonly AnalysisJobService _jobs;
    private readonly ILogger _logger;
    private readonly IGameReviewTextScheduler? _reviewTexts;
    private readonly GameAnalysisIngest _ingest;
    private readonly GameAnalysisTurns _turns;

    internal GameAnalysisPump(AppDbContext db, AnalysisJobService jobs, ILogger logger,
        IGameReviewTextScheduler? reviewTexts, QuietHours? quiet)
    {
        _db = db;
        _jobs = jobs;
        _logger = logger;
        _reviewTexts = reviewTexts;
        _ingest = new GameAnalysisIngest(db, jobs, logger);
        _turns = new GameAnalysisTurns(db, quiet);
    }

    /// <summary>Alle unfertigen Partien einen Schritt weiterbringen (Ergebnisse einsammeln, nachfüttern).</summary>
    internal async Task<int> PumpAllAsync(CancellationToken ct = default)
    {
        var ids = await _db.GameAnalyses
            .Where(g => g.Status == GameAnalysisStatus.Pending || g.Status == GameAnalysisStatus.Running
                || (g.Status == GameAnalysisStatus.Done && g.RefineDepth != null && g.RefinedAt == null))
            .OrderBy(g => g.CreatedAt)
            .Select(g => g.Id)
            .ToListAsync(ct);
        var moved = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            // Je Partie abschirmen: eine einzige stolpernde Analyse darf nicht den ganzen Durchlauf
            // abbrechen — sonst kaeme keine der dahinterliegenden Partien je an die Reihe.
            try { if (await PumpOneAsync(id, ct)) moved++; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { _logger.LogWarning(ex, "GameAnalysis {Id}: Durchlauf uebersprungen", id); }
        }
        return moved;
    }

    /// <summary>Eine Partie: fertige Aufträge übernehmen, danach bis zum Block-Limit nachfüttern.</summary>
    internal async Task<bool> PumpOneAsync(int analysisId, CancellationToken ct = default)
    {
        var analysis = await _db.GameAnalyses
            .Include(g => g.Positions)
            .FirstOrDefaultAsync(g => g.Id == analysisId, ct);
        if (analysis is null || analysis.Status == GameAnalysisStatus.Failed) return false;
        if (analysis.Status == GameAnalysisStatus.Done)
            return analysis.RefineDepth != null && analysis.RefinedAt == null && await RefineStepAsync(analysis, ct);

        var changed = await _ingest.IngestFinishedAsync(analysis, ct);
        // Nachgefuettert wird nur die Partie, die gerade DRAN ist — siehe GameAnalysisTurns.IsOwnersTurnAsync.
        if (await _turns.IsOwnersTurnAsync(analysis, ct))
            changed |= await EnqueueNextAsync(analysis, ct);

        var analyzed = analysis.Positions.Count(p => p.CandidatesJson != null);
        var finished = false;
        if (analyzed >= analysis.Positions.Count && analysis.Positions.Count > 0)
        {
            finished = true;
            analysis.Status = GameAnalysisStatus.Done;
            analysis.FinishedAt = DateTime.UtcNow;
            // Einmal gerechnet, an der Analyse abgelegt: die Partienliste zeigt die Genauigkeit beider
            // Seiten, ohne die Stellungen je Partie zu laden.
            var accuracy = GameAccuracy.FromPositions(analysis.Positions, analysis.PlyCount);
            analysis.AccuracyWhite = accuracy.White;
            analysis.AccuracyBlack = accuracy.Black;
            GameMoveClasses.Store(analysis);
            changed = true;
        }
        else if (analysis.Status == GameAnalysisStatus.Pending && analysis.Positions.Any(p => p.AnalysisJobId != null))
        {
            analysis.Status = GameAnalysisStatus.Running;
            changed = true;
        }

        if (changed)
        {
            analysis.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        // Erst NACH dem Speichern: der Hintergrund-Lauf liest die Analyse aus einem eigenen Scope.
        if (finished) _reviewTexts?.Schedule(analysis.Id, refined: false);
        return changed;
    }

    /// <summary>
    /// Ein Schritt der Vertiefung (<see cref="GameAnalysis.RefineDepth"/>): fertige Auftraege uebernehmen, nachlegen,
    /// wenn die Partie dran ist, und am Ende die Genauigkeit neu rechnen (sie haengt an den genaueren Zahlen).
    /// </summary>
    private async Task<bool> RefineStepAsync(GameAnalysis analysis, CancellationToken ct)
    {
        var changed = await _ingest.IngestFinishedAsync(analysis, ct);
        if (await _turns.IsOwnersRefineTurnAsync(analysis, ct))
            changed |= await EnqueueRefineAsync(analysis, ct);

        var refined = false;
        if (analysis.Positions.Count > 0 && analysis.Positions.All(p => p.Refined))
        {
            refined = true;
            analysis.RefinedAt = DateTime.UtcNow;
            var accuracy = GameAccuracy.FromPositions(analysis.Positions, analysis.PlyCount);
            analysis.AccuracyWhite = accuracy.White;
            analysis.AccuracyBlack = accuracy.Black;
            GameMoveClasses.Store(analysis);
            changed = true;
        }
        if (changed)
        {
            analysis.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        // Die genauere Rechnung kann Bestzug und Klasse verschieben — die Erklärungen werden neu geschrieben.
        if (refined) _reviewTexts?.Schedule(analysis.Id, refined: true);
        return changed;
    }

    /// <summary>Vertiefungs-Auftraege nachlegen — hoechstens <see cref="GameAnalysisTurnRules.RefineJobCap"/> offen, als
    /// Hintergrundarbeit (<see cref="AnalysisJob.Background"/>): erst die Stellungen um Fehler und Patzer
    /// (<see cref="GameAnalysisTurnRules.SuspectPlies"/>), dann der Rest, jeweils in Zugreihenfolge.</summary>
    private async Task<bool> EnqueueRefineAsync(GameAnalysis analysis, CancellationToken ct)
    {
        var open = analysis.Positions.Count(p => !p.Refined && p.AnalysisJobId != null);
        var room = GameAnalysisTurnRules.RefineJobCap(await _turns.EngineSlotsAsync(analysis, ct)) - open;
        if (room <= 0) return false;
        if (await _turns.HouseEngineWithdrawnAsync(analysis, ct))
        {
            // Die Vertiefung ist Zugabe: ohne Freigabe bleibt das Ergebnis des ersten Durchgangs stehen — wie bei einer
            // Stellung, die die Engine nicht annimmt (unten). Noch offene Auftraege laesst der Worker scheitern.
            var rest = analysis.Positions.Where(p => !p.Refined && p.AnalysisJobId == null).ToList();
            foreach (var pos in rest) pos.Refined = true;
            return rest.Count > 0;
        }
        var suspect = GameAnalysisTurnRules.SuspectPlies(analysis.Positions, analysis.PlyCount);
        var next = analysis.Positions
            .Where(p => !p.Refined && p.AnalysisJobId == null)
            .OrderBy(p => suspect.Contains(p.Ply) ? 0 : 1)
            .ThenBy(p => p.Ply)
            .Take(room)
            .ToList();

        var changed = false;
        foreach (var pos in next)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var job = await _jobs.CreateAsync(analysis.UserId, new CreateAnalysisJobRequest
                {
                    Fen = pos.Fen,
                    Title = GameAnalysisText.JobTitle(analysis, pos),
                    TargetDepth = analysis.RefineDepth!.Value,
                    MultiPv = analysis.RefineMultiPv ?? analysis.MultiPv,
                    EngineId = analysis.EngineId,
                }, ct, remember: false, engineOwnerUserId: analysis.EngineOwnerUserId, background: true,
                    batch: GameAnalysisOrigins.IsBatch(analysis.Origin));
                pos.AnalysisJobId = job.Id;
                changed = true;
            }
            catch (InvalidOperationException)
            {
                // Deckel erreicht oder gerade keine Engine: die Vertiefung ist Hintergrundarbeit — naechster Lauf.
                break;
            }
            catch (ArgumentException ex)
            {
                pos.Refined = true;   // diese Stellung nimmt die Engine nicht an — das erste Ergebnis bleibt
                changed = true;
                _logger.LogWarning(ex, "GameAnalysis {Id}: Stellung {Ply} nicht vertiefbar", analysis.Id, pos.Ply);
            }
        }
        return changed;
    }

    /// <summary>Bis zum Block-Limit neue Aufträge einreihen (in Zugreihenfolge). Meister- und Vereinspartien des Stapels
    /// (<see cref="GameAnalysisOrigins.IsBatch"/>) nur ausserhalb der Sperrzeiten und nur als Hintergrundarbeit.</summary>
    private async Task<bool> EnqueueNextAsync(GameAnalysis analysis, CancellationToken ct)
    {
        var batch = GameAnalysisOrigins.IsBatch(analysis.Origin);
        // In der Sperrzeit steht der Stapel still: schon eingereihte Stellungen laufen noch zu Ende (hoechstens
        // ein Block), neue kommen erst, wenn das Fenster wieder offen ist.
        // Seit 0.679.0 nicht mehr pauschal an den Sperrzeiten: eine Engine, deren Client einen Zeitplan meldet,
        // rechnet nach diesem, nur Engines ohne Meldung halten sich an die Sperrzeiten von RookHub.
        if (batch && (await _turns.UsableEnginesAsync(analysis, ct)).Count == 0) return false;
        var open = analysis.Positions.Count(p => p.CandidatesJson == null && p.AnalysisJobId != null);
        var room = GameAnalysisDefaults.MaxOpenJobsPerGame - open;
        if (room <= 0) return false;

        var next = analysis.Positions
            .Where(p => p.CandidatesJson == null && p.AnalysisJobId == null)
            .OrderBy(p => p.Ply)
            .Take(room)
            .ToList();
        if (next.Count == 0) return false;
        if (await _turns.HouseEngineWithdrawnAsync(analysis, ct))
        {
            analysis.Status = GameAnalysisStatus.Failed;
            analysis.LastError = EngineOwnerResolver.HouseEngineWithdrawnError;
            return true;
        }

        var changed = false;
        foreach (var pos in next)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var job = await _jobs.CreateAsync(analysis.UserId, new CreateAnalysisJobRequest
                {
                    Fen = pos.Fen,
                    Title = GameAnalysisText.JobTitle(analysis, pos),
                    TargetDepth = analysis.TargetDepth,
                    TargetNodes = analysis.TargetNodes,   // lc0-Analyse: Knoten statt Tiefe, auch nach Neustart
                    MultiPv = analysis.MultiPv,
                    EngineId = analysis.EngineId,
                    // Nicht in „Gemerkte Stellungen" spiegeln: eine Partie erzeugt je Halbzug einen
                    // Auftrag — 80 Zeilen je Partie wuerden die Merkliste des Nutzers zuschuetten.
                }, ct, remember: false, engineOwnerUserId: analysis.EngineOwnerUserId, background: batch, batch: batch);
                pos.AnalysisJobId = job.Id;
                changed = true;
            }
            catch (InvalidOperationException ex)
            {
                // Kein Platz mehr (globaler Deckel) oder keine Hintergrund-Engine.
                if (ex.Message.Contains("engine", StringComparison.OrdinalIgnoreCase))
                {
                    analysis.Status = GameAnalysisStatus.Failed;
                    analysis.LastError = "Keine Hintergrund-Engine konfiguriert";
                    return true;
                }
                break;   // Deckel erreicht → beim nächsten Pump-Lauf weiter
            }
            catch (ArgumentException ex)
            {
                // Diese eine Stellung ist der Engine nicht zumutbar (z. B. unbrauchbare FEN).
                pos.CandidatesJson = "[]";
                pos.AnalyzedAt = DateTime.UtcNow;
                changed = true;
                _logger.LogWarning(ex, "GameAnalysis {Id}: Stellung {Ply} nicht einreihbar", analysis.Id, pos.Ply);
            }
        }
        return changed;
    }

    /// <summary>
    /// Die Partie noch einmal anstossen: alles, was nicht gerechnet ist, kommt frisch in die
    /// Warteschlange.
    ///
    /// <para><b>Warum das ein eigener Knopf sein muss.</b> Ein Auftrag klebt an DER Engine, die
    /// beim Anlegen die kuerzeste Schlange hatte — und er wechselt nie wieder. Ist die Engine
    /// danach aus (ein Rechner, den jemand abends ausmacht), versucht der Auftrag es ewig dort und
    /// nirgends sonst; der Broker antwortet mit 503, der Auftrag pausiert eine Minute, und das
    /// wiederholt sich bis in alle Ewigkeit. Am 2026-09-11 hingen so fuenf Partien an ihren letzten
    /// Halbzuegen fest, waehrend fuenf freie Engines danebenstanden. Der Auftrag wird deshalb
    /// GELOESCHT und neu angelegt — erst dadurch waehlt <c>PickBackgroundEngineAsync</c> erneut.</para>
    ///
    /// <para>Aufgegebene Stellungen kommen ebenfalls zurueck (<c>CandidatesJson = "[]"</c> nach
    /// <see cref="GameAnalysisDefaults.MaxPositionAttempts"/> Anlaeufen). Sie zaehlen als
    /// „gerechnet" und sind der Grund, warum eine Partie fertig aussehen kann und in der
    /// Punktepartie trotzdem Loecher hat. Genau die soll ein Reset schliessen.</para>
    ///
    /// <para>Eine pinnte Engine (<see cref="GameAnalysis.EngineId"/>) bleibt, wo sie ist: wer eine
    /// bestimmte Maschine gewaehlt hat, meint sie auch.</para>
    /// </summary>
    /// <returns><c>false</c>, wenn es die Partie nicht (mehr) gibt.</returns>
    internal async Task<bool> RestartAsync(int userId, int id, CancellationToken ct = default)
    {
        var analysis = await _db.GameAnalyses
            .Include(g => g.Positions)
            .FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId, ct);
        if (analysis is null) return false;

        // Steckengebliebene Auftraege weg — sonst behielte die Stellung ihren alten Auftrag auf der
        // alten Engine, und der Reset aenderte genau gar nichts.
        var stuck = analysis.Positions
            .Where(p => p.CandidatesJson == null && p.AnalysisJobId != null)
            .Select(p => p.AnalysisJobId!.Value)
            .ToList();
        foreach (var jobId in stuck)
        {
            try { await _jobs.DeleteAsync(userId, jobId, ct); }
            catch (Exception ex) { _logger.LogDebug(ex, "GameAnalysis {Id}: Auftrag {JobId} liess sich nicht loeschen", id, jobId); }
        }

        var reopened = 0;
        foreach (var pos in analysis.Positions)
        {
            if (pos.CandidatesJson == "[]")
            {
                pos.CandidatesJson = null;
                pos.AnalyzedAt = null;
                pos.EvalText = null;
                pos.Depth = 0;
            }
            if (pos.CandidatesJson != null) continue;

            pos.AnalysisJobId = null;
            pos.FailedAttempts = 0;
            reopened++;
        }

        analysis.Status = GameAnalysisStatus.Pending;
        analysis.LastError = null;
        analysis.FinishedAt = null;
        analysis.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("GameAnalysis {Id}: neu angestossen ({Count} Stellungen, {Jobs} Auftraege verworfen)",
            id, reopened, stuck.Count);

        // Sofort nachfuettern, damit der Knopf etwas tut, das man sieht.
        await PumpOneAsync(analysis.Id, ct);
        return true;
    }
}
