using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public class AutoSubscriptionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutoSubscriptionService> _logger;

    public AutoSubscriptionService(IServiceScopeFactory scopeFactory, ILogger<AutoSubscriptionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AutoSubscriptionService started");

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
            tournamentsJson = await proxy.GetAsync(queryString);
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
            if (!string.IsNullOrWhiteSpace(endDateStr) &&
                DateTime.TryParseExact(endDateStr, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate))
            {
                if (endDate.Date < today) continue;
            }

            // Skip if already subscribed
            if (existingSet.Contains(tournamentId)) continue;

            // Create subscription
            db.TournamentSubscriptions.Add(new TournamentSubscription
            {
                UserId = userId,
                CrawlerTournamentId = tournamentId,
                TournamentName = tournamentName
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
            playersJson = await proxy.GetAsync($"/api/tournaments/{Uri.EscapeDataString(crawlerTournamentId)}/players");
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
            var snr = p.TryGetProperty("snr", out var s) ? s.GetInt32() : 0;
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
                // Name match (fallback)
                else if (name.Contains(profile.LastName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(profile.FirstName))
                        matched = name.Contains(profile.FirstName, StringComparison.OrdinalIgnoreCase);
                    else
                        matched = true;
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
}
