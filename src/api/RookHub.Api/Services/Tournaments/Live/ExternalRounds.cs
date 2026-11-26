using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace RookHub.Api.Services.Tournaments.Live;

/// <summary>Eine fremde Lichess-Runde, wie Lichess sie meldet (für die Überschrift „Turnier · Runde“ der zusätzlichen Bretter).</summary>
public sealed record ExternalRoundInfo(string RoundId, string RoundName, string TourId, string TourName, string Url, bool Ongoing, bool Finished);

/// <summary>Ein zusätzliches Brett aus einer fremden Übertragung: <c>roundId.gameId</c> in der Adresse (<c>?ext=</c>).</summary>
public sealed record ExternalBoardRef(string RoundId, string GameId)
{
    public override string ToString() => $"{RoundId}.{GameId}";
}

/// <summary>Zeile der Partieauswahl im Dialog „Brett hinzufügen“.</summary>
public sealed record ExternalGameRow(string Id, string White, string Black, string? WhiteTitle, string? BlackTitle,
    int? WhiteElo, int? BlackElo, string Result);

/// <summary>Antwort von <c>GET /api/live-boards/external?url=</c>.</summary>
public sealed record ExternalRoundView(string RoundId, string RoundName, string TourName, string Url, bool Ongoing, bool Finished,
    string? GameId, IReadOnlyList<ExternalGameRow> Games);

/// <summary>
/// Zusätzliche Bretter aus FREMDEN Lichess-Übertragungen (1.025.0, Wunsch 2026-11-26: „Schwaz-Mannschaftskampf ansehen und
/// daneben die WM-Partie live“). Der Strom einer fremden Runde läuft über denselben <see cref="BroadcastLiveHub"/> (ein
/// Lichess-Strom je Runde für alle Zuschauer, Leerlauf-Schließen wie gehabt) — hier nur der Missbrauchsschutz:
/// <list type="bullet">
/// <item>nur Runden, die Lichess wirklich kennt (einmal <c>/api/broadcast/-/-/{roundId}</c>, Treffer 5 min, Fehltreffer
///   10 min gecacht — eine erfundene Kennung kostet höchstens einen Abruf je 10 min);</item>
/// <item>höchstens <see cref="MaxRounds"/> fremde Runden gleichzeitig serverweit (<c>LiveBoards:MaxExternalRounds</c>, Vorgabe
///   10), je Verbindung <see cref="MaxRoundsPerStream"/> Runden und <see cref="MaxBoardsPerStream"/> Bretter.</item>
/// </list>
/// Keine Speicherung in der Datenbank: die Auswahl steht nur in der Adresse der Seite.
/// </summary>
public sealed partial class ExternalRoundRegistry
{
    public const int MaxRoundsPerStream = 4;
    public const int MaxBoardsPerStream = 12;
    private static readonly TimeSpan HitTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MissTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan GamesTtl = TimeSpan.FromSeconds(60);

    private readonly ILichessLiveSource _source;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ExternalRoundRegistry> _logger;
    private readonly Dictionary<string, int> _watched = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public ExternalRoundRegistry(ILichessLiveSource source, IMemoryCache cache, ILogger<ExternalRoundRegistry> logger, int maxRounds = 10)
    {
        _source = source;
        _cache = cache;
        _logger = logger;
        MaxRounds = maxRounds;
    }

    public int MaxRounds { get; }

    /// <summary>Fremde Runden, die gerade jemand ansieht (Tests, Diagnose).</summary>
    public int WatchedRounds { get { lock (_lock) return _watched.Count; } }

