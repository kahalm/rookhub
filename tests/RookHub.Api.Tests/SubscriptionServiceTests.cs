using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class SubscriptionServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public SubscriptionServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> CreateUserAsync(string username = "testuser")
    {
        var user = new AppUser
        {
            Username = username,
            Email = $"{username}@example.com",
            PasswordHash = "hash",
            Profile = new UserProfile()
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Standard: der Crawler kennt kein Turnier (404) — das Abo bleibt unter der mitgebrachten Kennung.</summary>
    private SubscriptionController CreateController(int userId, RoutingHttpMessageHandler? crawler = null)
    {
        crawler ??= new RoutingHttpMessageHandler().Map("/api/tournaments/", "{}", System.Net.HttpStatusCode.NotFound);
        var proxy = new CrawlerProxyService(new HttpClient(crawler) { BaseAddress = new Uri("http://crawler") });
        var controller = new SubscriptionController(_db, proxy);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString())
                }, "test"))
            }
        };
        return controller;
    }

    [Fact]
    public async Task GetAll_ReturnsUserSubscriptions()
    {
        var userId = await CreateUserAsync();
        _db.TournamentSubscriptions.Add(new TournamentSubscription
        {
            UserId = userId, CrawlerTournamentId = "100", TournamentName = "T1"
        });
        _db.TournamentSubscriptions.Add(new TournamentSubscription
        {
            UserId = userId, CrawlerTournamentId = "200", TournamentName = "T2"
        });
        await _db.SaveChangesAsync();

        var controller = CreateController(userId);
        var result = await controller.GetAll();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var subs = Assert.IsType<List<TournamentSubscriptionDto>>(okResult.Value);
        Assert.Equal(2, subs.Count);
    }

    [Fact]
    public async Task GetAll_ReturnsEmpty_WhenNoSubscriptions()
    {
        var userId = await CreateUserAsync();
        var controller = CreateController(userId);

        var result = await controller.GetAll();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var subs = Assert.IsType<List<TournamentSubscriptionDto>>(okResult.Value);
        Assert.Empty(subs);
    }

    [Fact]
    public async Task Create_AddsSubscription()
    {
        var userId = await CreateUserAsync();
        var controller = CreateController(userId);

        var result = await controller.Create(new CreateSubscriptionDto
        {
            CrawlerTournamentId = "100", TournamentName = "Test Tournament"
        });

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<TournamentSubscriptionDto>(okResult.Value);
        Assert.Equal("100", dto.CrawlerTournamentId);
        Assert.Equal("Test Tournament", dto.TournamentName);
        Assert.Single(await _db.TournamentSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Create_Duplicate_ReturnsConflict()
    {
        var userId = await CreateUserAsync();
        _db.TournamentSubscriptions.Add(new TournamentSubscription
        {
            UserId = userId, CrawlerTournamentId = "100", TournamentName = "T1"
        });
        await _db.SaveChangesAsync();

        var controller = CreateController(userId);
        var result = await controller.Create(new CreateSubscriptionDto
        {
            CrawlerTournamentId = "100", TournamentName = "T1"
        });

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task Delete_RemovesSubscription()
    {
        var userId = await CreateUserAsync();
        var sub = new TournamentSubscription
        {
            UserId = userId, CrawlerTournamentId = "100", TournamentName = "T1"
        };
        _db.TournamentSubscriptions.Add(sub);
        await _db.SaveChangesAsync();

        var controller = CreateController(userId);
        var result = await controller.Delete(sub.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await _db.TournamentSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task Delete_NotFound_WhenWrongUser()
    {
        var userId1 = await CreateUserAsync("user1");
        var userId2 = await CreateUserAsync("user2");
        var sub = new TournamentSubscription
        {
            UserId = userId1, CrawlerTournamentId = "100", TournamentName = "T1"
        };
        _db.TournamentSubscriptions.Add(sub);
        await _db.SaveChangesAsync();

        var controller = CreateController(userId2);
        var result = await controller.Delete(sub.Id);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Delete_NotFound_WhenInvalidId()
    {
        var userId = await CreateUserAsync();
        var controller = CreateController(userId);

        var result = await controller.Delete(99999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    /// <summary>
    /// Abo loesen ueber die TURNIER-Nummer. Gebraucht von der Kurzansicht auf Karte, Liste und
    /// Kalender: die kennt das Turnier, nicht das Abo — ueber die Abo-Id zu gehen hiesse, erst
    /// die ganze Abo-Liste zu holen, um eine Id zu suchen, die der Server ohnehin kennt.
    /// </summary>
    [Fact]
    public async Task DeleteByTournament_RemovesOnlyTheOwnSubscription()
    {
        var mine = await CreateUserAsync("ich");
        var other = await CreateUserAsync("jemand");
        _db.TournamentSubscriptions.AddRange(
            new TournamentSubscription { UserId = mine, CrawlerTournamentId = "1457129", TournamentName = "Braunau" },
            new TournamentSubscription { UserId = mine, CrawlerTournamentId = "1405166", TournamentName = "Liga" },
            new TournamentSubscription { UserId = other, CrawlerTournamentId = "1457129", TournamentName = "Braunau" });
        await _db.SaveChangesAsync();

        var result = await CreateController(mine).DeleteByTournament("1457129");

        Assert.IsType<NoContentResult>(result);
        // Das eigene ist weg, das des anderen Nutzers und das andere eigene stehen.
        Assert.Equal(["1405166"], await _db.TournamentSubscriptions
            .Where(s => s.UserId == mine).Select(s => s.CrawlerTournamentId).ToListAsync());
        Assert.Single(_db.TournamentSubscriptions.Where(s => s.UserId == other));
    }

    /// <summary>
    /// IDEMPOTENT: der Knopf ist ein Umschalter, und „war schon nicht gemerkt" ist kein Fehler,
    /// den ein Nutzer sehen muesste (zwei Klicks, ein langsames Netz — schon passiert).
    /// </summary>
    [Fact]
    public async Task DeleteByTournament_WithoutSubscription_IsNoContent()
    {
        var userId = await CreateUserAsync();

        Assert.IsType<NoContentResult>(await CreateController(userId).DeleteByTournament("999"));
    }

    // --- A5-001: eine Kennung je Turnier ---

    private static RoutingHttpMessageHandler CrawlerKnows57As1234567() => new RoutingHttpMessageHandler()
        .Map("/api/tournaments/57", """{"id":57,"chessResultsId":"1234567"}""")
        .Map("/api/tournaments/1234567", """{"id":57,"chessResultsId":"1234567"}""");

    /// <summary>
    /// Die Turnierseite schickt ihren Routenwert — die Crawler-DB-Id. Das Abo steht trotzdem unter
    /// der chess-results-Nummer: nur die findet Kalender, Verzeichnis-Meldungen und Refresh-Crawl.
    /// </summary>
    [Fact]
    public async Task Create_WithCrawlerDbId_StoresTheChessResultsNumber()
    {
        var userId = await CreateUserAsync();

        var result = await CreateController(userId, CrawlerKnows57As1234567())
            .Create(new CreateSubscriptionDto { CrawlerTournamentId = "57", TournamentName = "Open" });

        var dto = Assert.IsType<TournamentSubscriptionDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("1234567", dto.CrawlerTournamentId);
        Assert.Equal("1234567", (await _db.TournamentSubscriptions.SingleAsync()).CrawlerTournamentId);
    }

    /// <summary>
    /// Ein Alt-Abo unter der DB-Id und ein Klick auf „Merken" im Kalender (mit der Nummer): vorher ein
    /// ZWEITES Abo, jetzt wird das vorhandene auf die Nummer umgeschluesselt.
    /// </summary>
    [Fact]
    public async Task Create_CalendarNumber_WithLegacyDbIdSubscription_RekeysInsteadOfDuplicating()
    {
        var userId = await CreateUserAsync();
        _db.TournamentSubscriptions.Add(new TournamentSubscription { UserId = userId, CrawlerTournamentId = "57", TournamentName = "Open" });
        await _db.SaveChangesAsync();

        var result = await CreateController(userId, CrawlerKnows57As1234567())
            .Create(new CreateSubscriptionDto { CrawlerTournamentId = "1234567", TournamentName = "Open" });

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(["1234567"], await _db.TournamentSubscriptions.Select(s => s.CrawlerTournamentId).ToListAsync());
    }

    /// <summary>Unter der Nummer schon gemerkt, die Seite schickt die DB-Id → 409 wie bisher, kein zweites Abo.</summary>
    [Fact]
    public async Task Create_DbId_WhenAlreadySubscribedUnderTheNumber_ReturnsConflict()
    {
        var userId = await CreateUserAsync();
        _db.TournamentSubscriptions.Add(new TournamentSubscription { UserId = userId, CrawlerTournamentId = "1234567", TournamentName = "Open" });
        await _db.SaveChangesAsync();

        var result = await CreateController(userId, CrawlerKnows57As1234567())
            .Create(new CreateSubscriptionDto { CrawlerTournamentId = "57", TournamentName = "Open" });

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Single(await _db.TournamentSubscriptions.ToListAsync());
    }

    /// <summary>Crawler nicht erreichbar: Merken scheitert nicht daran, das Abo steht unter der mitgebrachten Kennung.</summary>
    [Fact]
    public async Task Create_CrawlerDown_KeepsTheGivenId()
    {
        var userId = await CreateUserAsync();
        var down = new RoutingHttpMessageHandler().Map("/api/tournaments/", "boom", System.Net.HttpStatusCode.BadGateway);

        var result = await CreateController(userId, down)
            .Create(new CreateSubscriptionDto { CrawlerTournamentId = "57", TournamentName = "Open" });

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("57", (await _db.TournamentSubscriptions.SingleAsync()).CrawlerTournamentId);
    }
}
