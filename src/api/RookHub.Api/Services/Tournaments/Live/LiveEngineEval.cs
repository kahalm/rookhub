using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.Services.EngineBroker;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Services.Tournaments.Live;

/// <summary>Bewertet EINE Stellung mit einer eigenen Engine (Weiß-Sicht, <c>source: "engine"</c>); <c>null</c> = keine Engine
/// da/online oder keine Antwort.</summary>
public interface ILiveEngineEvaluator
{
    Task<LiveEval?> EvaluateAsync(string fen, CancellationToken ct);
}

/// <summary>Welche Engine die Live-Bretter rechnet und wem sie gehört (für den Broker und den Aktivitätszähler).</summary>
public sealed record LiveEngineChoice(EngineRef Engine, int OwnerUserId);

/// <summary>Sucht die Engine für die Live-Bretter aus (Schnittstelle für Tests).</summary>
public interface ILiveEngineSelector
{
    Task<LiveEngineChoice?> SelectAsync(CancellationToken ct);
}

/// <summary>Einstellungen <c>LiveBoards:Engine:*</c>.</summary>
public sealed class LiveEngineOptions
{
    /// <summary><c>LiveBoards:Engine:Enabled</c> (Vorgabe an).</summary>
    public bool Enabled { get; init; } = true;
    /// <summary><c>LiveBoards:Engine:OwnerUserName</c> — Konto, dessen Engine rechnet. Leer = das erste Admin-Konto (kleinste Id).</summary>
    public string? OwnerUserName { get; init; }
    /// <summary><c>LiveBoards:Engine:EngineId</c> — feste Engine (<c>rhe_…</c>, auch <c>eei_…</c>). Leer = die erste ONLINE
    /// eigene Engine (<c>rhe_</c>) des Kontos, die NICHT in seiner Hintergrund-Liste steht (= seine Live-Engine).</summary>
    public string? EngineId { get; init; }
    /// <summary><c>LiveBoards:Engine:MovetimeMs</c> (Vorgabe 1500) — Rechenzeit je Stellung.</summary>
    public int MovetimeMs { get; init; } = 1500;
    /// <summary><c>LiveBoards:Engine:Depth</c> — statt der Zeit eine feste Tiefe (0 = Zeit gilt). Die Suche wird trotzdem nach
    /// <see cref="MaxSeconds"/> abgebrochen.</summary>
    public int Depth { get; init; }
    /// <summary><c>LiveBoards:Engine:MaxSeconds</c> (Vorgabe 6) — harte Grenze je Stellung, auch bei hängendem Provider.</summary>
    public int MaxSeconds { get; init; } = 6;
    public int Threads { get; init; } = 2;
    public int HashMb { get; init; } = 64;

    public static LiveEngineOptions From(IConfiguration c)
    {
        var s = c.GetSection("LiveBoards:Engine");
        return new LiveEngineOptions
        {
            Enabled = s.GetValue<bool?>("Enabled") ?? true,
            OwnerUserName = s["OwnerUserName"] is { Length: > 0 } o ? o.Trim() : null,
            EngineId = s["EngineId"] is { Length: > 0 } e ? e.Trim() : null,
            MovetimeMs = Math.Clamp(s.GetValue<int?>("MovetimeMs") ?? 1500, 200, 10_000),
            Depth = Math.Clamp(s.GetValue<int?>("Depth") ?? 0, 0, 40),
            MaxSeconds = Math.Clamp(s.GetValue<int?>("MaxSeconds") ?? 6, 2, 60),
            Threads = Math.Clamp(s.GetValue<int?>("Threads") ?? 2, 1, 16),
            HashMb = Math.Clamp(s.GetValue<int?>("HashMb") ?? 64, 16, 1024),
        };
    }
}

/// <summary>
/// Engine-Auswahl über die vorhandene <see cref="EngineRegistry"/> (keine eigene Engine-Liste): Besitzer aus der
/// Konfiguration, sonst das erste Admin-Konto; Engine fest konfiguriert, sonst <see cref="Pick"/>. Die Hintergrund-Engines
/// der Analyseaufträge bleiben unberührt.
/// </summary>
public sealed class RegistryLiveEngineSelector : ILiveEngineSelector
{
    private readonly AppDbContext _db;
    private readonly EngineRegistry _registry;
    private readonly LiveEngineOptions _options;

