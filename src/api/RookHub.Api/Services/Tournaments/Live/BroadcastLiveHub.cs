using System.Threading.Channels;

namespace RookHub.Api.Services.Tournaments.Live;

/// <summary>Was ein Zuschauer der Live-Bretter vom Hub bekommt.</summary>
public abstract record LiveEvent;
/// <summary>Vollstand der Runde (erstes Ereignis jeder Verbindung, sobald geladen).</summary>
public sealed record LiveSnapshot(IReadOnlyList<LiveGame> Games, string State) : LiveEvent;
/// <summary>Eine Partie hat sich geändert (neuer Zug, Uhr, Ergebnis) oder ist neu dazugekommen.</summary>
public sealed record LiveGameChanged(LiveGame Game) : LiveEvent;
/// <summary>Zustand der Verbindung zu Lichess: <c>connecting</c>, <c>live</c>, <c>reconnecting</c>, <c>finished</c>.</summary>
public sealed record LiveStateChanged(string State) : LiveEvent;

/// <summary>
/// Live-Bretter: hält je Lichess-Runde, die gerade mindestens ein Zuschauer offen hat, GENAU EINE Verbindung zum Strom
/// <c>/api/stream/broadcast/round/{id}.pgn</c> und den Stand aller Partien im Speicher (Singleton). Ablauf je Runde:
/// <list type="number">
/// <item>Erster Zuschauer → einmal das ganze Runden-PGN laden (Vollstand für alle), dann den Strom öffnen. Der schickt nach
///   dem Verbinden alle Partien noch einmal — Unverändertes (gleicher <see cref="LiveGame.Hash"/>) geht nicht hinaus.</item>
/// <item>Strom zu / Netzfehler → neu verbinden mit wachsender Pause (<see cref="HubOptions.ReconnectMin"/> …
///   <see cref="HubOptions.ReconnectMax"/>), 429 → <see cref="HubOptions.RateLimitPause"/>.</item>
/// <item>Fertige Runde (Lichess meldet sie fertig oder alle Partien haben ein Ergebnis) → kein Strom; die Endstellungen
///   bleiben <see cref="HubOptions.FinishedKeep"/> im Speicher, weitere Zuschauer laden nichts nach.</item>
/// <item>Letzter Zuschauer weg → nach <see cref="HubOptions.IdleClose"/> wird der Strom geschlossen (ein Neuladen der
///   Seite reißt ihn also nicht ab).</item>
/// </list>
/// Engine-Balken: solange mindestens ein Zuschauer der Runde ihn an hat, fragt der Hub für Stellungen ohne
/// <c>[%eval]</c> das Lichess Cloud-Eval (<see cref="LichessCloudEval"/>) und schickt die Partie mit Bewertung erneut.
/// </summary>
public sealed class BroadcastLiveHub : IAsyncDisposable
{
    public sealed class HubOptions
    {
        public TimeSpan IdleClose { get; init; } = TimeSpan.FromSeconds(90);
        public TimeSpan ReconnectMin { get; init; } = TimeSpan.FromSeconds(2);
        public TimeSpan ReconnectMax { get; init; } = TimeSpan.FromSeconds(60);
        public TimeSpan RateLimitPause { get; init; } = HttpLichessLiveSource.RateLimitPause;
        public TimeSpan FinishedKeep { get; init; } = TimeSpan.FromMinutes(30);
        /// <summary>Höchstzahl wartender Ereignisse je Zuschauer; ein hängender Browser verliert die ältesten (jedes
        /// Ereignis trägt den ganzen Stand seiner Partie, Späteres ersetzt Früheres).</summary>
        public int QueueLimit { get; init; } = 512;
    }

    private readonly ILichessLiveSource _source;
    private readonly LichessCloudEval? _cloud;
    private readonly LiveEngineQueue? _engine;
    private readonly ILogger<BroadcastLiveHub> _logger;
    private readonly HubOptions _options;
    private readonly Dictionary<string, Feed> _feeds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (List<LiveGame> Games, DateTime Until)> _finished = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _shutdown = new();