    /// <summary>
    /// <c>?ext=roundId.gameId,…</c> → gültige, verschiedene Bretter in Reihenfolge; Ungültiges fällt still weg, gedeckelt auf
    /// <see cref="MaxBoardsPerStream"/> Bretter aus höchstens <see cref="MaxRoundsPerStream"/> Runden.
    /// </summary>
    public static List<ExternalBoardRef> ParseRefs(string? csv)
    {
        var list = new List<ExternalBoardRef>();
        var rounds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = RefRe().Match(raw);
            if (!m.Success) continue;
            var r = new ExternalBoardRef(m.Groups[1].Value, m.Groups[2].Value);
            if (list.Contains(r)) continue;
            if (!rounds.Contains(r.RoundId) && rounds.Count >= MaxRoundsPerStream) continue;
            rounds.Add(r.RoundId);
            list.Add(r);
            if (list.Count >= MaxBoardsPerStream) break;
        }
        return list;
    }

    [GeneratedRegex("^([A-Za-z0-9]{8})\\.([A-Za-z0-9]{8})$")]
    private static partial Regex RefRe();

    /// <summary>Kennt Lichess diese Runde? <c>null</c> = nein (oder Lichess gerade nicht erreichbar — dann ungecacht).</summary>
    public async Task<ExternalRoundInfo?> ResolveAsync(string roundId, CancellationToken ct)
    {
        if (!LichessBroadcasts.IsId(roundId)) return null;
        var key = "live-ext-round:" + roundId;
        if (_cache.TryGetValue(key, out ExternalRoundInfo? hit)) return hit;
        ExternalRoundInfo? info = null;
        try
        {
            var json = await _source.GetTourOfRoundAsync(roundId, ct);
            if (json is not null && LichessBroadcasts.ParseTourDetail(json) is { } d && d.Rounds.FirstOrDefault(r => r.Id == roundId) is { } round)
                info = Info(d, round);
        }
        catch (Exception e) when (e is LichessRateLimitedException or HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogInformation("Live-Bretter: fremde Runde {Round} nicht gelesen ({Error})", roundId, e.Message);
            return null;
        }
        _cache.Set(key, info, info is null ? MissTtl : HitTtl);
        return info;
    }

    private static ExternalRoundInfo Info(LichessBroadcasts.TourDetail d, LichessBroadcasts.Round r) =>
        new(r.Id, r.Name, d.Tour.Id, d.Tour.Name, $"https://lichess.org/broadcast/-/-/{r.Id}", r.Ongoing, r.Finished);

    /// <summary>
    /// Link aus dem Dialog → Runde + ihre Partien. Turnier-Link → die laufende Runde (sonst die zuletzt begonnene/fertige).
    /// Reason: <c>invalidUrl</c>, <c>notFound</c>, <c>gameNotFound</c>.
    /// </summary>
    public async Task<(ExternalRoundView? View, string? Reason)> LookupAsync(string? url, BroadcastLiveHub hub, CancellationToken ct)
    {
        if (LichessBroadcasts.BoardLinkOf(url) is not { } ids) return (null, "invalidUrl");
        ExternalRoundInfo? info;
        if (ids.TourId is { } tourId)
        {
            var json = await _source.GetTourAsync(tourId, ct);
            if (json is null || LichessBroadcasts.ParseTourDetail(json) is not { Rounds.Count: > 0 } d) return (null, "notFound");
            var r = d.Rounds.LastOrDefault(x => x.Ongoing)
                    ?? d.Rounds.LastOrDefault(x => x.Finished || x.StartsAt is { } s && s <= DateTime.UtcNow)
                    ?? d.Rounds[0];
            info = Info(d, r);
            _cache.Set("live-ext-round:" + r.Id, info, HitTtl);
        }
        else info = await ResolveAsync(ids.RoundId!, ct);
        if (info is null) return (null, "notFound");
        var games = await GamesAsync(info.RoundId, hub, ct);
        if (ids.GameId is { } g && games.Count > 0 && games.All(x => x.Id != g)) return (null, "gameNotFound");
        return (new ExternalRoundView(info.RoundId, info.RoundName, info.TourName, info.Url, info.Ongoing, info.Finished, ids.GameId,
            games.Select(x => new ExternalGameRow(x.Id, x.White.Name, x.Black.Name, x.White.Title, x.Black.Title, x.White.Elo, x.Black.Elo, x.Result))
                .ToList()), null);
    }

    /// <summary>Partien einer Runde für die Auswahl: was der Hub ohnehin hat, sonst einmal das Runden-PGN (60 s gecacht).</summary>
    private async Task<IReadOnlyList<LiveGame>> GamesAsync(string roundId, BroadcastLiveHub hub, CancellationToken ct)
    {
        if (hub.Peek(roundId) is { } open) return open;
        return (await RoundPgnsAsync(roundId, ct)).Select(x => x.Game).ToList();
    }

    /// <summary>Partien des Runden-PGN samt Rohtext — einmal abgerufen, 60 s gecacht (Auswahl und „Analysieren“ teilen den Abruf).</summary>
    private async Task<IReadOnlyList<(LiveGame Game, string Pgn)>> RoundPgnsAsync(string roundId, CancellationToken ct)
    {
        var key = "live-ext-games:" + roundId;
        if (_cache.TryGetValue(key, out IReadOnlyList<(LiveGame, string)>? cached) && cached is not null) return cached;
        string? pgn;
        try { pgn = await _source.GetRoundPgnAsync(roundId, ct); }
        catch (Exception e) when (e is LichessRateLimitedException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return [];
        }
        var splitter = new BroadcastPgnSplitter();
        var games = splitter.Feed(pgn ?? "").Concat(splitter.Flush())
            .Select(text => (Game: LiveGameParser.Parse(text), Pgn: text)).Where(x => x.Game is not null)
            .Select(x => (x.Game!, x.Pgn)).ToList();
        _cache.Set(key, (IReadOnlyList<(LiveGame, string)>)games, GamesTtl);
        return games;
    }

    /// <summary>
    /// PGN EINER Partie für „Analysieren“ (1.026.0): aus dem Hub (offener Strom), sonst — nur für Runden, die Lichess kennt — aus
    /// dem gecachten Runden-PGN. <c>null</c> = unbekannte Runde oder Partie.
    /// </summary>
    public async Task<string?> GamePgnAsync(string roundId, string gameId, BroadcastLiveHub hub, CancellationToken ct)
    {
        if (!LichessBroadcasts.IsId(roundId) || !LichessBroadcasts.IsId(gameId)) return null;
        if (hub.PeekPgn(roundId, gameId) is { } live) return live;
        if (await ResolveAsync(roundId, ct) is null) return null;
        return (await RoundPgnsAsync(roundId, ct)).FirstOrDefault(x => x.Game.Id == gameId).Pgn;
    }

    /// <summary>Platz für eine fremde Runde belegen (Freigabe per Dispose). Eine schon beobachtete Runde kostet keinen neuen
    /// Platz; <c>null</c> = serverweit schon <see cref="MaxRounds"/> verschiedene fremde Runden offen.</summary>
    public IDisposable? TryWatch(string roundId)
    {
        lock (_lock)
        {
            var n = _watched.GetValueOrDefault(roundId);
            if (n == 0 && _watched.Count >= MaxRounds) return null;
            _watched[roundId] = n + 1;
        }
        return new Lease(this, roundId);
    }

    private void Release(string roundId)
    {
        lock (_lock)
        {
            if (_watched.TryGetValue(roundId, out var n) && n > 1) _watched[roundId] = n - 1;
            else _watched.Remove(roundId);
        }
    }

    private sealed class Lease(ExternalRoundRegistry owner, string roundId) : IDisposable
    {
        private int _done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) owner.Release(roundId);
        }
    }
}
