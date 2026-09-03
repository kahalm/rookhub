using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

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
/// zuletzt Verkehr (dann ist es kein Untätigkeits-Timeout)? Leerzeilen sind die Lebenszeichen des
/// Providers bzw. des Proxys, Zeilen ohne Tiefe alles Übrige.</summary>
public sealed class StreamTally
{
    public int DataLines { get; private set; }
    public int Heartbeats { get; private set; }
    public int OtherLines { get; private set; }
    public DateTime? LastDataUtc { get; private set; }
    public DateTime? LastAnyUtc { get; private set; }

    public void Note(string line, DateTime nowUtc)
    {
        LastAnyUtc = nowUtc;
        if (string.IsNullOrWhiteSpace(line)) { Heartbeats++; return; }
        if (AnalysisJobStream.DepthOf(line) is not null) { DataLines++; LastDataUtc = nowUtc; return; }
        OtherLines++;
    }

    /// <summary>Sekunden seit der letzten ZEILE MIT DATEN (null, wenn nie eine kam).</summary>
    public double? DataGapSeconds(DateTime nowUtc) => LastDataUtc is { } t ? (nowUtc - t).TotalSeconds : null;

    /// <summary>Sekunden seit dem letzten Byte überhaupt — inklusive Lebenszeichen.</summary>
    public double? AnyGapSeconds(DateTime nowUtc) => LastAnyUtc is { } t ? (nowUtc - t).TotalSeconds : null;
}

