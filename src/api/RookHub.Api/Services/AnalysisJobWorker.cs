using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Services;

/// <summary>Reine, testbare Bausteine des Workers: Zeilen des Broker-Streams lesen und entscheiden,
/// ob eine Zeile das gespeicherte Ergebnis ersetzt.</summary>
public static class AnalysisJobStream
{
    /// <summary>Tiefe einer ndjson-Zeile (<c>{"depth": n, …}</c>); null bei Leer-/Heartbeat-/kaputter Zeile.</summary>
    public static int? DepthOf(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("depth", out var d) && d.ValueKind == JsonValueKind.Number
                ? d.GetInt32() : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Suchtempo einer ndjson-Zeile (Knoten/Sekunde) aus <c>nodes</c> + verstrichener <c>time</c> (ms);
    /// null, wenn eines fehlt oder 0 ist (die ersten Zeilen tragen oft time=0).</summary>
    public static int? NpsOf(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            if (!r.TryGetProperty("nodes", out var n) || n.ValueKind != JsonValueKind.Number) return null;
            if (!r.TryGetProperty("time", out var t) || t.ValueKind != JsonValueKind.Number) return null;
            var nodes = n.GetInt64(); var ms = t.GetInt64();
            return ms > 0 && nodes > 0 ? (int)Math.Min(int.MaxValue, nodes * 1000 / ms) : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Knotenzahl einer ndjson-Zeile; null, wenn sie fehlt.</summary>
    public static long? NodesOf(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            return r.ValueKind == JsonValueKind.Object && r.TryGetProperty("nodes", out var n) && n.ValueKind == JsonValueKind.Number
                ? n.GetInt64() : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Knotenziel erreicht? Meldet die Engine selbst das Ende (<paramref name="streamEnded"/>), genügt knapp
    /// darunter (Lc0 hört bei <c>go nodes N</c> teils ein paar Knoten früher auf); sonst muss das Ziel voll da sein.</summary>
    public static bool NodeGoalMet(long target, long nodes, bool streamEnded) =>
        nodes >= target || (streamEnded && nodes >= target - target / 20);

    /// <summary>Trägt die Zeile ein <c>bestmove</c>? So endet eine Suche, die die Engine SELBST beendet hat — aber
    /// ebenso eine, die von außen gestoppt wurde (UCI antwortet auf <c>stop</c> mit <c>bestmove</c>).</summary>
    public static bool HasBestMove(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("bestmove", out var b) && b.ValueKind == JsonValueKind.String;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// Hat die Engine einen Knotenauftrag VOR dem Ziel aus eigenem Ermessen beendet (0.681.1, gemeldet 2026-10-06: Lc0
    /// rechnete dieselben Stellungen alle 30 s neu, ohne je fertig zu werden)? Dann ist das Ergebnis endgültig.
    ///
    /// <para>Lc0 bricht eine Suche mit Knotenlimit ab, sobald der beste Zug mit dem Restbudget nicht mehr kippen kann
    /// (Smart Pruning, ab Werk an): bei 50 000 Knoten oft schon bei 39 000. Ein neuer Lauf hört an derselben Stelle
    /// wieder auf. Ein <c>bestmove</c> unter dem Ziel kommt aber AUCH, wenn jemand die Suche von außen stoppt (UCI
    /// antwortet auf <c>stop</c> mit <c>bestmove</c>) — die Frage ist also, ob wir einen solchen Stopp sehen würden:</para>
    /// <list type="bullet">
    /// <item><b>Direkt angebunden</b> (<c>rhe_…</c>): jede Arbeit für diese Engine läuft über UNSEREN Broker. Ein Stopp von
    /// außen wäre unser eigener — der Worker hat abgebrochen (<paramref name="cancelledByUs"/>) oder eine Live-Analyse
    /// rechnet auf der Engine (<paramref name="liveOnEngine"/>). Ist beides nicht der Fall, hat die Engine selbst
    /// aufgehört: EIN Lauf genügt, nichts wird doppelt gerechnet.</item>
    /// <item><b>Über Lichess</b> (<c>eei_…</c>): dort kann jemand die Engine im Analysebrett von lichess.org benutzen, und
    /// davon erfahren wir nichts. Erst ein zweiter Lauf, der wieder sauber endet und nicht spürbar weiter kommt (höchstens
    /// 10 % mehr Knoten), beweist das eigene Ende — eine gestoppte Suche käme beim nächsten Versuch weiter.</item>
    /// </list>
    /// </summary>
    public static bool EngineEndedOwnSearch(bool directEngine, bool sawBestMove, bool cancelledByUs, bool liveOnEngine,
        long? previousNodes, long nodes)
    {
        if (!sawBestMove || cancelledByUs || liveOnEngine || nodes <= 0) return false;
        return directEngine || StoppedEarlyAgain(previousNodes, nodes);
    }

    /// <summary>Zweiter Lauf an derselben Stelle: höchstens 10 % mehr Knoten als der vorige (siehe
    /// <see cref="EngineEndedOwnSearch"/>, Fall Lichess).</summary>
    public static bool StoppedEarlyAgain(long? previousNodes, long nodes)
        => previousNodes is long prev && prev > 0 && nodes > 0 && nodes <= prev + prev / 10;

    /// <summary>Eine Fortsetzung (und ein Neustart mit mehr Linien) liefert die flachen Iterationen erneut —
    /// übernommen wird nur, was mindestens so tief ist wie das gespeicherte Ergebnis.</summary>
    public static bool ShouldPersist(int lineDepth, int reachedDepth) => lineDepth >= reachedDepth;

    /// <summary>Liest den ndjson-Stream zeilenweise und ruft <paramref name="onLine"/> für jede Zeile mit Tiefe.
    /// Endet mit dem Stream oder per Abbruch (Exception wird durchgereicht). <paramref name="tally"/> zählt
    /// mit, WAS über die Leitung kam — beim Abriss ist das die entscheidende Frage.</summary>
    public static async Task ConsumeAsync(Stream ndjson, Func<string, int, Task> onLine, CancellationToken ct,
                                          StreamTally? tally = null)
    {
        using var reader = new StreamReader(ndjson);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            var now = DateTime.UtcNow;
            tally?.Note(line, now);
            if (DepthOf(line) is int depth)
                await onLine(line, depth);
        }
    }
}

/// <summary>Zählwerk EINES Stream-Laufs. Beantwortet beim Abriss die Frage, an der sich die Ursachen
/// scheiden: war die Leitung vorher stumm (dann kappt jemand eine untätige Verbindung) oder lief bis
/// zuletzt Verkehr (dann ist es kein Untätigkeits-Timeout)? Lebenszeichen sind Leerzeilen (Proxy),
/// <c>{"keepalive":true}</c> (Provider, vom Broker durchgereicht) und zeichengleich wiederholte
/// Datenzeilen (älterer RookHub-Provider); Zeilen ohne Tiefe sind alles Übrige.</summary>
public sealed class StreamTally
{
    public int DataLines { get; private set; }
    public int Heartbeats { get; private set; }
    public int OtherLines { get; private set; }
    public DateTime? LastDataUtc { get; private set; }
    public DateTime? LastAnyUtc { get; private set; }

    private string? _lastData;

    /// <summary>Eine Datenzeile, die ZEICHENGLEICH ihre Vorgaengerin wiederholt, ist KEIN Fortschritt,
    /// sondern ein Lebenszeichen: der RookHub-Provider bis 0.478.10 sendete die letzte weitergegebene
    /// <c>info</c>-Zeile erneut, wenn nach oben laenger nichts ging, und auf fremden Rechnern laeuft er
    /// weiter. Eine rechnende Engine kann sich nicht wiederholen: <c>time</c> und <c>nodes</c> wandern
    /// mit jeder Zeile. Das zu unterscheiden ist die Grundlage des Stillstands-Waechters — ohne sie
    /// sieht eine haengende Engine genauso aus wie eine, die gerade an einer tiefen Iteration rechnet.</summary>
    public static bool IsRepeat(string? line, string? previous) => previous is not null && line == previous;

    /// <summary>Das Lebenszeichen des offiziellen Providers (seit lichess-org/external-engine d0eeb242):
    /// alle 15 s <c>{"keepalive":true}</c>, vom Broker als eigene Zeile an den Empfaenger weitergereicht.
    /// Es traegt keine Tiefe und stellt den Stillstands-Waechter deshalb NICHT neu — gezaehlt wird es
    /// trotzdem als Lebenszeichen, sonst meldete der Abriss-Log eine stumme Leitung, die keine war.</summary>
    public static bool IsKeepalive(string line) =>
        line.AsSpan().TrimStart().StartsWith("{\"keepalive\"", StringComparison.Ordinal);

    public void Note(string line, DateTime nowUtc)
    {
        LastAnyUtc = nowUtc;
        if (string.IsNullOrWhiteSpace(line) || IsKeepalive(line)) { Heartbeats++; return; }
        if (AnalysisJobStream.DepthOf(line) is not null)
        {
            if (IsRepeat(line, _lastData)) { Heartbeats++; return; }   // Wiederholung = Lebenszeichen
            _lastData = line;
            DataLines++; LastDataUtc = nowUtc; return;
        }
        OtherLines++;
    }

    /// <summary>Sekunden seit der letzten ZEILE MIT DATEN (null, wenn nie eine kam).</summary>
    public double? DataGapSeconds(DateTime nowUtc) => LastDataUtc is { } t ? (nowUtc - t).TotalSeconds : null;

    /// <summary>Sekunden seit dem letzten Byte überhaupt — inklusive Lebenszeichen.</summary>
    public double? AnyGapSeconds(DateTime nowUtc) => LastAnyUtc is { } t ? (nowUtc - t).TotalSeconds : null;
}

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
/// </summary>
public class AnalysisJobWorker : BackgroundService, IAnalysisJobControl
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

    public AnalysisJobWorker(IServiceScopeFactory scopeFactory, EngineActivityTracker tracker,
        IEngineBroker broker, ILogger<AnalysisJobWorker> logger, IConfiguration config, AnalysisJobLive live)
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
            try { await TickAsync(stoppingToken); }
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

    private async Task RunJobAsync(Running run, int jobId, CancellationToken shutdown)
    {
        var ct = run.Cts.Token;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.AnalysisJobs.FirstOrDefaultAsync(j => j.Id == jobId, CancellationToken.None);
            if (job is null) return;

            // Zwischen der Live-Prüfung im Tick und dem Eintrag in _running liegen DB-Roundtrips. Startet in
            // diesem Fenster ein Live-Stream auf DIESER Engine, lief sein LiveStarted ins Leere (kein Eintrag
            // zum Abbrechen) — ohne diese zweite Prüfung rechnete der Hintergrund daneben weiter.
            if (_tracker.IsEngineBusy(run.EngineId))
            {
                await PauseAsync(db, job, null);
                return;
            }

            // Token und Engine-Registrierung kommen vom ENGINE-BESITZER: bei einer eingeworfenen
            // Punktepartie ohne eigene Engine ist das nicht der Auftraggeber, sondern das Haus-Konto.
            // Eine Engine „RookHub direkt" (rhe_) braucht keinen Token, eine Lichess-Engine (eei_) schon.
            var engineOwnerId = job.EngineOwnerUserId ?? job.UserId;
            // Fremde Rechenzeit nur, solange die Freigabe gilt (A4-004): schon eingereihte Auftraege fremder Nutzer
            // liefen sonst nach dem Zuruecknehmen der Haus-Engine (oder dem Verlust der Admin-Rolle) weiter.
            if (job.EngineOwnerUserId is int houseOwner
                && !await EngineOwnerResolver.IsHouseEngineSharedAsync(db, houseOwner, ct))
            {
                await FailAsync(db, job, EngineOwnerResolver.HouseEngineWithdrawnError);
                return;
            }
            var registry = scope.ServiceProvider.GetRequiredService<EngineRegistry>();
            EngineLookup lookup;
            try { lookup = await registry.ResolveAsync(engineOwnerId, job.EngineId, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Live hat begonnen / gelöscht / Shutdown — MUSS vor dem Filter darunter stehen, sonst wäre
                // der eigene Abbruch als „Lichess nicht erreichbar" mit 60 s Backoff im Auftrag gelandet.
                await PauseAsync(db, job, null);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await BackoffAsync(db, job, "Lichess nicht erreichbar", TimeSpan.FromSeconds(60));
                return;
            }
            if (lookup.Failure == EngineLookupFailure.NoToken)
            {
                await FailAsync(db, job, "Kein Lichess-Token hinterlegt");
                return;
            }
            if (lookup.Engine is not { } engine)
            {
                await FailAsync(db, job, "Engine nicht (mehr) registriert");
                return;
            }

            job.Status = AnalysisJobStatus.Running;
            job.LastRunAt = DateTime.UtcNow;
            job.LastError = null;
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);

            var work = new EngineWork(
                SessionId: $"rh-bg-{job.UserId}", Threads: Math.Max(1, engine.MaxThreads),
                Hash: Math.Clamp(engine.MaxHash, 16, 32768),
                // Das Protokoll erlaubt 1..5; ein größerer Wert würde vom Broker abgewiesen und der Auftrag
                // liefe endlos in die Wiederholung. Zweiter Riegel neben AnalysisJobService.MaxMultiPv.
                MultiPv: Math.Clamp(job.MultiPv, 1, EngineProtocol.MaxMultiPv),
                // Knotenauftrag: `nodes` statt `depth` — GENAU EINES der drei Limits geht an die Engine
                // (EngineWork.ToJson/WorkSanitizer). Eine Tiefe obendrauf liefe bei Lc0 ins Leere.
                InitialFen: job.Fen, Moves: [],
                Depth: job.TargetNodes is null ? job.TargetDepth : null, Nodes: job.TargetNodes);

            EngineAnalysisSession upstream;
            // Frist NUR für die Antwort-KOPFZEILEN: der HttpClient des Brokers ist bewusst timeout-los
            // (eine Suche darf Stunden dauern), und der `firstLine`-Wächter unten beginnt erst NACH den
            // Headern. Antwortet der Broker mit Verbindungsaufbau, aber ohne Header — er wartet auf einen
            // Provider, der nie kommt —, stand `RunJobAsync` hier unbegrenzt: der Auftrag blieb in der
            // Datenbank auf „läuft" mit Tiefe 0, die Engine blieb belegt, und alle weiteren Aufträge
            // dieser Engine warteten mit. Auflösbar war das nur durch einen Live-Stream oder Neustart.
            using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            headerCts.CancelAfter(_firstLineTimeout);
            try { upstream = await _broker.AnalyseAsync(engine, work, headerCts.Token); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { await PauseAsync(db, job, null); return; }
            catch (OperationCanceledException)
            {
                // Nur die Kopfzeilen-Frist ist abgelaufen (der Job-Token ist nicht abgebrochen).
                _logger.LogWarning("AnalysisJob {JobId}: Broker antwortete nicht binnen {Timeout} (keine Kopfzeilen) — pausiert",
                    job.Id, _firstLineTimeout);
                await PauseAsync(db, job, "Broker antwortete nicht", TimeSpan.FromMinutes(5));
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await BackoffAsync(db, job, "Broker nicht erreichbar", TimeSpan.FromSeconds(60));
                return;
            }

            await using (upstream)
            {
                if (!upstream.IsSuccess)
                {
                    var code = upstream.StatusCode;
                    // Der EIGENE Broker weist mit 400 nur ab, was die Stellung selbst betrifft (Sanitizer:
                    // Gegner im Schach, falsche Rochaderechte …) — das ändert sich durch Warten nicht. Bei
                    // Lichess bleibt es beim bisherigen Backoff.
                    if (code == 400 && engine.Source == EngineSource.Local)
                    {
                        await FailAsync(db, job, $"Stellung abgewiesen: {upstream.Error}");
                        return;
                    }
                    // Lichess sagt nicht, WORAN es liegt — meist an einer Stellung, die Gera laedt und lila-engine
                    // abweist (Gegner im Schach …), und die aendert sich durch Warten nicht. Ohne Zaehler lief so ein
                    // Auftrag ewig im Zwei-Minuten-Takt gegen Lichess und belegte einen offenen Platz (A4-006). Nicht
                    // sofort Failed wie beim eigenen Broker: nach MaxFruitlessAttempts Anlaeufen (Gutschrift bei Fortschritt).
                    if (code == 400 && ++job.FruitlessAttempts >= AnalysisJob.MaxFruitlessAttempts)
                    {
                        await FailAsync(db, job, $"Broker antwortete {code} in {job.FruitlessAttempts} Läufen");
                        return;
                    }
                    // 503/504 heisst beim Broker: fuer DIESE Engine ist gerade kein Provider
                    // verbunden. Das ist eine Aussage ueber die ENGINE und nicht ueber den Auftrag,
                    // also umhaengen statt zwei Minuten dieselbe Tuer anzuklopfen. Am 2026-09-12 auf
                    // Prod gemessen: 309 solcher Antworten in 25 Minuten, und 24 von 32 Auftraegen
                    // pendelten dabei im Zwei-Minuten-Takt gegen die zwoelf Engines der zweiten
                    // Maschine, waehrend die vier der ersten allein weiterrechneten.
                    var alt = job.EngineId;
                    if (code is 503 or 504
                        && await SwitchEngineAsync(db, job, engineOwnerId, CancellationToken.None))
                    {
                        await PauseAsync(db, job, $"Broker antwortete {code} — andere Engine", _engineSwitchBackoff);
                        _logger.LogInformation(
                            "AnalysisJob {JobId}: Broker antwortete {Code} fuer {Old} — weiter auf {New}",
                            job.Id, code, alt, job.EngineId);
                        return;
                    }
                    await BackoffAsync(db, job, $"Broker antwortete {code}", TimeSpan.FromSeconds(120));
                    return;
                }
                var runStart = DateTime.UtcNow;
                // Anzeige-Stand ohne Datenbank: die Zeit läuft ab HIER, auch wenn die Engine noch schweigt.
                _live.Start(job.Id, job.UserId, job.SecondsSpent, runStart);
                var lastPersist = runStart;
                // Knotenauftrag: die Suche beginnt bei Tiefe 1 von vorn, ein Rest-Stand aus einem früheren Lauf
                // (andere Zählung, kalte Hashtabelle) würde sonst alle flacheren Zeilen verwerfen.
                // Knoten des VORIGEN Laufs (seine letzte Zeile steht noch im Ergebnis) — siehe StoppedEarlyAgain.
                var previousNodes = job.TargetNodes is not null && job.ResultJson is { } previous
                    ? AnalysisJobStream.NodesOf(previous) : null;
                if (job.TargetNodes is not null) job.ReachedDepth = 0;
                var depthAtStart = job.ReachedDepth;
                long currentNodes = 0; var nodeGoalReached = false; var engineFinished = false;
                // Knotenauftrag: Zwischenstände je Schwelle mitschreiben — dieselben Zeilen, keine Mehrrechnung.
                var recorder = job.TargetNodes is not null && _snapshotStep > 0
                    ? new NodeStepRecorder(_snapshotStep, NodeSteps.Parse(job.NodeStepsJson)) : null;
                string? pendingLine = null; var pendingDepth = job.ReachedDepth;
                var currentDepth = 0; var currentNps = 0;
                var gotData = false;
                string? lastLine = null;
                var tally = new StreamTally();
                // Wächter gegen STILLSTAND. Bis zur ersten Datenzeile gilt die kurze Frist (ein Provider,
                // der gar nicht rechnet, soll den Slot nicht halten); danach die lange, und sie wird bei
                // jeder FRISCHEN Zeile neu gestellt. Eine zeichengleich wiederholte Zeile stellt sie NICHT
                // neu — das ist das Lebenszeichen des Providers, nicht die Engine. Früher wurde der Wächter
                // nach der ersten Zeile für immer entschärft; eine Engine, die danach hängen blieb, hielt
                // ihren Auftrag und (über die Ein-Partie-Pumpe) die ganze Warteschlange unbegrenzt fest.
                using var silence = new CancellationTokenSource(_firstLineTimeout);
                using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct, silence.Token);
                var streamCt = streamCts.Token;
                try
                {
                    var stream = upstream.Ndjson!;
                    await AnalysisJobStream.ConsumeAsync(stream, async (line, depth) =>
                    {
                        // Nur eine FRISCHE Zeile stellt den Wächter neu — die Wiederholung ist ein Lebenszeichen.
                        if (!StreamTally.IsRepeat(line, lastLine)) { gotData = true; silence.CancelAfter(_stallTimeout); }
                        lastLine = line;
                        // Laufender Stand IMMER mitschreiben — auch wenn die Zeile flacher ist als das Ergebnis.
                        // Nach einer Fortsetzung rechnet die Engine erst wieder von Tiefe 1 hoch; ohne das stünde
                        // die Anzeige minutenlang still (keine Tiefe, kein Tempo, nicht einmal die Zeit lief mit).
                        currentDepth = depth;
                        currentNps = AnalysisJobStream.NpsOf(line) ?? currentNps;
                        currentNodes = AnalysisJobStream.NodesOf(line) ?? currentNodes;
                        if (AnalysisJobStream.HasBestMove(line)) engineFinished = true;
                        if (recorder is not null) ObserveStep(recorder, line, job.Fen);
                        _live.Update(job.Id, currentDepth, currentNps);
                        var keep = AnalysisJobStream.ShouldPersist(depth, job.ReachedDepth);
                        if (keep) { pendingLine = line; pendingDepth = depth; }
                        var now = DateTime.UtcNow;
                        if ((keep && depth > job.ReachedDepth) || now - lastPersist >= _persistInterval)
                        {
                            if (recorder is { Dirty: true }) job.NodeStepsJson = NodeSteps.ToJson(recorder.Snapshot());
                            var rest = await PersistProgressAsync(db, job, pendingLine, pendingDepth, now - lastPersist,
                                                                  currentDepth, currentNps);
                            lastPersist = now - rest; pendingLine = null;   // angebrochene Sekunde mitnehmen
                            // Ziel/Linien können sich unterdessen geändert haben (Service in eigenem Scope).
                            await db.Entry(job).ReloadAsync(CancellationToken.None);
                        }
                        // Knotenauftrag: Selbst-Abbruch, sobald das Ziel da ist — die Zeile steht oben schon als
                        // pendingLine und wird nach dem Stream gesichert. (Die Engine bekommt `nodes` mit und endet
                        // meist selbst; das hier ist der Riegel für eine, die das Limit überzieht.)
                        if (job.TargetNodes is { } nodeTarget && currentNodes >= nodeTarget)
                        {
                            nodeGoalReached = true;
                            streamCts.Cancel();
                        }
                        // BEWUSST kein Selbst-Abbruch bei erreichter Zieltiefe (gilt für TIEFENaufträge): die Engine bekommt `depth` als
                        // Limit mitgeschickt und beendet den Stream selbst. Bräche der Worker schon bei der
                        // ERSTEN Zeile der Zieltiefe ab, trüge nur die Hauptvariante diese Tiefe — die Linien
                        // 2..K blieben eine Iteration flacher (jede pv hat ihre eigene Tiefe).
                    }, streamCt, tally);
                }
                catch (OperationCanceledException) when (silence.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    if (!gotData)
                    {
                        await PauseAsync(db, job, "Engine lieferte keine Daten", TimeSpan.FromMinutes(5));
                        _logger.LogWarning("AnalysisJob {JobId}: keine Datenzeile binnen {Timeout} — pausiert",
                            job.Id, _firstLineTimeout);
                        return;
                    }
                    // Die Engine HAT gerechnet und ist dann stehen geblieben. Das ist eine Aussage über die
                    // ENGINE, nicht über die Stellung — die kürzeste Schlange wählte ausgerechnet sie erneut
                    // (sie hat ja nichts zu tun), deshalb wird hier auf die nächste hinterlegte umgehängt.
                    var stalled = job.EngineId;
                    var moved = await SwitchEngineAsync(db, job, engineOwnerId, CancellationToken.None);
                    // Der Zähler ist nötig, damit ein durchweg unbrauchbarer Auftrag nicht ewig im Kreis
                    // läuft; er wird bei jedem Lauf MIT Tiefenfortschritt wieder auf 0 gesetzt, eine bloß
                    // langsame tiefe Suche kann also nicht daran scheitern.
                    job.FruitlessAttempts++;
                    _logger.LogWarning(
                        "AnalysisJob {JobId}: Stillstand — seit {Timeout} keine neue Datenzeile, nur Wiederholungen "
                        + "({DataLines} Datenzeilen, {Heartbeats} Lebenszeichen, Tiefe {Depth}/{Target}); "
                        + "Engine {Stalled} → {Next}",
                        job.Id, _stallTimeout, tally.DataLines, tally.Heartbeats, currentDepth, job.TargetDepth,
                        stalled, moved ? job.EngineId : "(keine andere hinterlegt)");
                    if (job.FruitlessAttempts >= AnalysisJob.MaxFruitlessAttempts)
                        await FailAsync(db, job, $"Engine blieb in {job.FruitlessAttempts} Läufen stehen");
                    else
                        await PauseAsync(db, job, $"Engine blieb bei Tiefe {currentDepth} stehen", TimeSpan.FromSeconds(60));
                    return;
                }
                catch (OperationCanceledException) { /* Pause (Live), Löschung oder Shutdown */ }
                catch (IOException ex)
                {
                    // Die Zahlen entscheiden die Ursachenfrage: kam bis zuletzt Verkehr, ist es KEIN
                    // Untätigkeits-Timeout; war die Leitung stumm, schon. Deshalb beides getrennt —
                    // Datenzeilen und Lebenszeichen (Leerzeilen) haben unterschiedliche Aussagekraft.
                    var now = DateTime.UtcNow;
                    _logger.LogWarning(ex,
                        "AnalysisJob {JobId}: Stream abgerissen nach {RunSeconds:F0} s — {DataLines} Datenzeilen, "
                        + "{Heartbeats} Lebenszeichen, {OtherLines} sonstige; letzte Datenzeile vor {DataGap} s, "
                        + "letztes Byte vor {AnyGap} s (Engine {EngineId}, Tiefe {Depth}/{Target})",
                        job.Id, (now - runStart).TotalSeconds, tally.DataLines, tally.Heartbeats, tally.OtherLines,
                        Fmt(tally.DataGapSeconds(now)), Fmt(tally.AnyGapSeconds(now)), run.EngineId,
                        currentDepth, job.TargetDepth);
                }

                var endedAt = DateTime.UtcNow;
                // Auch das saubere Ende protokollieren — ohne Vergleichsmaßstab sagen die Abriss-Zahlen nichts.
                _logger.LogInformation(
                    "AnalysisJob {JobId}: Lauf beendet nach {RunSeconds:F0} s — {DataLines} Datenzeilen, "
                    + "{Heartbeats} Lebenszeichen, {OtherLines} sonstige; letzte Datenzeile vor {DataGap} s "
                    + "(Engine {EngineId}, Tiefe {Depth}/{Target})",
                    job.Id, (endedAt - runStart).TotalSeconds, tally.DataLines, tally.Heartbeats, tally.OtherLines,
                    Fmt(tally.DataGapSeconds(endedAt)), run.EngineId, currentDepth, job.TargetDepth);

                if (recorder is not null)
                {
                    recorder.Finish(job.TargetNodes);
                    if (recorder.Dirty) job.NodeStepsJson = NodeSteps.ToJson(recorder.Snapshot());
                }
                await PersistProgressAsync(db, job, pendingLine, pendingLine is null ? job.ReachedDepth : pendingDepth,
                                           DateTime.UtcNow - lastPersist, currentDepth, currentNps);

                await db.Entry(job).ReloadAsync(CancellationToken.None);
                var directEngine = run.EngineId.StartsWith(ExternalEngineRegistration.IdPrefix, StringComparison.Ordinal);
                var endedOwnSearch = job.TargetNodes is { } earlyGoal && !nodeGoalReached
                    && !AnalysisJobStream.NodeGoalMet(earlyGoal, currentNodes, streamEnded: true)
                    && AnalysisJobStream.EngineEndedOwnSearch(directEngine, engineFinished, ct.IsCancellationRequested,
                        _tracker.IsEngineBusy(run.EngineId), previousNodes, currentNodes);
                var goalMet = job.TargetNodes is { } goalNodes
                    ? nodeGoalReached || endedOwnSearch
                      || (!ct.IsCancellationRequested && gotData && AnalysisJobStream.NodeGoalMet(goalNodes, currentNodes, streamEnded: true))
                    : job.ReachedDepth >= job.TargetDepth;
                if (endedOwnSearch)
                    _logger.LogInformation(
                        "AnalysisJob {JobId}: Engine beendet die Suche selbst vor dem Ziel ({Nodes} von {Target} Knoten, z. B. Lc0 "
                        + "Smart Pruning) — Ergebnis gilt", job.Id, currentNodes, job.TargetNodes);
                if (goalMet)
                {
                    job.Status = AnalysisJobStatus.Done; job.FinishedAt = DateTime.UtcNow; job.UpdatedAt = job.FinishedAt.Value;
                    job.FruitlessAttempts = 0;
                    job.CurrentDepth = 0; job.CurrentNps = 0;   // es rechnet nichts mehr
                    await db.SaveChangesAsync(CancellationToken.None);
                    _logger.LogInformation("AnalysisJob {JobId}: fertig bei Tiefe {Depth} ({Nodes} Knoten)", job.Id, job.ReachedDepth, currentNodes);
                }
                else if (ct.IsCancellationRequested)
                {
                    await PauseAsync(db, job, null);   // Live hatte Vorrang / gelöscht / Shutdown — kein Fehlversuch
                }
                else if (job.ReachedDepth > depthAtStart)
                {
                    // Der Broker hat mitten in der Rechnung abgebrochen, aber es ging voran → einfach weiter.
                    job.FruitlessAttempts = 0;
                    await PauseAsync(db, job, "Stream vor der Zieltiefe beendet", TimeSpan.FromSeconds(30));
                }
                else if (_tracker.IsEngineBusy(run.EngineId))
                {
                    // Kein Fortschritt, WEIL der Nutzer live rechnet: teilen sich Live und Hintergrund dieselbe
                    // Engine (nur eine registriert), verdrängt jede Live-Anfrage den laufenden Auftrag — der
                    // Stream endet dann von SELBST (nicht durch unser Cancel) und sähe wie ein Fehlschlag aus.
                    // Nach drei solchen Runden hätte der Auftrag fälschlich als „gescheitert" gegolten.
                    await PauseAsync(db, job, null, TimeSpan.FromSeconds(30));
                }
                else if (DateTime.UtcNow - runStart >= _fruitlessMinRuntime)
                {
                    // LANG gerechnet und trotzdem keine tiefere Zeile: keine Sackgasse, sondern eine Umgebung, die
                    // den Stream vorzeitig kappt — der klassische Fall ist der Wachhund des offiziellen Providers,
                    // der seinen „zuletzt benutzt"-Stempel erst am Stream-ENDE setzt und eine Suche, die länger als
                    // `--keep-alive` dauert, mitten im Rechnen terminiert (bei Tiefe 29+ mit 5 Linien der Normalfall;
                    // Abhilfe dort: KEEP_ALIVE hochsetzen). Das darf NICHT als Fehlversuch zählen, sonst gilt ein
                    // völlig gesunder Auftrag nach drei Runden als gescheitert — genau so gesehen (Job bei 29/40).
                    await PauseAsync(db, job, "Stream vorzeitig beendet — wird fortgesetzt", TimeSpan.FromSeconds(30));
                }
                else
                {
                    // Kein Fortschritt in KURZER Zeit: das ist die deterministische Sackgasse (Matt-/Patt-Stellung,
                    // vom Provider abgelehnte Arbeit) — dort endet der Stream sofort. Ohne Zähler liefe der Auftrag
                    // ewig im 30-s-Takt gegen dieselbe Wand.
                    job.FruitlessAttempts++;
                    if (job.FruitlessAttempts >= AnalysisJob.MaxFruitlessAttempts)
                        await FailAsync(db, job, $"Engine lieferte in {job.FruitlessAttempts} Läufen sofort keine Bewertung");
                    else
                        await PauseAsync(db, job, "Stream vor der Zieltiefe beendet", TimeSpan.FromSeconds(30));
                }
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            // Auftrag wurde parallel gelöscht — nichts zu retten.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "AnalysisJob {JobId}: Lauf fehlgeschlagen", jobId);
            // Der Auftrag steht jetzt auf „Running", ohne dass etwas läuft — und Running wird nirgends wieder
            // aufgegriffen (nur ResetInterruptedAsync beim Start). Also hier zurückstellen, mit eigenem Scope:
            // der DbContext des Laufs kann genau der sein, der eben geworfen hat.
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var job = await db.AnalysisJobs.FirstOrDefaultAsync(j => j.Id == jobId, CancellationToken.None);
                if (job is { Status: AnalysisJobStatus.Running })
                    await PauseAsync(db, job, "Unerwarteter Fehler im Lauf", TimeSpan.FromMinutes(1));
            }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx, "AnalysisJob {JobId}: Zurückstellen nach Fehler misslungen", jobId);
            }
        }
        finally
        {
            _live.Stop(jobId);
            _running.TryRemove(new KeyValuePair<string, Running>(run.EngineId, run));
            run.Cts.Dispose();
            _wake.Wake();   // die Engine ist frei — nächsten Auftrag sofort holen, nicht erst beim nächsten Tick
        }
    }

    /// <summary>Sekunden auf eine Stelle, „—" wenn es den Messwert nicht gibt (nie eine Zeile gesehen).</summary>
    private static string Fmt(double? seconds) => seconds is { } v ? v.ToString("F1") : "—";

    /// <summary>Ergebnis + Rechenzeit sichern. Gibt zurück, wie viel Zeit NICHT verbucht wurde (der Bruchteil
    /// unter einer Sekunde) — der Aufrufer schiebt ihn ins nächste Intervall, sonst summierte sich bei jedem
    /// Persist ein verlorener Rest, und in der ersten Sekunden-Salve flacher Tiefen ginge fast alles verloren.</summary>
    /// <summary>Eine Datenzeile in den Zwischenstand-Schreiber geben: bester Zug (MultiPV 1) mit Bewertung aus Sicht der Seite am Zug.</summary>
    internal static void ObserveStep(NodeStepRecorder recorder, string line, string fen)
    {
        if (AnalysisJobStream.NodesOf(line) is not { } nodes) return;
        if (BrokerCandidates.Parse(line, fen) is not { Count: > 0 } cands) return;
        recorder.Observe(nodes, cands[0].Uci, cands[0].Cp, cands[0].Mate);
    }

    private static async Task<TimeSpan> PersistProgressAsync(AppDbContext db, AnalysisJob job, string? line, int depth,
        TimeSpan elapsed, int currentDepth = 0, int currentNps = 0)
    {
        if (currentDepth > 0) job.CurrentDepth = currentDepth;
        if (currentNps > 0) job.CurrentNps = currentNps;
        if (line is not null)
        {
            job.ResultJson = line;
            job.ReachedDepth = Math.Max(job.ReachedDepth, depth);
            job.EvalText = AnalysisJobService.EvalTextOf(line);   // Listen zeigen nur diesen Wert, ohne die Roh-Zeile zu laden
        }
        var whole = (int)Math.Max(0, elapsed.TotalSeconds);
        job.SecondsSpent += whole;
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        return elapsed > TimeSpan.Zero ? elapsed - TimeSpan.FromSeconds(whole) : TimeSpan.Zero;
    }

    /// <summary>Den Auftrag auf die NAECHSTE hinterlegte Hintergrund-Engine umhaengen, nachdem die
    /// bisherige stehen geblieben ist. Gibt zurueck, ob gewechselt wurde (gespeichert wird erst vom
    /// aufrufenden Pause/Fail).
    /// <para>Bewusst REIHUM und nicht ueber die kuerzeste Schlange: eine haengende Engine hat gerade gar
    /// nichts zu tun und gewaenne jeden Schlangenvergleich — der Auftrag liefe ihr sofort wieder in die
    /// Arme. Bewusst nur, wenn die aktuelle Engine ueberhaupt in der Liste steht: eine von Hand
    /// festgelegte Engine ist eine Entscheidung des Nutzers und bleibt.</para></summary>
    private async Task<bool> SwitchEngineAsync(AppDbContext db, AnalysisJob job, int engineOwnerId, CancellationToken ct)
    {
        var cred = await db.LichessEngineCredentials.FirstOrDefaultAsync(c => c.UserId == engineOwnerId, ct);
        if (NextEngineAfter(cred?.BackgroundEngines ?? [], job.EngineId, _explicitOnly) is not { } next) return false;
        job.EngineId = next;
        return true;
    }

    /// <summary>Die naechste Engine REIHUM nach <paramref name="current"/>; <c>null</c>, wenn es keinen
    /// Wechsel gibt — weil nur eine hinterlegt ist oder weil die aktuelle gar nicht in der Liste steht
    /// (dann hat der Nutzer sie von Hand gewaehlt, und das bleibt seine Entscheidung).</summary>
    internal static string? NextEngineAfter(IReadOnlyList<string> engines, string current,
        IReadOnlySet<string>? explicitOnly = null)
    {
        // Eine Nur-auf-Anforderung-Engine rotiert weder hinein noch heraus: ein Auftrag, der sie nannte, bleibt dort,
        // und ein Stockfish-Auftrag landet nicht bei Lc0.
        if (explicitOnly is { Count: > 0 })
        {
            if (explicitOnly.Contains(current)) return null;
            engines = ExplicitOnlyEngines.Automatic(engines, explicitOnly);
        }
        if (engines.Count < 2) return null;
        for (var i = 0; i < engines.Count; i++)
            if (engines[i] == current) return engines[(i + 1) % engines.Count];
        return null;
    }

    private static async Task PauseAsync(AppDbContext db, AnalysisJob job, string? error, TimeSpan? backoff = null)
    {
        job.Status = AnalysisJobStatus.Paused;
        job.CurrentDepth = 0; job.CurrentNps = 0;   // der laufende Stand gehört zum Lauf, nicht zum Auftrag
        job.LastError = error;
        job.NextAttemptAt = backoff is null ? null : DateTime.UtcNow + backoff;
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static Task BackoffAsync(AppDbContext db, AnalysisJob job, string error, TimeSpan backoff)
        => PauseAsync(db, job, error, backoff);

    private static async Task FailAsync(AppDbContext db, AnalysisJob job, string error)
    {
        job.Status = AnalysisJobStatus.Failed;
        job.LastError = error;
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    public override void Dispose()
    {
        _tracker.LiveStarted -= PauseEngine;
        base.Dispose();
    }
}
