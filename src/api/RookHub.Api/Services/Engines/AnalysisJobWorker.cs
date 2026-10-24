using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Services.Engines;

/// <summary>
/// Arbeitet Hintergrund-Analyseaufträge (<see cref="AnalysisJob"/>) über den Broker ihrer Engine ab (eigener
/// Broker für <c>rhe_</c>, Lichess für <c>eei_</c> — <see cref="IEngineBroker"/>) — höchstens
/// einer je ENGINE (ein Stockfish-Prozess kann nur eine Suche; Aufträge auf verschiedenen Engines laufen
/// deshalb parallel). Vorrang der Live-Analyse: meldet der <see cref="EngineActivityTracker"/> einen
/// Live-Stream AUF EINER ENGINE, wird genau der Auftrag auf dieser Engine abgebrochen (Status Paused — der
/// Provider stoppt Stockfish, die Hashtabelle bleibt warm), Aufträge auf anderen Engines laufen ungestört
/// weiter; nach <c>AnalysisJobs:IdleGraceSeconds</c> Ruhe (Standard 20 s) geht es dort weiter. Ergebnis-Zeilen werden nur
/// übernommen, wenn sie mindestens die gespeicherte Tiefe haben (<see cref="AnalysisJobStream.ShouldPersist"/>).
/// Kein 10-Minuten-Deckel wie beim Live-Proxy: ein Auftrag darf Stunden rechnen.
///
/// <para><b>Admin-Schalter „Hintergrundjobs pausieren"</b> (<see cref="BackgroundPause"/>): solange er steht, startet
/// der Worker keine neuen Läufe, und LAUFENDE werden wie beim Live-Vorrang abgebrochen — Status Paused, KEIN
/// Fehlversuch, Ergebnis und erreichte Tiefe bleiben, nach dem Fortsetzen geht es dort weiter. Bewusst nicht „zu Ende
/// rechnen lassen": ein Auftrag darf Stunden dauern, die Pause soll aber sofort Rechenzeit freigeben.</para>
/// </summary>
public class AnalysisJobWorker : BackgroundService, IAnalysisJobControl, IBackgroundJobInfo
{
    private sealed record Running(int JobId, string EngineId, CancellationTokenSource Cts, bool Background = false);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EngineActivityTracker _tracker;
    private readonly AnalysisJobLive _live;
    private readonly IEngineBroker _broker;
    private readonly ILogger<AnalysisJobWorker> _logger;
    private readonly TimeSpan _tick;
    private readonly TimeSpan _idleGrace;
    private readonly TimeSpan _persistInterval;
    /// <summary>Ohne erste Datenzeile binnen dieser Frist gilt der Provider als nicht rechnend — sonst hinge der
    /// Auftrag unbegrenzt in „läuft" (der HttpClient hat bewusst kein Timeout) und blockierte den Slot des Users.</summary>
    private readonly TimeSpan _firstLineTimeout;
    /// <summary>Frist fuer jede WEITERE Datenzeile, nachdem die erste da war. Der Wert muss ueber der
    /// laengsten ehrlichen Iteration liegen (Tiefe 40+ mit fuenf Linien dauert Minuten), deshalb sehr
    /// grosszuegig — er faengt nicht die langsame, sondern die HAENGENDE Engine.
    /// <para>Am 2026-09-12 auf Prod gebraucht: ein Auftrag stand vier Stunden auf „laeuft" bei Tiefe 11,
    /// waehrend der Provider unveraendert dieselbe <c>info</c>-Zeile wiederholte (time 27 ms, nodes 24006,
    /// Tempo auf die Stelle genau eingefroren). Der Waechter der ersten Zeile war da laengst entschaerft,
    /// einen zweiten gab es nicht — und weil die Pumpe je Nutzer nur EINE Partie fuettert, standen darueber
    /// 434 wartende Partien und elf freie Engines still.</para>
    /// <para>Konfiguration: <c>AnalysisJobs:StallTimeoutSeconds</c> (300..86400, Vorgabe 1800).</para></summary>
    private readonly TimeSpan _stallTimeout;
    /// <summary>Wartezeit, nachdem ein Auftrag wegen eines 503/504 auf eine ANDERE Engine
    /// umgehaengt wurde. Kurz, weil die neue Engine sofort laufen kann — und trotzdem
    /// unbedenklich: der Worker rechnet je Engine nur einen Auftrag, es sind also hoechstens so
    /// viele Versuche gleichzeitig unterwegs wie Engines hinterlegt sind, nicht so viele wie
    /// Auftraege offen sind. Faellt der Broker ganz aus, pendeln die Auftraege damit im Takt
    /// dieser Frist statt im Zwei-Minuten-Takt — bei sechzehn Engines rund ein Abruf je Sekunde.
    /// <para>Konfiguration: <c>AnalysisJobs:EngineSwitchBackoffSeconds</c> (5..600, Vorgabe 15).</para></summary>
    private readonly TimeSpan _engineSwitchBackoff;
    /// <summary>Ab dieser Laufzeit gilt „kein Tiefenfortschritt" NICHT mehr als Fehlversuch: eine echte Sackgasse
    /// (Matt/Patt, abgelehnte Arbeit) endet in Sekunden, ein langer Lauf ohne neue Zeile ist eine gekappte
    /// Verbindung — dafür darf der Auftrag nicht als gescheitert gelten.</summary>
    private readonly TimeSpan _fruitlessMinRuntime;
    private readonly ConcurrentDictionary<string, Running> _running = new();   // key = EngineId
    /// <summary>Weckt die Schleife, sobald ein Lauf endet — die Engine soll nicht bis zum nächsten Tick warten.</summary>
    private readonly WakeSignal _wake = new();

