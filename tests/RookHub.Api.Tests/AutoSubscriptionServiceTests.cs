using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class AutoSubscriptionServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public AutoSubscriptionServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> CreateUserAsync(string username = "testuser", string? lastName = null,
        string? firstName = null, string? chessResultsId = null, string? fideId = null)
    {
        var user = new AppUser
        {
            Username = username,
            Email = $"{username}@example.com",
            PasswordHash = "hash",
            Profile = new UserProfile
            {
                LastName = lastName,
                FirstName = firstName,
                ChessResultsId = chessResultsId,
                FideId = fideId
            }
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private static CrawlerProxyService CreateMockProxy(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new MockHttpMessageHandler(responseJson, statusCode);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8080")
        };
        return new CrawlerProxyService(httpClient);
    }

    [Fact]
    public async Task CheckUserAsync_FindsUpcomingTournament_CreatesSubscription()
    {
        var userId = await CreateUserAsync(lastName: "Oberschmid", firstName: "Patrik", chessResultsId: "144749");

        var tournaments = JsonSerializer.Serialize(new[]
        {
            new { tournamentId = "1202326", tournamentName = "Salzkammergut Open", endDate = "25.12.2099" }
        });
        var proxy = CreateMockProxy(tournaments);

        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.CheckUserAsync(_db, proxy, userId, CancellationToken.None);

        var subs = await _db.TournamentSubscriptions.Where(s => s.UserId == userId).ToListAsync();
        Assert.Single(subs);
        Assert.Equal("1202326", subs[0].CrawlerTournamentId);
        Assert.Equal("Salzkammergut Open", subs[0].TournamentName);
    }

    [Fact]
    public async Task CheckUserAsync_TournamentAlreadySubscribed_SkipsDuplicate()
    {
        var userId = await CreateUserAsync(lastName: "Oberschmid", firstName: "Patrik", chessResultsId: "144749");

        // Pre-existing subscription
        _db.TournamentSubscriptions.Add(new TournamentSubscription
        {
            UserId = userId,
            CrawlerTournamentId = "1202326",
            TournamentName = "Salzkammergut Open"
        });
        await _db.SaveChangesAsync();

        var tournaments = JsonSerializer.Serialize(new[]
        {
            new { tournamentId = "1202326", tournamentName = "Salzkammergut Open", endDate = "25.12.2099" }
        });
        var proxy = CreateMockProxy(tournaments);

        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.CheckUserAsync(_db, proxy, userId, CancellationToken.None);

        var subs = await _db.TournamentSubscriptions.Where(s => s.UserId == userId).ToListAsync();
        Assert.Single(subs); // No duplicate created
    }

    [Fact]
    public async Task CheckUserAsync_PastTournament_Ignored()
    {
        var userId = await CreateUserAsync(lastName: "Oberschmid", firstName: "Patrik", chessResultsId: "144749");

        var tournaments = JsonSerializer.Serialize(new[]
        {
            new { tournamentId = "1202326", tournamentName = "Past Tournament", endDate = "01.01.2020" }
        });
        var proxy = CreateMockProxy(tournaments);

        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.CheckUserAsync(_db, proxy, userId, CancellationToken.None);

        var subs = await _db.TournamentSubscriptions.Where(s => s.UserId == userId).ToListAsync();
        Assert.Empty(subs);
    }

    [Fact]
    public async Task CheckUserAsync_NoLastName_DoesNothing()
    {
        var userId = await CreateUserAsync(lastName: null, chessResultsId: "144749");

        var proxy = CreateMockProxy("[]");

        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.CheckUserAsync(_db, proxy, userId, CancellationToken.None);

        var subs = await _db.TournamentSubscriptions.Where(s => s.UserId == userId).ToListAsync();
        Assert.Empty(subs);
    }

    [Fact]
    public async Task CheckUserAsync_MultipleUpcomingTournaments_CreatesAll()
    {
        var userId = await CreateUserAsync(lastName: "Oberschmid", firstName: "Patrik", chessResultsId: "144749");

        var tournaments = JsonSerializer.Serialize(new[]
        {
            new { tournamentId = "1202326", tournamentName = "Tournament A", endDate = "25.12.2099" },
            new { tournamentId = "1199999", tournamentName = "Tournament B", endDate = "30.12.2099" }
        });
        var proxy = CreateMockProxy(tournaments);

        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.CheckUserAsync(_db, proxy, userId, CancellationToken.None);

        var subs = await _db.TournamentSubscriptions.Where(s => s.UserId == userId).ToListAsync();
        Assert.Equal(2, subs.Count);
    }

    [Fact]
    public async Task CheckUserAsync_NoChessResultsId_DoesNothing()
    {
        var userId = await CreateUserAsync(lastName: "Oberschmid", chessResultsId: null);

        var proxy = CreateMockProxy("[]");

        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.CheckUserAsync(_db, proxy, userId, CancellationToken.None);

        var subs = await _db.TournamentSubscriptions.Where(s => s.UserId == userId).ToListAsync();
        Assert.Empty(subs);
    }

    [Fact]
    public async Task CheckUserAsync_SetsEventDateFromEndDate()
    {
        var userId = await CreateUserAsync(lastName: "Oberschmid", firstName: "Patrik", chessResultsId: "144749");

        var tournaments = JsonSerializer.Serialize(new[]
        {
            new { tournamentId = "1202326", tournamentName = "Open", endDate = "25.12.2099" }
        });
        var proxy = CreateMockProxy(tournaments);

        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.CheckUserAsync(_db, proxy, userId, CancellationToken.None);

        var sub = await _db.TournamentSubscriptions.SingleAsync(s => s.UserId == userId);
        Assert.Equal(new DateOnly(2099, 12, 25), sub.EventDate);
    }

    // --- Refresh aktiver Abos (Paarungen/Ergebnisse nachladen) ---

    private static CrawlerProxyService CreateProxy(RoutingHttpMessageHandler handler)
        => new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") });

    [Fact]
    public async Task Refresh_InWindowTournament_TriggersCrawl()
    {
        var userId = await CreateUserAsync(chessResultsId: "1");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        _db.TournamentSubscriptions.Add(new TournamentSubscription
        { UserId = userId, CrawlerTournamentId = "T1", TournamentName = "X", EventDate = today });
        await _db.SaveChangesAsync();

        var handler = new RoutingHttpMessageHandler().Map("/api/crawl", "{}");
        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.RefreshActiveSubscriptionsAsync(_db, CreateProxy(handler), CancellationToken.None);

        Assert.Equal(1, handler.Hits.GetValueOrDefault("/api/crawl"));
    }

    [Fact]
    public async Task Refresh_OutOfWindowTournament_NoCrawl()
    {
        var userId = await CreateUserAsync(chessResultsId: "1");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        _db.TournamentSubscriptions.Add(new TournamentSubscription
        { UserId = userId, CrawlerTournamentId = "T1", TournamentName = "X", EventDate = today.AddDays(-30) });
        await _db.SaveChangesAsync();

        var handler = new RoutingHttpMessageHandler().Map("/api/crawl", "{}");
        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.RefreshActiveSubscriptionsAsync(_db, CreateProxy(handler), CancellationToken.None);

        Assert.False(handler.Hits.ContainsKey("/api/crawl"));
    }

    [Fact]
    public async Task Refresh_NullEventDate_BackfillsFromCrawlerAndCrawls()
    {
        var userId = await CreateUserAsync(chessResultsId: "1");
        _db.TournamentSubscriptions.Add(new TournamentSubscription
        { UserId = userId, CrawlerTournamentId = "T1", TournamentName = "X", EventDate = null });
        await _db.SaveChangesAsync();

        var todayStr = DateTime.UtcNow.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        var handler = new RoutingHttpMessageHandler()
            .Map("/api/crawl", "{}")
            .Map("/api/tournaments/T1", $$"""{"date":"{{todayStr}}","location":"Y"}""");
        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.RefreshActiveSubscriptionsAsync(_db, CreateProxy(handler), CancellationToken.None);

        var sub = await _db.TournamentSubscriptions.SingleAsync();
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow.Date), sub.EventDate);
        Assert.Equal(1, handler.Hits.GetValueOrDefault("/api/crawl"));
    }

    [Fact]
    public async Task Refresh_SameTournamentMultipleUsers_CrawlsOnce()
    {
        var u1 = await CreateUserAsync(username: "a", chessResultsId: "1");
        var u2 = await CreateUserAsync(username: "b", chessResultsId: "2");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        _db.TournamentSubscriptions.AddRange(
            new TournamentSubscription { UserId = u1, CrawlerTournamentId = "T1", TournamentName = "X", EventDate = today },
            new TournamentSubscription { UserId = u2, CrawlerTournamentId = "T1", TournamentName = "X", EventDate = today });
        await _db.SaveChangesAsync();

        var handler = new RoutingHttpMessageHandler().Map("/api/crawl", "{}");
        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.RefreshActiveSubscriptionsAsync(_db, CreateProxy(handler), CancellationToken.None);

        Assert.Equal(1, handler.Hits.GetValueOrDefault("/api/crawl"));
    }

    // --- AutoFavorite Tests ---

    private static string PlayersJson(params (int snr, string name, string? fideId)[] players)
    {
        var list = players.Select(p => new { snr = p.snr, name = p.name, fideId = p.fideId });
        return JsonSerializer.Serialize(list);
    }

    [Fact]
    public async Task AutoFavorite_MatchesByFideId_CreatesFavorite()
    {
        var userId = await CreateUserAsync(lastName: "Mustermann", firstName: "Max", fideId: "1234567");

        _db.TournamentSubscriptions.Add(new TournamentSubscription
        { UserId = userId, CrawlerTournamentId = "T1", TournamentName = "Test" });
        await _db.SaveChangesAsync();

        var proxy = CreateMockProxy(PlayersJson((1, "Mustermann, Max", "1234567"), (2, "Other, Player", "9999999")));
        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.AutoFavoritePlayersAsync(_db, proxy, userId, "T1", CancellationToken.None);

        var favs = await _db.TournamentFavorites.Where(f => f.UserId == userId).ToListAsync();
        Assert.Single(favs);
        Assert.Equal(1, favs[0].PlayerSnr);
    }

    [Fact]
    public async Task AutoFavorite_MatchesByName_CreatesFavorite()
    {
        var userId = await CreateUserAsync(lastName: "Schmidt", firstName: "Anna");

        var proxy = CreateMockProxy(PlayersJson((5, "Schmidt, Anna", null), (6, "Mueller, Hans", null)));
        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.AutoFavoritePlayersAsync(_db, proxy, userId, "T2", CancellationToken.None);

        var favs = await _db.TournamentFavorites.Where(f => f.UserId == userId).ToListAsync();
        Assert.Single(favs);
        Assert.Equal(5, favs[0].PlayerSnr);
    }

    [Fact]
    public async Task AutoFavorite_MatchesFriend_CreatesFavorite()
    {
        var userId = await CreateUserAsync(username: "user1", lastName: "Huber", firstName: "Karl");
        var friendId = await CreateUserAsync(username: "friend1", lastName: "Berger", firstName: "Lisa", fideId: "7777777");

        _db.Friendships.Add(new Friendship
        {
            RequesterId = userId,
            AddresseeId = friendId,
            Status = FriendshipStatus.Accepted
        });
        await _db.SaveChangesAsync();

        var proxy = CreateMockProxy(PlayersJson(
            (10, "Berger, Lisa", "7777777"),
            (11, "Huber, Karl", null),
            (12, "Unknown, Person", null)));
        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.AutoFavoritePlayersAsync(_db, proxy, userId, "T3", CancellationToken.None);

        var favs = await _db.TournamentFavorites.Where(f => f.UserId == userId).OrderBy(f => f.PlayerSnr).ToListAsync();
        Assert.Equal(2, favs.Count);
        Assert.Equal(10, favs[0].PlayerSnr); // Friend matched by FIDE-ID
        Assert.Equal(11, favs[1].PlayerSnr); // User matched by name
    }

    [Fact]
    public async Task AutoFavorite_AlreadyFavorited_SkipsDuplicate()
    {
        var userId = await CreateUserAsync(lastName: "Huber", firstName: "Karl", fideId: "5555555");

        _db.TournamentFavorites.Add(new TournamentFavorite
        { UserId = userId, CrawlerTournamentId = "T4", PlayerSnr = 3 });
        await _db.SaveChangesAsync();

        var proxy = CreateMockProxy(PlayersJson((3, "Huber, Karl", "5555555")));
        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.AutoFavoritePlayersAsync(_db, proxy, userId, "T4", CancellationToken.None);

        var favs = await _db.TournamentFavorites.Where(f => f.UserId == userId && f.CrawlerTournamentId == "T4").ToListAsync();
        Assert.Single(favs); // No duplicate
    }

    [Fact]
    public async Task AutoFavorite_NoPlayers_DoesNothing()
    {
        var userId = await CreateUserAsync(lastName: "Huber", firstName: "Karl");

        var proxy = CreateMockProxy("[]");
        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.AutoFavoritePlayersAsync(_db, proxy, userId, "T5", CancellationToken.None);

        var favs = await _db.TournamentFavorites.Where(f => f.UserId == userId).ToListAsync();
        Assert.Empty(favs);
    }

    [Fact]
    public async Task AutoFavorite_NoMatchingProfile_DoesNothing()
    {
        var userId = await CreateUserAsync(lastName: "Huber", firstName: "Karl");

        var proxy = CreateMockProxy(PlayersJson((1, "Completely, Different", null), (2, "Another, Person", null)));
        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.AutoFavoritePlayersAsync(_db, proxy, userId, "T6", CancellationToken.None);

        var favs = await _db.TournamentFavorites.Where(f => f.UserId == userId).ToListAsync();
        Assert.Empty(favs);
    }

    [Fact]
    public async Task AutoFavorite_SubstringLastName_NoFalseMatch()
    {
        // Regression: "Ott" darf nicht auf "Ottenweller" matchen (vorher Substring).
        var userId = await CreateUserAsync(lastName: "Ott", firstName: "Hans");

        var proxy = CreateMockProxy(PlayersJson((1, "Ottenweller, Hans-Peter", null)));
        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.AutoFavoritePlayersAsync(_db, proxy, userId, "TF1", CancellationToken.None);

        var favs = await _db.TournamentFavorites.Where(f => f.UserId == userId).ToListAsync();
        Assert.Empty(favs);
    }

    [Fact]
    public async Task AutoFavorite_ExactLastFirst_StillMatches()
    {
        var userId = await CreateUserAsync(lastName: "Ott", firstName: "Hans");

        // Substring-Treffer (1) darf NICHT matchen, exakter (2) schon.
        var proxy = CreateMockProxy(PlayersJson((1, "Ottenweller, Hans", null), (2, "Ott, Hans", null)));
        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.AutoFavoritePlayersAsync(_db, proxy, userId, "TF2", CancellationToken.None);

        var favs = await _db.TournamentFavorites.Where(f => f.UserId == userId).ToListAsync();
        Assert.Single(favs);
        Assert.Equal(2, favs[0].PlayerSnr);
    }

    [Fact]
    public async Task AutoFavorite_LastNameOnly_TooShort_NoMatch()
    {
        // Ohne Vorname + kurzer Nachname -> kein (mehrdeutiger) Auto-Match.
        var userId = await CreateUserAsync(lastName: "Li", firstName: null);

        var proxy = CreateMockProxy(PlayersJson((1, "Li, Wei", null)));
        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.AutoFavoritePlayersAsync(_db, proxy, userId, "TF3", CancellationToken.None);

        var favs = await _db.TournamentFavorites.Where(f => f.UserId == userId).ToListAsync();
        Assert.Empty(favs);
    }

    [Fact]
    public async Task CheckUserAsync_ProxyError_DoesNotThrow()
    {
        var userId = await CreateUserAsync(lastName: "Test", firstName: "User", chessResultsId: "12345");
        var proxy = CreateMockProxy("", HttpStatusCode.InternalServerError);

        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        // Should not throw — errors are caught and logged
        await service.CheckUserAsync(_db, proxy, userId, CancellationToken.None);

        var subs = await _db.TournamentSubscriptions.Where(s => s.UserId == userId).ToListAsync();
        Assert.Empty(subs);
    }

    [Fact]
    public async Task CheckUserAsync_EmptyArray_DoesNothing()
    {
        var userId = await CreateUserAsync(lastName: "Test", firstName: "User", chessResultsId: "12345");
        var proxy = CreateMockProxy("[]");

        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.CheckUserAsync(_db, proxy, userId, CancellationToken.None);

        var subs = await _db.TournamentSubscriptions.Where(s => s.UserId == userId).ToListAsync();
        Assert.Empty(subs);
    }

    [Fact]
    public async Task CheckUserAsync_NonArrayResponse_DoesNothing()
    {
        var userId = await CreateUserAsync(lastName: "Test", firstName: "User", chessResultsId: "12345");
        var proxy = CreateMockProxy("""{"error":"not found"}""");

        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance);
        await service.CheckUserAsync(_db, proxy, userId, CancellationToken.None);

        var subs = await _db.TournamentSubscriptions.Where(s => s.UserId == userId).ToListAsync();
        Assert.Empty(subs);
    }

    // --- Retry-Verhalten (transiente Crawler-Verbindungsfehler) ---

    [Fact]
    public async Task CheckUserAsync_TransientConnectionError_RetriesThenSucceeds()
    {
        var userId = await CreateUserAsync(lastName: "Oberschmid", firstName: "Patrik", chessResultsId: "144749");

        var tournaments = JsonSerializer.Serialize(new[]
        {
            new { tournamentId = "1202326", tournamentName = "Salzkammergut Open", endDate = "25.12.2099" }
        });
        // Erste 2 Versuche des Tournament-Fetch werfen "Connection refused", der 3. liefert die Turniere.
        var handler = new FlakyHttpMessageHandler(failuresBeforeSuccess: 2, successBody: tournaments,
            failPathContains: "/api/players/tournaments");
        var proxy = new CrawlerProxyService(new HttpClient(handler) { BaseAddress = new Uri("http://gluetun:8080") });

        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance)
        {
            CrawlerRetryBackoff = TimeSpan.Zero // Test nicht künstlich verlangsamen
        };
        await service.CheckUserAsync(_db, proxy, userId, CancellationToken.None);

        Assert.Equal(3, handler.FailPathAttempts); // 2 Fehlversuche + 1 Erfolg auf dem Tournament-Pfad
        var subs = await _db.TournamentSubscriptions.Where(s => s.UserId == userId).ToListAsync();
        Assert.Single(subs);
        Assert.Equal("1202326", subs[0].CrawlerTournamentId);
    }

    [Fact]
    public async Task CheckUserAsync_TransientErrorPersists_GivesUpAfterMaxAttempts()
    {
        var userId = await CreateUserAsync(lastName: "Oberschmid", firstName: "Patrik", chessResultsId: "144749");

        // Tournament-Fetch schlägt immer fehl → Retry erschöpft sich, keine Subscription, kein Throw.
        var handler = new FlakyHttpMessageHandler(failuresBeforeSuccess: int.MaxValue, successBody: "[]",
            failPathContains: "/api/players/tournaments");
        var proxy = new CrawlerProxyService(new HttpClient(handler) { BaseAddress = new Uri("http://gluetun:8080") });

        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance)
        {
            CrawlerRetryBackoff = TimeSpan.Zero,
            MaxCrawlerAttempts = 3
        };
        await service.CheckUserAsync(_db, proxy, userId, CancellationToken.None);

        Assert.Equal(3, handler.FailPathAttempts); // genau MaxCrawlerAttempts, dann Aufgabe
        var subs = await _db.TournamentSubscriptions.Where(s => s.UserId == userId).ToListAsync();
        Assert.Empty(subs);
    }

    [Fact]
    public async Task CheckUserAsync_HttpErrorStatus_NotRetried()
    {
        var userId = await CreateUserAsync(lastName: "Test", firstName: "User", chessResultsId: "12345");

        // 500 ist eine HTTP-Antwort (CrawlerRequestException), kein transienter Verbindungsfehler → kein Retry.
        var handler = new FlakyHttpMessageHandler(failuresBeforeSuccess: 0, successBody: "",
            successStatus: HttpStatusCode.InternalServerError, failPathContains: "/api/players/tournaments");
        var proxy = new CrawlerProxyService(new HttpClient(handler) { BaseAddress = new Uri("http://gluetun:8080") });

        var service = new AutoSubscriptionService(null!, NullLogger<AutoSubscriptionService>.Instance)
        {
            CrawlerRetryBackoff = TimeSpan.Zero
        };
        await service.CheckUserAsync(_db, proxy, userId, CancellationToken.None);

        Assert.Equal(1, handler.FailPathAttempts); // nur ein Versuch, kein Retry bei 5xx-Antwort
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _response;
        private readonly HttpStatusCode _statusCode;

        public MockHttpMessageHandler(string response, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _response = response;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>
    /// Simuliert transiente Crawler-Verbindungsfehler auf einem bestimmten Pfad. Auf dem Ziel-Pfad
    /// (<c>failPathContains</c>, null = jeder Pfad) werfen die ersten <c>failuresBeforeSuccess</c>
    /// Aufrufe eine <see cref="HttpRequestException"/> ("Connection refused"), danach kommt
    /// <c>successBody</c>/<c>successStatus</c>. Alle anderen Pfade antworten mit "[]" (200), damit
    /// Folgeaufrufe (Crawl-Start, Players-Fetch) den Test nicht stoeren. <see cref="FailPathAttempts"/>
    /// zaehlt nur die Versuche auf dem Ziel-Pfad, <see cref="Attempts"/> alle.
    /// </summary>
    private class FlakyHttpMessageHandler : HttpMessageHandler
    {
        private readonly int _failuresBeforeSuccess;
        private readonly string _successBody;
        private readonly HttpStatusCode _successStatus;
        private readonly string? _failPathContains;

        public int Attempts { get; private set; }
        public int FailPathAttempts { get; private set; }

        public FlakyHttpMessageHandler(int failuresBeforeSuccess, string successBody,
            HttpStatusCode successStatus = HttpStatusCode.OK, string? failPathContains = null)
        {
            _failuresBeforeSuccess = failuresBeforeSuccess;
            _successBody = successBody;
            _successStatus = successStatus;
            _failPathContains = failPathContains;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Attempts++;
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            var isTarget = _failPathContains == null || path.Contains(_failPathContains);

            if (!isTarget)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                });

            FailPathAttempts++;
            if (FailPathAttempts <= _failuresBeforeSuccess)
                throw new HttpRequestException("Connection refused (gluetun:8080)");

            return Task.FromResult(new HttpResponseMessage(_successStatus)
            {
                Content = new StringContent(_successBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
