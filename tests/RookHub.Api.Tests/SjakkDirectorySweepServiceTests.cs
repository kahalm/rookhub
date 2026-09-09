using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Aktivitaeten-Feed des norwegischen Verbands (sjakk.no).
///
/// <para>chess-results fuehrt fuer NOR <b>null</b> kuenftige Turniere — der Zugewinn ist
/// rechnerisch vollstaendig. Der Preis dafuer steht am anderen Ende: der Feed hat KEIN Ortsfeld,
/// und selbst die Detailseite nennt nur bei 17 von 80 kuenftigen Terminen ein „Spillsted". Der
/// haeufigere Ortshinweis ist der ausrichtende VEREIN (52 von 80), und ein daraus abgeleiteter
/// Pin ist eine Ableitung, kein Spielort — deshalb <see cref="GeoSource.TeamHint"/>.</para>
/// </summary>
public class SjakkDirectorySweepServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public SjakkDirectorySweepServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private RouteHandler _routes = new();

    private SjakkDirectorySweepService CreateService(
        string listJson, string? detailJson = null, int batchSize = 120)
    {
        _routes = new RouteHandler(listJson, detailJson);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TournamentDirectory:SjakkDetailBatchSize"] = batchSize.ToString(),
        }).Build();

        return new SjakkDirectorySweepService(_db, new StubClientFactory(_routes),
            new GeocodingService(_db), config, new TestLogger<SjakkDirectorySweepService>());
    }

    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    private static string Row(string name, string eventId = "kongsvingerlyn-ngp-2026",
        DateOnly? end = null) =>
        $$"""
          {"eventId":"{{eventId}}","name":"{{name}}",
           "startDate":"{{Soon:yyyy-MM-dd}}","endDate":"{{(end ?? Soon):yyyy-MM-dd}}",
           "url":"https://www.sjakk.no/aktiviteter/{{eventId}}"}
          """;

    private static string Detail(string? venue = null, string? organizer = "Kongsvinger Sjakklubb",
        string? timeControl = "3 min pluss 2 sekunder", int? rounds = 12, string? system = "swiss") =>
        $$"""
          {"venue":{{Json(venue)}},"organizer":{{Json(organizer)}},
           "timeControl":{{Json(timeControl)}},
           "rounds":{{(rounds is null ? "null" : rounds.ToString())}},"system":{{Json(system)}},
           "website":"https://tournamentservice.com/invitation.aspx"}
          """;

    private static string Json(string? value) => value is null ? "null" : $"\"{value}\"";

    private async Task SeedPlaceAsync(string name, string normalized, double lat, double lon)
    {
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "NO", Name = name, NameNormalized = normalized,
            Lat = lat, Lon = lon, Kind = GeoPlaceKind.City, Population = 18000,
        });
        await _db.SaveChangesAsync();
    }

    // ----- Liste + Detail ----------------------------------------------------

    [Fact]
    public async Task RunAsync_UnknownTournament_IsAddedWithItsDetailPage()
    {
        var result = await CreateService($"[{Row("Kongsvingerlyn NGP 2026")}]", Detail()).RunAsync();

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Updated);   // Updated zaehlt die gelesenen Detailseiten
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("NOR", entry.Federation);
        Assert.Equal(Soon, entry.StartDate);
        Assert.Equal(12, entry.Rounds);
        Assert.Equal(TournamentSystem.Swiss, entry.System);
        Assert.Equal("3 min pluss 2 sekunder", entry.TimeControlText);
    }

    /// <summary>
    /// Der Feed allein traegt Name und Termin und sonst nichts — faellt die Detailseite aus, ist
    /// das kein Grund, den Termin wegzulassen. Er ist der eigentliche Zugewinn.
    /// </summary>
    [Fact]
    public async Task RunAsync_DetailPageMissing_StillKeepsTheDate()
    {
        var result = await CreateService($"[{Row("Bronstein Cup NGP 2026")}]", detailJson: null)
            .RunAsync();

        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Updated);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("Bronstein Cup NGP 2026", entry.Name);
        Assert.Equal(Soon, entry.StartDate);
        Assert.Null(entry.LocationText);
        Assert.Null(entry.Lat);
    }

    /// <summary>
    /// Der Detailabruf lohnt nur EINMAL je Turnier — bei 81 kuenftigen Terminen ist das der
    /// gesamte teure Teil des Durchgangs. „Schon geholt" steht in der Adresse des
    /// Herkunftsvermerks.
    /// </summary>
    [Fact]
    public async Task RunAsync_SecondSweep_DoesNotFetchTheDetailPageAgain()
    {
        var list = $"[{Row("Kongsvingerlyn NGP 2026")}]";
        await CreateService(list, Detail()).RunAsync();
        Assert.Equal(1, _routes.DetailCalls);

        var service = CreateService(list, Detail());
        var result = await service.RunAsync();

        Assert.Equal(0, _routes.DetailCalls);
        Assert.Equal(0, result.Added);
        Assert.Single(_db.TournamentDirectoryEntries.ToList());
    }

    /// <summary>
    /// Der Deckel begrenzt die Detailabrufe eines Durchgangs; die uebrigen Termine kommen
    /// trotzdem herein und holen ihre Seite in der naechsten Nacht nach.
    /// </summary>
    [Fact]
    public async Task RunAsync_DetailBudgetSpent_StillAddsTheRemainingEvents()
    {
        var list = $"[{Row("Erstes", "erstes-2026")},{Row("Zweites", "zweites-2026")}]";

        var result = await CreateService(list, Detail(), batchSize: 1).RunAsync();

        Assert.Equal(2, result.Added);
        Assert.Equal(1, _routes.DetailCalls);
        Assert.Equal(2, _db.TournamentDirectoryEntries.Count());
    }

    // ----- Der Ort -----------------------------------------------------------

    /// <summary>
    /// Der seltene, aber beste Fall: die Detailseite nennt ein „Spillsted" (17 von 80). Ein
    /// Spielort-Treffer ist KEIN <see cref="GeoSource.TeamHint"/>.
    /// </summary>
    [Fact]
    public async Task RunAsync_VenueNamesAPlace_PinsItAsAVenue()
    {
        await SeedPlaceAsync("Fagernes", "fagernes", 60.988, 9.231);

        await CreateService($"[{Row("Fagernes Autumn Blitz 2026", "fagernes-autumn-blitz-2026")}]",
            Detail(venue: "Scandic Valdres Hotell Fagernes", organizer: null)).RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("Scandic Valdres Hotell Fagernes", entry.LocationText);
        Assert.Equal(GeoSource.City, entry.GeoSource);
        Assert.Equal(60.988, entry.Lat);
    }

    /// <summary>
    /// Der HAEUFIGERE Fall (52 von 80): kein Spielort, aber ein Verein. Ein norwegischer
    /// Vereinsname traegt fast immer seinen Ort — aber er ist eine Ableitung, kein Spielort.
    /// <b>Ohne diesen zweiten Weg haetten vier statt 27 Eintraege einen Pin.</b>
    /// </summary>
    [Fact]
    public async Task RunAsync_NoVenue_FallsBackToTheClubAndMarksItAsAHint()
    {
        await SeedPlaceAsync("Kongsvinger", "kongsvinger", 60.19, 11.99);

        await CreateService($"[{Row("Kongsvingerlyn NGP 2026")}]",
            Detail(venue: null, organizer: "Kongsvinger Sjakklubb")).RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("Kongsvinger Sjakklubb", entry.LocationText);
        Assert.Equal(GeoSource.TeamHint, entry.GeoSource);
        Assert.Equal(60.19, entry.Lat);
    }

    /// <summary>
    /// „Diverse" ist der Sammelbegriff der acht Ligawochenenden fuer ihre ueber das Land
    /// verteilten Spielstaetten. Ein Pin darauf waere frei erfunden — und ein Lexikon-Eintrag
    /// dieses Namens duerfte ihn erst recht nicht ausloesen.
    /// </summary>
    [Fact]
    public async Task RunAsync_VenueSaysDiverse_GetsNoPinAndNoLocationText()
    {
        await SeedPlaceAsync("Diverse", "diverse", 59.9, 10.7);

        await CreateService($"[{Row("Seriesjakkens 1. helg 26/27", "seriesjakkens-1-helg-26-27")}]",
            Detail(venue: "Diverse", organizer: null)).RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Null(entry.LocationText);
        Assert.Null(entry.Lat);
    }

    [Theory]
    [InlineData("Scandic Valdres Hotell Fagernes", null, "Scandic Valdres Hotell Fagernes", false)]
    [InlineData(null, "Kongsvinger Sjakklubb", "Kongsvinger Sjakklubb", true)]
    [InlineData("Diverse", "Sotra Sjakklubb", "Sotra Sjakklubb", true)]
    [InlineData("Online", "Rustad Sjakk", "Rustad Sjakk", true)]
    [InlineData(null, null, null, false)]
    [InlineData("Diverse", null, null, false)]
    public void LocationOf_PrefersTheVenueAndSaysWhenItIsOnlyTheClub(
        string? venue, string? organizer, string? expected, bool fromClub)
    {
        var (location, hint) = SjakkDirectorySweepService.LocationOf(venue, organizer);

        Assert.Equal(expected, location);
        Assert.Equal(fromClub, hint);
    }

    /// <summary>
    /// Der TURNIERNAME wird bewusst nicht verortet, obwohl er weitere elf Pins braechte: er ist
    /// keine Aussage ueber einen Ort, und <c>LocationText</c> ist die Spalte „Ort" der Anzeige.
    /// </summary>
    [Fact]
    public async Task RunAsync_PlaceOnlyInTheTournamentName_IsNotUsedAsALocation()
    {
        await SeedPlaceAsync("Geilo", "geilo", 60.53, 8.20);

        await CreateService(
            $"[{Row("Geilo Sjakkfestival III - Barnas Grand Prix", "geilo-sjakkfestival-iii")}]",
            Detail(venue: null, organizer: null)).RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Null(entry.LocationText);
        Assert.Null(entry.Lat);
    }

    // ----- Kennung und Abgleich ----------------------------------------------

    /// <summary>
    /// Die Kennung ist der Adressbestandteil der Detailseite, und der traegt norwegische
    /// Buchstaben. Gekuerzt wird nicht — zwei Turniere zu einem zu machen ist der teuerste Fehler
    /// an dieser Stelle.
    /// </summary>
    [Fact]
    public void PublicIdOf_FitsTheColumnAndKeepsSimilarSlugsApart()
    {
        var a = SjakkDirectorySweepService.PublicIdOf("horten-bgp-høst-2027");
        var b = SjakkDirectorySweepService.PublicIdOf("horten-bgp-høst-2026");

        Assert.NotEqual(a, b);
        Assert.True(a.Length <= 24, a);
        Assert.StartsWith("no", a);
        Assert.Equal(a, SjakkDirectorySweepService.PublicIdOf("Horten-BGP-Høst-2027"));
    }

    /// <summary>
    /// Holt die Turniersuche dieselbe Veranstaltung ein, gehoert ihr der Eintrag — der eigene
    /// wird zurueckgezogen, statt dasselbe Turnier zweimal zu fuehren. Heute der seltene Fall
    /// (chess-results fuehrt fuer NOR nichts), aber genau dafuer ist er da.
    /// </summary>
    [Fact]
    public async Task RunAsync_TournamentSearchCaughtUp_RetiresTheOwnEntry()
    {
        await CreateService($"[{Row("Fagernes International Autumn 2026", "fagernes-int-2026")}]",
            Detail()).RunAsync();
        var own = Assert.Single(_db.TournamentDirectoryEntries.ToList());

        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1477777", ChessResultsId = "1477777",
            Name = "Fagernes International Autumn 2026", Federation = "NOR",
            StartDate = Soon, EndDate = Soon, FirstSeenAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var result = await CreateService(
            $"[{Row("Fagernes International Autumn 2026", "fagernes-int-2026")}]", Detail())
            .RunAsync();

        Assert.Equal(1, result.Matched);
        Assert.Equal(1, result.Retired);
        Assert.NotNull(_db.TournamentDirectoryEntries.Single(e => e.Id == own.Id).RemovedAt);
    }

    /// <summary>
    /// Der Herkunftsvermerk traegt den lesbaren Adressbestandteil — die <c>PublicId</c> ist ein
    /// Kurzwert und sagt niemandem, welche Seite gemeint war.
    /// </summary>
    [Fact]
    public async Task RunAsync_NotesWhereTheEntryCameFrom()
    {
        await CreateService($"[{Row("Kongsvingerlyn NGP 2026")}]", Detail()).RunAsync();

        var source = Assert.Single(_db.TournamentDirectorySources.ToList());
        Assert.Equal(DirectorySourceKind.NorwegianChessFederation, source.Kind);
        Assert.Equal("kongsvingerlyn-ngp-2026", source.ExternalId);
        Assert.Equal("https://www.sjakk.no/aktiviteter/kongsvingerlyn-ngp-2026", source.Url);
    }

    /// <summary>
    /// Ein Vermerk OHNE Adresse heisst „aus dem Feed, Detailseite fehlt noch" — daran erkennt der
    /// naechste Durchgang, dass er sie holen muss.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithoutDetailPage_LeavesTheSourceUrlEmpty()
    {
        await CreateService($"[{Row("Bronstein Cup NGP 2026")}]", detailJson: null).RunAsync();

        Assert.Null(Assert.Single(_db.TournamentDirectorySources.ToList()).Url);
    }

    [Fact]
    public async Task RunAsync_EmptyList_DoesNothing()
    {
        var result = await CreateService("[]").RunAsync();

        Assert.Equal(0, result.Read);
        Assert.Empty(_db.TournamentDirectoryEntries.ToList());
    }

    [Fact]
    public async Task RunAsync_CrawlerUnavailable_Throws() =>
        await Assert.ThrowsAsync<RookHub.Api.Exceptions.CrawlerRequestException>(
            () => CreateService("kaputt").RunAsync());

    // ----- Stubs -------------------------------------------------------------

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://crawler:8080") };
    }

    /// <summary>Zwei Routen, ein Handler: der Feed und die Detailseite.</summary>
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
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