    private readonly IReadOnlySet<string> _explicitOnly;
    /// <summary>Schrittweite der Zwischenstände einer Knotenanalyse (<c>AnalysisJobs:SnapshotStepNodes</c>, Vorgabe 10 000, 0 = aus).</summary>
    private readonly long _snapshotStep;
    /// <summary>Admin-Schalter „Hintergrundjobs pausieren" (ohne Registrierung, z. B. in Tests: läuft immer).</summary>
    private readonly BackgroundPause? _pauseSwitch;
    private readonly PauseGate _pause;

    public AnalysisJobWorker(IServiceScopeFactory scopeFactory, EngineActivityTracker tracker,
        IEngineBroker broker, ILogger<AnalysisJobWorker> logger, IConfiguration config, AnalysisJobLive live,
        BackgroundPause? pause = null)
    {
        _scopeFactory = scopeFactory;
        _tracker = tracker;
        _live = live;
        _broker = broker;
        _logger = logger;
        _tick = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:TickSeconds") ?? 5, 1, 60));
        _idleGrace = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:IdleGraceSeconds") ?? 20, 0, 600));
        _persistInterval = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:PersistIntervalSeconds") ?? 5, 1, 60));
        _firstLineTimeout = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:FirstLineTimeoutSeconds") ?? 300, 30, 3600));
        _stallTimeout = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:StallTimeoutSeconds") ?? 1800, 300, 86400));
        _engineSwitchBackoff = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:EngineSwitchBackoffSeconds") ?? 15, 5, 600));
        _fruitlessMinRuntime = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:FruitlessMinRuntimeSeconds") ?? 60, 5, 3600));
        _explicitOnly = ExplicitOnlyEngines.From(config);
        var step = config.GetValue<long?>("AnalysisJobs:SnapshotStepNodes") ?? 10_000;
        _snapshotStep = step <= 0 ? 0 : Math.Clamp(step, 1_000, 1_000_000);
        _tracker.LiveStarted += PauseEngine;
        _pauseSwitch = pause;
        _pause = new PauseGate(nameof(AnalysisJobWorker), logger).Use(pause);
        if (pause is not null) pause.Changed += OnPauseChanged;
    }

