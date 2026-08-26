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

    /// <summary>Eine Fortsetzung (und ein Neustart mit mehr Linien) liefert die flachen Iterationen erneut —
    /// übernommen wird nur, was mindestens so tief ist wie das gespeicherte Ergebnis.</summary>
    public static bool ShouldPersist(int lineDepth, int reachedDepth) => lineDepth >= reachedDepth;

    /// <summary>Liest den ndjson-Stream zeilenweise und ruft <paramref name="onLine"/> für jede Zeile mit Tiefe.
    /// Endet mit dem Stream oder per Abbruch (Exception wird durchgereicht).</summary>
    public static async Task ConsumeAsync(Stream ndjson, Func<string, int, Task> onLine, CancellationToken ct)
    {
        using var reader = new StreamReader(ndjson);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (DepthOf(line) is int depth)
                await onLine(line, depth);
        }
    }
}

/// <summary>
/// Arbeitet Hintergrund-Analyseaufträge (<see cref="AnalysisJob"/>) über den Lichess-Broker ab — je User
/// höchstens einer gleichzeitig, auf dessen Hintergrund-Engine. Vorrang der Live-Analyse: meldet der
/// <see cref="EngineActivityTracker"/> einen Live-Stream des Users, wird dessen laufender Auftrag sofort
/// abgebrochen (Status Paused — der Provider stoppt Stockfish, die Hashtabelle bleibt warm); erst nach
/// <c>AnalysisJobs:IdleGraceSeconds</c> Ruhe (Standard 20 s) läuft er weiter. Ergebnis-Zeilen werden nur
/// übernommen, wenn sie mindestens die gespeicherte Tiefe haben (<see cref="AnalysisJobStream.ShouldPersist"/>).
/// Kein 10-Minuten-Deckel wie beim Live-Proxy: ein Auftrag darf Stunden rechnen.
/// </summary>
public class AnalysisJobWorker : BackgroundService, IAnalysisJobControl
{
    private sealed record Running(int JobId, int UserId, CancellationTokenSource Cts);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EngineActivityTracker _tracker;
    private readonly LichessEngineService _lichess;
    private readonly ILogger<AnalysisJobWorker> _logger;
    private readonly TimeSpan _tick;
    private readonly TimeSpan _idleGrace;
    private readonly TimeSpan _persistInterval;
    /// <summary>Ohne erste Datenzeile binnen dieser Frist gilt der Provider als nicht rechnend — sonst hinge der
    /// Auftrag unbegrenzt in „läuft" (der HttpClient hat bewusst kein Timeout) und blockierte den Slot des Users.</summary>
    private readonly TimeSpan _firstLineTimeout;
    private readonly ConcurrentDictionary<int, Running> _running = new();   // key = UserId

