using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.Services.EngineBroker;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Services.Tournaments.Live;

/// <summary>Die Engines, auf denen die Live-Bretter rechnen dürfen, und wem sie gehören.</summary>
public sealed record LiveEnginePool(int OwnerUserId, IReadOnlyList<EngineRef> Engines);

/// <summary>Sucht die Engines für die Live-Bretter aus (Schnittstelle für Tests).</summary>
public interface ILiveEnginePoolSource
{
    Task<LiveEnginePool?> GetAsync(CancellationToken ct);
}

/// <summary>Bewertet EINE Stellung auf EINER Engine bis zur Zieltiefe; meldet Zwischenstände (Weiß-Sicht, <c>source: "engine"</c>).
/// <c>null</c> = keine Antwort (Engine weg, Fehler).</summary>
public interface ILiveEngineEvaluator
{
    Task<LiveEval?> EvaluateAsync(EngineRef engine, int ownerUserId, string fen, Action<LiveEval> progress, CancellationToken ct);
}

/// <summary>Einstellungen <c>LiveBoards:Engine:*</c> (1.027.0: Hintergrund-Engines, Zieltiefe 30).</summary>
public sealed class LiveEngineOptions
{
    /// <summary><c>LiveBoards:Engine:Enabled</c> (Vorgabe an).</summary>
    public bool Enabled { get; init; } = true;
    /// <summary><c>LiveBoards:Engine:OwnerUserName</c> — wessen Hintergrund-Engines. Leer = das Konto der Haus-Engine
    /// (<see cref="EngineOwnerResolver.HouseOwnerAsync"/>), sonst das erste Admin-Konto MIT Hintergrund-Engines.</summary>
    public string? OwnerUserName { get; init; }
    /// <summary><c>LiveBoards:Engine:EngineIds</c> (Liste, auch <c>EngineId</c>) — nur diese Engines. Leer = die ganze
    /// Hintergrund-Liste des Besitzers ohne die Nur-auf-Anforderung-Engines (<c>AnalysisJobs:ExplicitOnlyEngineIds</c>).</summary>
    public IReadOnlyList<string> EngineIds { get; init; } = [];
    /// <summary><c>LiveBoards:Engine:Depth</c> (Vorgabe 30) — Zieltiefe je Stellung. Eine Cloud-Bewertung mindestens so tief
    /// genügt, dann rechnet keine Engine.</summary>
    public int Depth { get; init; } = 30;
    /// <summary><c>LiveBoards:Engine:MaxSeconds</c> (Vorgabe 180) — harte Grenze je Stellung, das bis dahin Erreichte gilt.</summary>
    public int MaxSeconds { get; init; } = 180;
    /// <summary><c>LiveBoards:Engine:MaxParallel</c> (Vorgabe 0 = so viele, wie Hintergrund-Engines frei sind).</summary>
    public int MaxParallel { get; init; }
    /// <summary><c>LiveBoards:Engine:Threads</c> / <c>HashMb</c> (0 = was die Engine anbietet, wie die Hintergrund-Aufträge).</summary>
    public int Threads { get; init; }
    public int HashMb { get; init; }

    public static LiveEngineOptions From(IConfiguration c)
    {
        var s = c.GetSection("LiveBoards:Engine");
        var ids = (s.GetSection("EngineIds").Get<string[]>() ?? [])
            .Concat(s["EngineId"] is { Length: > 0 } one ? [one] : [])
            .Select(x => x?.Trim()).Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Distinct().ToList();
        return new LiveEngineOptions
        {
            Enabled = s.GetValue<bool?>("Enabled") ?? true,
            OwnerUserName = s["OwnerUserName"] is { Length: > 0 } o ? o.Trim() : null,
            EngineIds = ids,
            Depth = Math.Clamp(s.GetValue<int?>("Depth") ?? 30, 10, 60),
            MaxSeconds = Math.Clamp(s.GetValue<int?>("MaxSeconds") ?? 180, 10, 1800),
            MaxParallel = Math.Clamp(s.GetValue<int?>("MaxParallel") ?? 0, 0, 64),
            Threads = Math.Clamp(s.GetValue<int?>("Threads") ?? 0, 0, 256),
            HashMb = Math.Clamp(s.GetValue<int?>("HashMb") ?? 0, 0, 32768),
        };
    }
}