    /// <inheritdoc/>
    public bool PausableByAdmin => true;

    /// <inheritdoc/>
    public DateTime? LastTickUtc => _pause.LastTickUtc;

    /// <summary>Schalter umgelegt: bei Pause SOFORT alle laufenden Aufträge anhalten (nicht erst beim nächsten Tick),
    /// in jedem Fall die Schleife wecken.</summary>
    private void OnPauseChanged(bool paused)
    {
        if (paused) InterruptAll();
        _wake.Wake();
    }

    /// <summary>Alle laufenden Aufträge abbrechen — sie enden auf Paused, ohne Fehlversuch.</summary>
    private void InterruptAll()
    {
        foreach (var r in _running.Values) TryCancel(r.Cts);
    }

    /// <summary>Laufenden Auftrag AUF DIESER ENGINE unterbrechen (Live hat dort Vorrang). Aufträge auf
    /// anderen Engines bleiben unberührt — sie belegen einen eigenen Prozess.</summary>
    public void PauseEngine(string engineId)
    {
        if (_running.TryGetValue(engineId, out var r)) TryCancel(r.Cts);
    }

    public void Interrupt(int jobId)
    {
        foreach (var r in _running.Values)
            if (r.JobId == jobId) TryCancel(r.Cts);
    }

    /// <summary>Hintergrund-Auftrag AUF DIESER ENGINE unterbrechen: ein normaler Auftrag wurde fuer sie eingereiht
    /// (<see cref="AnalysisJobService.CreateAsync"/>, <see cref="AnalysisJobService.CreateManyAsync"/>). Der Lauf endet wie bei Live-Vorrang auf Paused, ohne Fehlversuch,
    /// und das Ende weckt die Schleife — die Engine nimmt sofort den normalen Auftrag (normal vor Hintergrund in
    /// <see cref="AnalysisJobService.PickNextForEngineAsync"/>). Ein normaler Lauf wird NIE unterbrochen.</summary>
    public void PreemptBackground(string engineId)
    {
        if (_running.TryGetValue(engineId, out var r) && r.Background) TryCancel(r.Cts);
    }