    public AnalysisJobWorker(IServiceScopeFactory scopeFactory, EngineActivityTracker tracker,
        LichessEngineService lichess, ILogger<AnalysisJobWorker> logger, IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _tracker = tracker;
        _lichess = lichess;
        _logger = logger;
        _tick = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:TickSeconds") ?? 5, 1, 60));
        _idleGrace = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:IdleGraceSeconds") ?? 20, 0, 600));
        _persistInterval = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:PersistIntervalSeconds") ?? 5, 1, 60));
        _firstLineTimeout = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("AnalysisJobs:FirstLineTimeoutSeconds") ?? 300, 30, 3600));
        _tracker.LiveStarted += PauseUser;
    }

    /// <summary>Laufenden Auftrag eines Users unterbrechen (Live hat Vorrang).</summary>
    public void PauseUser(int userId)
    {
        if (_running.TryGetValue(userId, out var r)) TryCancel(r.Cts);
    }

    public void Interrupt(int jobId)
    {
        foreach (var r in _running.Values)
            if (r.JobId == jobId) TryCancel(r.Cts);
    }

    /// <summary>Abbrechen, ohne an einem gerade beendeten Lauf zu scheitern: zwischen dem Griff ins Dictionary
    /// und dem Cancel kann <see cref="RunJobAsync"/> seinen Eintrag entfernt UND die CTS disposed haben —
    /// <c>Cancel()</c> würde dann werfen. Der Wurf liefe bei <see cref="PauseUser"/> im LiveStarted-Handler
    /// bis in den Request-Thread des Live-Streams und ließe dessen Zähler dauerhaft stehen (der User bekäme
    /// nie wieder einen Hintergrund-Auftrag), bei <see cref="Interrupt"/> würde Ändern/Löschen zu 500.</summary>
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
        List<int> users;
        using (var scope = _scopeFactory.CreateScope())
            users = await scope.ServiceProvider.GetRequiredService<AnalysisJobService>().UsersWithRunnableJobsAsync(now, ct);

        foreach (var userId in users)
        {
            if (_running.ContainsKey(userId)) continue;
            if (_tracker.IsLiveActive(userId) || _tracker.IdleFor(userId) < _idleGrace) continue;

            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<AnalysisJobService>();
            var job = await svc.PickNextAsync(userId, now, ct);
            if (job is null) continue;

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var run = new Running(job.Id, userId, cts);
            if (!_running.TryAdd(userId, run)) { cts.Dispose(); continue; }
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

            var token = await svc.TokenAsync(job.UserId, CancellationToken.None);
            if (token is null)
            {
                await FailAsync(db, job, "Kein Lichess-Token hinterlegt");
                return;
            }
            LichessExternalEngine? engine;
            try { engine = await _lichess.ResolveEngineAsync(job.UserId, token, job.EngineId, ct); }
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
            try { upstream = await _lichess.AnalyseAsync(engine, work, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { await PauseAsync(db, job, null); return; }
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
                var lastPersist = runStart;
                var depthAtStart = job.ReachedDepth;
                string? pendingLine = null; var pendingDepth = job.ReachedDepth;
                var gotData = false;
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
                        if (!AnalysisJobStream.ShouldPersist(depth, job.ReachedDepth)) return;
                        pendingLine = line; pendingDepth = depth;
                        var now = DateTime.UtcNow;
                        if (depth > job.ReachedDepth || now - lastPersist >= _persistInterval)
                        {
                            await PersistProgressAsync(db, job, pendingLine, pendingDepth, now - lastPersist);
                            lastPersist = now; pendingLine = null;
                            // Ziel/Linien können sich unterdessen geändert haben (Service in eigenem Scope).
                            await db.Entry(job).ReloadAsync(CancellationToken.None);
                        }
                        // BEWUSST kein Selbst-Abbruch bei erreichter Zieltiefe: die Engine bekommt `depth` als
                        // Limit mitgeschickt und beendet den Stream selbst. Bräche der Worker schon bei der
                        // ERSTEN Zeile der Zieltiefe ab, trüge nur die Hauptvariante diese Tiefe — die Linien
                        // 2..K blieben eine Iteration flacher (jede pv hat ihre eigene Tiefe).
                    }, streamCt);
                }
                catch (OperationCanceledException) when (firstLine.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    await PauseAsync(db, job, "Engine lieferte keine Daten", TimeSpan.FromMinutes(5));
                    _logger.LogWarning("AnalysisJob {JobId}: keine Datenzeile binnen {Timeout} — pausiert", job.Id, _firstLineTimeout);
                    return;
                }
                catch (OperationCanceledException) { /* Pause (Live), Löschung oder Shutdown */ }
                catch (IOException ex) { _logger.LogWarning(ex, "AnalysisJob {JobId}: Stream abgerissen", job.Id); }

                if (pendingLine is not null)
                    await PersistProgressAsync(db, job, pendingLine, pendingDepth, DateTime.UtcNow - lastPersist);
                else
                    await PersistProgressAsync(db, job, null, job.ReachedDepth, DateTime.UtcNow - lastPersist);

                await db.Entry(job).ReloadAsync(CancellationToken.None);
                if (job.ReachedDepth >= job.TargetDepth)
                {
                    job.Status = AnalysisJobStatus.Done; job.FinishedAt = DateTime.UtcNow; job.UpdatedAt = job.FinishedAt.Value;
                    job.FruitlessAttempts = 0;
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
                else
                {
                    // Kein Fortschritt: das kann transient sein — oder deterministisch (Matt-/Patt-Stellung, vom
                    // Provider abgelehnte Arbeit). Ohne Zähler liefe der Auftrag ewig im 30-s-Takt gegen dieselbe Wand.
                    job.FruitlessAttempts++;
                    if (job.FruitlessAttempts >= AnalysisJob.MaxFruitlessAttempts)
                        await FailAsync(db, job, $"Engine lieferte in {job.FruitlessAttempts} Läufen keine tiefere Bewertung");
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
            _running.TryRemove(new KeyValuePair<int, Running>(run.UserId, run));
            run.Cts.Dispose();
        }
    }

    private static async Task PersistProgressAsync(AppDbContext db, AnalysisJob job, string? line, int depth, TimeSpan elapsed)
    {
        if (line is not null)
        {
            job.ResultJson = line;
            job.ReachedDepth = Math.Max(job.ReachedDepth, depth);
            job.EvalText = AnalysisJobService.EvalTextOf(line);   // Listen zeigen nur diesen Wert, ohne die Roh-Zeile zu laden
        }
        job.SecondsSpent += (int)Math.Max(0, elapsed.TotalSeconds);
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task PauseAsync(AppDbContext db, AnalysisJob job, string? error, TimeSpan? backoff = null)
    {
        job.Status = AnalysisJobStatus.Paused;
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
        _tracker.LiveStarted -= PauseUser;
        base.Dispose();
    }
}