/// <summary>
/// Arbeitet Hintergrund-Analyseaufträge (<see cref="AnalysisJob"/>) über den Lichess-Broker ab — höchstens
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
    private sealed record Running(int JobId, string EngineId, CancellationTokenSource Cts);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EngineActivityTracker _tracker;
    private readonly AnalysisJobLive _live;
    private readonly LichessEngineService _lichess;
    private readonly ILogger<AnalysisJobWorker> _logger;
    private readonly TimeSpan _tick;
    private readonly TimeSpan _idleGrace;
    private readonly TimeSpan _persistInterval;
    /// <summary>Ohne erste Datenzeile binnen dieser Frist gilt der Provider als nicht rechnend — sonst hinge der
    /// Auftrag unbegrenzt in „läuft" (der HttpClient hat bewusst kein Timeout) und blockierte den Slot des Users.</summary>
    private readonly TimeSpan _firstLineTimeout;
    /// <summary>Ab dieser Laufzeit gilt „kein Tiefenfortschritt" NICHT mehr als Fehlversuch: eine echte Sackgasse
    /// (Matt/Patt, abgelehnte Arbeit) endet in Sekunden, ein langer Lauf ohne neue Zeile ist eine gekappte
    /// Verbindung — dafür darf der Auftrag nicht als gescheitert gelten.</summary>
    private readonly TimeSpan _fruitlessMinRuntime;
    private readonly ConcurrentDictionary<string, Running> _running = new();   // key = EngineId

    public AnalysisJobWorker(IServiceScopeFactory scopeFactory, EngineActivityTracker tracker,
        LichessEngineService lichess, ILogger<AnalysisJobWorker> logger, IConfiguration config, AnalysisJobLive live)
    {
        _scopeFactory = scopeFactory;
        _tracker = tracker;
        _live = live;
        _lichess = lichess;
        _logger = logger;
        _tick = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:TickSeconds") ?? 5, 1, 60));
        _idleGrace = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:IdleGraceSeconds") ?? 20, 0, 600));
        _persistInterval = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:PersistIntervalSeconds") ?? 5, 1, 60));
        _firstLineTimeout = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:FirstLineTimeoutSeconds") ?? 300, 30, 3600));
        _fruitlessMinRuntime = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:FruitlessMinRuntimeSeconds") ?? 60, 5, 3600));
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

        using var timer = new PeriodicTimer(_tick);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "AnalysisJobWorker: Tick fehlgeschlagen"); }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
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
            var run = new Running(job.Id, engineId, cts);
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
            var svc = scope.ServiceProvider.GetRequiredService<AnalysisJobService>();
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

            var token = await svc.TokenAsync(job.UserId, CancellationToken.None);
            if (token is null)
            {
                await FailAsync(db, job, "Kein Lichess-Token hinterlegt");
                return;
            }
            LichessExternalEngine? engine;
            try { engine = await _lichess.ResolveEngineAsync(job.UserId, token, job.EngineId, ct); }
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
            if (engine is null)
            {
                await FailAsync(db, job, "Engine bei Lichess nicht (mehr) registriert");
                return;
            }

            job.Status = AnalysisJobStatus.Running;
            job.LastRunAt = DateTime.UtcNow;
            job.LastError = null;
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);

            var work = new
            {
                sessionId = $"rh-bg-{job.UserId}", threads = Math.Max(1, engine.MaxThreads),
                hash = Math.Clamp(engine.MaxHash, 16, 32768),
                // Das Protokoll erlaubt 1..5; ein größerer Wert würde vom Broker abgewiesen und der Auftrag
                // liefe endlos in die Wiederholung. Zweiter Riegel neben AnalysisJobService.MaxMultiPv.
                multiPv = Math.Clamp(job.MultiPv, 1, 5), variant = "chess",
                initialFen = job.Fen, moves = Array.Empty<string>(), depth = job.TargetDepth,
            };

            HttpResponseMessage upstream;
            // Frist NUR für die Antwort-KOPFZEILEN: der HttpClient des Brokers ist bewusst timeout-los
            // (eine Suche darf Stunden dauern), und der `firstLine`-Wächter unten beginnt erst NACH den
            // Headern. Antwortet der Broker mit Verbindungsaufbau, aber ohne Header — er wartet auf einen
            // Provider, der nie kommt —, stand `RunJobAsync` hier unbegrenzt: der Auftrag blieb in der
            // Datenbank auf „läuft" mit Tiefe 0, die Engine blieb belegt, und alle weiteren Aufträge
            // dieser Engine warteten mit. Auflösbar war das nur durch einen Live-Stream oder Neustart.
            using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            headerCts.CancelAfter(_firstLineTimeout);
            try { upstream = await _lichess.AnalyseAsync(engine, work, headerCts.Token); }
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

            using (upstream)
            {
                if (!upstream.IsSuccessStatusCode)
                {
                    await BackoffAsync(db, job, $"Broker antwortete {(int)upstream.StatusCode}", TimeSpan.FromSeconds(120));
                    return;
                }
                var runStart = DateTime.UtcNow;
                // Anzeige-Stand ohne Datenbank: die Zeit läuft ab HIER, auch wenn die Engine noch schweigt.
                _live.Start(job.Id, job.UserId, job.SecondsSpent, runStart);
                var lastPersist = runStart;
                var depthAtStart = job.ReachedDepth;
                string? pendingLine = null; var pendingDepth = job.ReachedDepth;
                var currentDepth = 0; var currentNps = 0;
                var gotData = false;
                var tally = new StreamTally();
                // Wächter NUR für die erste Zeile: danach dürfen zwischen zwei Iterationen Stunden liegen
                // (Tiefe 40+ mit mehreren Linien), aber ein stummer Provider soll den Slot nicht ewig halten.
                using var firstLine = new CancellationTokenSource(_firstLineTimeout);
                using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct, firstLine.Token);
                var streamCt = streamCts.Token;
                try
                {
                    await using var stream = await upstream.Content.ReadAsStreamAsync(streamCt);
                    await AnalysisJobStream.ConsumeAsync(stream, async (line, depth) =>
                    {
                        if (!gotData) { gotData = true; firstLine.CancelAfter(Timeout.Infinite); }   // Wächter entschärfen
                        // Laufender Stand IMMER mitschreiben — auch wenn die Zeile flacher ist als das Ergebnis.
                        // Nach einer Fortsetzung rechnet die Engine erst wieder von Tiefe 1 hoch; ohne das stünde
                        // die Anzeige minutenlang still (keine Tiefe, kein Tempo, nicht einmal die Zeit lief mit).
                        currentDepth = depth;
                        currentNps = AnalysisJobStream.NpsOf(line) ?? currentNps;
                        _live.Update(job.Id, currentDepth, currentNps);
                        var keep = AnalysisJobStream.ShouldPersist(depth, job.ReachedDepth);
                        if (keep) { pendingLine = line; pendingDepth = depth; }
                        var now = DateTime.UtcNow;
                        if ((keep && depth > job.ReachedDepth) || now - lastPersist >= _persistInterval)
                        {
                            var rest = await PersistProgressAsync(db, job, pendingLine, pendingDepth, now - lastPersist,
                                                                  currentDepth, currentNps);
                            lastPersist = now - rest; pendingLine = null;   // angebrochene Sekunde mitnehmen
                            // Ziel/Linien können sich unterdessen geändert haben (Service in eigenem Scope).
                            await db.Entry(job).ReloadAsync(CancellationToken.None);
                        }
                        // BEWUSST kein Selbst-Abbruch bei erreichter Zieltiefe: die Engine bekommt `depth` als
                        // Limit mitgeschickt und beendet den Stream selbst. Bräche der Worker schon bei der
                        // ERSTEN Zeile der Zieltiefe ab, trüge nur die Hauptvariante diese Tiefe — die Linien
                        // 2..K blieben eine Iteration flacher (jede pv hat ihre eigene Tiefe).
                    }, streamCt, tally);
                }
                catch (OperationCanceledException) when (firstLine.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    await PauseAsync(db, job, "Engine lieferte keine Daten", TimeSpan.FromMinutes(5));
                    _logger.LogWarning("AnalysisJob {JobId}: keine Datenzeile binnen {Timeout} — pausiert", job.Id, _firstLineTimeout);
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

                await PersistProgressAsync(db, job, pendingLine, pendingLine is null ? job.ReachedDepth : pendingDepth,
                                           DateTime.UtcNow - lastPersist, currentDepth, currentNps);

                await db.Entry(job).ReloadAsync(CancellationToken.None);
                if (job.ReachedDepth >= job.TargetDepth)
                {
                    job.Status = AnalysisJobStatus.Done; job.FinishedAt = DateTime.UtcNow; job.UpdatedAt = job.FinishedAt.Value;
                    job.FruitlessAttempts = 0;
                    job.CurrentDepth = 0; job.CurrentNps = 0;   // es rechnet nichts mehr
                    await db.SaveChangesAsync(CancellationToken.None);
                    _logger.LogInformation("AnalysisJob {JobId}: fertig bei Tiefe {Depth}", job.Id, job.ReachedDepth);
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
        }
    }

    /// <summary>Sekunden auf eine Stelle, „—" wenn es den Messwert nicht gibt (nie eine Zeile gesehen).</summary>
    private static string Fmt(double? seconds) => seconds is { } v ? v.ToString("F1") : "—";

    /// <summary>Ergebnis + Rechenzeit sichern. Gibt zurück, wie viel Zeit NICHT verbucht wurde (der Bruchteil
    /// unter einer Sekunde) — der Aufrufer schiebt ihn ins nächste Intervall, sonst summierte sich bei jedem
    /// Persist ein verlorener Rest, und in der ersten Sekunden-Salve flacher Tiefen ginge fast alles verloren.</summary>
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
