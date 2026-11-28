using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace RookHub.Api.Services.Tournaments.Live;

/// <summary>chess.com antwortet nicht (Drosselung 429, Cloudflare-Sperre 403, Ausfall, Zeitüberschreitung). Erbt von
/// <see cref="LichessRateLimitedException"/>, damit der <see cref="BroadcastLiveHub"/> wie nach einem Lichess-429 pausiert.</summary>
public sealed class ChessComUnavailableException(string message) : LichessRateLimitedException(message);

/// <summary>Roh-Abruf der chess.com-Events-Endpunkte (eigene Schnittstelle, damit die Abfrage-Logik ohne Netz testbar ist).</summary>
public interface IChessComEventsApi
{
    /// <summary><c>POST https://www.chess.com/events/v1/api/{path}</c> (leerer Rumpf, so ruft es die Seite selbst). <c>null</c> = 404.
    /// Wirft <see cref="ChessComUnavailableException"/> bei 429/403/5xx/Zeitüberschreitung.</summary>
    Task<string?> PostAsync(string path, CancellationToken ct);
}

/// <summary>
/// HTTP-Abruf, höflich: eigener User-Agent (wie der Lichess-Code, ohne persönliche Daten), gzip, höchstens zwei Abrufe
/// gleichzeitig und mindestens <see cref="MinSpacing"/> zwischen zwei Abrufen, 15 s Zeitgrenze, nach 429/403 eine Pause von
/// <see cref="RateLimitPause"/> für ALLE Abrufe (währenddessen wirft jeder Abruf sofort, ohne chess.com zu fragen).
/// </summary>
public sealed class HttpChessComEventsApi : IChessComEventsApi
{
    public const string ClientName = "chesscom-live";
    public static readonly TimeSpan RateLimitPause = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan MinSpacing = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly IHttpClientFactory _factory;
    private readonly string _base;
    private readonly SemaphoreSlim _gate = new(2, 2);
    private readonly Lock _lock = new();
    private DateTime _pausedUntil = DateTime.MinValue;
    private DateTime _next = DateTime.MinValue;

    public HttpChessComEventsApi(IHttpClientFactory factory, IConfiguration? config = null)
    {
        _factory = factory;
        _base = (config?["ChessCom:SiteUrl"] ?? ChessComEvents.Site).TrimEnd('/') + "/events/v1/api/";
    }