/// <summary>
/// Die Engines über die vorhandene <see cref="EngineRegistry"/>: die Hintergrund-Engines DES Kontos, dem die
/// Analyseaufträge gehören (Haus-Engine) — nicht blind „kleinste Admin-Id“ (Prod 26.11.: Admin 2 hat keine Engines, alle
/// gehören Admin 5, die Live-Engine rechnete nie). Nur online stehende eigene Engines (<c>rhe_</c>).
/// </summary>
public sealed class RegistryLiveEnginePoolSource : ILiveEnginePoolSource
{
    private readonly AppDbContext _db;
    private readonly EngineRegistry _registry;
    private readonly LiveEngineOptions _options;
    private readonly IReadOnlySet<string> _explicitOnly;

    public RegistryLiveEnginePoolSource(AppDbContext db, EngineRegistry registry, LiveEngineOptions options, IConfiguration? config = null)
    {
        _db = db;
        _registry = registry;
        _options = options;
        _explicitOnly = ExplicitOnlyEngines.From(config);
    }

    public async Task<LiveEnginePool?> GetAsync(CancellationToken ct)
    {
        if (!_options.Enabled) return null;
        if (await OwnerAsync(_db, _options, ct) is not { } ownerId) return null;
        var listing = await _registry.ListAsync(ownerId, ct);
        var engines = Pick(listing.Engines, listing.Credential?.BackgroundEngines ?? [], _options.EngineIds, _explicitOnly);
        return engines.Count == 0 ? null : new LiveEnginePool(ownerId, engines);
    }

    /// <summary>Wessen Engines: konfiguriert, sonst die Haus-Engine, sonst das erste Admin-Konto MIT Hintergrund-Engines.</summary>
    internal static async Task<int?> OwnerAsync(AppDbContext db, LiveEngineOptions options, CancellationToken ct)
    {
        if (options.OwnerUserName is { } name)
            return await db.AppUsers.AsNoTracking().Where(u => u.Username == name).Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
        if ((await EngineOwnerResolver.HouseOwnerAsync(db, ct))?.UserId is { } house) return house;
        var creds = await db.LichessEngineCredentials.AsNoTracking()
            .Where(c => c.BackgroundEngineIds != null && c.User!.IsAdmin).OrderBy(c => c.UserId).ToListAsync(ct);
        return creds.FirstOrDefault(c => c.BackgroundEngines.Count > 0)?.UserId;
    }

    /// <summary>Feste Liste, wenn konfiguriert; sonst die Hintergrund-Liste (in ihrer Reihenfolge) ohne Nur-auf-Anforderung.
    /// Immer nur eigene, online stehende Engines (für Lichess-Engines wissen wir „online“ nicht).</summary>
    public static List<EngineRef> Pick(IEnumerable<EngineRef> engines, IReadOnlyList<string> background, IReadOnlyList<string> configured,
        IReadOnlySet<string> explicitOnly)
    {
        var byId = engines.Where(e => e.Source == EngineSource.Local && e.Online == true).ToDictionary(e => e.Id, StringComparer.Ordinal);
        var ids = configured.Count > 0 ? configured : ExplicitOnlyEngines.Automatic(background, explicitOnly);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }
}

/// <summary>
/// Rechnet über den vorhandenen <see cref="IEngineBroker"/> (eigener Broker bzw. Lichess-Broker, je nach Engine) — kein neuer
/// Engine-Weg. Bis <see cref="LiveEngineOptions.Depth"/> (1 Linie), meldet jede tiefere Zeile als Zwischenstand und sich beim
/// <see cref="EngineActivityTracker"/> als Live-Rechnung an (die Admin-Seite „Engines“ zeigt sie; der Worker startet dort nichts
/// Neues). Die Warteschlange gibt nur Engines, auf denen kein Auftrag rechnet — abgewürgt wird nichts.
/// </summary>
public sealed class BrokerLiveEngineEvaluator : ILiveEngineEvaluator
{
    private const string SessionId = "rh-live-boards";

