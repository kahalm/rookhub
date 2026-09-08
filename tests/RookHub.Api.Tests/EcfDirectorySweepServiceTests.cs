using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Kalender des englischen Verbands (ECF): 256 kuenftige Turniere, 86 % davon nicht auf
/// chess-results — und die einzige Quelle des Projekts, die KOORDINATEN mitliefert.
/// </summary>
public class EcfDirectorySweepServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public EcfDirectorySweepServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private EcfDirectorySweepService CreateService(string json,
        HttpStatusCode status = HttpStatusCode.OK) =>
        new(_db, new StubClientFactory(new StubHandler(json, status)),
            new GeocodingService(_db), new TestLogger<EcfDirectorySweepService>());

    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    private static string Row(string name, string eventId = "88941",
        string? place = "Durham Clayport Library, 8 Millennium Place, Durham, DH1 1WA",
        string? city = "Durham", string? country = null, double? lat = 54.7779, double? lon = -1.5752,
        string categories = "\"ECF Rated\"", DateOnly? end = null) =>
        $$"""
          {"eventId":"{{eventId}}","name":"{{name}}",
           "startDate":"{{Soon:yyyy-MM-dd}}","endDate":"{{(end ?? Soon):yyyy-MM-dd}}",
           "url":"https://www.englishchess.org.uk/event/{{eventId}}/",
           "place":{{Json(place)}},"city":{{Json(city)}},"postalCode":null,
           "country":{{Json(country)}},
           "lat":{{Num(lat)}},"lon":{{Num(lon)}},
           "categories":[{{categories}}],"website":null}
          """;

    private static string Json(string? v) => v is null ? "null" : $"\"{v}\"";
    private static string Num(double? v) =>
        v is null ? "null" : v.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // ----- Anlegen -----------------------------------------------------------

    [Fact]
    public async Task RunAsync_UnknownTournament_IsAdded()
    {
        var result = await CreateService($"[{Row("Durham Rapidplay")}]").RunAsync();

        Assert.Equal(1, result.Added);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("en88941", entry.PublicId);
        Assert.Equal("ENG", entry.Federation);
        Assert.Equal(Soon, entry.StartDate);
    }

    /// <summary>
    /// Der Sonderfall dieser Quelle: die Koordinaten kommen MIT. Es wird nichts aufgeloest und
    /// nichts geraten — das haelt <see cref="GeoSource.SourceProvided"/> fest.
    /// </summary>
    [Fact]
    public async Task RunAsync_TakesTheCoordinatesFromTheSource()
    {
        var result = await CreateService($"[{Row("Durham Rapidplay")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(GeoSource.SourceProvided, entry.GeoSource);
        Assert.Equal(54.7779, entry.Lat!.Value, 4);
        Assert.Equal(-1.5752, entry.Lon!.Value, 4);
        Assert.Equal(1, result.Updated);   // Updated zaehlt die verorteten Eintraege
    }

    /// <summary>
    /// Eine mitgelieferte Koordinate meint die SPIELSTAETTE, ein Lexikon-Treffer bestenfalls die
    /// Stadtmitte. Sie ersetzt deshalb einen bestehenden Lexikon-Pin.
    /// </summary>
    [Fact]
    public async Task RunAsync_TheSourceCoordinateBeatsAnEarlierGazetteerPin()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "en88941", Name = "Durham Rapidplay", Federation = "ENG",
            StartDate = Soon, EndDate = Soon,
            Lat = 51.5, Lon = -0.12, GeoSource = GeoSource.City, GeoPlaceName = "London",
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("Durham Rapidplay")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(GeoSource.SourceProvided, entry.GeoSource);
        Assert.Equal(54.7779, entry.Lat!.Value, 4);
    }

    /// <summary>Eine von HAND gesetzte Koordinate bleibt dagegen unangetastet.</summary>
    [Fact]
    public async Task RunAsync_LeavesAManualCoordinateAlone()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "en88941", Name = "Durham Rapidplay", Federation = "ENG",
            StartDate = Soon, EndDate = Soon,
            Lat = 1.0, Lon = 2.0, GeoSource = GeoSource.Manual,
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("Durham Rapidplay")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(GeoSource.Manual, entry.GeoSource);
        Assert.Equal(1.0, entry.Lat!.Value, 4);
    }

    /// <summary>
    /// Ein Drittel der Spielstaetten hat keine hinterlegten Koordinaten — dort gilt der normale
    /// Weg ueber den Ortstext.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithoutCoordinates_FallsBackToTheGazetteer()
    {
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "GB", Name = "London", NameNormalized = "london",
            Lat = 51.5074, Lon = -0.1278, Kind = GeoPlaceKind.City, Population = 8_000_000,
        });
        await _db.SaveChangesAsync();

        await CreateService(
            $"[{Row("Muswell Hill FIDE Chess", place: "The Clissold Arms, 105 Fortis Green, London", city: null, lat: null, lon: null)}]")
            .RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(GeoSource.City, entry.GeoSource);
        Assert.Equal(51.5074, entry.Lat!.Value, 3);
    }

    // ----- Die Schlagworte ---------------------------------------------------

    /// <summary>„Meeting" ist eine Sitzung des Verbands und kein Turnier.</summary>
    [Fact]
    public async Task RunAsync_DoesNotAddMeetings()
    {
        var result = await CreateService(
            $"[{Row("ECF Council Meeting", categories: "\"Meeting\"")}]").RunAsync();

        Assert.Equal(0, result.Added);
        Assert.Empty(_db.TournamentDirectoryEntries.ToList());
    }

    [Fact]
    public async Task RunAsync_RetiresAnEntryThatTurnsOutToBeAMeeting()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "en88941", Name = "ECF Council", Federation = "ENG",
            StartDate = Soon, EndDate = Soon,
        });
        await _db.SaveChangesAsync();

        var result = await CreateService(
            $"[{Row("ECF Council", categories: "\"Meeting\"")}]").RunAsync();

        Assert.Equal(1, result.Retired);
        Assert.NotNull(_db.TournamentDirectoryEntries.Single().RemovedAt);
    }

    /// <summary>„Online" hat keinen Spielort — weder Ortstext noch Pin.</summary>
    [Fact]
    public async Task RunAsync_OnlineTournament_GetsNoLocationAndNoPin()
    {
        await CreateService(
            $"[{Row("4NCL Online Season 14", place: "Online", categories: "\"Online\"")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Null(entry.LocationText);
        Assert.Null(entry.Lat);
    }

    /// <summary>„Juniors Only" ist eine verlaessliche Jugend-Angabe der Quelle.</summary>
    [Fact]
    public async Task RunAsync_JuniorsOnly_MarksTheAudience()
    {
        await CreateService(
            $"[{Row("Maidenhead Junior Tournament", categories: "\"ECF Rated\",\"Juniors Only\"")}]")
            .RunAsync();

        Assert.Equal(TournamentAgeGroups.YouthUnspecified,
            Assert.Single(_db.TournamentDirectoryEntries.ToList()).AgeGroups);
    }

    /// <summary>Sagt der Name eine KLASSE, ist sie die bessere Auskunft.</summary>
    [Fact]
    public async Task RunAsync_ANamedClassBeatsTheJuniorsOnlyTag()
    {
        await CreateService(
            $"[{Row("English U14 Championship", categories: "\"Juniors Only\"")}]").RunAsync();

        Assert.Equal(TournamentAgeGroups.U14,
            Assert.Single(_db.TournamentDirectoryEntries.ToList()).AgeGroups);
    }

    // ----- Foederation -------------------------------------------------------

    /// <summary>
    /// Ohne Landesangabe gilt England — es ist der englische Kalender. Nennt die Quelle ein
    /// ANDERES Land, bekommt der Eintrag KEINE Foederation: „ENG" waere dann nachweislich falsch.
    /// </summary>
    [Theory]
    [InlineData(null, "ENG")]
    [InlineData("", "ENG")]
    [InlineData("United Kingdom", "ENG")]
    [InlineData("France", null)]
    public void FederationOf_OnlyClaimsEnglandWhenTheSourceDoes(string? country, string? expected) =>
        Assert.Equal(expected, EcfDirectorySweepService.FederationOf(country));

    [Fact]
    public async Task RunAsync_AForeignVenue_GetsNoFederation()
    {
        await CreateService(
            $"[{Row("Les betises de Cambrai GM", country: "France", lat: 50.175, lon: 3.228)}]")
            .RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Null(entry.Federation);
        Assert.Equal(GeoSource.SourceProvided, entry.GeoSource);
    }

    // ----- Zuordnung und Herkunft -------------------------------------------

    [Fact]
    public async Task RunAsync_RetiresItsOwnEntryOnceTheSearchCatchesUp()
    {
        _db.TournamentDirectoryEntries.AddRange(
            new TournamentDirectoryEntry
            {
                PublicId = "en88941", Name = "Sheffield Chesterfield Autumn Congress", Federation = "ENG",
                StartDate = Soon, EndDate = Soon,
            },
            new TournamentDirectoryEntry
            {
                PublicId = "1490100", ChessResultsId = "1490100",
                Name = "Sheffield Chesterfield Autumn Congress",
                Federation = "ENG", StartDate = Soon, EndDate = Soon,
            });
        await _db.SaveChangesAsync();

        var result = await CreateService($"[{Row("Sheffield Chesterfield Autumn Congress")}]")
            .RunAsync();

        Assert.Equal(1, result.Matched);
        Assert.Equal(1, result.Retired);
        Assert.NotNull(_db.TournamentDirectoryEntries.Single(e => e.PublicId == "en88941").RemovedAt);
    }

    [Fact]
    public async Task RunAsync_NotesWhereTheEntryCameFrom()
    {
        await CreateService($"[{Row("Durham Rapidplay")}]").RunAsync();

        var source = Assert.Single(_db.TournamentDirectorySources.ToList());
        Assert.Equal(DirectorySourceKind.EnglishChessFederation, source.Kind);
        Assert.Equal("88941", source.ExternalId);
        Assert.Equal("https://www.englishchess.org.uk/event/88941/", source.Url);
    }

    [Fact]
    public async Task RunAsync_CrawlerFailure_Throws() =>
        await Assert.ThrowsAsync<RookHub.Api.Exceptions.CrawlerRequestException>(
            () => CreateService("kaputt", HttpStatusCode.BadGateway).RunAsync());

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://crawler:8080") };
    }

    private sealed class StubHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