    public async Task<string?> PostAsync(string path, CancellationToken ct)
    {
        lock (_lock)
            if (DateTime.UtcNow < _pausedUntil) throw new ChessComUnavailableException("chess.com: paused after 429/403");
        await _gate.WaitAsync(ct);
        try
        {
            TimeSpan wait;
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var slot = _next > now ? _next : now;
                _next = slot + MinSpacing;
                wait = slot - now;
            }
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);
            HttpResponseMessage r;
            try
            {
                r = await _factory.CreateClient(ClientName).PostAsync(_base + path, new ByteArrayContent([]), timeout.Token);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                throw new ChessComUnavailableException("chess.com: " + e.Message);
            }
            using (r)
            {
                if (r.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
                {
                    lock (_lock) _pausedUntil = DateTime.UtcNow + RateLimitPause;
                    throw new ChessComUnavailableException($"chess.com: {(int)r.StatusCode}");
                }
                if (r.StatusCode == HttpStatusCode.NotFound) return null;
                if (!r.IsSuccessStatusCode) throw new ChessComUnavailableException($"chess.com: {(int)r.StatusCode}");
                return await r.Content.ReadAsStringAsync(timeout.Token);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>
/// chess.com-Übertragungen als Quelle der Live-Bretter (1.040.0) — von außen sieht sie aus wie Lichess (PGN je Runde, „Strom“
/// aus PGN-Stücken, Turnier-JSON in Lichess-Form), damit <see cref="BroadcastLiveHub"/>, <see cref="ExternalRoundRegistry"/> und
/// <see cref="TournamentLiveService"/> unverändert bleiben. chess.com hat keinen offenen Strom; stattdessen POLLT die Quelle:
/// <list type="bullet">
/// <item><b>Ein Raum-Abruf je Event und Takt</b> (<see cref="Options.Poll"/>, Vorgabe 8 s) für ALLE Runden und Zuschauer —
///   der Hub hält ohnehin nur einen „Strom“ je Runde, und mehrere Runden desselben Events teilen sich den gecachten Raum.
///   Nur solange jemand zuschaut (der Hub beendet den Strom 90 s nach dem letzten Zuschauer); eine Runde ohne begonnene Partie
///   fragt nur alle <see cref="Options.IdlePoll"/> (30 s).</item>
/// <item><b>Züge nur bei Änderung</b>: die Raum-Antwort trägt je Partie eine <c>revision</c>; nur Partien mit neuer Revision
///   werden einzeln abgerufen, höchstens <see cref="Options.GameFetchesPerTick"/> je Runde und Takt — zuerst die, die ein
///   Zuschauer ausdrücklich gewählt hat (<see cref="WantedGames"/>, z. B. hinzugefügte Bretter), dann die am längsten nicht
///   geholten. Bis dahin zeigt die Partie die Stellung aus der Raum-Antwort bzw. die zuletzt geholten Züge.</item>
/// <item><b>Fehler</b>: nach 429/403 ruht die ganze Quelle (<see cref="HttpChessComEventsApi.RateLimitPause"/>); sonst wartet der
///   Takt doppelt so lange (bis 2 min), nach drei Fehlern in Folge gibt der Strom an den Hub zurück (der pausiert 90 s).</item>
/// </list>
/// </summary>
public sealed class ChessComLiveSource
{
    public sealed class Options
    {
        public TimeSpan Poll { get; init; } = TimeSpan.FromSeconds(8);
        public TimeSpan IdlePoll { get; init; } = TimeSpan.FromSeconds(30);
        public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(2);
        public int GameFetchesPerTick { get; init; } = 6;
        /// <summary>Einmaliger Vollstand (erster Zuschauer, Abgleich alle 5 min): so viele Partien höchstens auf einmal.</summary>
        public int GameFetchesPerSnapshot { get; init; } = 12;

        public static Options From(IConfiguration config) => new()
        {
            Poll = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("LiveBoards:ChessCom:PollSeconds") ?? 8, 5, 120)),
            GameFetchesPerTick = Math.Clamp(config.GetValue<int?>("LiveBoards:ChessCom:GameFetchesPerTick") ?? 6, 1, 50),
        };
    }

    private sealed record RoomEntry(ChessComEvents.Room Room, DateTime At);
    /// <summary>Züge einer Partie zur Revision; <c>Moves</c> = <c>null</c>: chess.com kannte die Partie nicht (404) — bis zur
    /// nächsten Revision nicht erneut fragen.</summary>
    private sealed record MovesEntry(long Revision, IReadOnlyList<ChessComEvents.Move>? Moves, DateTime At);

    private static readonly TimeSpan MovesKeep = TimeSpan.FromHours(3);

    private readonly IChessComEventsApi _api;
    private readonly ILogger<ChessComLiveSource> _logger;
    private readonly Options _options;
    private readonly ConcurrentDictionary<string, RoomEntry> _rooms = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _roomLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MovesEntry> _moves = new(StringComparer.Ordinal);
    private DateTime _lastSweep = DateTime.UtcNow;

    public ChessComLiveSource(IChessComEventsApi api, ILogger<ChessComLiveSource> logger, Options? options = null)
    {
        _api = api;
        _logger = logger;
        _options = options ?? new Options();
    }

    /// <summary>Partien, die Zuschauer einer Runde ausdrücklich gewählt haben (<see cref="BroadcastLiveHub.WantedGames"/>);
    /// <c>null</c> = keine Vorliebe. Wird nach dem Anlegen des Hubs gesetzt.</summary>
    public Func<string, IReadOnlySet<string>?>? WantedGames { get; set; }

    /// <summary>Abrufe der Partie-Züge bisher (Tests, Diagnose).</summary>
    public int GameFetches => _gameFetches;
    private int _gameFetches;

    /// <summary>Der Raum, höchstens <paramref name="maxAge"/> alt (ein Abruf je Event, auch wenn mehrere gleichzeitig fragen).</summary>
    public async Task<ChessComEvents.Room?> RoomAsync(string slug, TimeSpan maxAge, CancellationToken ct)
    {
        if (_rooms.TryGetValue(slug, out var hit) && DateTime.UtcNow - hit.At < maxAge) return hit.Room;
        var gate = _roomLocks.GetOrAdd(slug, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_rooms.TryGetValue(slug, out hit) && DateTime.UtcNow - hit.At < maxAge) return hit.Room;
            var json = await _api.PostAsync("room/" + Uri.EscapeDataString(slug), ct);
            ChessComEvents.Room? room;
            try { room = json is null ? null : ChessComEvents.ParseRoom(json); }
            catch (JsonException e) { throw new ChessComUnavailableException("chess.com: unreadable room (" + e.Message + ")"); }
            if (room is null) { _rooms.TryRemove(slug, out _); return null; }
            _rooms[slug] = new RoomEntry(room, DateTime.UtcNow);
            return room;
        }
        finally { gate.Release(); }
    }

