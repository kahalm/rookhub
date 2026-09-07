using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Turnierverlauf. Zwei chess-results-Seiten tragen ihn, und sie kosten sehr
/// unterschiedlich viel: die Spielersuche liefert in EINEM Abruf alle Teilnahmen (mit Platz und
/// Startnummer), die Spielerkarte liefert Punkte und Performance — aber einen Abruf je Turnier.
/// An einem echten Konto gemessen: 23 Turniere, 12 davon gespielt.
/// </summary>
public class TournamentHistoryServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RoutingHandler _handler = new();

    public TournamentHistoryServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private TournamentHistoryService CreateService(IBackgroundTaskQueue? queue = null) =>
        new(_db, new ClientFactory(_handler), queue ?? new CountingQueue(),
            new TestLogger<TournamentHistoryService>());

    private async Task<int> CreateUserAsync(
        string username = "spieler", string? lastName = "Oberschmid", string? firstName = "Patrik",
        string? fideId = "1693034", string? chessResultsId = "144749")
    {
        var user = new AppUser { Username = username, PasswordHash = "x", Email = $"{username}@example.com" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();

        _db.UserProfiles.Add(new UserProfile
        {
            UserId = user.Id, LastName = lastName, FirstName = firstName,
            FideId = fideId, ChessResultsId = chessResultsId,
        });
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private const string TwoRows = """
        [{"tournamentId":"1107064","tournamentName":"Schach Tirol Open 2025","endDate":"2025/08/30",
          "snr":44,"identNumber":"144749","fideId":"1693034","rank":56,"rounds":9,"playerCount":56},
         {"tournamentId":"1479344","tournamentName":"TMM 1.Klasse 2026/2027","endDate":"2027/04/17",
          "snr":118,"identNumber":"144749","fideId":"1693034","rank":null,"rounds":11,"playerCount":226}]
        """;

    private const string Card = """
        {"points":1.5,"rank":56,"performanceRating":1740,"ratingChange":-51.6,
         "ratingInternational":1923,"hasResult":true}
        """;

    // ----- Die Liste --------------------------------------------------------

    [Fact]
    public async Task GetAsync_FirstCall_StoresTheWholeList()
    {
        var userId = await CreateUserAsync();
        _handler.History = TwoRows;

        var history = Assert.Single(await CreateService().GetAsync([userId]));

        Assert.Equal(TournamentHistoryService.HistoryStatus.Ok, history.Status);
        Assert.Equal(2, history.Results.Count);
        // Neueste zuerst — das letzte Turnier ist das, dessen Ergebnis man sucht.
        Assert.Equal("1479344", history.Results[0].ChessResultsId);
        Assert.Equal(44, history.Results.Single(r => r.ChessResultsId == "1107064").Snr);
    }

    /// <summary>
    /// Ein Turnier kommt Wochen vor dem Termin in die Liste und verschwindet nie. Bei jedem
    /// Seitenaufruf nachzufragen waere nichts als Last bei chess-results.
    /// </summary>
    [Fact]
    public async Task GetAsync_SecondCallWithinTheTtl_DoesNotFetchAgain()
    {
        var userId = await CreateUserAsync();
        _handler.History = TwoRows;

        await CreateService().GetAsync([userId]);
        await CreateService().GetAsync([userId]);

        Assert.Equal(1, _handler.HistoryCalls);
    }

    [Fact]
    public async Task GetAsync_StaleList_IsFetchedAgain()
    {
        var userId = await CreateUserAsync();
        _handler.History = TwoRows;
        await CreateService().GetAsync([userId]);

        var sync = await _db.PlayerHistorySyncs.SingleAsync();
        sync.LastFetchedAt = DateTime.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();

        await CreateService().GetAsync([userId]);

        Assert.Equal(2, _handler.HistoryCalls);
    }

    /// <summary>
    /// Ohne Nachnamen laesst sich die chess-results-Spielersuche nicht stellen — sie sucht ueber
    /// den NAMEN. Ein Grund ist besser als eine leere Tabelle: „trage deinen Namen ein" ist eine
    /// Handlungsanweisung, „keine Turniere" waere eine Falschaussage.
    /// </summary>
    [Fact]
    public async Task GetAsync_ProfileWithoutName_SaysWhy()
    {
        var userId = await CreateUserAsync(lastName: null);

        var history = Assert.Single(await CreateService().GetAsync([userId]));

        Assert.Equal(TournamentHistoryService.HistoryStatus.NoName, history.Status);
        Assert.Empty(history.Results);
        Assert.Equal(0, _handler.HistoryCalls);
    }

    /// <summary>
    /// Ist chess-results nicht erreichbar, gilt der Zwischenspeicher weiter — eine alte Liste ist
    /// besser als keine — und der Zeitstempel bleibt ALT, damit der naechste Aufruf es wieder
    /// versucht statt zwoelf Stunden zu warten.
    /// </summary>
    [Fact]
    public async Task GetAsync_SourceDown_KeepsTheCacheAndRetriesNextTime()
    {
        var userId = await CreateUserAsync();
        _handler.History = TwoRows;
        await CreateService().GetAsync([userId]);

        var sync = await _db.PlayerHistorySyncs.SingleAsync();
        sync.LastFetchedAt = DateTime.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();
        _handler.HistoryStatus = HttpStatusCode.InternalServerError;

        var history = Assert.Single(await CreateService().GetAsync([userId]));

        Assert.Equal(TournamentHistoryService.HistoryStatus.SourceUnavailable, history.Status);
        Assert.Equal(2, history.Results.Count);           // Zwischenspeicher steht

        // Und der naechste Aufruf versucht es erneut, statt den Fehlschlag als frisch zu werten.
        var before = _handler.HistoryCalls;
        await CreateService().GetAsync([userId]);
        Assert.Equal(before + 1, _handler.HistoryCalls);
    }

    // ----- Wer ist der Spieler? --------------------------------------------

    /// <summary>
    /// Die Suche geht ueber den NAMEN und liefert damit auch Namensgleiche. Die FIDE-ID
    /// entscheidet — und sie hat Vorrang, weil bei einem AUSLANDS-Turnier in der
    /// chess-results-Ident-Spalte „0" steht und dann allein sie die Identitaet traegt.
    /// </summary>
    [Fact]
    public async Task GetAsync_NamesakeInTheResults_IsFilteredOut()
    {
        var userId = await CreateUserAsync();
        _handler.History = """
            [{"tournamentId":"1","tournamentName":"Meines","endDate":"2025/08/30","snr":44,
              "identNumber":"144749","fideId":"1693034","rank":10,"rounds":7,"playerCount":50},
             {"tournamentId":"2","tournamentName":"Fremdes","endDate":"2025/08/30","snr":7,
              "identNumber":"999999","fideId":"9999999","rank":3,"rounds":7,"playerCount":50}]
            """;

        var history = Assert.Single(await CreateService().GetAsync([userId]));

        Assert.Equal("Meines", Assert.Single(history.Results).TournamentName);
    }

    /// <summary>
    /// Bei einem Auslandsturnier ist die Ident-Nummer „0" — die Zeile gehoert trotzdem dazu, weil
    /// die FIDE-ID passt. Ohne diese Regel fehlten genau die Turniere im Ausland.
    /// </summary>
    [Fact]
    public async Task GetAsync_ForeignTournamentWithoutIdentNumber_IsKept()
    {
        var userId = await CreateUserAsync();
        _handler.History = """
            [{"tournamentId":"3","tournamentName":"Tegernsee","endDate":"2025/04/15","snr":30,
              "identNumber":"0","fideId":"1693034","rank":30,"rounds":7,"playerCount":165}]
            """;

        var history = Assert.Single(await CreateService().GetAsync([userId]));

        Assert.Equal("Tegernsee", Assert.Single(history.Results).TournamentName);
    }

    [Theory]
    [InlineData("1693034", "144749", "fide:1693034")]
    [InlineData(null, "144749", "cr:144749")]
    [InlineData("1693034", null, "fide:1693034")]
    public void IdentityOf_PrefersTheFideId(string? fide, string? ident, string expectedKey)
    {
        var identity = TournamentHistoryService.IdentityOf("Oberschmid", "Patrik", fide, ident);

        Assert.Equal(expectedKey, identity!.Key);
    }

    /// <summary>„0" ist bei chess-results „keine Nummer" und darf nicht als Kennung gelten.</summary>
    [Fact]
    public void IdentityOf_ZeroIdentNumber_IsNotAnIdentity()
    {
        var identity = TournamentHistoryService.IdentityOf("Oberschmid", "Patrik", null, "0");

        Assert.StartsWith("name:", identity!.Key);
        Assert.Null(identity.IdentNumber);
    }

    // ----- Die Spielerkarten ------------------------------------------------

    /// <summary>
    /// Ein KUENFTIGES Turnier bekommt keinen Kartenabruf: es hat noch kein Ergebnis, und der Platz
    /// in der Trefferliste steht auf „-". Bei dem gemessenen Konto spart das elf von 23 Abrufen.
    /// </summary>
    [Fact]
    public async Task GetAsync_OnlyPlayedTournaments_AreQueuedForTheirCard()
    {
        var userId = await CreateUserAsync();
        _handler.History = TwoRows;
        var queue = new CountingQueue();

        var history = Assert.Single(await CreateService(queue).GetAsync([userId]));

        Assert.Equal(1, history.PendingResults);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public async Task FetchCardAsync_StoresPointsAndPerformance()
    {
        var userId = await CreateUserAsync();
        _handler.History = TwoRows;
        _handler.Card = Card;
        await CreateService().GetAsync([userId]);

        await CreateService().FetchCardAsync("fide:1693034", "1107064", 44);

        var result = await _db.PlayerTournamentResults
            .SingleAsync(r => r.ChessResultsId == "1107064");
        Assert.Equal(1.5m, result.Points);
        Assert.Equal(1740, result.PerformanceRating);
        Assert.Equal(-51.6m, result.RatingChange);
        Assert.Equal(1923, result.RatingInternational);
        Assert.NotNull(result.CardFetchedAt);
    }

    /// <summary>Ein abgeschlossenes Turnier aendert sich nie wieder — die Karte kommt einmal.</summary>
    [Fact]
    public async Task GetAsync_AfterTheCardArrived_QueuesNothingMore()
    {
        var userId = await CreateUserAsync();
        _handler.History = TwoRows;
        _handler.Card = Card;
        await CreateService().GetAsync([userId]);
        await CreateService().FetchCardAsync("fide:1693034", "1107064", 44);

        var queue = new CountingQueue();
        var history = Assert.Single(await CreateService(queue).GetAsync([userId]));

        Assert.Equal(0, history.PendingResults);
        Assert.Equal(0, queue.Count);
    }

    /// <summary>
    /// Auch ein LEERES Kartenergebnis wird vermerkt: sonst wird dieselbe Seite bei jedem
    /// Seitenaufruf erneut geholt. Ein NETZfehler dagegen nicht — der ist keine Auskunft.
    /// </summary>
    [Fact]
    public async Task FetchCardAsync_EmptyCard_IsRememberedButFailureIsNot()
    {
        var userId = await CreateUserAsync();
        _handler.History = TwoRows;
        _handler.Card = """{"hasResult":false}""";
        await CreateService().GetAsync([userId]);

        await CreateService().FetchCardAsync("fide:1693034", "1107064", 44);
        Assert.NotNull((await _db.PlayerTournamentResults
            .SingleAsync(r => r.ChessResultsId == "1107064")).CardFetchedAt);

        _handler.CardStatus = HttpStatusCode.InternalServerError;
        await CreateService().FetchCardAsync("fide:1693034", "1479344", 118);
        Assert.Null((await _db.PlayerTournamentResults
            .SingleAsync(r => r.ChessResultsId == "1479344")).CardFetchedAt);
    }

    /// <summary>
    /// Ein zweiter Listen-Abruf darf die schon geholten ERGEBNISSE nicht leeren — die Liste kennt
    /// sie nicht, und sie zu ueberschreiben hiesse, die Kartenabrufe wegzuwerfen.
    /// </summary>
    [Fact]
    public async Task GetAsync_ListRefresh_KeepsTheFetchedResults()
    {
        var userId = await CreateUserAsync();
        _handler.History = TwoRows;
        _handler.Card = Card;
        await CreateService().GetAsync([userId]);
        await CreateService().FetchCardAsync("fide:1693034", "1107064", 44);

        var sync = await _db.PlayerHistorySyncs.SingleAsync();
        sync.LastFetchedAt = DateTime.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();
        await CreateService().GetAsync([userId]);

        Assert.Equal(1.5m, (await _db.PlayerTournamentResults
            .SingleAsync(r => r.ChessResultsId == "1107064")).Points);
    }

    // ----- Attrappen --------------------------------------------------------

    /// <summary>Antwortet je nach Pfad — Trefferliste oder Spielerkarte.</summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        public string History { get; set; } = "[]";
        public string Card { get; set; } = """{"hasResult":false}""";
        public HttpStatusCode HistoryStatus { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode CardStatus { get; set; } = HttpStatusCode.OK;
        public int HistoryCalls { get; private set; }
        public int CardCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri?.ToString() ?? "";
            var card = url.Contains("player-card", StringComparison.Ordinal);
            if (card) CardCalls++; else HistoryCalls++;

            return Task.FromResult(new HttpResponseMessage(card ? CardStatus : HistoryStatus)
            {
                Content = new StringContent(card ? Card : History, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://crawler:8080") };
    }

    /// <summary>Zaehlt die eingereihten Auftraege, ohne sie auszufuehren.</summary>
    private sealed class CountingQueue : IBackgroundTaskQueue
    {
        public int Count { get; private set; }

        public ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
        {
            Count++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
