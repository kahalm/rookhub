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
    /// ALLE angenommenen Freunde stehen in der Liste — auch die ohne Namen im Profil, dann aber
    /// mit `hasName: false`. Vorher wurden sie ausgefiltert, und wer nur solche Freunde hat, bekam
    /// eine leere Auswahl ohne jeden Grund („ich habe Freunde, kann aber keine auswaehlen").
    /// Brauchbare zuerst, damit die Liste nicht mit Konten ohne Verlauf beginnt.
    /// </summary>
    [Fact]
    public async Task Friends_ListsEveryone_ButMarksThoseWithoutAName()
    {
        var me = await CreateUserAsync("ich");
        var withName = await CreateUserAsync("mitname");
        var withoutName = await CreateUserAsync("ohnename", lastName: null);
        await BefriendAsync(me, withName);
        await BefriendAsync(me, withoutName);

        var result = await Controller(me).Friends();
        var list = Assert.IsType<List<HistoryFriendDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(2, list.Count);
        Assert.Equal(withName, list[0].UserId);
        Assert.True(list[0].HasName);
        Assert.Equal(withoutName, list[1].UserId);
        Assert.False(list[1].HasName);
        // Ohne Namen gibt es auch keine Kennung, die etwas genauer machen koennte.
        Assert.False(list[1].Exact);
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

    // ----- Verfolgte Spieler ------------------------------------------------

    /// <summary>
    /// Verfolgt wird, was die Spielersuche zurueckgegeben hat — und der Schluessel ist derselbe
    /// wie beim Verlauf eines Kontos: die FIDE-Kennung hat Vorrang.
    /// </summary>
    [Fact]
    public async Task Track_StoresThePlayer_WithTheSameKeyAsAnAccount()
    {
        var me = await CreateUserAsync("ich");

        var result = await Controller(me).Track(new AddTrackedPlayerDto
        {
            LastName = "Oberschmid", FirstName = "Patrik", FideId = "1693034",
            ChessResultsId = "144749", DisplayName = "Oberschmid, Patrik",
        });

        var dto = Assert.IsType<TrackedPlayerDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Oberschmid, Patrik", dto.DisplayName);
        Assert.True(dto.Exact);

        var stored = await _db.TrackedPlayers.SingleAsync();
        Assert.Equal(me, stored.UserId);
        Assert.Equal("fide:1693034", stored.PlayerKey);
        Assert.Equal("Oberschmid", stored.LastName);
    }

    /// <summary>Ohne Anzeigenamen entsteht er aus dem Namen — der Reiter braucht eine Beschriftung.</summary>
    [Fact]
    public async Task Track_WithoutDisplayName_FallsBackToTheName()
    {
        var me = await CreateUserAsync("ich");

        await Controller(me).Track(new AddTrackedPlayerDto { LastName = "Kasparov", FirstName = "Garry" });

        Assert.Equal("Kasparov, Garry", (await _db.TrackedPlayers.SingleAsync()).DisplayName);
    }

    /// <summary>
    /// Ohne Kennung sucht der Verlauf ueber den NAMEN und zeigt Namensgleiche mit — das steht als
    /// <c>exact: false</c> in der Antwort, damit die Ansicht es sagen kann.
    /// </summary>
    [Fact]
    public async Task Track_WithoutIdentifiers_IsMarkedInexact()
    {
        var me = await CreateUserAsync("ich");

        var result = await Controller(me).Track(new AddTrackedPlayerDto { LastName = "Mueller", FirstName = "Hans" });

        var dto = Assert.IsType<TrackedPlayerDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.False(dto.Exact);
        Assert.Equal("name:mueller-hans", (await _db.TrackedPlayers.SingleAsync()).PlayerKey);
    }

    /// <summary>
    /// Denselben Spieler ein zweites Mal: kein Fehler, kein zweiter Reiter — der vorhandene
    /// Eintrag kommt zurueck. Ein 409 zwaenge die Oberflaeche, einen Fehler zu zeigen, wo nichts
    /// fehlt.
    /// </summary>
    [Fact]
    public async Task Track_SamePlayerTwice_ReturnsTheExistingEntry()
    {
        var me = await CreateUserAsync("ich");
        var dto = new AddTrackedPlayerDto { LastName = "Carlsen", FirstName = "Magnus", FideId = "1503014" };

        var first = Assert.IsType<TrackedPlayerDto>(
            Assert.IsType<OkObjectResult>((await Controller(me).Track(dto)).Result).Value);
        var second = Assert.IsType<TrackedPlayerDto>(
            Assert.IsType<OkObjectResult>((await Controller(me).Track(dto)).Result).Value);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await _db.TrackedPlayers.ToListAsync());
    }

    /// <summary>Ohne Nachnamen gibt es keine Spielersuche — also auch nichts zu verfolgen.</summary>
    [Fact]
    public async Task Track_WithoutLastName_IsRejected()
    {
        var me = await CreateUserAsync("ich");

        var result = await Controller(me).Track(new AddTrackedPlayerDto { LastName = "K", FirstName = "Magnus" });

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await _db.TrackedPlayers.ToListAsync());
    }

    /// <summary>
    /// Jeder verfolgte Spieler kostet den naechtlichen Durchgang mindestens einen Seitenabruf —
    /// ohne Deckel waere die Liste ein Weg, den Crawler mit einem Konto auszulasten.
    /// </summary>
    [Fact]
    public async Task Track_BeyondTheLimit_IsRejected()
    {
        var me = await CreateUserAsync("ich");
        for (var i = 0; i < 20; i++)
            _db.TrackedPlayers.Add(new TrackedPlayer
            {
                UserId = me, PlayerKey = $"fide:{i}", DisplayName = $"Spieler {i}", LastName = $"Spieler{i}",
            });
        await _db.SaveChangesAsync();

        var result = await Controller(me).Track(new AddTrackedPlayerDto { LastName = "Zuviel", FideId = "77" });

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(20, await _db.TrackedPlayers.CountAsync());
    }

    /// <summary>Die Liste gehoert dem Konto, das sie angelegt hat.</summary>
    [Fact]
    public async Task Tracked_ListsOnlyOwnPlayers()
    {
        var me = await CreateUserAsync("ich");
        var other = await CreateUserAsync("wer-anders");
        await Controller(me).Track(new AddTrackedPlayerDto { LastName = "Meiner", FideId = "1" });
        await Controller(other).Track(new AddTrackedPlayerDto { LastName = "Fremder", FideId = "2" });

        var list = Assert.IsType<List<TrackedPlayerDto>>(
            Assert.IsType<OkObjectResult>((await Controller(me).Tracked()).Result).Value);

        Assert.Equal("Meiner", Assert.Single(list).DisplayName);
    }

    /// <summary>
    /// Der Verlauf eines verfolgten Spielers laeuft ueber denselben Zwischenspeicher wie der
    /// eines Kontos — der haengt am SPIELER, nicht am Konto.
    /// </summary>
    [Fact]
    public async Task TrackedHistory_ReturnsTheStoredResultsOfThatPlayer()
    {
        var me = await CreateUserAsync("ich");
        var added = Assert.IsType<TrackedPlayerDto>(Assert.IsType<OkObjectResult>(
            (await Controller(me).Track(new AddTrackedPlayerDto
            {
                LastName = "Oberschmid", FirstName = "Patrik", FideId = "1693034",
            })).Result).Value);

        _db.PlayerTournamentResults.Add(new PlayerTournamentResult
        {
            PlayerKey = "fide:1693034", ChessResultsId = "1107064", Snr = 44,
            TournamentName = "Schach Tirol Open", EndDate = new DateOnly(2025, 8, 30), Rank = 56,
        });
        // Frisch geholt: sonst laeuft der Abruf gegen den Stub und die Zeile faellt heraus.
        _db.PlayerHistorySyncs.Add(new PlayerHistorySync
        {
            PlayerKey = "fide:1693034", LastFetchedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var result = await Controller(me).TrackedHistory(added.Id);
        var history = Assert.IsType<PlayerHistoryDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal("Oberschmid, Patrik", history.DisplayName);
        Assert.Equal("1107064", Assert.Single(history.Entries).ChessResultsId);
        // Kein Konto dahinter — die Ansicht fuehrt den Reiter ueber die Eintrags-Id.
        Assert.Equal(0, history.UserId);
    }

    /// <summary>Ein fremder Eintrag ist keiner: weder lesbar noch loeschbar.</summary>
    [Fact]
    public async Task TrackedHistory_OfSomeoneElsesEntry_IsNotFound()
    {
        var me = await CreateUserAsync("ich");
        var other = await CreateUserAsync("wer-anders");
        var theirs = Assert.IsType<TrackedPlayerDto>(Assert.IsType<OkObjectResult>(
            (await Controller(other).Track(new AddTrackedPlayerDto { LastName = "Fremder", FideId = "2" })).Result).Value);

        Assert.IsType<NotFoundResult>((await Controller(me).TrackedHistory(theirs.Id)).Result);
        Assert.IsType<NotFoundResult>(await Controller(me).Untrack(theirs.Id));
        Assert.Single(await _db.TrackedPlayers.ToListAsync());
    }

    /// <summary>
    /// Nicht mehr verfolgen entfernt den REITER. Der geholte Verlauf bleibt — er gehoert dem
    /// Spieler, und ein anderes Konto verfolgt ihn vielleicht weiter.
    /// </summary>
    [Fact]
    public async Task Untrack_RemovesTheEntry_ButKeepsTheFetchedResults()
    {
        var me = await CreateUserAsync("ich");
        var added = Assert.IsType<TrackedPlayerDto>(Assert.IsType<OkObjectResult>(
            (await Controller(me).Track(new AddTrackedPlayerDto { LastName = "Carlsen", FideId = "1503014" })).Result).Value);
        _db.PlayerTournamentResults.Add(new PlayerTournamentResult
        {
            PlayerKey = "fide:1503014", ChessResultsId = "1", Snr = 1, TournamentName = "Irgendwas",
        });
        await _db.SaveChangesAsync();

        Assert.IsType<NoContentResult>(await Controller(me).Untrack(added.Id));

        Assert.Empty(await _db.TrackedPlayers.ToListAsync());
        Assert.Single(await _db.PlayerTournamentResults.ToListAsync());
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