    /// <summary>Gruppen-Link (<c>/events/info/{slug}</c>) → Slug des Raums (siehe <see cref="ChessComEvents.PickRoom"/>).</summary>
    public async Task<string?> ResolveIndexAsync(string indexSlug, CancellationToken ct)
    {
        var json = await _api.PostAsync("index/" + Uri.EscapeDataString(indexSlug), ct);
        if (json is null) return null;
        try { return ChessComEvents.PickRoom(ChessComEvents.ParseIndex(json), DateTime.UtcNow)?.Slug; }
        catch (JsonException) { return null; }
    }

    /// <summary><c>cc~{event}</c> → Turnier-JSON in Lichess-Form; <c>null</c> = gibt es nicht.</summary>
    public async Task<string?> GetTourAsync(string tourId, CancellationToken ct)
    {
        if (ChessComEvents.RoomOfTour(tourId) is not { } slug) return null;
        var room = await RoomAsync(slug, TimeSpan.FromSeconds(30), ct);
        return room is null ? null : ChessComEvents.ToTourJson(room, DateTime.UtcNow);
    }

    /// <summary><c>cc~{event}~{runde}</c> → Turnier-JSON (mit allen Runden); <c>null</c> = Runde unbekannt.</summary>
    public async Task<string?> GetTourOfRoundAsync(string roundId, CancellationToken ct)
    {
        if (ChessComEvents.SplitRound(roundId) is not { } ids) return null;
        var room = await RoomAsync(ids.Room, TimeSpan.FromSeconds(30), ct);
        return room?.RoundBySlug(ids.Round) is null ? null : ChessComEvents.ToTourJson(room, DateTime.UtcNow);
    }

    /// <summary>Alle Partien der Runde als PGN (Vollstand); <c>null</c> = Runde unbekannt.</summary>
    public async Task<string?> GetRoundPgnAsync(string roundId, CancellationToken ct)
    {
        var built = await BuildAsync(roundId, _options.GameFetchesPerSnapshot, _options.Poll, ct);
        return built is null ? null : string.Concat(built.Value.Games.Select(x => x.Pgn));
    }

    /// <summary>PGN EINER Partie mit allen Zügen (für „Analysieren“) — holt die Züge, wenn sie nicht aktuell im Speicher sind.</summary>
    public async Task<string?> GetGamePgnAsync(string roundId, string gameSlug, CancellationToken ct)
    {
        if (ChessComEvents.SplitRound(roundId) is not { } ids || !ChessComEvents.IsGameId(gameSlug)) return null;
        var room = await RoomAsync(ids.Room, _options.Poll, ct);
        if (room?.RoundBySlug(ids.Round) is not { } round) return null;
        if (room.GamesOf(round).FirstOrDefault(g => g.Slug == gameSlug) is not { } game) return null;
        var moves = Current(ids.Room, round, game) is { } entry ? entry.Moves : await FetchMovesAsync(ids.Room, round, game, ct);
        return ChessComEvents.ToPgn(room, round, game, moves);
    }

