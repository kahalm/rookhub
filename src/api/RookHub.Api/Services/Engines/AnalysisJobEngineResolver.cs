using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Services.Engines;

/// <summary>
/// Vor dem Lauf eines Hintergrund-Analyseauftrags (zweite Zerlegungsrunde W7b aus <see cref="AnalysisJobWorker"/>):
/// wessen Engine, ob sie (noch) rechnen darf, und welche Arbeit an den Broker geht. Scheitert ein Schritt, stellt er
/// den Auftrag selbst ein (<see cref="AnalysisJobTransitions"/>) und liefert <c>null</c>.
/// </summary>
internal static class AnalysisJobEngineResolver
{
    /// <summary>Token und Engine-Registrierung kommen vom ENGINE-BESITZER: bei einer eingeworfenen
    /// Punktepartie ohne eigene Engine ist das nicht der Auftraggeber, sondern das Haus-Konto.
    /// Eine Engine „RookHub direkt" (rhe_) braucht keinen Token, eine Lichess-Engine (eei_) schon.</summary>
    internal static int OwnerOf(AnalysisJob job) => job.EngineOwnerUserId ?? job.UserId;

    /// <summary>Vor dem Lauf: Live-Vorrang, Freigabe der Haus-Engine, Engine des Besitzers auflösen.
    /// <c>null</c> = es geht nicht weiter, der Auftrag ist schon eingestellt.</summary>
    internal static async Task<EngineRef?> ResolveAsync(EngineActivityTracker tracker, IServiceScope scope, AppDbContext db,
        AnalysisJob job, string engineId, CancellationToken ct)
    {
        // Zwischen der Live-Prüfung im Tick und dem Eintrag in _running liegen DB-Roundtrips. Startet in
        // diesem Fenster ein Live-Stream auf DIESER Engine, lief sein LiveStarted ins Leere (kein Eintrag
        // zum Abbrechen) — ohne diese zweite Prüfung rechnete der Hintergrund daneben weiter.
        if (tracker.IsEngineBusy(engineId))
        {
            await AnalysisJobTransitions.PauseAsync(db, job, null);
            return null;
        }

        var engineOwnerId = OwnerOf(job);
        // Fremde Rechenzeit nur, solange die Freigabe gilt (A4-004): schon eingereihte Auftraege fremder Nutzer
        // liefen sonst nach dem Zuruecknehmen der Haus-Engine (oder dem Verlust der Admin-Rolle) weiter.
        if (job.EngineOwnerUserId is int houseOwner
            && !await EngineOwnerResolver.IsHouseEngineSharedAsync(db, houseOwner, ct))
        {
            await AnalysisJobTransitions.FailAsync(db, job, EngineOwnerResolver.HouseEngineWithdrawnError);
            return null;
        }
        var registry = scope.ServiceProvider.GetRequiredService<EngineRegistry>();
        EngineLookup lookup;
        try { lookup = await registry.ResolveAsync(engineOwnerId, job.EngineId, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Live hat begonnen / gelöscht / Shutdown — MUSS vor dem Filter darunter stehen, sonst wäre
            // der eigene Abbruch als „Lichess nicht erreichbar" mit 60 s Backoff im Auftrag gelandet.
            await AnalysisJobTransitions.PauseAsync(db, job, null);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await AnalysisJobTransitions.ApplyAsync(db, job, BrokerFailurePolicy.LichessUnreachable);
            return null;
        }
        if (lookup.Failure == EngineLookupFailure.NoToken)
        {
            await AnalysisJobTransitions.FailAsync(db, job, "Kein Lichess-Token hinterlegt");
            return null;
        }
        if (lookup.Engine is not { } engine)
        {
            await AnalysisJobTransitions.FailAsync(db, job, "Engine nicht (mehr) registriert");
            return null;
        }
        return engine;
    }

    /// <summary>Die Arbeit für den Broker: genau EIN Limit (Tiefe oder Knoten), Linien auf das Protokoll begrenzt.</summary>
    internal static EngineWork WorkOf(AnalysisJob job, EngineRef engine) => new(
        SessionId: $"rh-bg-{job.UserId}", Threads: Math.Max(1, engine.MaxThreads),
        Hash: Math.Clamp(engine.MaxHash, 16, 32768),
        // Das Protokoll erlaubt 1..5; ein größerer Wert würde vom Broker abgewiesen und der Auftrag
        // liefe endlos in die Wiederholung. Zweiter Riegel neben AnalysisJobService.MaxMultiPv.
        MultiPv: Math.Clamp(job.MultiPv, 1, EngineProtocol.MaxMultiPv),
        // Knotenauftrag: `nodes` statt `depth` — GENAU EINES der drei Limits geht an die Engine
        // (EngineWork.ToJson/WorkSanitizer). Eine Tiefe obendrauf liefe bei Lc0 ins Leere.
        InitialFen: job.Fen, Moves: [],
        Depth: job.TargetNodes is null ? job.TargetDepth : null, Nodes: job.TargetNodes);
}