    public BroadcastLiveHub(ILichessLiveSource source, LichessCloudEval? cloud, ILogger<BroadcastLiveHub> logger, HubOptions? options = null,
        LiveEngineQueue? engine = null)
    {
        _source = source;
        _cloud = cloud;
        _engine = engine;
        _logger = logger;
        _options = options ?? new HubOptions();
    }

    /// <summary>Runden mit offenem Feed (Tests, Diagnose).</summary>
    public int OpenFeeds { get { lock (_lock) return _feeds.Count; } }

    /// <summary>Zuschauer einer Runde (Tests).</summary>
    public int Viewers(string roundId)
    {
        lock (_lock) return _feeds.TryGetValue(roundId, out var f) ? f.ViewerCount : 0;
    }

    /// <summary>
    /// Was der Server von einer Runde gerade ohnehin weiß — Partien eines offenen Feeds oder die 30 min gemerkten Endstellungen
    /// einer fertigen Runde —, OHNE einen Strom zu öffnen (Link-Vorschau, <c>docs/api/link-vorschau.md</c>). <c>null</c> = nichts
    /// im Speicher (niemand schaut zu); ein Vorschau-Abruf baut nie eine Verbindung zu Lichess auf.
    /// </summary>
    public IReadOnlyList<LiveGame>? Peek(string roundId)
    {
        lock (_lock)
        {
            if (_feeds.TryGetValue(roundId, out var feed) && !feed.IsClosed && feed.IsLoaded) return feed.Games();
            return _finished.TryGetValue(roundId, out var kept) && kept.Until > DateTime.UtcNow ? kept.Games : null;
        }
    }

    /// <summary>Zuschauen. <paramref name="finished"/> = Lichess meldet die Runde fertig (dann kein Strom).
    /// Abmelden über <see cref="RoundSubscription.DisposeAsync"/>.</summary>
    /// <remarks>Finden/Anlegen des Feeds UND das Anmelden daran laufen unter dem Hub-Lock — Schließen (<see cref="TryCloseIdle"/>)
    /// nimmt denselben Lock, ein Zuschauer kann also nie an einen Feed geraten, der gerade geschlossen wird (Sperrreihenfolge
    /// immer Hub → Feed).</remarks>
    public RoundSubscription Subscribe(string roundId, bool finished, bool wantsEval)
    {
        RoundSubscription sub;
        lock (_lock)
        {
            if (!_feeds.TryGetValue(roundId, out var feed) || feed.IsClosed)
            {
                feed = new Feed(this, roundId, finished);
                if (_finished.TryGetValue(roundId, out var kept) && kept.Until > DateTime.UtcNow) feed.Preload(kept.Games);
                _feeds[roundId] = feed;
            }
            sub = feed.Add(wantsEval);
        }
        sub.AfterAdd();
        return sub;
    }

    /// <summary>Testhaken: läuft, nachdem der Leerlauf-Timer abgelaufen ist und BEVOR geschlossen wird (dort lag früher das
    /// Fenster, in dem ein neuer Zuschauer an den sterbenden Feed geriet).</summary>
    internal Func<Task>? BeforeIdleClose { get; set; }