    private readonly IServiceScopeFactory _scopes;
    private readonly EngineActivityTracker? _activity;
    private readonly LiveEngineOptions _options;
    private readonly ILogger<BrokerLiveEngineEvaluator> _logger;

    public BrokerLiveEngineEvaluator(IServiceScopeFactory scopes, LiveEngineOptions options, ILogger<BrokerLiveEngineEvaluator> logger,
        EngineActivityTracker? activity = null)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
        _activity = activity;
    }

    public async Task<LiveEval?> EvaluateAsync(EngineRef engine, int ownerUserId, string fen, Action<LiveEval> progress, CancellationToken ct)
    {
        var work = new EngineWork(SessionId,
            _options.Threads > 0 ? Math.Min(_options.Threads, Math.Max(1, engine.MaxThreads)) : Math.Max(1, engine.MaxThreads),
            _options.HashMb > 0 ? Math.Min(_options.HashMb, Math.Max(16, engine.MaxHash)) : Math.Clamp(engine.MaxHash, 16, 32768),
            1, fen, [], Depth: _options.Depth);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.MaxSeconds));
        await using var scope = _scopes.CreateAsyncScope();
        _activity?.Begin(ownerUserId, engine.Id);
        try
        {
            var broker = scope.ServiceProvider.GetRequiredService<IEngineBroker>();
            await using var session = await broker.AnalyseAsync(engine, work, deadline.Token);
            if (!session.IsSuccess)
            {
                _logger.LogDebug("Live-Bretter: Engine {Engine} lehnt ab ({Status} {Error})", engine.Id, session.StatusCode, session.Error);
                return null;
            }
            return await ReadAsync(session.Ndjson!, FenFields.WhiteToMove(fen), progress, deadline.Token);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogDebug(e, "Live-Bretter: Engine-Bewertung abgebrochen");
            return null;
        }
        finally
        {
            _activity?.End(ownerUserId, engine.Id, grace: false);
        }
    }

    /// <summary>Zeilen des ndjson-Stroms (<c>{"depth":…,"pvs":[{"cp"|"mate":…}]}</c>): jede TIEFERE geht als Zwischenstand
    /// hinaus, die letzte ist das Ergebnis. Läuft die Zeit ab, zählt das Erreichte.</summary>
    internal static async Task<LiveEval?> ReadAsync(Stream ndjson, bool whiteToMove, Action<LiveEval>? progress, CancellationToken ct)
    {
        LiveEval? last = null;
        using var reader = new StreamReader(ndjson);
        try
        {
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (ParseLine(line, whiteToMove) is not { } ev) continue;
                var deeper = last is null || (ev.Depth ?? 0) > (last.Depth ?? 0);
                last = ev;
                if (deeper) progress?.Invoke(ev);
            }
        }
        catch (OperationCanceledException) when (last is not null) { /* Zeit um — das Beste bisher */ }
        return last;
    }

    internal static LiveEval? ParseLine(string line, bool whiteToMove)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("pvs", out var pvs) || pvs.ValueKind != JsonValueKind.Array
                || pvs.GetArrayLength() == 0) return null;
            var pv = pvs[0];
            int? depth = r.TryGetProperty("depth", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32()
                : pv.TryGetProperty("depth", out var pd) && pd.ValueKind == JsonValueKind.Number ? pd.GetInt32() : null;
            var sign = whiteToMove ? 1 : -1;
            if (pv.TryGetProperty("mate", out var m) && m.ValueKind == JsonValueKind.Number) return new LiveEval(null, sign * m.GetInt32(), "engine", depth);
            if (pv.TryGetProperty("cp", out var cp) && cp.ValueKind == JsonValueKind.Number) return new LiveEval(sign * cp.GetInt32(), null, "engine", depth);
            return null;
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>
/// Warteschlange der Engine-Bewertungen über ALLE Runden (Singleton, 1.027.0). Wünsche „Brett → Stellung“ kommen vom Hub, und
/// zwar nur für Bretter, die ein Zuschauer mit eingeschaltetem Balken gerade SIEHT (Filter der Ansicht, siehe
/// <c>docs/api/live-bretter.md</c>). Je Brett gilt nur die neueste Stellung: ein neuer Zug ersetzt den wartenden Wunsch an
/// seinem Platz und bricht eine laufende Rechnung des alten ab. Gerechnet wird PARALLEL auf allen freien Hintergrund-Engines
/// (<see cref="LiveEngineOptions.MaxParallel"/>), je Engine eine Stellung, reihum über die Bretter. Frei = kein Hintergrund-
/// Auftrag rechnet dort (<see cref="LiveEngineLane.JobRunning"/>) und niemand analysiert dort live; solange Wünsche warten,
/// beansprucht die Schlange die Engines (<see cref="LiveEngineLane.Claim"/>), der Worker startet dort nichts Neues.
/// Zwischenstände gehen höchstens alle <see cref="ProgressSpacing"/> hinaus; das Ergebnis je Stellung bleibt 6 h gecacht.
/// Vor dem Start, bei jedem Zwischenstand und vor dem Übernehmen fragt sie den Auftraggeber, ob die Stellung noch gebraucht
/// wird — sonst bricht sie ab.
/// </summary>
public sealed class LiveEngineQueue : IAsyncDisposable
{
    private sealed record Entry(string Fen, Func<string, bool> Wanted, Action<string, LiveEval> Apply);
    private sealed record Run(string Key, string Fen, string EngineId, CancellationTokenSource Cts);

    private static readonly TimeSpan HitTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan PoolTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EngineCooldown = TimeSpan.FromSeconds(60);
    /// <summary>Wartet etwas, ist aber keine Engine frei: so oft nachsehen (der Worker weckt zusätzlich, wenn ein Auftrag endet).</summary>
    internal TimeSpan Poll { get; init; } = TimeSpan.FromSeconds(2);
    /// <summary>Höchstens so oft ein Zwischenstand je Brett (das Ergebnis geht immer hinaus).</summary>
    internal TimeSpan ProgressSpacing { get; init; } = TimeSpan.FromMilliseconds(700);

    private readonly ILiveEngineEvaluator _evaluator;
    private readonly IServiceScopeFactory? _scopes;
    private readonly Func<CancellationToken, Task<LiveEnginePool?>>? _poolOverride;
    private readonly LiveEngineOptions _options;
    private readonly LiveEngineLane? _lane;
    private readonly EngineActivityTracker? _activity;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LiveEngineQueue> _logger;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Entry> _pending = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = new();
    private readonly Dictionary<string, Run> _running = new(StringComparer.Ordinal);       // key = Brett
    private readonly Dictionary<string, DateTime> _cooldown = new(StringComparer.Ordinal); // key = Engine
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stop = new();
    private LiveEnginePool? _pool;
    private DateTime _poolAt = DateTime.MinValue;
    private Task? _worker;

    public LiveEngineQueue(ILiveEngineEvaluator evaluator, IServiceScopeFactory scopes, LiveEngineOptions options, IMemoryCache cache,
        ILogger<LiveEngineQueue> logger, LiveEngineLane? lane = null, EngineActivityTracker? activity = null)
        : this(evaluator, options, cache, logger, lane, activity)
    {
        _scopes = scopes;
    }

    /// <summary>Tests: feste Engine-Liste statt der Registry.</summary>
    internal LiveEngineQueue(ILiveEngineEvaluator evaluator, LiveEngineOptions options, IMemoryCache cache, ILogger<LiveEngineQueue> logger,
        LiveEngineLane? lane, EngineActivityTracker? activity, Func<CancellationToken, Task<LiveEnginePool?>>? pool = null)
    {
        _evaluator = evaluator;
        _options = options;
        _cache = cache;
        _logger = logger;
        _lane = lane;
        _activity = activity;
        _poolOverride = pool;
        if (_lane is not null) _lane.JobEnded += Wake;
    }

    /// <summary>Zieltiefe — eine Bewertung mindestens so tief braucht keine Engine mehr.</summary>
    public int TargetDepth => _options.Depth;

    /// <summary>Ist diese Stellung fertig gerechnet (Ergebnis im Cache)?</summary>
    public bool IsSettled(string fen) => _cache.TryGetValue("live-engine-eval:" + fen, out LiveEval? hit) && hit is not null;

    /// <summary>Wartende Bretter (Tests).</summary>
    public int Pending { get { lock (_lock) return _pending.Count; } }

    /// <summary>Gerade rechnende Bretter (Tests, Diagnose).</summary>
    public int RunningCount { get { lock (_lock) return _running.Count; } }

    private void Wake() => _signal.Release();

    /// <summary>Stellung <paramref name="fen"/> für das Brett <paramref name="key"/> vormerken. Ersetzt einen älteren Wunsch
    /// desselben Bretts an dessen Platz; rechnet gerade eine ANDERE Stellung dieses Bretts, wird sie abgebrochen.
    /// <paramref name="apply"/> bekommt Zwischenstände und das Ergebnis.</summary>
    public void Enqueue(string key, string fen, Func<string, bool> wanted, Action<string, LiveEval> apply)
    {
        if (_cache.TryGetValue("live-engine-eval:" + fen, out LiveEval? hit) && hit is not null)
        {
            if (wanted(fen)) apply(fen, hit);
            return;
        }
        lock (_lock)
        {
            if (_running.TryGetValue(key, out var run))
            {
                if (run.Fen == fen) return;                                       // rechnet schon genau diese Stellung
                TryCancel(run.Cts);                                              // neuer Zug: alte Rechnung weg
            }
            if (!_pending.ContainsKey(key)) _order.AddLast(key);
            _pending[key] = new Entry(fen, wanted, apply);
            _worker ??= Task.Run(() => LoopAsync(_stop.Token));
        }
        Wake();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                bool waiting;
                lock (_lock) waiting = _pending.Count > 0;
                if (waiting) await _signal.WaitAsync(Poll, ct);
                else await _signal.WaitAsync(ct);
                while (_signal.CurrentCount > 0) await _signal.WaitAsync(ct);   // mehrere Weckrufe = einer
                try { await DispatchAsync(ct); }
                catch (Exception e) when (e is not OperationCanceledException) { _logger.LogWarning(e, "Live-Bretter: Verteilen fehlgeschlagen"); }
            }
        }
        catch (OperationCanceledException) { /* Herunterfahren */ }
    }

    /// <summary>Wartende Bretter auf freie Engines verteilen; danach die Engines beanspruchen, solange noch etwas wartet.</summary>
    private async Task DispatchAsync(CancellationToken ct)
    {
        lock (_lock) if (_pending.Count == 0) { _lane?.Claim(new HashSet<string>()); return; }
        var pool = await PoolAsync(ct);
        if (pool is null) { DropAll(); return; }
        var now = DateTime.UtcNow;
        lock (_lock)
        {
            var busy = _running.Values.Select(r => r.EngineId).ToHashSet(StringComparer.Ordinal);
            var limit = _options.MaxParallel > 0 ? _options.MaxParallel : int.MaxValue;
            foreach (var engine in pool.Engines)
            {
                if (_running.Count >= limit || _order.Count == 0) break;
                if (busy.Contains(engine.Id) || (_cooldown.TryGetValue(engine.Id, out var until) && until > now)) continue;
                if (_lane?.JobRunning(engine.Id) == true || _activity?.IsEngineBusy(engine.Id) == true) continue;
                while (_order.First is { } node)
                {
                    _order.RemoveFirst();
                    _pending.Remove(node.Value, out var entry);
                    if (entry is null || !entry.Wanted(entry.Fen)) continue;
                    var run = new Run(node.Value, entry.Fen, engine.Id, CancellationTokenSource.CreateLinkedTokenSource(ct));
                    _running[node.Value] = run;
                    busy.Add(engine.Id);
                    _ = Task.Run(() => ExecuteAsync(run, entry, engine, pool.OwnerUserId), CancellationToken.None);
                    break;
                }
            }
            // Noch Wünsche offen? Dann gehören die Engines des Pools den Live-Brettern, sobald sie frei werden.
            _lane?.Claim(_pending.Count > 0 && _running.Count < limit
                ? pool.Engines.Select(e => e.Id).ToHashSet(StringComparer.Ordinal) : new HashSet<string>());
        }
    }

    private async Task ExecuteAsync(Run run, Entry entry, EngineRef engine, int ownerUserId)
    {
        var lastSent = DateTime.MinValue;
        LiveEval? result = null;
        try
        {
            result = await _evaluator.EvaluateAsync(engine, ownerUserId, run.Fen, ev =>
            {
                if (!entry.Wanted(run.Fen)) { TryCancel(run.Cts); return; }          // niemand sieht das Brett mehr / weitergezogen
                var now = DateTime.UtcNow;
                if (now - lastSent < ProgressSpacing) return;
                lastSent = now;
                entry.Apply(run.Fen, ev);
            }, run.Cts.Token);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "Live-Bretter: Engine-Bewertung fehlgeschlagen");
        }
        catch (OperationCanceledException) { /* abgebrochen */ }
        var cancelled = run.Cts.IsCancellationRequested;
        lock (_lock)
        {
            if (_running.TryGetValue(run.Key, out var r) && ReferenceEquals(r, run)) _running.Remove(run.Key);
            if (result is null && !cancelled)
            {
                // Engine lieferte nichts (Provider weg, abgelehnt): eine Weile auslassen, Wunsch wieder hinten an.
                _cooldown[engine.Id] = DateTime.UtcNow + EngineCooldown;
                if (!_pending.ContainsKey(run.Key)) { _pending[run.Key] = entry; _order.AddLast(run.Key); }
                _pool = null;                                                       // beim nächsten Mal neu auswählen
            }
        }
        run.Cts.Dispose();
        if (result is not null)
        {
            // Fertig gerechnet (Zieltiefe oder Zeitgrenze) → gecacht; abgebrochen (neuer Zug, niemand sieht hin) → nicht.
            if (!cancelled) _cache.Set("live-engine-eval:" + run.Fen, result, HitTtl);
            if (!cancelled && entry.Wanted(run.Fen)) entry.Apply(run.Fen, result);
        }
        Wake();
    }

    private async Task<LiveEnginePool?> PoolAsync(CancellationToken ct)
    {
        if (_pool is not null && DateTime.UtcNow - _poolAt < PoolTtl) return _pool;
        LiveEnginePool? pool = null;
        try
        {
            if (_poolOverride is not null) pool = await _poolOverride(ct);
            else if (_scopes is not null)
            {
                await using var scope = _scopes.CreateAsyncScope();
                pool = await scope.ServiceProvider.GetRequiredService<ILiveEnginePoolSource>().GetAsync(ct);
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException && !ct.IsCancellationRequested)
        {
            _logger.LogDebug(e, "Live-Bretter: Engine-Auswahl fehlgeschlagen");
        }
        _pool = pool;
        _poolAt = DateTime.UtcNow;
        return pool;
    }

    /// <summary>Keine Engine da: Wünsche verwerfen (Cloud-Werte bleiben stehen), nichts beanspruchen.</summary>
    private void DropAll()
    {
        lock (_lock)
        {
            _pending.Clear();
            _order.Clear();
        }
        _lane?.Claim(new HashSet<string>());
    }

    private static void TryCancel(CancellationTokenSource cts)
    {
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { /* gerade fertig geworden */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (_lane is not null) _lane.JobEnded -= Wake;
        _lane?.Claim(new HashSet<string>());
        await _stop.CancelAsync();
        if (_worker is not null) { try { await _worker; } catch { /* egal */ } }
        _stop.Dispose();
    }
}