    /// <summary>Abbrechen, ohne an einem gerade beendeten Lauf zu scheitern: zwischen dem Griff ins Dictionary
    /// und dem Cancel kann <see cref="RunJobAsync"/> seinen Eintrag entfernt UND die CTS disposed haben —
    /// <c>Cancel()</c> würde dann werfen. Der Wurf liefe bei <see cref="PauseEngine"/> im LiveStarted-Handler
    /// bis in den Request-Thread des Live-Streams und ließe dessen Zähler dauerhaft stehen (die Engine bliebe
    /// für immer „belegt"), bei <see cref="Interrupt"/> würde Ändern/Löschen zu 500.</summary>
    internal static void TryCancel(CancellationTokenSource cts)
    {
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { /* Lauf ist im selben Moment zu Ende gegangen — nichts zu tun */ }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var n = await scope.ServiceProvider.GetRequiredService<AnalysisJobService>().ResetInterruptedAsync(stoppingToken);
            if (n > 0) _logger.LogInformation("AnalysisJobWorker: {Count} unterbrochene Aufträge auf Paused gesetzt", n);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "AnalysisJobWorker: Start-Aufräumen fehlgeschlagen");
        }

        // Fester Takt ODER Weckruf: ein beendeter Lauf weckt die Schleife (siehe WakeSignal), damit die
        // frei gewordene Engine sofort den nächsten Auftrag bekommt statt bis zu einen Tick zu warten.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Pause: keine neuen Läufe, und was noch läuft (Schalter in der Datenbank/Konfiguration umgelegt, ohne
                // dass Changed hier ankam — oder ein Lauf, der im selben Moment startete), wird angehalten.
                if (await _pause.ShouldSkipAsync(null, stoppingToken)) InterruptAll();
                else
                {
                    await TickAsync(stoppingToken);
                    _pause.MarkTick();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "AnalysisJobWorker: Tick fehlgeschlagen"); }
            try { await _wake.WaitAsync(_tick, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
        foreach (var r in _running.Values) TryCancel(r.Cts);
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        List<string> engines;
        using (var scope = _scopeFactory.CreateScope())
            engines = await scope.ServiceProvider.GetRequiredService<AnalysisJobService>().EnginesWithRunnableJobsAsync(now, ct);

        foreach (var engineId in engines)
        {
            if (_running.ContainsKey(engineId)) continue;                                   // Engine rechnet schon einen Auftrag
            if (_tracker.IsEngineBusy(engineId) || _tracker.EngineIdleFor(engineId) < _idleGrace) continue;   // Live hat Vorrang

            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<AnalysisJobService>();
            var job = await svc.PickNextForEngineAsync(engineId, now, ct);
            if (job is null) continue;

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var run = new Running(job.Id, engineId, cts, job.Background);
            if (!_running.TryAdd(engineId, run)) { cts.Dispose(); continue; }
            _ = Task.Run(() => RunJobAsync(run, job.Id, ct), CancellationToken.None);
        }
    }

    /// <summary>Ein Lauf: Engine auflösen, als laufend markieren, Broker anfragen, Stream lesen, Auftrag einordnen —
    /// jeder Schritt stellt den Auftrag selbst ein, wenn es nicht weitergeht. Die Statusregeln stehen in
    /// <see cref="AnalysisJobRunEnd"/> und <see cref="BrokerFailurePolicy"/>.</summary>
    private async Task RunJobAsync(Running run, int jobId, CancellationToken shutdown)
    {
        var ct = run.Cts.Token;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.AnalysisJobs.FirstOrDefaultAsync(j => j.Id == jobId, CancellationToken.None);
            if (job is null) return;
            if (await ResolveEngineAsync(scope, db, job, run, ct) is not { } engine) return;
            await MarkRunningAsync(db, job);

            // Frist NUR für die Antwort-KOPFZEILEN: der HttpClient des Brokers ist bewusst timeout-los
            // (eine Suche darf Stunden dauern), und der `firstLine`-Wächter (JobStreamRun) beginnt erst NACH den
            // Headern. Antwortet der Broker mit Verbindungsaufbau, aber ohne Header — er wartet auf einen
            // Provider, der nie kommt —, stand `RunJobAsync` hier unbegrenzt: der Auftrag blieb in der
            // Datenbank auf „läuft" mit Tiefe 0, die Engine blieb belegt, und alle weiteren Aufträge
            // dieser Engine warteten mit. Auflösbar war das nur durch einen Live-Stream oder Neustart.
            // Die Quelle lebt bis zum Ende des Laufs (wie vor der Zerlegung).
            using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            headerCts.CancelAfter(_firstLineTimeout);
            if (await OpenUpstreamAsync(db, job, engine, headerCts.Token, ct) is not { } upstream) return;

            await using (upstream)
            {
                if (!upstream.IsSuccess) await OnBrokerRejectedAsync(db, job, engine, upstream);
                else await StreamAndClassifyAsync(db, job, run, upstream, ct);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            // Auftrag wurde parallel gelöscht — nichts zu retten.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecoverAfterErrorAsync(jobId, ex);
        }
        finally
        {
            _live.Stop(jobId);
            _running.TryRemove(new KeyValuePair<string, Running>(run.EngineId, run));
            run.Cts.Dispose();
            _wake.Wake();   // die Engine ist frei — nächsten Auftrag sofort holen, nicht erst beim nächsten Tick
        }
    }

    /// <summary>Token und Engine-Registrierung kommen vom ENGINE-BESITZER: bei einer eingeworfenen
    /// Punktepartie ohne eigene Engine ist das nicht der Auftraggeber, sondern das Haus-Konto.
    /// Eine Engine „RookHub direkt" (rhe_) braucht keinen Token, eine Lichess-Engine (eei_) schon.</summary>
    private static int EngineOwnerOf(AnalysisJob job) => job.EngineOwnerUserId ?? job.UserId;

    /// <summary>Vor dem Lauf: Live-Vorrang, Freigabe der Haus-Engine, Engine des Besitzers auflösen.
    /// <c>null</c> = es geht nicht weiter, der Auftrag ist schon eingestellt.</summary>
    private async Task<EngineRef?> ResolveEngineAsync(IServiceScope scope, AppDbContext db, AnalysisJob job, Running run,
        CancellationToken ct)
    {
        // Zwischen der Live-Prüfung im Tick und dem Eintrag in _running liegen DB-Roundtrips. Startet in
        // diesem Fenster ein Live-Stream auf DIESER Engine, lief sein LiveStarted ins Leere (kein Eintrag
        // zum Abbrechen) — ohne diese zweite Prüfung rechnete der Hintergrund daneben weiter.
        if (_tracker.IsEngineBusy(run.EngineId))
        {
            await AnalysisJobTransitions.PauseAsync(db, job, null);
            return null;
        }

        var engineOwnerId = EngineOwnerOf(job);
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

    private static async Task MarkRunningAsync(AppDbContext db, AnalysisJob job)
    {
        job.Status = AnalysisJobStatus.Running;
        job.LastRunAt = DateTime.UtcNow;
        job.LastError = null;
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Die Arbeit für den Broker: genau EIN Limit (Tiefe oder Knoten), Linien auf das Protokoll begrenzt.</summary>
    private static EngineWork WorkOf(AnalysisJob job, EngineRef engine) => new(
        SessionId: $"rh-bg-{job.UserId}", Threads: Math.Max(1, engine.MaxThreads),
        Hash: Math.Clamp(engine.MaxHash, 16, 32768),
        // Das Protokoll erlaubt 1..5; ein größerer Wert würde vom Broker abgewiesen und der Auftrag
        // liefe endlos in die Wiederholung. Zweiter Riegel neben AnalysisJobService.MaxMultiPv.
        MultiPv: Math.Clamp(job.MultiPv, 1, EngineProtocol.MaxMultiPv),
        // Knotenauftrag: `nodes` statt `depth` — GENAU EINES der drei Limits geht an die Engine
        // (EngineWork.ToJson/WorkSanitizer). Eine Tiefe obendrauf liefe bei Lc0 ins Leere.
        InitialFen: job.Fen, Moves: [],
        Depth: job.TargetNodes is null ? job.TargetDepth : null, Nodes: job.TargetNodes);

    /// <summary>Den Broker anfragen, bis die Kopfzeilen da sind. <c>null</c> = kein Stream, der Auftrag ist eingestellt.</summary>
    private async Task<EngineAnalysisSession?> OpenUpstreamAsync(AppDbContext db, AnalysisJob job, EngineRef engine,
        CancellationToken headerCt, CancellationToken ct)
    {
        var work = WorkOf(job, engine);
        try { return await _broker.AnalyseAsync(engine, work, headerCt); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { await AnalysisJobTransitions.PauseAsync(db, job, null); return null; }
        catch (OperationCanceledException)
        {
            // Nur die Kopfzeilen-Frist ist abgelaufen (der Job-Token ist nicht abgebrochen).
            _logger.LogWarning("AnalysisJob {JobId}: Broker antwortete nicht binnen {Timeout} (keine Kopfzeilen) — pausiert",
                job.Id, _firstLineTimeout);
            await AnalysisJobTransitions.ApplyAsync(db, job, BrokerFailurePolicy.NoHeaders);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await AnalysisJobTransitions.ApplyAsync(db, job, BrokerFailurePolicy.Unreachable);
            return null;
        }
    }

    /// <summary>Der Broker sagte ab (Statuscode): einordnen nach <see cref="BrokerFailurePolicy.OnStatus"/>, bei 503/504
    /// vorher auf die nächste Engine umhängen. 503/504 heisst beim Broker: fuer DIESE Engine ist gerade kein Provider
    /// verbunden — eine Aussage ueber die ENGINE und nicht ueber den Auftrag, also umhaengen statt zwei Minuten dieselbe
    /// Tuer anzuklopfen (2026-09-12 auf Prod: 309 solcher Antworten in 25 Minuten).</summary>
    private async Task OnBrokerRejectedAsync(AppDbContext db, AnalysisJob job, EngineRef engine, EngineAnalysisSession upstream)
    {
        var code = upstream.StatusCode;
        var decision = BrokerFailurePolicy.OnStatus(code, engine.Source == EngineSource.Local,
            job.FruitlessAttempts, upstream.Error);
        var alt = job.EngineId;
        if (decision.TryOtherEngine
            && await BackgroundEngineRotation.SwitchAsync(db, job, EngineOwnerOf(job), _explicitOnly, CancellationToken.None))
        {
            await AnalysisJobTransitions.ApplyAsync(db, job, BrokerFailurePolicy.SwitchedEngine(code, _engineSwitchBackoff));
            _logger.LogInformation(
                "AnalysisJob {JobId}: Broker antwortete {Code} fuer {Old} — weiter auf {New}",
                job.Id, code, alt, job.EngineId);
            return;
        }
        await AnalysisJobTransitions.ApplyAsync(db, job, decision.Outcome);
    }

    /// <summary>Den Stream lesen (<see cref="JobStreamRun"/>) und den Auftrag danach einordnen
    /// (<see cref="AnalysisJobRunEnd.ClassifyRunEnd"/>; Regeln und Begründungen dort).</summary>
    private async Task StreamAndClassifyAsync(AppDbContext db, AnalysisJob job, Running run, EngineAnalysisSession upstream,
        CancellationToken ct)
    {
        var stream = new JobStreamRun(db, job, run.EngineId, _live, _logger, _snapshotStep, _persistInterval,
            _firstLineTimeout, _stallTimeout);
        if (await stream.ConsumeAsync(upstream.Ndjson, ct) == StreamEnd.Silent)
        {
            await OnSilenceAsync(db, job, stream);
            return;
        }
        await stream.FinishAsync();

        var facts = stream.Facts(ct.IsCancellationRequested, _tracker.IsEngineBusy(run.EngineId), _fruitlessMinRuntime);
        if (AnalysisJobRunEnd.EndedOwnSearch(facts))
            _logger.LogInformation(
                "AnalysisJob {JobId}: Engine beendet die Suche selbst vor dem Ziel ({Nodes} von {Target} Knoten, z. B. Lc0 "
                + "Smart Pruning) — Ergebnis gilt", job.Id, stream.CurrentNodes, job.TargetNodes);
        var outcome = AnalysisJobRunEnd.ClassifyRunEnd(facts);
        await AnalysisJobTransitions.ApplyAsync(db, job, outcome);
        if (outcome.Kind == RunEndKind.Done)
            _logger.LogInformation("AnalysisJob {JobId}: fertig bei Tiefe {Depth} ({Nodes} Knoten)", job.Id, job.ReachedDepth, stream.CurrentNodes);
    }

    /// <summary>Der Stillstands-Wächter hat ausgelöst (<see cref="AnalysisJobRunEnd.ClassifyStall"/>).</summary>
    private async Task OnSilenceAsync(AppDbContext db, AnalysisJob job, JobStreamRun stream)
    {
        if (!stream.GotData)
        {
            await AnalysisJobTransitions.ApplyAsync(db, job, AnalysisJobRunEnd.ClassifyStall(stream.GotData, job.FruitlessAttempts, stream.CurrentDepth));
            _logger.LogWarning("AnalysisJob {JobId}: keine Datenzeile binnen {Timeout} — pausiert",
                job.Id, _firstLineTimeout);
            return;
        }
        // Die Engine HAT gerechnet und ist dann stehen geblieben. Das ist eine Aussage über die
        // ENGINE, nicht über die Stellung — die kürzeste Schlange wählte ausgerechnet sie erneut
        // (sie hat ja nichts zu tun), deshalb wird hier auf die nächste hinterlegte umgehängt.
        var stalled = job.EngineId;
        var moved = await BackgroundEngineRotation.SwitchAsync(db, job, EngineOwnerOf(job), _explicitOnly, CancellationToken.None);
        // Der Fehlversuchs-Zähler (ClassifyStall) ist nötig, damit ein durchweg unbrauchbarer Auftrag nicht
        // ewig im Kreis läuft; er wird bei jedem Lauf MIT Tiefenfortschritt wieder auf 0 gesetzt, eine bloß
        // langsame tiefe Suche kann also nicht daran scheitern.
        _logger.LogWarning(
            "AnalysisJob {JobId}: Stillstand — seit {Timeout} keine neue Datenzeile, nur Wiederholungen "
            + "({DataLines} Datenzeilen, {Heartbeats} Lebenszeichen, Tiefe {Depth}/{Target}); "
            + "Engine {Stalled} → {Next}",
            job.Id, _stallTimeout, stream.Tally.DataLines, stream.Tally.Heartbeats, stream.CurrentDepth, job.TargetDepth,
            stalled, moved ? job.EngineId : "(keine andere hinterlegt)");
        await AnalysisJobTransitions.ApplyAsync(db, job, AnalysisJobRunEnd.ClassifyStall(stream.GotData, job.FruitlessAttempts, stream.CurrentDepth));
    }

    /// <summary>Unerwarteter Fehler im Lauf: der Auftrag steht jetzt auf „Running", ohne dass etwas läuft — und Running
    /// wird nirgends wieder aufgegriffen (nur ResetInterruptedAsync beim Start). Also hier zurückstellen, mit eigenem
    /// Scope: der DbContext des Laufs kann genau der sein, der eben geworfen hat.</summary>
    private async Task RecoverAfterErrorAsync(int jobId, Exception ex)
    {
        _logger.LogError(ex, "AnalysisJob {JobId}: Lauf fehlgeschlagen", jobId);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.AnalysisJobs.FirstOrDefaultAsync(j => j.Id == jobId, CancellationToken.None);
            if (job is { Status: AnalysisJobStatus.Running })
                await AnalysisJobTransitions.PauseAsync(db, job, "Unerwarteter Fehler im Lauf", TimeSpan.FromMinutes(1));
        }
        catch (Exception cleanupEx)
        {
            _logger.LogError(cleanupEx, "AnalysisJob {JobId}: Zurückstellen nach Fehler misslungen", jobId);
        }
    }

    /// <summary>Eine Datenzeile in den Zwischenstand-Schreiber geben: bester Zug (MultiPV 1) mit Bewertung aus Sicht der Seite am Zug.</summary>
    internal static void ObserveStep(NodeStepRecorder recorder, string line, string fen)
    {
        if (AnalysisJobStream.NodesOf(line) is not { } nodes) return;
        if (BrokerCandidates.Parse(line, fen) is not { Count: > 0 } cands) return;
        recorder.Observe(nodes, cands[0].Uci, cands[0].Cp, cands[0].Mate);
    }

    public override void Dispose()
    {
        _tracker.LiveStarted -= PauseEngine;
        if (_pauseSwitch is not null) _pauseSwitch.Changed -= OnPauseChanged;
        base.Dispose();
    }
}
