using System.Net;

namespace RookHub.Api.Services.League;

/// <summary>
/// Gemeinsame Pause je Gegenseite nach einem 429 (2026-11-17). Lichess verlangt nach einem 429 eine volle Minute Ruhe für
/// ALLE Anfragen; vorher fing jeder Schritt des <see cref="LeagueOnlineSyncScheduler"/> die Drossel für sich, die übrigen
/// Schritte fragten sofort weiter, und die nächste Runde begann nach einer Minute wieder mit einer Anfrage (Prod-Log
/// 17.11.: 144 Warnungen in drei Tagen). Das Tor sitzt als <see cref="LeagueRateGateHandler"/> am benannten Client
/// <see cref="LeagueOnlineSync.ClientName"/> (und an <see cref="LichessStudySource.ClientName"/>) und deckt damit alle
/// LeagueHub-Aufrufe an Lichess und chess.com ab — Lichess und chess.com getrennt.
/// Dauer: <see cref="BasePause"/> (60 s + Reserve), bei aufeinanderfolgenden 429 verdoppelt bis <see cref="MaxPause"/>,
/// mindestens ein <c>Retry-After</c> der Antwort (+ Reserve, höchstens <see cref="MaxRetryAfter"/>); eine Antwort ohne
/// 429 setzt die Folge zurück. Eine Warnung je BEGINN einer Pause, das Ende als Information.
/// </summary>
public sealed class LeagueRateGate
{
    public const string Lichess = "Lichess";
    public const string ChessCom = "chess.com";

    public static readonly TimeSpan BasePause = TimeSpan.FromSeconds(75);
    public static readonly TimeSpan MaxPause = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan RetryAfterReserve = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(1);

    private sealed class State
    {
        public DateTime Until;
        public int Strikes;
    }

    private readonly ILogger<LeagueRateGate> _logger;
    private readonly Func<DateTime> _now;
    private readonly Dictionary<string, State> _sites = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public LeagueRateGate(ILogger<LeagueRateGate> logger, Func<DateTime>? now = null)
    {
        _logger = logger;
        _now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>Wie lange die Gegenseite noch pausiert (<see cref="TimeSpan.Zero"/> = frei).</summary>
    public TimeSpan Remaining(string site)
    {
        lock (_lock)
        {
            if (!_sites.TryGetValue(site, out var s)) return TimeSpan.Zero;
            var left = s.Until - _now();
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public bool IsPaused(string site) => Remaining(site) > TimeSpan.Zero;

    /// <summary>Die längste laufende Pause über alle Gegenseiten.</summary>
    public TimeSpan LongestRemaining()
    {
        string[] sites;
        lock (_lock) sites = _sites.Keys.ToArray();
        return sites.Select(Remaining).DefaultIfEmpty(TimeSpan.Zero).Max();
    }

    /// <summary>Pausiert die Gegenseite, gibt es die <see cref="LeagueOnlineSync.RateLimitedException"/> ohne Netzaufruf.</summary>
    public void ThrowIfPaused(string site)
    {
        var left = Remaining(site);
        if (left > TimeSpan.Zero) throw new LeagueOnlineSync.RateLimitedException(site, left);
    }

    /// <summary>Ein 429 kam an → Pause beginnen (oder eine laufende verlängern, ohne erneut zu zählen). → die Restdauer.</summary>
    public TimeSpan RateLimited(string site, TimeSpan? retryAfter)
    {
        TimeSpan pause;
        int strikes;
        lock (_lock)
        {
            if (!_sites.TryGetValue(site, out var s)) _sites[site] = s = new State();
            var now = _now();
            var running = s.Until > now;
            if (!running) s.Strikes++;
            var grown = TimeSpan.FromTicks(Math.Min(MaxPause.Ticks, BasePause.Ticks * (1L << Math.Min(s.Strikes - 1, 16))));
            if (retryAfter is { } ra && ra > TimeSpan.Zero)
            {
                var wanted = (ra < MaxRetryAfter ? ra : MaxRetryAfter) + RetryAfterReserve;
                if (wanted > grown) grown = wanted;
            }
            var until = now + grown;
            if (until > s.Until) s.Until = until;
            pause = s.Until - now;
            strikes = s.Strikes;
            if (running) return pause;      // eine Anfrage, die schon unterwegs war — keine zweite Warnung
        }
        _logger.LogWarning("LeagueHub: {Site} drosselt (429) — alle Abrufe dorthin pausiert für {Seconds} s ({Strikes}. Drossel in Folge)",
            site, (int)Math.Ceiling(pause.TotalSeconds), strikes);
        return pause;
    }

    /// <summary>Eine Antwort ohne 429 → die Folge ist vorbei.</summary>
    public void Succeeded(string site)
    {
        lock (_lock)
        {
            if (!_sites.TryGetValue(site, out var s) || s.Strikes == 0) return;
            s.Strikes = 0;
        }
        _logger.LogInformation("LeagueHub: {Site} antwortet wieder — Pause beendet", site);
    }

    /// <summary>Die Gegenseite einer Adresse — <c>null</c> = nicht vom Tor erfasst.</summary>
    public static string? SiteOf(Uri? uri, string? lichessHost = null)
    {
        if (uri is null || !uri.IsAbsoluteUri) return null;
        var host = uri.Host.ToLowerInvariant();
        if (host == "lichess.org" || host.EndsWith(".lichess.org", StringComparison.Ordinal)
            || !string.IsNullOrEmpty(lichessHost) && host == lichessHost.ToLowerInvariant()) return Lichess;
        if (host == "chess.com" || host.EndsWith(".chess.com", StringComparison.Ordinal)) return ChessCom;
        return null;
    }

    /// <summary><c>Retry-After</c> als Dauer (Sekunden oder Zeitpunkt).</summary>
    public TimeSpan? RetryAfterOf(HttpResponseMessage r)
    {
        var ra = r.Headers.RetryAfter;
        if (ra?.Delta is { } d) return d;
        if (ra?.Date is { } at) return at.UtcDateTime - _now();
        return null;
    }
}

/// <summary>
/// Das Tor am HttpClient: pausierte Gegenseite → <see cref="LeagueOnlineSync.RateLimitedException"/> ohne Netzaufruf;
/// 429 → Pause beginnen und dieselbe Ausnahme werfen; jede andere Antwort beendet eine Drossel-Folge.
/// </summary>
public sealed class LeagueRateGateHandler(LeagueRateGate gate, string? lichessHost = null) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var site = LeagueRateGate.SiteOf(request.RequestUri, lichessHost);
        if (site is null) return await base.SendAsync(request, ct);
        gate.ThrowIfPaused(site);
        var r = await base.SendAsync(request, ct);
        if (r.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var left = gate.RateLimited(site, gate.RetryAfterOf(r));
            r.Dispose();
            throw new LeagueOnlineSync.RateLimitedException(site, left);
        }
        gate.Succeeded(site);
        return r;
    }
}
