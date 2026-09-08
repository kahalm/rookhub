using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Kalender des slowakischen Verbands (chess.sk) — die reichhaltigste Zusatzquelle: Anschrift
/// mit Postleitzahl, Bedenkzeit, Rundenzahl, System und Bedenkzeit-Klasse, und bei einem Drittel
/// der Eintraege die chess-results-Nummer als EXAKTER Zuordnungsschluessel.
/// </summary>
public class ChessSkDirectorySweepServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public ChessSkDirectorySweepServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private ChessSkDirectorySweepService CreateService(string json,
        HttpStatusCode status = HttpStatusCode.OK)
    {
        _handler = new StubHandler(json, status);
        return new ChessSkDirectorySweepService(_db, new StubClientFactory(_handler),
            new GeocodingService(_db), new TestLogger<ChessSkDirectorySweepService>());
    }

    private StubHandler _handler = new("[]", HttpStatusCode.OK);

    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    private static string Row(
        string name, string eventId = "5909", string? chessResultsId = null,
        string? city = "Prešov", string? address = null, string? timeControl = null,
        string? system = null, int? rounds = null, string? type = null,
        bool nonTournament = false, string? country = "SVK", DateOnly? end = null) =>
        $$"""
          {"eventId":"{{eventId}}","name":"{{name}}",
           "startDate":"{{Soon:yyyy-MM-dd}}","endDate":"{{(end ?? Soon):yyyy-MM-dd}}",
           "city":{{Json(city)}},"address":{{Json(address)}},"country":{{Json(country)}},
           "chessResultsId":{{Json(chessResultsId)}},"url":"https://chess.sk/x",
           "timeControl":{{Json(timeControl)}},"system":{{Json(system)}},
           "rounds":{{(rounds is null ? "null" : rounds.ToString())}},
           "type":{{Json(type)}},"nonTournament":{{(nonTournament ? "true" : "false")}}}
          """;

    private static string Json(string? value) => value is null ? "null" : $"\"{value}\"";

    // ----- Anlegen -----------------------------------------------------------

    [Fact]
    public async Task RunAsync_UnknownTournament_IsAdded()
    {
        var result = await CreateService($"[{Row("Majstrovstvá SR amatérov 2026")}]").RunAsync();

        Assert.Equal(1, result.Added);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("sk5909", entry.PublicId);
        Assert.Equal("SVK", entry.Federation);
        Assert.Equal(Soon, entry.StartDate);
    }

    /// <summary>
    /// Die Quelle liefert als einzige alles auf einmal — und alles davon muss auch ankommen.
    /// </summary>
    [Fact]
    public async Task RunAsync_KeepsEveryFieldTheSourceDelivers()
    {
        await CreateService($"""
            [{Row("Open Nitra", timeControl: "2x 15 min + 5 sek/ťah", system: "swiss",
                  rounds: 7, type: "Rapid", address: "Denné centrum, Podzámska 6")}]
            """).RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("2x 15 min + 5 sek/ťah", entry.TimeControlText);
        Assert.Equal(TournamentSpeed.Rapid, entry.Speed);
        Assert.Equal(TournamentSystem.Swiss, entry.System);
        Assert.Equal(7, entry.Rounds);
    }

    /// <summary>
    /// Die Turnierart bleibt <see cref="TournamentKind.Unknown"/>: die Quelle sagt nichts darueber,
    /// und Raten waere schlechter als Schweigen (dieselbe Regel wie bei FSI und SZS).
    /// </summary>
    [Fact]
    public async Task RunAsync_LeavesTheTournamentKindUntouched()
    {
        await CreateService($"[{Row("Open turnaj družstiev")}]").RunAsync();

        Assert.Equal(TournamentKind.Unknown,
            Assert.Single(_db.TournamentDirectoryEntries.ToList()).Kind);
    }

    /// <summary>Der Staat der Quelle IST die Foederation — vereinzelt steht dort „CZE".</summary>
    [Theory]
    [InlineData("SVK", "SVK")]
    [InlineData("CZE", "CZE")]
    [InlineData(null, "SVK")]
    public async Task RunAsync_TakesTheFederationFromTheSource(string? country, string expected)
    {
        await CreateService($"[{Row("Turnaj", country: country)}]").RunAsync();

        Assert.Equal(expected, Assert.Single(_db.TournamentDirectoryEntries.ToList()).Federation);
    }

    // ----- Zuordnung ---------------------------------------------------------

    /// <summary>
    /// Der eigentliche Gewinn dieser Quelle: sie nennt die chess-results-Nummer selbst. Damit ist
    /// die Zuordnung EXAKT — auch dann, wenn Name und Termin gar nicht zusammenpassen wuerden.
    /// </summary>
    [Fact]
    public async Task RunAsync_MatchesOnTheChessResultsNumberEvenWhenTheNameDiffers()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1482875", ChessResultsId = "1482875",
            Name = "Ganz anders geschriebenes Turnier", Federation = "SVK",
            StartDate = Soon, EndDate = Soon,
        });
        await _db.SaveChangesAsync();

        var result = await CreateService($"[{Row("Majstrovstvá SR amatérov 2026", chessResultsId: "1482875")}]")
            .RunAsync();

        Assert.Equal(1, result.Matched);
        Assert.Equal(0, result.Added);
        Assert.Single(_db.TournamentDirectoryEntries.ToList());
    }

    /// <summary>
    /// Ohne Nummer bleibt der Namensvergleich — und der verlangt ZWEI unterscheidende Woerter.
    /// In der Slowakei ist das wichtig: die „ŠACH-MAT NITRA"-Liga steht mit dreizehn Runden unter
    /// dreizehn fast gleichlautenden Namen im Kalender.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithoutTheNumber_FallsBackToTheNameMatch()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1482875", ChessResultsId = "1482875",
            Name = "Majstrovstva SR amaterov 2026", Federation = "SVK",
            StartDate = Soon, EndDate = Soon,
        });
        await _db.SaveChangesAsync();

        var result = await CreateService($"[{Row("Majstrovstva SR amaterov 2026")}]").RunAsync();

        Assert.Equal(1, result.Matched);
        Assert.Equal(0, result.Added);
    }

    /// <summary>
    /// Kennt chess-results das Turnier schon, fuellt diese Quelle nur LUECKEN. Sonst entschiede
    /// die Reihenfolge der naechtlichen Durchgaenge, welche Angabe gilt.
    /// </summary>
    [Fact]
    public async Task RunAsync_FillsGapsButNeverOverwrites()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1482875", ChessResultsId = "1482875", Name = "Open Nitra 2026",
            Federation = "SVK", StartDate = Soon, EndDate = Soon,
            TimeControlText = "aus chess-results", Rounds = 9,
            Speed = TournamentSpeed.Unknown, System = TournamentSystem.Unknown,
        });
        await _db.SaveChangesAsync();

        await CreateService($"""
            [{Row("Open Nitra 2026", chessResultsId: "1482875", timeControl: "2x 7 min",
                  rounds: 7, type: "Blitz", system: "roundRobin")}]
            """).RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("aus chess-results", entry.TimeControlText);   // nicht ersetzt
        Assert.Equal(9, entry.Rounds);                              // nicht ersetzt
        Assert.Equal(TournamentSpeed.Blitz, entry.Speed);           // Luecke gefuellt
        Assert.Equal(TournamentSystem.RoundRobin, entry.System);    // Luecke gefuellt
    }

    /// <summary>
    /// Holt die Turniersuche das eigene Turnier ein, steht dieselbe Veranstaltung unter zwei
    /// Kennungen — die eigene wird zurueckgezogen.
    /// </summary>
    [Fact]
    public async Task RunAsync_RetiresItsOwnEntryOnceTheSearchCatchesUp()
    {
        _db.TournamentDirectoryEntries.AddRange(
            new TournamentDirectoryEntry
            {
                PublicId = "sk5909", ChessResultsId = null, Name = "Open Nitra 2026",
                Federation = "SVK", StartDate = Soon, EndDate = Soon,
            },
            new TournamentDirectoryEntry
            {
                PublicId = "1482875", ChessResultsId = "1482875", Name = "Open Nitra 2026",
                Federation = "SVK", StartDate = Soon, EndDate = Soon,
            });
        await _db.SaveChangesAsync();

        var result = await CreateService($"[{Row("Open Nitra 2026", chessResultsId: "1482875")}]")
            .RunAsync();

        Assert.Equal(1, result.Retired);
        Assert.NotNull(_db.TournamentDirectoryEntries.Single(e => e.PublicId == "sk5909").RemovedAt);
        Assert.Null(_db.TournamentDirectoryEntries.Single(e => e.PublicId == "1482875").RemovedAt);
    }

    /// <summary>
    /// Die Nummer wird oft NACHGEREICHT — der Veranstalter traegt die Swiss-Manager-Adresse erst
    /// ein, wenn die Datei hochgeladen ist. Am eigenen Eintrag muss sie dann ankommen: an ihr
    /// haengen Rundenplan, Abo und der Verweis dorthin.
    /// </summary>
    [Fact]
    public async Task RunAsync_AddsTheChessResultsNumberToItsOwnEntryWhenItAppearsLater()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "sk5909", ChessResultsId = null, Name = "Open Nitra 2026",
            Federation = "SVK", StartDate = Soon, EndDate = Soon,
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("Open Nitra 2026", chessResultsId: "1482875")}]").RunAsync();

        Assert.Equal("1482875",
            _db.TournamentDirectoryEntries.Single(e => e.PublicId == "sk5909").ChessResultsId);
    }

    // ----- Was kein Turnier ist ---------------------------------------------

    /// <summary>
    /// Schiedsrichter-Lehrgaenge und Trainingslager stehen in derselben Liste (4 von 79) und
    /// gehoeren nicht in einen Turnierkalender.
    /// </summary>
    [Fact]
    public async Task RunAsync_DoesNotAddTrainingCourses()
    {
        var result = await CreateService(
            $"[{Row("Školenie rozhodcov 2. a 3. triedy", nonTournament: true)}]").RunAsync();

        Assert.Equal(0, result.Added);
        Assert.Empty(_db.TournamentDirectoryEntries.ToList());
    }

    /// <summary>Und ein frueher angelegter Eintrag wird zurueckgezogen, wenn er sich als Lehrgang entpuppt.</summary>
    [Fact]
    public async Task RunAsync_RetiresAnExistingEntryThatTurnsOutToBeACourse()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "sk6016", Name = "Skolenie", Federation = "SVK",
            StartDate = Soon, EndDate = Soon,
        });
        await _db.SaveChangesAsync();

        var result = await CreateService($"[{Row("Skolenie", eventId: "6016", nonTournament: true)}]")
            .RunAsync();

        Assert.Equal(1, result.Retired);
        Assert.NotNull(_db.TournamentDirectoryEntries.Single().RemovedAt);
    }

    // ----- Verortung ---------------------------------------------------------

    /// <summary>
    /// Der Grund, warum sich der Abruf je Turnier lohnt: die Anschrift traegt die Postleitzahl,
    /// und die ist der genaueste Weg des Geocoders. Die Liste allein kennt nur den Ort.
    /// </summary>
    [Fact]
    public async Task RunAsync_GeocodesFromThePostalCodeInTheAddress()
    {
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "SK", PostalCode = "824 12", Name = "Bratislava",
            NameNormalized = "bratislava", Lat = 48.1486, Lon = 17.1077,
            Kind = GeoPlaceKind.PostalCode,
        });
        await _db.SaveChangesAsync();

        await CreateService($"""
            [{Row("Bratislava Norm Week", city: "Bratislava - mestská časť Ružinov",
                  address: "Slovnaft Business Center, Vlčie hrdlo 1/A, 824 12 Bratislava")}]
            """).RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(GeoSource.PostalCode, entry.GeoSource);
        Assert.Equal(48.1486, entry.Lat!.Value, 3);
    }

    /// <summary>
    /// Ein ONLINE-Turnier hat keinen Spielort. Ihm einen Pin zu geben waere eine Behauptung ueber
    /// die Wirklichkeit — dieselbe Regel wie bei den ONL-Ereignissen des FIDE-Kalenders.
    /// </summary>
    [Fact]
    public async Task RunAsync_OnlineTournament_GetsNoCoordinates()
    {
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "SK", Name = "Bratislava", NameNormalized = "bratislava",
            Lat = 48.1486, Lon = 17.1077, Kind = GeoPlaceKind.City, Population = 400_000,
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("Online turnaj", city: "Bratislava", type: "Online")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Null(entry.Lat);
        Assert.Equal(TournamentSpeed.Unknown, entry.Speed);
    }

    /// <summary>
    /// Der Stadtteil faellt weg: im Lexikon stehen Gemeinden. Stehen bliebe er, suchte die
    /// Verortung nach „Rača" und faende nichts oder einen gleichnamigen Ort anderswo.
    /// </summary>
    [Theory]
    [InlineData("Bratislava - mestská časť Rača", "Bratislava")]
    [InlineData("Košice - mestská časť Staré mesto", "Košice")]
    [InlineData("Nitra", "Nitra")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void CityOf_DropsTheBorough(string? city, string? expected) =>
        Assert.Equal(expected, ChessSkDirectorySweepService.CityOf(city));

    /// <summary>
    /// Fehlt der Ort in der Anschrift, wird er HINTEN angehaengt: bei einem Text mit Ziffer gilt
    /// der LETZTE Ortstreffer als der eine Spielort.
    /// </summary>
    [Theory]
    [InlineData("Vlčie hrdlo 1/A, 824 12 Bratislava", "Bratislava - mestská časť Rača",
        "Vlčie hrdlo 1/A, 824 12 Bratislava")]
    [InlineData("Mlynská 27", "Košice", "Mlynská 27, Košice")]
    [InlineData(null, "Nitra", "Nitra")]
    [InlineData("Kultúrny dom Turie", null, "Kultúrny dom Turie")]
    public void LocationOf_PutsTheTownAtTheEndWhenTheAddressLacksIt(
        string? address, string? city, string? expected) =>
        Assert.Equal(expected, ChessSkDirectorySweepService.LocationOf(
            new ChessSkDirectorySweepService.CrawlerChessSkEvent(
                "1", "n", Soon, Soon, city, address, "SVK", null, null, null, null, null, null, false)));

    // ----- Bedenkzeit-Klasse -------------------------------------------------

    /// <summary>
    /// Die Quelle nennt die Klasse beim Namen — das ist eine Angabe, keine Rechnung. Nur wo sie
    /// fehlt (16 von 79 stehen auf „Nie je nastavené"), wird sie aus dem Text erschlossen.
    /// </summary>
    [Theory]
    [InlineData("Standard", null, TournamentSpeed.Standard)]
    [InlineData("Rapid", null, TournamentSpeed.Rapid)]
    [InlineData("Blitz", null, TournamentSpeed.Blitz)]
    // „Online" ist eine Aussage ueber den ORT, keine Bedenkzeit — die Klasse kommt dann aus dem Text.
    [InlineData("Online", "2x 15 min", TournamentSpeed.Rapid)]
    [InlineData(null, "2x 15 min + 5 sek", TournamentSpeed.Rapid)]
    [InlineData("Nie je nastavené", null, TournamentSpeed.Unknown)]
    public void SpeedOf_PrefersTheSourcesOwnAnswer(string? type, string? timeControl,
        TournamentSpeed expected) =>
        Assert.Equal(expected, ChessSkDirectorySweepService.SpeedOf(
            new ChessSkDirectorySweepService.CrawlerChessSkEvent(
                "1", "n", Soon, Soon, null, null, "SVK", null, null, timeControl, null, null,
                type, false)));

    // ----- Herkunft und Ausfaelle -------------------------------------------

    [Fact]
    public async Task RunAsync_NotesWhereTheEntryCameFrom()
    {
        await CreateService($"[{Row("Open Nitra 2026")}]").RunAsync();

        var source = Assert.Single(_db.TournamentDirectorySources.ToList());
        Assert.Equal(DirectorySourceKind.SlovakChessFederation, source.Kind);
        Assert.Equal("5909", source.ExternalId);
        Assert.Equal("https://chess.sk/x", source.Url);
    }

    /// <summary>Ein Ausfall des Crawlers darf nicht still zu „keine Turniere" werden.</summary>
    [Fact]
    public async Task RunAsync_CrawlerFailure_Throws()
    {
        await Assert.ThrowsAsync<RookHub.Api.Exceptions.CrawlerRequestException>(
            () => CreateService("kaputt", HttpStatusCode.BadGateway).RunAsync());
    }

    /// <summary>
    /// Der teure Teil ist abschaltbar — und dann darf der Crawler die Detailseiten gar nicht erst
    /// holen. Ohne den Parameter waere „ohne Details" eine Angabe ohne Wirkung.
    /// </summary>
    [Fact]
    public async Task RunAsync_PassesTheDetailsSwitchToTheCrawler()
    {
        await CreateService("[]").RunAsync(details: false);

        Assert.Contains("details=false", _handler.LastUrl);
    }

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://crawler:8080") };
    }

    private sealed class StubHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        public string LastUrl { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            LastUrl = r.RequestUri?.ToString() ?? "";
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
