using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public class AutoSubscriptionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutoSubscriptionService> _logger;

    /// <summary>
    /// Initiale Pause vor dem ersten Lauf. Beim Deploy startet die API regelmaessig ein paar
    /// Sekunden vor dem Crawler (gluetun:8080); ohne diese Pause liefe der erste Check sofort
    /// ins "Connection refused". Da der Service ohnehin nur alle 24 h laeuft, kostet die Pause nichts.
    /// (internal settable nur fuer Tests.)
    /// </summary>
    internal TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Anzahl Versuche fuer Crawler-GETs (transiente Verbindungsfehler).</summary>
    internal int MaxCrawlerAttempts { get; set; } = 3;

    /// <summary>Wartezeit zwischen den Crawler-Versuchen.</summary>
    internal TimeSpan CrawlerRetryBackoff { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Bis zu so viele Tage NACH dem Turniertermin wird noch ein Refresh-Crawl angestoßen
    /// (Ergebnisse werden bei chess-results.com teils erst Tage nach dem letzten Brett finalisiert).</summary>
    internal int RefreshDaysAfter { get; set; } = 10;

    /// <summary>Ab so viele Tage VOR dem Turniertermin wird schon refresht (frühe Paarungen abgreifen).</summary>
    internal int RefreshDaysBefore { get; set; } = 3;

    public AutoSubscriptionService(IServiceScopeFactory scopeFactory, ILogger<AutoSubscriptionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AutoSubscriptionService started");

        // Crawler beim Deploy erst hochfahren lassen, bevor der erste Check ihn anspricht.
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAllUsersAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected error in AutoSubscriptionService loop");
            }

            try
            {
                await RefreshActiveSubscriptionsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected error in AutoSubscriptionService refresh");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task CheckAllUsersAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proxy = scope.ServiceProvider.GetRequiredService<CrawlerProxyService>();

        var profiles = await db.UserProfiles
            .Where(p => p.ChessResultsId != null && p.LastName != null)
            .Select(p => new { p.UserId, p.LastName, p.FirstName })
            .ToListAsync(ct);

        _logger.LogInformation("AutoSubscriptionService checking {Count} users", profiles.Count);

        foreach (var profile in profiles)
        {
            try
            {
                await CheckUserAsync(db, proxy, profile.UserId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "AutoSubscription check failed for user {UserId}", profile.UserId);
            }
        }
    }

    private async Task RefreshActiveSubscriptionsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proxy = scope.ServiceProvider.GetRequiredService<CrawlerProxyService>();
        await RefreshActiveSubscriptionsAsync(db, proxy, ct);
    }

    /// <summary>
    /// Stößt für laufende bzw. frisch beendete abonnierte Turniere einen erneuten Full-Crawl an,
    /// damit Paarungen/Ergebnisse nachgeladen werden. Hintergrund: wird ein Turnier VOR Spielbeginn
    /// abonniert (chess-results.com hat dann noch keine Runden), holt der Erst-Crawl nur Spieler —
    /// die Paarungen blieben sonst für immer leer, weil nichts das Abo erneut crawlt. Turniere ohne
    /// bekanntes Datum (Altbestand) werden einmalig per Crawler-Detail nachgezogen und dabei datiert.
    /// Pro Turnier nur EIN Crawl, auch wenn es mehrere Abonnenten hat.
    /// </summary>
    public async Task RefreshActiveSubscriptionsAsync(AppDbContext db, CrawlerProxyService proxy, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var all = await db.TournamentSubscriptions
            .Select(s => new { s.CrawlerTournamentId, s.EventDate })
            .ToListAsync(ct);

        var tournaments = all
            .GroupBy(s => s.CrawlerTournamentId)
            .Select(g => (TournamentId: g.Key, EventDate: MaxDate(g.Select(s => s.EventDate))))
            .ToList();

        var refreshed = 0;
        foreach (var (tournamentId, knownDate) in tournaments)
        {
            ct.ThrowIfCancellationRequested();

            var eventDate = knownDate;
            // Datum unbekannt (Altbestand) → einmalig vom Crawler nachziehen + an allen Abos persistieren.
            if (eventDate == null)
                eventDate = await TryBackfillEventDateAsync(db, proxy, tournamentId, ct);

            // Außerhalb des Fensters überspringen; unbekanntes Datum bleibt refreshbar (bis es bekannt ist).
            if (eventDate != null &&
                (eventDate < today.AddDays(-RefreshDaysAfter) || eventDate > today.AddDays(RefreshDaysBefore)))
                continue;

            try
            {
                var crawlBody = JsonSerializer.Deserialize<JsonElement>(
                    JsonSerializer.Serialize(new { chessResultsId = tournamentId, jobType = "Full" }));
                await proxy.PostAsync("/api/crawl", crawlBody, ct);
                refreshed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Refresh-Crawl für Turnier {TournamentId} fehlgeschlagen", tournamentId);
            }
        }

        if (refreshed > 0)
            _logger.LogInformation("AutoSubscription: Refresh-Crawl für {Count} aktive Turniere angestoßen", refreshed);
    }

    /// <summary>
    /// Holt das Turnierdatum vom Crawler-Detail (Feld <c>date</c>, dd.MM.yyyy; bei Datumsbereichen das
    /// Ende-Datum) und schreibt es an allen noch undatierten Abos dieses Turniers fest. Gibt das Datum
    /// zurück oder <c>null</c>, wenn der Crawler keins liefert.
    /// </summary>
    private async Task<DateOnly?> TryBackfillEventDateAsync(AppDbContext db, CrawlerProxyService proxy,
        string tournamentId, CancellationToken ct)
    {
        JsonElement detail;
        try
        {
            detail = await GetWithRetryAsync(proxy, $"/api/tournaments/{Uri.EscapeDataString(tournamentId)}", ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Turnier-Detail-Fetch für {TournamentId} fehlgeschlagen", tournamentId);
            return null;
        }

        if (detail.ValueKind != JsonValueKind.Object) return null;
        var dateStr = detail.TryGetProperty("date", out var d) ? d.GetString() : null;
        if (string.IsNullOrWhiteSpace(dateStr)) return null;

        // Datumsbereich "dd.MM.yyyy-dd.MM.yyyy" → das Ende nehmen.
        var token = dateStr.Contains('-') ? dateStr.Split('-').Last().Trim() : dateStr.Trim();
        if (!DateTime.TryParseExact(token, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return null;

        var eventDate = DateOnly.FromDateTime(parsed);
        var rows = await db.TournamentSubscriptions
            .Where(s => s.CrawlerTournamentId == tournamentId && s.EventDate == null)
            .ToListAsync(ct);
        foreach (var r in rows) r.EventDate = eventDate;
        if (rows.Count > 0) await db.SaveChangesAsync(ct);
        return eventDate;
    }

    private static DateOnly? MaxDate(IEnumerable<DateOnly?> dates)
    {
        DateOnly? max = null;
        foreach (var d in dates)
            if (d is { } v && (max is null || v > max.Value))
                max = v;
        return max;
    }

    public async Task CheckUserAsync(AppDbContext db, CrawlerProxyService proxy, int userId, CancellationToken ct)
    {
        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null || string.IsNullOrWhiteSpace(profile.LastName) || string.IsNullOrWhiteSpace(profile.ChessResultsId))
            return;

        var lastName = profile.LastName;
        var firstName = profile.FirstName;

        // Query crawler for player tournaments
        var queryString = $"/api/players/tournaments?lastName={Uri.EscapeDataString(lastName)}";
        if (!string.IsNullOrWhiteSpace(firstName))
            queryString += $"&firstName={Uri.EscapeDataString(firstName)}";

        JsonElement tournamentsJson;
        try
        {
            tournamentsJson = await GetWithRetryAsync(proxy, queryString, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch player tournaments for user {UserId}", userId);
            return;
        }

        if (tournamentsJson.ValueKind != JsonValueKind.Array) return;

        var today = DateTime.UtcNow.Date;
        var newSubscriptions = 0;

        // Load existing subscriptions for this user
        var existingSubIds = await db.TournamentSubscriptions
            .Where(s => s.UserId == userId)
            .Select(s => s.CrawlerTournamentId)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existingSubIds);

        foreach (var tournament in tournamentsJson.EnumerateArray())
        {
            var tournamentId = tournament.TryGetProperty("tournamentId", out var tid) ? tid.GetString() : null;
            var tournamentName = tournament.TryGetProperty("tournamentName", out var tn) ? tn.GetString() : null;
            var endDateStr = tournament.TryGetProperty("endDate", out var ed) ? ed.GetString() : null;

            if (string.IsNullOrWhiteSpace(tournamentId) || string.IsNullOrWhiteSpace(tournamentName))
                continue;

            // Parse end date (dd.MM.yyyy) and only keep upcoming/current tournaments
            DateOnly? eventDate = null;
            if (!string.IsNullOrWhiteSpace(endDateStr) &&
                DateTime.TryParseExact(endDateStr, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate))
            {
                if (endDate.Date < today) continue;
                eventDate = DateOnly.FromDateTime(endDate);
            }

            // Skip if already subscribed
            if (existingSet.Contains(tournamentId)) continue;

            // Create subscription
            db.TournamentSubscriptions.Add(new TournamentSubscription
            {
                UserId = userId,
                CrawlerTournamentId = tournamentId,
                TournamentName = tournamentName,
                EventDate = eventDate
            });
            existingSet.Add(tournamentId);
            newSubscriptions++;

            // Start crawl job for the new tournament
            try
            {
                var crawlBody = JsonSerializer.Deserialize<JsonElement>(
                    JsonSerializer.Serialize(new
                    {
                        chessResultsId = tournamentId,
                        jobType = "Full"
                    }));
                await proxy.PostAsync("/api/crawl", crawlBody);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to start crawl for tournament {TournamentId}", tournamentId);
            }
        }

        if (newSubscriptions > 0)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("AutoSubscription: Created {Count} new subscriptions for user {UserId}", newSubscriptions, userId);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex, "Constraint violation saving subscriptions for user {UserId}, detaching added entities", userId);
                foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added))
                    entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            }
        }

        // Auto-favorite players for subscriptions that don't have favorites yet
        var subsWithoutFavorites = await db.TournamentSubscriptions
            .Where(s => s.UserId == userId)
            .Where(s => !db.TournamentFavorites.Any(f => f.UserId == userId
                && f.CrawlerTournamentId == s.CrawlerTournamentId))
            .Select(s => s.CrawlerTournamentId)
            .ToListAsync(ct);

        foreach (var tid in subsWithoutFavorites)
        {
            try
            {
                await AutoFavoritePlayersAsync(db, proxy, userId, tid, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AutoFavorite failed for user {UserId}, tournament {TournamentId}", userId, tid);
            }
        }
    }

    public async Task AutoFavoritePlayersAsync(AppDbContext db, CrawlerProxyService proxy,
        int userId, string crawlerTournamentId, CancellationToken ct)
    {
        // 1. Fetch players from crawler
        JsonElement playersJson;
        try
        {
            playersJson = await GetWithRetryAsync(proxy, $"/api/tournaments/{Uri.EscapeDataString(crawlerTournamentId)}/players", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch players for tournament {TournamentId}", crawlerTournamentId);
            return;
        }

        if (playersJson.ValueKind != JsonValueKind.Array) return;

        var players = new List<(int Snr, string Name, string? FideId)>();
        foreach (var p in playersJson.EnumerateArray())
        {
            // Defensiv: nur lesen, wenn 'snr' tatsächlich eine Zahl ist (Crawler liefert es sonst
            // ggf. als String/Null → s.GetInt32() würde werfen und die ganze Turnier-Verarbeitung killen).
            var snr = p.TryGetProperty("snr", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.Number
                && s.TryGetInt32(out var snrVal) ? snrVal : 0;
            var name = p.TryGetProperty("name", out var n) ? n.GetString() : null;
            var fideId = p.TryGetProperty("fideId", out var f) ? f.GetString() : null;
            if (snr > 0 && !string.IsNullOrWhiteSpace(name))
                players.Add((snr, name, fideId));
        }

        if (players.Count == 0) return;

        // 2. Load profiles: user + accepted friends
        var friendUserIds = await db.Friendships
            .Where(f => (f.RequesterId == userId || f.AddresseeId == userId)
                      && f.Status == FriendshipStatus.Accepted)
            .Select(f => f.RequesterId == userId ? f.AddresseeId : f.RequesterId)
            .ToListAsync(ct);

        var allUserIds = friendUserIds.Prepend(userId).Distinct().ToList();
        var profiles = await db.UserProfiles
            .Where(p => allUserIds.Contains(p.UserId))
            .ToListAsync(ct);

        if (profiles.Count == 0) return;

        // 3. Load existing favorites to avoid duplicates
        var existingFavSnrs = await db.TournamentFavorites
            .Where(f => f.UserId == userId && f.CrawlerTournamentId == crawlerTournamentId && f.PlayerSnr != null)
            .Select(f => f.PlayerSnr!.Value)
            .ToListAsync(ct);
        var existingSet = new HashSet<int>(existingFavSnrs);

        // 4. Match profiles against players
        var newFavorites = 0;
        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.LastName)) continue;

            foreach (var (snr, name, fideId) in players)
            {
                if (existingSet.Contains(snr)) continue;

                var matched = false;

                // FIDE-ID match (primary)
                if (!string.IsNullOrWhiteSpace(profile.FideId) && !string.IsNullOrWhiteSpace(fideId)
                    && string.Equals(profile.FideId, fideId, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                }
                // Name match (fallback): chess-results liefert "Nachname, Vorname".
                // Exakter Token-Vergleich statt Substring, sonst matcht z.B. "Ott"
                // auf "Ottenweller"/"Scott" und favorisiert falsche Spieler.
                else
                {
                    var comma = name.IndexOf(',');
                    var lastToken = (comma >= 0 ? name[..comma] : name).Trim();
                    var firstToken = comma >= 0 ? name[(comma + 1)..].Trim() : string.Empty;

                    if (string.Equals(lastToken, profile.LastName.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(profile.FirstName))
                            // Erstes Vornamens-Token vergleichen (CR listet teils mehrere Vornamen).
                            matched = string.Equals(FirstWord(firstToken), FirstWord(profile.FirstName),
                                StringComparison.OrdinalIgnoreCase);
                        else
                            // Ohne Vorname nur bei ausreichend eindeutigem (laengerem) Nachnamen.
                            matched = lastToken.Length >= 3;
                    }
                }

                if (matched)
                {
                    db.TournamentFavorites.Add(new TournamentFavorite
                    {
                        UserId = userId,
                        CrawlerTournamentId = crawlerTournamentId,
                        PlayerSnr = snr
                    });
                    existingSet.Add(snr);
                    newFavorites++;
                    break; // One match per profile is enough
                }
            }
        }

        if (newFavorites > 0)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                _logger.LogInformation("AutoFavorite: Created {Count} favorites for user {UserId} in tournament {TournamentId}",
                    newFavorites, userId, crawlerTournamentId);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex, "Constraint violation saving favorites for user {UserId}, tournament {TournamentId}", userId, crawlerTournamentId);
                foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added))
                    entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            }
        }
    }

    /// <summary>
    /// Crawler-GET mit kurzem Retry/Backoff. Wiederholt nur bei transienten Verbindungsfehlern
    /// (<see cref="HttpRequestException"/>, z. B. "Connection refused" waehrend der Crawler beim
    /// Deploy noch hochfaehrt). HTTP-Fehlerantworten (<see cref="CrawlerRequestException"/>) und
    /// alle uebrigen Fehler werden NICHT wiederholt, sondern direkt weitergereicht.
    /// </summary>
    private async Task<JsonElement> GetWithRetryAsync(CrawlerProxyService proxy, string path, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await proxy.GetAsync(path, ct);
            }
            catch (HttpRequestException ex) when (attempt < MaxCrawlerAttempts)
            {
                _logger.LogDebug(ex,
                    "Crawler GET {Path} attempt {Attempt}/{Max} failed (transient), retrying in {Backoff}s",
                    path, attempt, MaxCrawlerAttempts, CrawlerRetryBackoff.TotalSeconds);
                await Task.Delay(CrawlerRetryBackoff, ct);
            }
        }
    }

    private static string FirstWord(string s)
    {
        s = s.Trim();
        var sp = s.IndexOf(' ');
        return sp >= 0 ? s[..sp] : s;
    }
}