    public RegistryLiveEngineSelector(AppDbContext db, EngineRegistry registry, LiveEngineOptions options)
    {
        _db = db;
        _registry = registry;
        _options = options;
    }

    public async Task<LiveEngineChoice?> SelectAsync(CancellationToken ct)
    {
        if (!_options.Enabled) return null;
        var owner = _options.OwnerUserName is { } name
            ? await _db.AppUsers.AsNoTracking().Where(u => u.Username == name).Select(u => (int?)u.Id).FirstOrDefaultAsync(ct)
            : await _db.AppUsers.AsNoTracking().Where(u => u.IsAdmin).OrderBy(u => u.Id).Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
        if (owner is not { } ownerId) return null;
        var listing = await _registry.ListAsync(ownerId, ct);
        var engine = Pick(listing.Engines, listing.Credential?.BackgroundEngines ?? [], _options.EngineId);
        return engine is null ? null : new LiveEngineChoice(engine, ownerId);
    }

    /// <summary>Feste Engine, wenn konfiguriert (eine eigene muss online sein); sonst die erste online stehende eigene
    /// Engine außerhalb der Hintergrund-Liste. Lichess-Engines nur auf ausdrückliche Konfiguration.</summary>
    public static EngineRef? Pick(IEnumerable<EngineRef> engines, IReadOnlyCollection<string> background, string? configuredId)
    {
        var list = engines.ToList();
        if (configuredId is not null)
            return list.FirstOrDefault(e => e.Id == configuredId && (e.Source != EngineSource.Local || e.Online == true));
        return list.FirstOrDefault(e => e.Source == EngineSource.Local && e.Online == true && !background.Contains(e.Id));
    }
}

/// <summary>
/// Rechnet über den vorhandenen <see cref="IEngineBroker"/> (eigener Broker bzw. Lichess-Broker, je nach Engine) — kein neuer
/// Engine-Weg. Kurz (<see cref="LiveEngineOptions.MovetimeMs"/>, 1 Linie, wenig Hash), meldet sich beim
/// <see cref="EngineActivityTracker"/> als Live-Rechnung an (ein Hintergrund-Auftrag auf DERSELBEN Engine pausiert, andere
/// laufen weiter). Ergebnis je Stellung 6 h gecacht; die Auswahl der Engine 60 s (auch „keine").
/// </summary>
public sealed class BrokerLiveEngineEvaluator : ILiveEngineEvaluator
{
    private static readonly TimeSpan HitTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan ChoiceTtl = TimeSpan.FromSeconds(60);
    private const string SessionId = "rh-live-boards";

    private readonly IServiceScopeFactory _scopes;
    private readonly IMemoryCache _cache;
    private readonly EngineActivityTracker? _activity;
    private readonly LiveEngineOptions _options;
    private readonly ILogger<BrokerLiveEngineEvaluator> _logger;

    public BrokerLiveEngineEvaluator(IServiceScopeFactory scopes, IMemoryCache cache, LiveEngineOptions options,
        ILogger<BrokerLiveEngineEvaluator> logger, EngineActivityTracker? activity = null)
    {
        _scopes = scopes;
        _cache = cache;
        _options = options;
        _logger = logger;
        _activity = activity;
    }

