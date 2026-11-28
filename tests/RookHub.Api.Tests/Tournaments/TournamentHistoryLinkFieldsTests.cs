using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Tournaments;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Tournaments;

namespace RookHub.Api.Tests.Tournaments;

/// <summary>
/// Die Felder, mit denen der Turnierverlauf nach draussen verlinkt und den Elo-Rechner vorfuellt:
/// FIDE-Kennung, Startnummer, nationale Wertung und Geburtsjahr. Eigene Klasse, weil
/// <c>TournamentHistoryServiceTests</c> schon ueber 1 000 Zeilen hat.
/// </summary>
public class TournamentHistoryLinkFieldsTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeCrawler _crawler = new();

    public void Dispose() => _db.Dispose();

    private TournamentHistoryService CreateService() =>
        new(_db, new ClientFactory(_crawler), new NoOpTaskQueue(), new TestLogger<TournamentHistoryService>());

    private async Task<int> CreateUserAsync(string? fideId)
    {
        var user = new AppUser { Username = "spieler", PasswordHash = "x", Email = "spieler@example.com" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        _db.UserProfiles.Add(new UserProfile
        {
            UserId = user.Id, LastName = "Oberschmid", FirstName = "Patrik",
            FideId = fideId, ChessResultsId = "144749",
        });
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private const string Rows = """
        [{"tournamentId":"1107064","tournamentName":"Schach Tirol Open 2025","endDate":"2025/08/30",
          "snr":44,"identNumber":"144749","fideId":"1693034","rank":56,"rounds":9,"playerCount":56}]
        """;

    [Fact]
    public async Task From_CarriesFideIdAndStartNumber()
    {
        var userId = await CreateUserAsync("1693034");
        _crawler.History = Rows;

        var history = (await CreateService().GetAsync([userId])).Single();
        var dto = PlayerHistoryDto.From(history);

        Assert.Equal("1693034", dto.FideId);
        Assert.Equal(44, dto.Entries.Single().StartNumber);
    }

    [Fact]
    public async Task From_WithoutFideIdInTheProfile_HasNone()
    {
        var userId = await CreateUserAsync(null);
        _crawler.History = Rows.Replace("\"fideId\":\"1693034\"", "\"fideId\":\"0\"");

        var dto = PlayerHistoryDto.From((await CreateService().GetAsync([userId])).Single());

        Assert.Null(dto.FideId);
    }

    /// <summary>Startnummer 0 heisst „unbekannt" — ein Link mit <c>snr=0</c> fuehrte ins Leere.</summary>
    [Fact]
    public void EntryFrom_WithoutStartNumber_HasNone()
    {
        var dto = PlayerHistoryEntryDto.From(
            new PlayerTournamentResult { ChessResultsId = "1", Snr = 0 }, TournamentSpeed.Unknown);

        Assert.Null(dto.StartNumber);
    }

    [Fact]
    public async Task FetchCardAsync_StoresNationalRatingAndBirthYear_AndTheDtoCarriesThem()
    {
        var userId = await CreateUserAsync("1693034");
        _crawler.History = Rows;
        _crawler.Card = """
            {"points":1.5,"rank":56,"performanceRating":1740,"ratingChange":-51.6,
             "ratingInternational":1923,"ratingNational":1958,"yearOfBirth":1982,"hasResult":true}
            """;
        await CreateService().GetAsync([userId]);

        await CreateService().FetchCardAsync("fide:1693034", "1107064", 44);

        var stored = await _db.PlayerTournamentResults.SingleAsync();
        Assert.Equal(1958, stored.RatingNational);
        Assert.Equal(1982, stored.BirthYear);
        Assert.Equal(TournamentHistoryService.CurrentCardVersion, stored.CardVersion);

        var dto = PlayerHistoryDto.From((await CreateService().GetAsync([userId])).Single());
        Assert.Equal(1958, dto.Entries.Single().RatingNational);
        Assert.Equal(1982, dto.BirthYear);
    }

    /// <summary>Ein Geburtsjahr wie 0 oder 9999 ist ein Lesefehler, kein Alter.</summary>
    [Fact]
    public async Task FetchCardAsync_ImplausibleBirthYear_IsNotStored()
    {
        var userId = await CreateUserAsync("1693034");
        _crawler.History = Rows;
        _crawler.Card = """{"points":1,"ratingInternational":1923,"yearOfBirth":0,"hasResult":true}""";
        await CreateService().GetAsync([userId]);

        await CreateService().FetchCardAsync("fide:1693034", "1107064", 44);

        Assert.Null((await _db.PlayerTournamentResults.SingleAsync()).BirthYear);
    }

    /// <summary>Karten der Fassung 2 kennen die nationale Wertung nicht — sie werden einmal neu geholt.</summary>
    [Fact]
    public void CardVersion_IsThree()
    {
        Assert.Equal(3, TournamentHistoryService.CurrentCardVersion);
    }

    private sealed class FakeCrawler : HttpMessageHandler
    {
        public string History { get; set; } = "[]";
        public string Card { get; set; } = "{}";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri?.ToString() ?? "";
            var body = url.Contains("player-card", StringComparison.Ordinal) ? Card
                : url.Contains("player-history", StringComparison.Ordinal) ? History
                : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://crawler:8080") };
    }
}