    /// <summary>
    /// „Strom“ einer Runde: zuerst ein leeres Stück (verbunden), dann je Takt die PGNs der GEÄNDERTEN Partien (sonst ein leeres
    /// Stück als Lebenszeichen für den Wächter des Hubs). Endet, wenn alle Partien ein Ergebnis haben und ihre Züge geholt sind.
    /// </summary>
    public async IAsyncEnumerable<string> StreamRoundAsync(string roundId, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return "";
        var sent = new Dictionary<string, string>(StringComparer.Ordinal);
        var failures = 0;
        var wait = _options.Poll;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(wait, ct);
            (ChessComEvents.Room Room, ChessComEvents.Round Round, List<(ChessComEvents.Game Game, string Pgn, bool Complete)> Games)? built;
            try
            {
                built = await BuildAsync(roundId, _options.GameFetchesPerTick, _options.Poll - TimeSpan.FromMilliseconds(500), ct);
                failures = 0;
            }
            catch (ChessComUnavailableException e) when (++failures < 3)
            {
                wait = TimeSpan.FromTicks(Math.Min(wait.Ticks * 2, _options.MaxBackoff.Ticks));
                _logger.LogInformation("Live-Bretter: chess.com-Runde {Round} nicht gelesen ({Error}) — nächster Versuch in {Seconds} s",
                    roundId, e.Message, (int)wait.TotalSeconds);
                continue;
            }
            if (built is null) yield break;                                  // Runde verschwunden
            var (room, round, games) = built.Value;
            var sb = new StringBuilder();
            foreach (var (game, pgn, _) in games)
                if (!sent.TryGetValue(game.Slug, out var old) || old != pgn)
                {
                    sent[game.Slug] = pgn;
                    sb.Append(pgn);
                }
            yield return sb.ToString();
            if (ChessComEvents.IsFinished(room, round) && games.All(x => x.Complete)) yield break;
            wait = room.GamesOf(round).Any(g => g.LastMoveAt is not null || g.Finished) ? _options.Poll : _options.IdlePoll;
        }
    }

    /// <summary>Raum lesen, geänderte Partien (gedeckelt) nachholen, je Partie das PGN bauen. <c>Complete</c> = Züge aktuell.</summary>
    private async Task<(ChessComEvents.Room Room, ChessComEvents.Round Round, List<(ChessComEvents.Game Game, string Pgn, bool Complete)> Games)?>
        BuildAsync(string roundId, int budget, TimeSpan maxAge, CancellationToken ct)
    {
        if (ChessComEvents.SplitRound(roundId) is not { } ids) return null;
        var room = await RoomAsync(ids.Room, maxAge, ct);
        if (room?.RoundBySlug(ids.Round) is not { } round) return null;
        var games = room.GamesOf(round).ToList();
        var wanted = WantedGames?.Invoke(roundId);
        var stale = games.Where(g => Started(g) && Current(ids.Room, round, g) is null)
            .OrderByDescending(g => wanted?.Contains(g.Slug) == true)
            .ThenBy(g => _moves.TryGetValue(Key(ids.Room, round, g), out var m) ? m.At : DateTime.MinValue)
            .Take(budget).ToList();
        foreach (var g in stale)
        {
            ct.ThrowIfCancellationRequested();
            await FetchMovesAsync(ids.Room, round, g, ct);
        }
        Sweep();
        var list = games.Select(g =>
        {
            if (!Started(g)) return (g, ChessComEvents.ToPgn(room, round, g, []), true);
            var current = Current(ids.Room, round, g);
            var moves = current is not null ? current.Moves : _moves.TryGetValue(Key(ids.Room, round, g), out var m) ? m.Moves : null;
            return (g, ChessComEvents.ToPgn(room, round, g, moves), current is not null);
        }).ToList();
        return (room, round, list);
    }

    /// <summary>Hat die Partie schon begonnen (sonst gibt es keine Züge zu holen)?</summary>
    private static bool Started(ChessComEvents.Game g) => g.Finished || g.LastMoveAt is not null || g.Fen is not null;

    private static string Key(string room, ChessComEvents.Round r, ChessComEvents.Game g) => $"{room}~{r.Slug}~{g.Slug}";

    /// <summary>Gespeicherter Stand, wenn er mindestens so neu ist wie die Raum-Antwort; sonst <c>null</c> (neu holen).</summary>
    private MovesEntry? Current(string room, ChessComEvents.Round r, ChessComEvents.Game g) =>
        _moves.TryGetValue(Key(room, r, g), out var m) && m.Revision >= g.Revision ? m : null;

    private async Task<IReadOnlyList<ChessComEvents.Move>?> FetchMovesAsync(string room, ChessComEvents.Round r, ChessComEvents.Game g,
        CancellationToken ct)
    {
        Interlocked.Increment(ref _gameFetches);
        var json = await _api.PostAsync($"game/{Uri.EscapeDataString(room)}/{Uri.EscapeDataString(r.Slug)}/{Uri.EscapeDataString(g.Slug)}", ct);
        (ChessComEvents.Game Game, IReadOnlyList<ChessComEvents.Move> Moves)? detail = null;
        try { if (json is not null) detail = ChessComEvents.ParseGameDetail(json); }
        catch (JsonException e)
        {
            _logger.LogInformation("Live-Bretter: chess.com-Partie {Game} unlesbar ({Error})", g.Slug, e.Message);
        }
        if (detail is null)
        {
            _moves[Key(room, r, g)] = new MovesEntry(g.Revision, null, DateTime.UtcNow);   // nicht in jedem Takt neu fragen
            return null;
        }
        // Hinkt die Partie-Antwort der Raum-Antwort hinterher (Zwischenspeicher bei chess.com), gilt sie trotzdem für diese
        // Revision — sonst würde dieselbe Partie in jedem Takt neu geholt; der nächste Zug (neue Revision) holt ohnehin nach.
        var rev = Math.Max(detail.Value.Game.Revision, g.Revision);
        _moves[Key(room, r, g)] = new MovesEntry(rev, detail.Value.Moves, DateTime.UtcNow);
        return detail.Value.Moves;
    }

    /// <summary>Alte Züge und Räume aus dem Speicher räumen (höchstens alle 10 min).</summary>
    private void Sweep()
    {
        var now = DateTime.UtcNow;
        if (now - _lastSweep < TimeSpan.FromMinutes(10)) return;
        _lastSweep = now;
        foreach (var (k, v) in _moves) if (now - v.At > MovesKeep) _moves.TryRemove(k, out _);
        foreach (var (k, v) in _rooms) if (now - v.At > MovesKeep) _rooms.TryRemove(k, out _);
    }
}

