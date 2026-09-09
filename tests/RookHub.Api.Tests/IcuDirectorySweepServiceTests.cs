using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Kalender der Irish Chess Union — 81 kuenftige Turniere, rund 94 % davon nicht auf
/// chess-results. Die Liste traegt fast alles (samt Koordinaten aus ihrem eigenen Kartenblock);
/// die Detailseite wird nur einmal je Turnier geholt, weil sie fast nichts hinzufuegt.
/// </summary>
public class IcuDirectorySweepServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public IcuDirectorySweepServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private RouteHandler _routes = new();

    private IcuDirectorySweepService CreateService(
        string listJson, string? detailJson = null, int batchSize = 40)
    {
        _routes = new RouteHandler(listJson, detailJson);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TournamentDirectory:IcuDetailBatchSize"] = batchSize.ToString(),
            ["TournamentDirectory:IcuDetailDelaySeconds"] = "0",
        }).Build();

        return new IcuDirectorySweepService(_db, new StubClientFactory(_routes),
            new GeocodingService(_db), config, new TestLogger<IcuDirectorySweepService>());
    }

    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    private static string Row(string name, string eventId = "2305",
        string? place = "Raharney Chess Club, Raharney, Co. Westmeath",
        double? lat = 53.5242079, double? lon = -7.0968796,
        string categories = "\"Rapid\",\"FIDE-rated\"", bool nonTournament = false,
        DateOnly? end = null) =>
        $$"""
          {"eventId":"{{eventId}}","name":"{{name}}",
           "startDate":"{{Soon:yyyy-MM-dd}}","endDate":"{{(end ?? Soon):yyyy-MM-dd}}",
           "url":"https://www.icu.ie/events/{{eventId}}","place":{{Json(place)}},
           "lat":{{Num(lat)}},"lon":{{Num(lon)}},
           "categories":[{{categories}}],"nonTournament":{{(nonTournament ? "true" : "false")}}}
          """;

    private static string Detail(string? chessResultsId = null, int? players = null,
        string? website = null) =>
        $$"""
          {"chessResultsId":{{Json(chessResultsId)}},
           "playerCount":{{(players is null ? "null" : players.ToString())}},
           "website":{{Json(website)}}}
          """;

    private static string Json(string? v) => v is null ? "null" : $"\"{v}\"";
    private static string Num(double? v) =>
        v is null ? "null" : v.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // ----- Anlegen -----------------------------------------------------------

    [Fact]
    public async Task RunAsync_UnknownTournament_IsAdded()
    {
        var result = await CreateService($"[{Row("3rd Raharney Rapid FIDE Open")}]").RunAsync();

        Assert.Equal(1, result.Added);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("ie2305", entry.PublicId);
        Assert.Null(entry.ChessResultsId);
        Assert.Equal("IRL", entry.Federation);
        Assert.Equal(Soon, entry.StartDate);
        Assert.Equal(TournamentSpeed.Rapid, entry.Speed);
    }

    /// <summary>
    /// Der Sonderwert dieser Quelle: die Koordinaten stehen im Kartenblock derselben Trefferseite
    /// (30 von 81 Turnieren). Es wird nichts aufgeloest und nichts geraten — das haelt
    /// <see cref="GeoSource.SourceProvided"/> fest.
    /// </summary>
    [Fact]
    public async Task RunAsync_TakesTheCoordinatesFromTheSource()
    {
        await CreateService($"[{Row("3rd Raharney Rapid FIDE Open")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(GeoSource.SourceProvided, entry.GeoSource);
        Assert.Equal(53.5242079, entry.Lat!.Value, 4);
        Assert.Equal(-7.0968796, entry.Lon!.Value, 4);
    }

    [Fact]
    public async Task RunAsync_LeavesAManualCoordinateAlone()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "ie2305", Name = "3rd Raharney Rapid FIDE Open", Federation = "IRL",
            StartDate = Soon, EndDate = Soon,
            Lat = 1.0, Lon = 2.0, GeoSource = GeoSource.Manual,
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("3rd Raharney Rapid FIDE Open")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(GeoSource.Manual, entry.GeoSource);
        Assert.Equal(1.0, entry.Lat!.Value, 4);
    }

    /// <summary>
    /// Zwei Drittel der Turniere hat die Quelle selbst nicht verortet — dort gilt der normale Weg
    /// ueber den Ortstext, und der ist hier oft eine volle Anschrift.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithoutCoordinates_FallsBackToTheGazetteer()
    {
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "IE", Name = "Ennis", NameNormalized = "ennis",
            Lat = 52.8436, Lon = -8.9864, Kind = GeoPlaceKind.City, Population = 25_000,
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("Quad in Ennis", place: "Ennis", lat: null, lon: null)}]")
            .RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(GeoSource.City, entry.GeoSource);
        Assert.Equal(52.8436, entry.Lat!.Value, 3);
    }

    // ----- Nordirland --------------------------------------------------------

    /// <summary>
    /// Die Falle dieser Quelle: die ICU ist der GESAMTirische Verband — 11 der 81 Turniere liegen
    /// in Nordirland und damit im Vereinigten Koenigreich. Der Eintrag bleibt <c>IRL</c> (dort
    /// sucht ihn jemand), die Ortssuche aber muss nach <c>GB</c> gehen: eine BT-Postleitzahl gibt
    /// es im irischen Teil des Lexikons nicht.
    /// </summary>
    [Fact]
    public async Task RunAsync_ANorthernIrishVenue_IsLookedUpInBritainButStaysIrish()
    {
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "GB", Name = "Groomsport", NameNormalized = "groomsport",
            Lat = 54.6789, Lon = -5.6167, Kind = GeoPlaceKind.City, Population = 2_500,
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("FIDE RATED City of Bangor Rapid Congress", eventId: "2259",
            place: "Bangor Chess Club, 30 Main Street, Groomsport, County Down, Northern Ireland, BT19 6JR",
            lat: null, lon: null)}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("IRL", entry.Federation);
        Assert.Equal(54.6789, entry.Lat!.Value, 3);
    }

    [Theory]
    [InlineData("Ballyphehane Community Centre, Cork", "IRL")]
    [InlineData("Coleraine Townhall, 35 The Diamond, Coleraine, Northern Ireland, BT52 1DP", "ENG")]
    [InlineData("Washingbay Centre, Coalisland, County Tyrone, BT71 5DX", "ENG")]
    [InlineData(null, "IRL")]
    public void GeoFederationOf_SendsNorthernIrishAddressesToBritain(string? place, string expected) =>
        Assert.Equal(expected, IcuDirectorySweepService.GeoFederationOf(
            new IcuDirectorySweepService.CrawlerIcuEvent(
                "1", "x", Soon, Soon, place, null, null, null, [], false)));

    // ----- Was kein Turnier ist ---------------------------------------------

    /// <summary>
    /// Unterricht, Lehrgang und Jahreshauptversammlung stehen in derselben Liste (5 von 81) —
    /// und die zwei Unterrichtsreihen laufen ueber 78 bzw. 84 Tage, haetten im Kalender also ein
    /// Vierteljahr zugedeckt.
    /// </summary>
    [Fact]
    public async Task RunAsync_DoesNotAddLessonsCoursesOrTheAgm()
    {
        var result = await CreateService(
            $"[{Row("Chess Lessons Dublin Chess Club (DCC) 2026", nonTournament: true)}]").RunAsync();

        Assert.Equal(0, result.Added);
        Assert.Empty(_db.TournamentDirectoryEntries.ToList());
    }

    [Fact]
    public async Task RunAsync_RetiresAnEntryThatTurnsOutToBeNoTournament()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "ie2305", Name = "AGM 2026", Federation = "IRL",
            StartDate = Soon, EndDate = Soon,
        });
        await _db.SaveChangesAsync();

        var result = await CreateService($"[{Row("AGM 2026", nonTournament: true)}]").RunAsync();

        Assert.Equal(1, result.Retired);
        Assert.NotNull(_db.TournamentDirectoryEntries.Single().RemovedAt);
    }

    // ----- Bedenkzeit und Publikum ------------------------------------------

    /// <summary>
    /// Diese Quelle nennt die Bedenkzeit-Klasse selbst, statt sie im Namen zu verstecken. MEHRERE
    /// heissen aber „gemischt" und ergeben keine — sonst bekaeme der Filter „nur Blitz" ein
    /// Turnierschach-Wochenende.
    /// </summary>
    [Fact]
    public void SpeedOf_OneClassCounts_SeveralMeanMixed()
    {
        Assert.Equal(TournamentSpeed.Standard, IcuDirectorySweepService.SpeedOf(["Classical", "FIDE-rated"]));
        Assert.Equal(TournamentSpeed.Rapid, IcuDirectorySweepService.SpeedOf(["Rapid"]));
        Assert.Equal(TournamentSpeed.Blitz, IcuDirectorySweepService.SpeedOf(["Blitz"]));
        Assert.Equal(TournamentSpeed.Unknown, IcuDirectorySweepService.SpeedOf(["Rapid", "Blitz"]));
        Assert.Equal(TournamentSpeed.Unknown, IcuDirectorySweepService.SpeedOf(["FIDE-rated"]));
        Assert.Equal(TournamentSpeed.Unknown, IcuDirectorySweepService.SpeedOf([]));
    }

    /// <summary>
    /// „Junior International" ist bei einem Namen wie „14th ChessMates" die einzige Auskunft,
    /// dass es ein Jugendturnier ist.
    /// </summary>
    [Fact]
    public async Task RunAsync_JuniorInternational_MarksTheAudience()
    {
        await CreateService($"[{Row("14th ChessMates", categories: "\"Classical\",\"Junior International\"")}]")
            .RunAsync();

        Assert.Equal(TournamentAgeGroups.YouthUnspecified,
            Assert.Single(_db.TournamentDirectoryEntries.ToList()).AgeGroups);
    }

    /// <summary>Sagt der Name eine KLASSE, ist sie die bessere Auskunft.</summary>
    [Fact]
    public async Task RunAsync_ANamedClassBeatsTheJuniorTag()
    {
        await CreateService($"[{Row("Irish U14 Championship", categories: "\"Junior International\"")}]")
            .RunAsync();

        Assert.Equal(TournamentAgeGroups.U14,
            Assert.Single(_db.TournamentDirectoryEntries.ToList()).AgeGroups);
    }

    [Fact]
    public async Task RunAsync_WomenOnly_MarksTheAudience()
    {
        await CreateService($"[{Row("Mulcahy Memorial", categories: "\"Classical\",\"Women only\"")}]")
            .RunAsync();

        Assert.Equal(TournamentGender.Female,
            Assert.Single(_db.TournamentDirectoryEntries.ToList()).Gender);
    }

    /// <summary>
    /// „Foreign" ist die Aussage der Quelle, dass das Turnier im Ausland stattfindet (Olympiade
    /// in Samarkand). „IRL" waere dann nachweislich falsch, und eine falsche Angabe ist
    /// schlechter als keine.
    /// </summary>
    [Fact]
    public async Task RunAsync_AForeignTournament_GetsNoFederation()
    {
        await CreateService($"[{Row("46th Chess Olympiad 2026", eventId: "2221",
            place: "Samarkand, Uzbekistan", lat: null, lon: null,
            categories: "\"Classical\",\"Foreign\",\"FIDE-rated\"")}]").RunAsync();

        Assert.Null(Assert.Single(_db.TournamentDirectoryEntries.ToList()).Federation);
    }

    // ----- Die Detailseite ---------------------------------------------------

    /// <summary>
    /// Der Grund, warum die Detailseite ueberhaupt geholt wird: sie nennt bei 7 % der Turniere
    /// die chess-results-Nummer, und damit ist die Zuordnung EXAKT statt ueber einen
    /// Namensvergleich geraten. Hier traegt der bestehende Eintrag einen ganz anderen Namen —
    /// ueber den Namen faende ihn niemand.
    /// </summary>
    [Fact]
    public async Task RunAsync_UsesTheChessResultsNumberFromTheDetailPageForAnExactMatch()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1491223", ChessResultsId = "1491223",
            Name = "Raharney 3 Rapid", Federation = "IRL",
            StartDate = Soon, EndDate = Soon,
        });
        await _db.SaveChangesAsync();

        var result = await CreateService($"[{Row("3rd Raharney Rapid FIDE Open")}]",
            Detail(chessResultsId: "1491223")).RunAsync();

        Assert.Equal(1, result.Matched);
        Assert.Equal(0, result.Added);
        var source = Assert.Single(_db.TournamentDirectorySources.ToList());
        Assert.Equal(DirectorySourceKind.IrishChessUnion, source.Kind);
        Assert.Equal("2305", source.ExternalId);
    }

    /// <summary>
    /// Neben chessarbiter die einzige Quelle, die vor dem Turnier ueberhaupt eine Teilnehmerzahl
    /// nennt („28 entries") — 6 von 28 gemessenen Seiten.
    /// </summary>
    [Fact]
    public async Task RunAsync_TakesThePlayerCountFromTheDetailPage()
    {
        var result = await CreateService($"[{Row("3rd Raharney Rapid FIDE Open")}]",
            Detail(players: 28)).RunAsync();

        Assert.Equal(1, result.Updated);   // Updated zaehlt die gelesenen Detailseiten
        Assert.Equal(28, Assert.Single(_db.TournamentDirectoryEntries.ToList()).PlayerCount);
    }

    /// <summary>
    /// Ein Abruf je Turnier lohnt nur EINMAL — „schon geholt" steht in der Adresse des
    /// Herkunftsvermerks, die erst dabei gesetzt wird.
    /// </summary>
    [Fact]
    public async Task RunAsync_ReadsTheDetailPageOnlyOnce()
    {
        var service = CreateService($"[{Row("3rd Raharney Rapid FIDE Open")}]", Detail(players: 28));

        await service.RunAsync();
        await service.RunAsync();

        Assert.Equal(1, _routes.DetailCalls);
        Assert.Equal("https://www.icu.ie/events/2305",
            Assert.Single(_db.TournamentDirectorySources.ToList()).Url);
    }

    /// <summary>
    /// Bleibt die Detailseite aus, darf der Vermerk KEINE Adresse bekommen — sonst gaelte sie fuer
    /// immer als gelesen und der naechste Durchgang versuchte es nie wieder.
    /// </summary>
    [Fact]
    public async Task RunAsync_AFailedDetailPage_IsTriedAgainNextTime()
    {
        var service = CreateService($"[{Row("3rd Raharney Rapid FIDE Open")}]", detailJson: null);

        await service.RunAsync();
        await service.RunAsync();

        Assert.Equal(2, _routes.DetailCalls);
        Assert.Null(Assert.Single(_db.TournamentDirectorySources.ToList()).Url);
        Assert.Single(_db.TournamentDirectoryEntries.ToList());
    }

    /// <summary>Ohne Budget bleibt es bei der Liste — fuenf Abrufe fuer den ganzen Kalender.</summary>
    [Fact]
    public async Task RunAsync_WithoutDetailBudget_FetchesNoDetailPage()
    {
        var result = await CreateService($"[{Row("3rd Raharney Rapid FIDE Open")}]",
            Detail(players: 28), batchSize: 0).RunAsync();

        Assert.Equal(0, _routes.DetailCalls);
        Assert.Equal(0, result.Updated);
        Assert.Equal(1, result.Added);
        Assert.Null(Assert.Single(_db.TournamentDirectoryEntries.ToList()).PlayerCount);
    }

    // ----- Zuordnung und Herkunft -------------------------------------------

    [Fact]
    public async Task RunAsync_RetiresItsOwnEntryOnceTheSearchCatchesUp()
    {
        _db.TournamentDirectoryEntries.AddRange(
            new TournamentDirectoryEntry
            {
                PublicId = "ie2305", Name = "Kilkenny Chess Congress 2026", Federation = "IRL",
                StartDate = Soon, EndDate = Soon,
            },
            new TournamentDirectoryEntry
            {
                PublicId = "1490100", ChessResultsId = "1490100",
                Name = "Kilkenny Chess Congress 2026", Federation = "IRL",
                StartDate = Soon, EndDate = Soon,
            });
        await _db.SaveChangesAsync();

        var result = await CreateService($"[{Row("Kilkenny Chess Congress 2026")}]").RunAsync();

        Assert.Equal(1, result.Matched);
        Assert.Equal(1, result.Retired);
        Assert.NotNull(_db.TournamentDirectoryEntries.Single(e => e.PublicId == "ie2305").RemovedAt);
    }

    [Fact]
    public async Task RunAsync_NotesWhereTheEntryCameFrom()
    {
        await CreateService($"[{Row("3rd Raharney Rapid FIDE Open")}]", Detail()).RunAsync();

        var source = Assert.Single(_db.TournamentDirectorySources.ToList());
        Assert.Equal(DirectorySourceKind.IrishChessUnion, source.Kind);
        Assert.Equal("2305", source.ExternalId);
        Assert.Equal("https://www.icu.ie/events/2305", source.Url);
    }

    [Fact]
    public async Task RunAsync_CrawlerFailure_Throws() =>
        await Assert.ThrowsAsync<RookHub.Api.Exceptions.CrawlerRequestException>(
            () => CreateService("kaputt").RunAsync());

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://crawler:8080") };
    }

    /// <summary>Zwei Routen, ein Handler: die Liste und die Detailseite.</summary>
    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly string _list;
        private readonly string? _detail;

        public RouteHandler(string list = "[]", string? detail = null)
        {
            _list = list;
            _detail = detail;
        }

        public int DetailCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith("/detail", StringComparison.Ordinal))
            {
                DetailCalls++;
                return Task.FromResult(_detail is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : Ok(_detail));
            }

            return Task.FromResult(_list == "kaputt"
                ? new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("") }
                : Ok(_list));
        }

        private static HttpResponseMessage Ok(string body) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
    }
}
