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
        string? firstName = null, string? chessResultsId = null)
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
                ChessResultsId = chessResultsId
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
}