    public async Task<LiveEval?> EvaluateAsync(string fen, CancellationToken ct)
    {
        var key = "live-engine-eval:" + fen;
        if (_cache.TryGetValue(key, out LiveEval? hit) && hit is not null) return hit;
        if (!_options.Enabled) return null;

        await using var scope = _scopes.CreateAsyncScope();
        LiveEngineChoice? choice;
        if (!_cache.TryGetValue("live-engine-choice", out choice))
        {
            try { choice = await scope.ServiceProvider.GetRequiredService<ILiveEngineSelector>().SelectAsync(ct); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                _logger.LogDebug(e, "Live-Bretter: Engine-Auswahl fehlgeschlagen");
                choice = null;
            }
            _cache.Set("live-engine-choice", choice, ChoiceTtl);
        }
        if (choice is null) return null;

        var engine = choice.Engine;
        var work = new EngineWork(SessionId, Math.Min(_options.Threads, Math.Max(1, engine.MaxThreads)),
            Math.Min(_options.HashMb, Math.Max(1, engine.MaxHash)), 1, fen, [],
            Depth: _options.Depth > 0 ? _options.Depth : null, Movetime: _options.Depth > 0 ? null : _options.MovetimeMs);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.MaxSeconds));
        _activity?.Begin(choice.OwnerUserId, engine.Id);
        try
        {
            var broker = scope.ServiceProvider.GetRequiredService<IEngineBroker>();
            await using var session = await broker.AnalyseAsync(engine, work, deadline.Token);
            if (!session.IsSuccess)
            {
                _logger.LogDebug("Live-Bretter: Engine {Engine} lehnt ab ({Status} {Error})", engine.Id, session.StatusCode, session.Error);
                if (session.StatusCode == 503) _cache.Remove("live-engine-choice");   // Provider weg: beim nächsten Mal neu wählen
                return null;
            }
            var ev = await ReadAsync(session.Ndjson!, FenFields.WhiteToMove(fen), deadline.Token);
            if (ev is not null) _cache.Set(key, ev, HitTtl);
            return ev;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogDebug(e, "Live-Bretter: Engine-Bewertung abgebrochen");
            return null;
        }
        finally
        {
            _activity?.End(choice.OwnerUserId, engine.Id);
        }
    }

    /// <summary>Die letzte Zeile mit Bewertung aus dem ndjson-Strom des Brokers (<c>{"depth":…,"pvs":[{"cp"|"mate":…}]}</c>).
    /// Der Wert der UCI-Engine gilt für die Seite am Zug → für Schwarz am Zug umdrehen. Läuft die Zeit ab, zählt das Letzte.</summary>
    internal static async Task<LiveEval?> ReadAsync(Stream ndjson, bool whiteToMove, CancellationToken ct)
    {
        LiveEval? last = null;
        using var reader = new StreamReader(ndjson);
        try
        {
            while (await reader.ReadLineAsync(ct) is { } line)
                if (ParseLine(line, whiteToMove) is { } ev) last = ev;
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
/// Warteschlange der Engine-Bewertungen über ALLE Runden (Singleton): höchstens EINE Rechnung gleichzeitig, je Brett nur die
/// neueste Stellung (ein neuer Zug ersetzt den wartenden Wunsch an seinem Platz), reihum über die Bretter (wer gerechnet
/// wurde, stellt sich hinten an). Vor dem Rechnen und vor dem Übernehmen fragt sie den Auftraggeber, ob die Stellung noch
/// gewünscht ist — kein Zuschauer mit Balken, Partie weitergezogen oder Feed zu → verworfen.
/// </summary>
public sealed class LiveEngineQueue : IAsyncDisposable
{
    private sealed record Entry(string Fen, Func<string, bool> Wanted, Action<string, LiveEval> Apply);

    private readonly ILiveEngineEvaluator _evaluator;
    private readonly ILogger<LiveEngineQueue> _logger;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Entry> _pending = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;

    public LiveEngineQueue(ILiveEngineEvaluator evaluator, ILogger<LiveEngineQueue> logger)
    {
        _evaluator = evaluator;
        _logger = logger;
    }

    /// <summary>Wartende Bretter (Tests).</summary>
    public int Pending { get { lock (_lock) return _pending.Count; } }

    /// <summary>Stellung <paramref name="fen"/> für das Brett <paramref name="key"/> vormerken (ersetzt einen älteren Wunsch
    /// desselben Bretts an dessen Platz).</summary>
    public void Enqueue(string key, string fen, Func<string, bool> wanted, Action<string, LiveEval> apply)
    {
        lock (_lock)
        {
            if (!_pending.ContainsKey(key)) _order.AddLast(key);
            _pending[key] = new Entry(fen, wanted, apply);
            _worker ??= Task.Run(() => RunAsync(_stop.Token));
        }
        _signal.Release();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _signal.WaitAsync(ct);
                Entry? next;
                lock (_lock)
                {
                    if (_order.First is not { } node) continue;
                    _order.RemoveFirst();
                    _pending.Remove(node.Value, out next);
                }
                if (next is null || !next.Wanted(next.Fen)) continue;
                LiveEval? ev = null;
                try { ev = await _evaluator.EvaluateAsync(next.Fen, ct); }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _logger.LogWarning(e, "Live-Bretter: Engine-Bewertung fehlgeschlagen");
                }
                if (ev is not null && next.Wanted(next.Fen)) next.Apply(next.Fen, ev);
            }
        }
        catch (OperationCanceledException) { /* Herunterfahren */ }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_worker is not null) { try { await _worker; } catch { /* egal */ } }
        _stop.Dispose();
    }
}
