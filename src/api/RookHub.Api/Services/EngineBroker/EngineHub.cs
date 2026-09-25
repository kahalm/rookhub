using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>
/// Ein Analyse-Auftrag auf dem Weg vom Anfragenden (Analysebrett-Proxy, Auftrags-Worker) zum Provider
/// und zurück. Wartet in der Schlange seines Selectors, bis ein Provider ihn abholt, dann in
/// <see cref="EngineHub"/>s „laufend"-Tabelle, bis der Upload beginnt.
/// </summary>
public sealed class PendingJob
{
    /// <summary>Wie viele fertige Zeilen zwischen Upload und Anfragendem liegen dürfen, bevor der Upload
    /// wartet (Rückstau bis zum Provider — lila-engine nimmt 1).</summary>
    public const int LineBuffer = 64;

    private readonly CancellationTokenSource _requesterGone = new();
    private int _cancelled;

    public PendingJob(string selector, string engineId, JsonObject engineJson, EngineWork work, string rootFen)
    {
        Selector = selector;
        EngineId = engineId;
        EngineJson = engineJson;
        Work = work;
        RootFen = rootFen;
        Lines = Channel.CreateBounded<string>(new BoundedChannelOptions(LineBuffer)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public string Selector { get; }
    public string EngineId { get; }
    /// <summary>Das <c>engine</c>-Objekt der Abhol-Antwort (Lichess-Form, mit clientSecret).</summary>
    public JsonObject EngineJson { get; }
    public EngineWork Work { get; }
    /// <summary>Stellung nach <c>initialFen</c> + <c>moves</c> — dort werden die Varianten nachgespielt.</summary>
    public string RootFen { get; }

    /// <summary>Vergeben beim Abholen (16 Zeichen).</summary>
    public string? Id { get; internal set; }
    public DateTime? AcquiredAt { get; internal set; }

    /// <summary>Fertige ndjson-Zeilen (mit <c>\n</c>) für den Anfragenden.</summary>
    public Channel<string> Lines { get; }

    /// <summary>Gesetzt, sobald der Provider mit dem Upload beginnt — der Anfragende wartet darauf höchstens
    /// <see cref="LocalBrokerOptions.ProviderTimeout"/>.</summary>
    public TaskCompletionSource Accepted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Der Anfragende ist weg (Stellungswechsel, Pause, Timeout) — der Upload endet sofort mit 200.</summary>
    public CancellationToken RequesterGone => _requesterGone.Token;

    /// <summary>lila-engine <c>is_valid</c>: ein Auftrag, dessen Anfragender weg ist, wird beim Abholen übersprungen.</summary>
    public bool IsValid => !_requesterGone.IsCancellationRequested;

    public void CancelRequester()
    {
        if (Interlocked.Exchange(ref _cancelled, 1) == 1) return;
        try { _requesterGone.Cancel(); }
        catch (ObjectDisposedException) { }
        Lines.Writer.TryComplete();
    }
}

/// <summary>
/// Die Vermittlung des eigenen Brokers (Singleton, im Speicher — wie <c>hub.rs</c> + <c>ongoing.rs</c> von
/// lila-engine): je Provider-Selector eine Warteschlange, aus der der Long-Poll des Providers
/// (<c>POST /api/external-engine/work</c>) den ältesten GÜLTIGEN Auftrag holt, und die Tabelle der abgeholten
/// Aufträge, deren Upload noch nicht begonnen hat.
///
/// <para><b>Warum im Speicher:</b> der einzige Anfragende ist diese API selbst (in-Prozess), ein Auftrag
/// lebt Sekunden bis Minuten und ist ohne den wartenden Anfragenden wertlos. Eine zweite API-Instanz gegen
/// dieselbe Datenbank ist ohnehin ausgeschlossen (sie stritte mit dem Auftrags-Worker um die Engines).</para>
///
/// <para><b>Wartende Provider bekommen einen Auftrag DIREKT übergeben</b> (kein Umweg über die Schlange):
/// sonst läge er bis zum nächsten Weckruf herum. Eine Schlange wird nur entfernt, wenn sie leer ist UND
/// niemand wartet — unter ihrem eigenen Schloss und mit Marke, damit ein gleichzeitiges Einreihen nicht in
/// einer verwaisten Schlange landet.</para>
/// </summary>
public sealed class EngineHub : IDisposable
{
    private sealed class SelectorQueue
    {
        public readonly LinkedList<PendingJob> Items = new();
        public readonly LinkedList<TaskCompletionSource<PendingJob>> Waiters = new();
        public bool Removed;
    }

    private readonly ConcurrentDictionary<string, SelectorQueue> _queues = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingJob> _ongoing = new(StringComparer.Ordinal);
    private readonly LocalBrokerOptions _options;
    private readonly Func<DateTime> _now;
    private readonly Timer? _sweeper;

    public EngineHub(LocalBrokerOptions options) : this(options, () => DateTime.UtcNow, startSweeper: true) { }

    public EngineHub(LocalBrokerOptions options, Func<DateTime> now, bool startSweeper)
    {
        _options = options;
        _now = now;
        if (startSweeper)
            _sweeper = new Timer(_ => Sweep(), null, TimeSpan.FromSeconds(31), TimeSpan.FromSeconds(31));
    }

    public BrokerStats Stats { get; } = new();

    /// <summary>Einreihen. <c>false</c> = Schlange des Selectors voll (→ 503 an den Anfragenden).</summary>
    public bool Submit(PendingJob job)
    {
        while (true)
        {
            var q = _queues.GetOrAdd(job.Selector, _ => new SelectorQueue());
            lock (q)
            {
                if (q.Removed) continue;
                while (q.Waiters.First is { } waiter)
                {
                    q.Waiters.RemoveFirst();
                    if (waiter.Value.TrySetResult(job)) return true;
                }
                PruneInvalid(q);
                if (q.Items.Count >= _options.MaxQueuedPerEngine) return false;
                q.Items.AddLast(job);
                return true;
            }
        }
    }

    /// <summary>Long-Poll des Providers: ältester gültiger Auftrag des Selectors, sonst nach
    /// <paramref name="wait"/> <c>null</c>. Ein abgeholter Auftrag bekommt seine Kennung und steht bis zum
    /// Upload in der „laufend"-Tabelle.</summary>
    public async Task<PendingJob?> AcquireAsync(string selector, TimeSpan wait, CancellationToken ct)
    {
        SelectorQueue q;
        TaskCompletionSource<PendingJob> waiter;
        while (true)
        {
            q = _queues.GetOrAdd(selector, _ => new SelectorQueue());
            lock (q)
            {
                if (q.Removed) continue;
                while (q.Items.First is { } node)
                {
                    q.Items.RemoveFirst();
                    if (node.Value.IsValid) return Claim(node.Value);
                }
                waiter = new TaskCompletionSource<PendingJob>(TaskCreationOptions.RunContinuationsAsynchronously);
                q.Waiters.AddLast(waiter);
                break;
            }
        }

        try
        {
            var job = await waiter.Task.WaitAsync(wait, ct);
            return Claim(job);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            PendingJob? handedOver = null;
            lock (q)
            {
                // Zwischen Ablauf und Schloss kann ein Auftrag übergeben worden sein — er darf nicht verloren gehen.
                if (!q.Waiters.Remove(waiter) && waiter.Task.IsCompletedSuccessfully) handedOver = waiter.Task.Result;
                if (handedOver is not null && ct.IsCancellationRequested)
                {
                    // Der Provider ist weg: zurück an den Anfang der Schlange statt verfallen lassen.
                    if (handedOver.IsValid) q.Items.AddFirst(handedOver);
                    handedOver = null;
                }
                RemoveIfIdle(selector, q);
            }
            if (handedOver is not null) return Claim(handedOver);
            if (ex is OperationCanceledException) throw;
            return null;
        }
    }

    /// <summary>Upload beginnt: den abgeholten Auftrag aus der Tabelle nehmen (genau einmal).</summary>
    public PendingJob? TakeOngoing(string jobId) =>
        _ongoing.TryRemove(jobId, out var job) ? job : null;

    /// <summary>Aufräumen (alle 31 s): ungültige Aufträge aus den Schlangen, verfallene aus der
    /// „laufend"-Tabelle (abgeholt, aber kein Upload binnen <see cref="LocalBrokerOptions.OngoingExpiry"/>).</summary>
    public void Sweep()
    {
        var now = _now();
        foreach (var (selector, q) in _queues)
        {
            lock (q)
            {
                PruneInvalid(q);
                RemoveIfIdle(selector, q);
            }
        }
        foreach (var (id, job) in _ongoing)
        {
            if (!job.IsValid || (job.AcquiredAt is { } at && now - at > _options.OngoingExpiry))
            {
                if (_ongoing.TryRemove(new KeyValuePair<string, PendingJob>(id, job)))
                {
                    job.CancelRequester();
                    Stats.Note(job.EngineId, s => s.OngoingExpired++);
                }
            }
        }
    }

    /// <summary>Wartende Aufträge dieses Selectors (für Tests und Diagnose).</summary>
    public int QueuedCount(string selector)
    {
        if (!_queues.TryGetValue(selector, out var q)) return 0;
        lock (q) return q.Items.Count(j => j.IsValid);
    }

    public int OngoingCount => _ongoing.Count;

    private PendingJob Claim(PendingJob job)
    {
        job.Id = ProviderSecrets.NewJobId();
        job.AcquiredAt = _now();
        _ongoing[job.Id] = job;
        return job;
    }

    private static void PruneInvalid(SelectorQueue q)
    {
        var node = q.Items.First;
        while (node is not null)
        {
            var next = node.Next;
            if (!node.Value.IsValid) q.Items.Remove(node);
            node = next;
        }
    }

    /// <summary>Unter dem Schloss von <paramref name="q"/> aufrufen.</summary>
    private void RemoveIfIdle(string selector, SelectorQueue q)
    {
        if (q.Items.Count > 0 || q.Waiters.Count > 0) return;
        q.Removed = true;
        _queues.TryRemove(new KeyValuePair<string, SelectorQueue>(selector, q));
    }

    public void Dispose() => _sweeper?.Dispose();
}

/// <summary>Zähler je Engine für das Protokoll (Aufträge, Uploads ohne bestmove, Provider-Timeouts, vom
/// Anfragenden abgebrochene Uploads, volle Schlangen, verfallene Abholungen).</summary>
public sealed class BrokerStats
{
    public sealed class EngineCounters
    {
        public long Jobs;
        public long Completed;
        public long WithoutBestmove;
        public long RequesterGone;
        public long ProviderGone;
        public long ProviderTimeouts;
        public long QueueFull;
        public long OngoingExpired;

        public override string ToString() =>
            $"jobs={Jobs} completed={Completed} noBestmove={WithoutBestmove} requesterGone={RequesterGone} "
            + $"providerGone={ProviderGone} providerTimeout={ProviderTimeouts} queueFull={QueueFull} expired={OngoingExpired}";
    }

    private readonly ConcurrentDictionary<string, EngineCounters> _byEngine = new(StringComparer.Ordinal);
    private long _version;

    public void Note(string engineId, Action<EngineCounters> change)
    {
        var c = _byEngine.GetOrAdd(engineId, _ => new EngineCounters());
        lock (c) change(c);
        Interlocked.Increment(ref _version);
    }

    public long Version => Interlocked.Read(ref _version);

    public IReadOnlyDictionary<string, string> Snapshot() =>
        _byEngine.ToDictionary(kv => kv.Key, kv => { lock (kv.Value) return kv.Value.ToString(); });

    public EngineCounters? For(string engineId) => _byEngine.TryGetValue(engineId, out var c) ? c : null;
}