/// <summary>
/// Die Quelle der Live-Bretter: Kennungen mit <c>cc~</c> gehen an chess.com (<see cref="ChessComLiveSource"/>), alles andere an
/// Lichess. Suche, Top-Liste und Cloud-Eval gibt es nur bei Lichess.
/// </summary>
public sealed class LiveSourceRouter(ILichessLiveSource lichess, ChessComLiveSource chessCom) : ILichessLiveSource
{
    public ChessComLiveSource ChessCom => chessCom;

    public Task<string?> GetRoundPgnAsync(string roundId, CancellationToken ct) =>
        ChessComEvents.IsChessCom(roundId) ? chessCom.GetRoundPgnAsync(roundId, ct) : lichess.GetRoundPgnAsync(roundId, ct);

    public IAsyncEnumerable<string> StreamRoundAsync(string roundId, CancellationToken ct) =>
        ChessComEvents.IsChessCom(roundId) ? chessCom.StreamRoundAsync(roundId, ct) : lichess.StreamRoundAsync(roundId, ct);

    public Task<string?> GetTourAsync(string tourId, CancellationToken ct) =>
        ChessComEvents.IsChessCom(tourId) ? chessCom.GetTourAsync(tourId, ct) : lichess.GetTourAsync(tourId, ct);

    public Task<string?> GetTourOfRoundAsync(string roundId, CancellationToken ct) =>
        ChessComEvents.IsChessCom(roundId) ? chessCom.GetTourOfRoundAsync(roundId, ct) : lichess.GetTourOfRoundAsync(roundId, ct);

    public Task<string?> GetTopAsync(CancellationToken ct) => lichess.GetTopAsync(ct);
    public Task<string?> SearchAsync(string query, CancellationToken ct) => lichess.SearchAsync(query, ct);
    public Task<string?> CloudEvalAsync(string fen, CancellationToken ct) => lichess.CloudEvalAsync(fen, ct);
}
