using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Wer darf welchen Turnierverlauf sehen.
///
/// <para>Die Daten selbst sind auf chess-results oeffentlich — die VERKNUEPFUNG von Konto und
/// Spielerkennung ist es nicht. `PublicProfileDto` gibt die ChessResultsId bewusst nicht heraus,
/// und dabei bleibt es: ein fremder Verlauf nur zwischen ANGENOMMENEN Freunden, dieselbe Regel
/// wie bei /api/friends/{userId}/stats.</para>
/// </summary>
public class TournamentHistoryControllerTests : IDisposable
{
    private readonly AppDbContext _db;

    public TournamentHistoryControllerTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private TournamentHistoryController Controller(int userId, string history = "[]")
    {
        var service = new TournamentHistoryService(_db, new ClientFactory(new StubHandler(history)),
            new NoOpTaskQueue(), new TestLogger<TournamentHistoryService>());
        var controller = new TournamentHistoryController(service, new FriendService(_db, new NotificationService(_db)), _db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test")),
                },
            },
        };
        return controller;
    }

    private async Task<int> CreateUserAsync(string username, string? lastName = "Spieler")
    {
        var user = new AppUser { Username = username, PasswordHash = "x", Email = $"{username}@e.at" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        _db.UserProfiles.Add(new UserProfile
        {
            UserId = user.Id, LastName = lastName, FirstName = "Test", FideId = $"999{user.Id}",
        });
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private async Task BefriendAsync(int a, int b)
    {
        _db.Friendships.Add(new Friendship
        {
            RequesterId = a, AddresseeId = b, Status = FriendshipStatus.Accepted,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Get_WithoutUserIds_ReturnsOwnHistory()
    {
        var me = await CreateUserAsync("ich");

        var result = await Controller(me).Get();
        var list = Assert.IsType<List<PlayerHistoryDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(me, Assert.Single(list).UserId);
    }

    [Fact]
    public async Task Get_FriendsHistory_IsAllowed()
    {
        var me = await CreateUserAsync("ich");
        var friend = await CreateUserAsync("freund");
        await BefriendAsync(me, friend);

        var result = await Controller(me).Get($"{me},{friend}");
        var list = Assert.IsType<List<PlayerHistoryDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(2, list.Count);
    }

    /// <summary>
    /// Ein fremdes Konto ohne Freundschaft: 403, und zwar fuer die GANZE Anfrage. Ein einzelnes
    /// Konto still zu ueberspringen waere eine Luecke ohne Erklaerung.
    /// </summary>
    [Fact]
    public async Task Get_StrangersHistory_IsForbidden()
    {
        var me = await CreateUserAsync("ich");
        var stranger = await CreateUserAsync("fremd");

        var result = await Controller(me).Get($"{me},{stranger}");

        Assert.IsType<ForbidResult>(result.Result);
    }

    /// <summary>Eine offene, nicht angenommene Anfrage genuegt nicht.</summary>
    [Fact]
    public async Task Get_PendingFriendship_IsForbidden()
    {
        var me = await CreateUserAsync("ich");
        var other = await CreateUserAsync("angefragt");
        _db.Friendships.Add(new Friendship
        {
            RequesterId = me, AddresseeId = other, Status = FriendshipStatus.Pending,
        });
        await _db.SaveChangesAsync();

        Assert.IsType<ForbidResult>((await Controller(me).Get($"{other}")).Result);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1,x")]
    [InlineData("0")]
    [InlineData("-3")]
    public async Task Get_InvalidUserIds_AreRejected(string ids)
    {
        var me = await CreateUserAsync("ich");

        Assert.IsType<BadRequestObjectResult>((await Controller(me).Get(ids)).Result);
    }

    /// <summary>
    /// „Alle Freunde" ist der Zweck der Umschaltung, aber jedes Konto kostet im schlechtesten
    /// Fall einen Seitenabruf — deshalb ein Deckel.
    /// </summary>
    [Fact]
    public async Task Get_TooManyUsers_IsRejected()
    {
        var me = await CreateUserAsync("ich");
        var ids = string.Join(',', Enumerable.Range(1, 21));

        Assert.IsType<BadRequestObjectResult>((await Controller(me).Get(ids)).Result);
    }

    /// <summary>
    /// Die Auswahl bietet nur Freunde an, bei denen etwas zu sehen ist — ein Freund ohne Namen im
    /// Profil ist in der Liste nur eine Enttaeuschung.
    /// </summary>
    [Fact]
    public async Task Friends_OnlyThoseWithAUsableIdentity()
    {
        var me = await CreateUserAsync("ich");
        var withName = await CreateUserAsync("mitname");
        var withoutName = await CreateUserAsync("ohnename", lastName: null);
        await BefriendAsync(me, withName);
        await BefriendAsync(me, withoutName);

        var result = await Controller(me).Friends();
        var list = Assert.IsType<List<HistoryFriendDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(withName, Assert.Single(list).UserId);
    }

    /// <summary>
    /// Ohne Kennung im Profil sucht die Historie ueber den NAMEN und findet damit auch
    /// Namensgleiche. Das gehoert gesagt, nicht verschwiegen.
    /// </summary>
    [Fact]
    public async Task Friends_WithoutAnIdentifier_IsMarkedAsInexact()
    {
        var me = await CreateUserAsync("ich");
        var friend = await CreateUserAsync("freund");
        await BefriendAsync(me, friend);

        var profile = await _db.UserProfiles.SingleAsync(p => p.UserId == friend);
        profile.FideId = null;
        profile.ChessResultsId = null;
        await _db.SaveChangesAsync();

        var result = await Controller(me).Friends();
        var list = Assert.IsType<List<HistoryFriendDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.False(Assert.Single(list).Exact);
    }

    [Fact]
    public async Task Friends_WithoutFriends_IsEmpty()
    {
        var me = await CreateUserAsync("ich");

        var result = await Controller(me).Friends();

        Assert.Empty(Assert.IsType<List<HistoryFriendDto>>(
            Assert.IsType<OkObjectResult>(result.Result).Value));
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://crawler:8080") };
    }
}