    /// <summary>Leerlauf abgelaufen: atomar unter Hub- und Feed-Lock prüfen, ob noch niemand schaut, dann Feed schließen und
    /// aus der Liste nehmen. Kam inzwischen ein Zuschauer (oder ein neuerer Timer), bleibt alles, wie es ist.</summary>
    private void TryCloseIdle(Feed feed, CancellationTokenSource idle)
    {
        lock (_lock)
        {
            if (!feed.CloseIfIdle(idle)) return;
            if (_feeds.TryGetValue(feed.RoundId, out var f) && ReferenceEquals(f, feed)) _feeds.Remove(feed.RoundId);
            if (feed.IsFinished) _finished[feed.RoundId] = (feed.Games(), DateTime.UtcNow + _options.FinishedKeep);
            foreach (var k in _finished.Where(x => x.Value.Until <= DateTime.UtcNow).Select(x => x.Key).ToList()) _finished.Remove(k);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync();
        List<Feed> feeds;
        lock (_lock) feeds = _feeds.Values.ToList();
        foreach (var f in feeds) f.Stop();
        _shutdown.Dispose();
    }

    /// <summary>Ein Zuschauer: Ereignisse lesen, Engine-Balken umschalten, abmelden.</summary>
    public sealed class RoundSubscription : IAsyncDisposable
    {
        private readonly Feed _feed;
        internal readonly Channel<LiveEvent> Channel;
        internal volatile bool WantsEval;
        private int _disposed;

        internal RoundSubscription(Feed feed, bool wantsEval, int queueLimit)
        {
            _feed = feed;
            WantsEval = wantsEval;
            Channel = System.Threading.Channels.Channel.CreateBounded<LiveEvent>(new BoundedChannelOptions(queueLimit)
            {
                FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true,
            });
        }

        public ChannelReader<LiveEvent> Events => Channel.Reader;

        /// <summary>Nach dem Anmelden außerhalb der Locks: offene Bewertungen anstoßen.</summary>
        internal void AfterAdd()
        {
            if (WantsEval) _feed.QueueAllEvals();
        }

        /// <summary>Engine-Balken dieses Zuschauers an/aus.</summary>
        public void SetEval(bool on)
        {
            WantsEval = on;
            if (on) _feed.QueueAllEvals();
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) _feed.Remove(this);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Eine Runde: Partien, Zuschauer, Strom-Schleife, Bewertungs-Schleife.</summary>
    internal sealed class Feed
    {
        private readonly BroadcastLiveHub _hub;
        private readonly Lock _lock = new();
        private readonly Dictionary<string, LiveGame> _games = new(StringComparer.Ordinal);
        private readonly List<RoundSubscription> _subs = [];
        private readonly Channel<string> _evalQueue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        private readonly HashSet<string> _evalQueued = new(StringComparer.Ordinal);
        private CancellationTokenSource? _run;
        private CancellationTokenSource? _idle;
        private bool _loaded;
        private bool _lichessFinished;
        private string _state = "connecting";
        private bool _evalLoopStarted;
        private bool _closed;

        public Feed(BroadcastLiveHub hub, string roundId, bool finished)
        {
            _hub = hub;
            RoundId = roundId;
            _lichessFinished = finished;
        }

        public string RoundId { get; }
        public bool IsFinished { get { lock (_lock) return _state == "finished"; } }
        public int ViewerCount { get { lock (_lock) return _subs.Count; } }
        /// <summary>Geschlossen (Leerlauf) — nimmt keine Zuschauer mehr an; <see cref="Subscribe"/> legt dann einen neuen an.</summary>
        public bool IsClosed { get { lock (_lock) return _closed; } }
        /// <summary>Vollstand schon geladen (vorher gäbe <see cref="Games"/> eine halbe Runde).</summary>
        public bool IsLoaded { get { lock (_lock) return _loaded; } }

        public List<LiveGame> Games()
        {
            lock (_lock) return _games.Values.ToList();
        }

        public void Preload(List<LiveGame> games)
        {
            lock (_lock)
            {
                foreach (var g in games) _games[g.Id] = g;
                _loaded = true;
                _state = "finished";
            }
        }

        public RoundSubscription Add(bool wantsEval)
        {
            var sub = new RoundSubscription(this, wantsEval, _hub._options.QueueLimit);
            lock (_lock)
            {
                if (_closed) throw new InvalidOperationException("Feed ist geschlossen");   // kann unter dem Hub-Lock nicht passieren
                _subs.Add(sub);
                _idle?.Cancel();
                _idle = null;
                sub.Channel.Writer.TryWrite(_loaded ? new LiveSnapshot(_games.Values.ToList(), _state) : new LiveStateChanged(_state));
                if (_run is null && _state != "finished")
                {
                    _run = CancellationTokenSource.CreateLinkedTokenSource(_hub._shutdown.Token);
                    var token = _run.Token;
                    _ = Task.Run(() => RunAsync(token), CancellationToken.None);
                }
            }
            return sub;
        }

        public void Remove(RoundSubscription sub)
        {
            lock (_lock)
            {
                _subs.Remove(sub);
                sub.Channel.Writer.TryComplete();
                if (_subs.Count > 0) return;
                _idle?.Cancel();
                var idle = _idle = CancellationTokenSource.CreateLinkedTokenSource(_hub._shutdown.Token);
                _ = IdleAsync(idle);
            }
        }

        private async Task IdleAsync(CancellationTokenSource idle)
        {
            try { await Task.Delay(_hub._options.IdleClose, idle.Token); }
            catch (OperationCanceledException) { return; }
            if (_hub.BeforeIdleClose is { } hook) await hook();
            _hub.TryCloseIdle(this, idle);
        }

        /// <summary>Schließen, falls noch leer und dieser Timer der aktuelle ist (Aufrufer hält den Hub-Lock). → geschlossen?</summary>
        public bool CloseIfIdle(CancellationTokenSource idle)
        {
            lock (_lock)
            {
                if (_closed || _subs.Count > 0 || !ReferenceEquals(_idle, idle)) return false;
                _closed = true;
                _run?.Cancel();
                _run = null;
                _evalQueue.Writer.TryComplete();
                return true;
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _run?.Cancel();
                foreach (var s in _subs) s.Channel.Writer.TryComplete();
            }
        }

        private void Broadcast(LiveEvent e)
        {
            lock (_lock)
                foreach (var s in _subs) s.Channel.Writer.TryWrite(e);
        }

        private void SetState(string state)
        {
            lock (_lock)
            {
                if (_state == state) return;
                _state = state;
            }
            Broadcast(new LiveStateChanged(state));
        }

        private bool AllFinished()
        {
            lock (_lock) return _games.Count > 0 && _games.Values.All(g => g.Finished);
        }

        /// <summary>Partie übernehmen; → geändert? Eine schon geholte Cloud-Bewertung bleibt bei gleicher Stellung stehen.</summary>
        private LiveGame? Apply(string pgn)
        {
            var g = LiveGameParser.Parse(pgn);
            if (g is null) return null;
            lock (_lock)
            {
                if (_games.TryGetValue(g.Id, out var old))
                {
                    if (old.Hash == g.Hash) return null;
                    if (g.Eval is null && old.Eval is { Source: "cloud" or "engine" } && old.Fen == g.Fen) g = g with { Eval = old.Eval };
                }
                _games[g.Id] = g;
            }
            return g;
        }

        private async Task RunAsync(CancellationToken ct)
        {
            var o = _hub._options;
            var backoff = o.ReconnectMin;
            try
            {
                // 1) Vollstand
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var pgn = await _hub._source.GetRoundPgnAsync(RoundId, ct) ?? "";
                        var splitter = new BroadcastPgnSplitter();
                        foreach (var game in splitter.Feed(pgn).Concat(splitter.Flush())) Apply(game);
                        List<LiveGame> games;
                        lock (_lock) { _loaded = true; games = _games.Values.ToList(); }
                        if (_lichessFinished || AllFinished()) { lock (_lock) _state = "finished"; }
                        else lock (_lock) _state = "live";
                        string state;
                        lock (_lock) state = _state;
                        Broadcast(new LiveSnapshot(games, state));
                        QueueAllEvals();
                        break;
                    }
                    catch (LichessRateLimitedException) { await Task.Delay(o.RateLimitPause, ct); }
                    catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
                    {
                        _hub._logger.LogInformation("Live-Bretter: Runde {Round} nicht geladen ({Error}) — neuer Versuch", RoundId, e.Message);
                        await Task.Delay(backoff, ct);
                        backoff = Next(backoff);
                    }
                }
                if (IsFinished) return;

                // 2) Strom
                backoff = o.ReconnectMin;
                while (!ct.IsCancellationRequested)
                {
                    var gotData = false;
                    try
                    {
                        var splitter = new BroadcastPgnSplitter();
                        await foreach (var chunk in _hub._source.StreamRoundAsync(RoundId, ct))
                        {
                            if (!gotData) { gotData = true; SetState("live"); }
                            foreach (var pgn in splitter.Feed(chunk)) Changed(Apply(pgn));
                        }
                        foreach (var pgn in splitter.Flush()) Changed(Apply(pgn));
                    }
                    catch (LichessRateLimitedException)
                    {
                        SetState("reconnecting");
                        await Task.Delay(o.RateLimitPause, ct);
                        continue;
                    }
                    catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
                    {
                        _hub._logger.LogInformation("Live-Bretter: Strom der Runde {Round} unterbrochen ({Error})", RoundId, e.Message);
                    }
                    if (ct.IsCancellationRequested) break;
                    if (AllFinished()) { SetState("finished"); return; }
                    if (gotData) backoff = o.ReconnectMin;
                    SetState("reconnecting");
                    await Task.Delay(backoff, ct);
                    backoff = Next(backoff);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* letzter Zuschauer weg / Herunterfahren */ }
            catch (Exception e)
            {
                _hub._logger.LogWarning(e, "Live-Bretter: Runde {Round} abgebrochen", RoundId);
                SetState("reconnecting");
            }
            finally
            {
                lock (_lock) if (_state != "finished") _run = null;
            }
        }

        private TimeSpan Next(TimeSpan t) => TimeSpan.FromTicks(Math.Min(t.Ticks * 2, _hub._options.ReconnectMax.Ticks));

        private void Changed(LiveGame? g)
        {
            if (g is null) return;
            Broadcast(new LiveGameChanged(g));
            if (g.Eval is null) QueueEval(g.Id);
        }

        private bool AnyWantsEval()
        {
            lock (_lock) return _subs.Any(s => s.WantsEval);
        }

        public void QueueAllEvals()
        {
            if (!CanEval || !AnyWantsEval()) return;
            List<string> ids;
            lock (_lock) ids = _games.Values.Where(g => g.Eval is null).Select(g => g.Id).ToList();
            foreach (var id in ids) QueueEval(id);
        }

        private void QueueEval(string id)
        {
            if (!CanEval || !AnyWantsEval()) return;
            lock (_lock)
            {
                if (_closed || !_evalQueued.Add(id)) return;
                if (!_evalLoopStarted)
                {
                    _evalLoopStarted = true;
                    var token = _hub._shutdown.Token;
                    _ = Task.Run(() => EvalLoopAsync(token), CancellationToken.None);
                }
            }
            _evalQueue.Writer.TryWrite(id);
        }

        private bool CanEval => _hub._cloud is not null || _hub._engine is not null;

        /// <summary>Wird diese Stellung noch gebraucht? Zuschauer mit Balken da, Feed offen, Partie steht noch dort, ohne Bewertung.</summary>
        private bool StillWanted(string id, string fen)
        {
            lock (_lock)
                return !_closed && _subs.Any(s => s.WantsEval)
                       && _games.TryGetValue(id, out var g) && g.Fen == fen && g.Eval is null;
        }

        private void ApplyEval(string id, string fen, LiveEval ev)
        {
            LiveGame? updated = null;
            lock (_lock)
            {
                if (_games.TryGetValue(id, out var now) && now.Fen == fen && now.Eval is null)
                    _games[id] = updated = now with { Eval = ev };
            }
            if (updated is not null) Broadcast(new LiveGameChanged(updated));
        }

        /// <summary>Eine Bewertung nach der anderen (die Drossel sitzt in <see cref="LichessCloudEval"/>); hört auf zu fragen,
        /// sobald niemand mehr den Balken an hat — offene Wünsche fallen dann weg.</summary>
        private async Task EvalLoopAsync(CancellationToken ct)
        {
            try
            {
                await foreach (var id in _evalQueue.Reader.ReadAllAsync(ct))
                {
                    lock (_lock) _evalQueued.Remove(id);
                    if (!AnyWantsEval() || ViewerCount == 0) continue;
                    LiveGame? g;
                    lock (_lock) _games.TryGetValue(id, out g);
                    if (g is null || g.Eval is not null) continue;
                    var ev = _hub._cloud is null ? null : await _hub._cloud.GetAsync(g.Fen, ct);
                    if (ev is not null) { ApplyEval(id, g.Fen, ev); continue; }
                    // Nicht im Cloud-Speicher (bei Mittelspielstellungen der Normalfall): eigene Engine — über die EINE
                    // globale Schlange (höchstens eine Rechnung, je Brett nur die neueste Stellung, reihum).
                    var gameId = id;
                    _hub._engine?.Enqueue($"{RoundId}/{gameId}", g.Fen, fen => StillWanted(gameId, fen), (fen, e) => ApplyEval(gameId, fen, e));
                }
            }
            catch (OperationCanceledException) { /* Herunterfahren */ }
        }
    }
}
