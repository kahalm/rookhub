using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Kalender von Chess Scotland: 43-44 kuenftige Turniere in EINEM Abruf, gegen 8 auf
/// chess-results fuer SCO. Die Bedenkzeit-KLASSE steht als Schlagwort strukturiert dabei — der
/// Sonderwert dieser Quelle. Ein Spielort dagegen nur mit Vorbehalt: die Detailseite (Freitext)
/// nennt bei 8 von 43 gemessenen Terminen eine britische Postleitzahl, sonst keinen Ort.
/// </summary>
public class ChessScotlandDirectorySweepServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public ChessScotlandDirectorySweepServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private RouteHandler _routes = new();

    private ChessScotlandDirectorySweepService CreateService(
        string listJson, string? detailJson = null, int batchSize = 50)
    {
        _routes = new RouteHandler(listJson, detailJson);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TournamentDirectory:ChessScotlandDetailBatchSize"] = batchSize.ToString(),
        }).Build();

        return new ChessScotlandDirectorySweepService(_db, new StubClientFactory(_routes),
            new GeocodingService(_db), config, new TestLogger<ChessScotlandDirectorySweepService>());
    }

    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    private static string Row(string name, string slug = "ayr-congress-2026",
        string categories = "\"Adult\",\"Junior\"", string timeControls = "\"Standard\",\"Blitz\",\"Fide\"",
        DateOnly? end = null) =>
        $$"""
          {"slug":"{{slug}}","name":"{{name}}",
           "startDate":"{{Soon:yyyy-MM-dd}}","endDate":"{{(end ?? Soon):yyyy-MM-dd}}",
           "url":"https://www.chessscotland.com/calendar/{{slug}}",
           "categories":[{{categories}}],"timeControls":[{{timeControls}}]}
          """;

    private static string Detail(string? venue) => $$"""{"venue":{{Json(venue)}}}""";

    private static string Json(string? value) => value is null ? "null" : $"\"{value}\"";

    private async Task SeedPlaceAsync(string name, string normalized, double lat, double lon)
    {
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "GB", Name = name, NameNormalized = normalized,
            Lat = lat, Lon = lon, Kind = GeoPlaceKind.City, Population = 46_000,
        });
        await _db.SaveChangesAsync();
    }

    // ----- Liste + Detail ----------------------------------------------------

    [Fact]
    public async Task RunAsync_UnknownTournament_IsAddedWithItsTimeControlAndSpeed()
    {
        var result = await CreateService($"[{Row("Ayr Congress 2026")}]", Detail(null)).RunAsync();

        Assert.Equal(1, result.Added);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("SCO", entry.Federation);
        Assert.Equal(Soon, entry.StartDate);
        Assert.Equal("Standard, Blitz, Fide", entry.TimeControlText);
        Assert.Equal(TournamentSpeed.Standard, entry.Speed);
    }

    /// <summary>Der haeufigere Fall (35 von 43 gemessen): die Seite ist lesbar, nennt aber keinen Ort.</summary>
    [Fact]
    public async Task RunAsync_DetailPageHasNoAddress_StillMarksItAsRead()
    {
        var result = await CreateService($"[{Row("Ayr Congress 2026")}]", Detail(null)).RunAsync();

        Assert.Equal(1, result.Updated);   // Updated zaehlt die GELESENEN Detailseiten, nicht die gefundenen Orte
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Null(entry.LocationText);
        Assert.Null(entry.Lat);
        Assert.NotNull(Assert.Single(_db.TournamentDirectorySources.ToList()).Url);
    }

    /// <summary>Die Liste allein traegt Name, Termin, Kategorien und Bedenkzeit — faellt die Detailseite ganz aus, bleibt das erhalten.</summary>
    [Fact]
    public async Task RunAsync_DetailPageUnreachable_StillKeepsTheDateAndTimeControl()
    {
        var result = await CreateService($"[{Row("Ayr Congress 2026")}]", detailJson: null).RunAsync();

        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Updated);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("Ayr Congress 2026", entry.Name);
        Assert.Equal("Standard, Blitz, Fide", entry.TimeControlText);
        Assert.Null(entry.LocationText);
        Assert.Null(Assert.Single(_db.TournamentDirectorySources.ToList()).Url);
    }

    /// <summary>Der Detailabruf lohnt nur EINMAL je Turnier — „schon geholt" steht im Herkunftsvermerk.</summary>
    [Fact]
    public async Task RunAsync_SecondSweep_DoesNotFetchTheDetailPageAgain()
    {
        var list = $"[{Row("Ayr Congress 2026")}]";
        await CreateService(list, Detail(null)).RunAsync();
        Assert.Equal(1, _routes.DetailCalls);

        var service = CreateService(list, Detail(null));
        var result = await service.RunAsync();

        Assert.Equal(0, _routes.DetailCalls);
        Assert.Equal(0, result.Added);
        Assert.Single(_db.TournamentDirectoryEntries.ToList());
    }

    [Fact]
    public async Task RunAsync_DetailBudgetSpent_StillAddsTheRemainingEvents()
    {
        var list = $"[{Row("Erstes", "erstes-2026")},{Row("Zweites", "zweites-2026")}]";

        var result = await CreateService(list, Detail(null), batchSize: 1).RunAsync();

        Assert.Equal(2, result.Added);
        Assert.Equal(1, _routes.DetailCalls);
        Assert.Equal(2, _db.TournamentDirectoryEntries.Count());
    }

    /// <summary>Ein Online-Turnier hat keinen Spielort — der Detailabruf wird gar nicht erst versucht.</summary>
    [Fact]
    public async Task RunAsync_OnlineTournament_SkipsTheDetailFetchEntirely()
    {
        await CreateService(
            $"[{Row("Main 4NCL Online S14", "main-4ncl-online-s14", categories: "\"Online\"", timeControls: "")}]",
            Detail("should never be fetched")).RunAsync();

        Assert.Equal(0, _routes.DetailCalls);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Null(entry.LocationText);
        Assert.Null(entry.TimeControlText);
    }

    // ----- Der Ort -------------------------------------------------------------

    /// <summary>
    /// Ohne Postleitzahl-Bestand fuer Grossbritannien im Ortslexikon traegt die vom Crawler
    /// gelieferte Zeile trotzdem meist einen echten Ortsnamen VOR der Postleitzahl — die normale
    /// Aufloesung ueber den Staedtenamen greift also.
    /// </summary>
    [Fact]
    public async Task RunAsync_VenueLineWithACityName_ResolvesViaTheGazetteer()
    {
        await SeedPlaceAsync("Ayr", "ayr", 55.4586, -4.6292);

        await CreateService($"[{Row("Ayr Congress 2026")}]",
            Detail("Dalblair Road, Ayr. KA7 1UG")).RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("Dalblair Road, Ayr. KA7 1UG", entry.LocationText);
        Assert.Equal(GeoSource.City, entry.GeoSource);
        Assert.Equal(55.4586, entry.Lat!.Value, 3);
    }

    [Fact]
    public async Task RunAsync_LeavesAManualCoordinateAlone()
    {
        await SeedPlaceAsync("Ayr", "ayr", 55.4586, -4.6292);
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = ChessScotlandDirectorySweepService.PublicIdOf("ayr-congress-2026"),
            Name = "Ayr Congress 2026", Federation = "SCO", StartDate = Soon, EndDate = Soon,
            Lat = 1.0, Lon = 2.0, GeoSource = GeoSource.Manual,
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("Ayr Congress 2026")}]",
            Detail("Dalblair Road, Ayr. KA7 1UG")).RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(GeoSource.Manual, entry.GeoSource);
        Assert.Equal(1.0, entry.Lat!.Value, 4);
    }

    // ----- Bedenkzeit-Klasse -----------------------------------------------------

    [Theory]
    [InlineData(new[] { "Standard" }, TournamentSpeed.Standard)]
    [InlineData(new[] { "Standard", "Blitz", "Fide" }, TournamentSpeed.Standard)]
    [InlineData(new[] { "Allegro" }, TournamentSpeed.Rapid)]
    [InlineData(new[] { "Allegro", "Fide" }, TournamentSpeed.Rapid)]
    [InlineData(new[] { "Blitz" }, TournamentSpeed.Blitz)]
    [InlineData(new[] { "Fide" }, TournamentSpeed.Unknown)]
    [InlineData(new string[0], TournamentSpeed.Unknown)]
    public void SpeedOf_TheMostSeriousClassWins(string[] timeControls, TournamentSpeed expected) =>
        Assert.Equal(expected, ChessScotlandDirectorySweepService.SpeedOf(timeControls));

    [Fact]
    public void TimeControlTextOf_JoinsTheRawValues_OrNullWhenEmpty()
    {
        Assert.Equal("Standard, Blitz, Fide",
            ChessScotlandDirectorySweepService.TimeControlTextOf(["Standard", "Blitz", "Fide"]));
        Assert.Null(ChessScotlandDirectorySweepService.TimeControlTextOf([]));
    }

    // ----- Publikum: Junior/Adult -------------------------------------------------

    /// <summary>25 von 43 gemessenen Terminen tragen „Adult" UND „Junior" zugleich — kein Jugendturnier.</summary>
    [Fact]
    public async Task RunAsync_AdultAndJuniorTogether_IsNotMarkedAsYouth()
    {
        await CreateService($"[{Row("Ayr Congress 2026", categories: "\"Adult\",\"Junior\"")}]",
            Detail(null)).RunAsync();

        Assert.Equal(TournamentAgeGroups.None,
            Assert.Single(_db.TournamentDirectoryEntries.ToList()).AgeGroups);
    }

    /// <summary>Nur „Junior" ohne „Adult" (16 von 43 gemessen) ist die verlaessliche Jugend-Angabe.</summary>
    [Fact]
    public async Task RunAsync_JuniorAlone_MarksTheAudienceAsYouth()
    {
        await CreateService(
            $"[{Row("NEJCA Albyn Trophy 2026", categories: "\"Junior\"", timeControls: "\"Allegro\"")}]",
            Detail(null)).RunAsync();

        Assert.Equal(TournamentAgeGroups.YouthUnspecified,
            Assert.Single(_db.TournamentDirectoryEntries.ToList()).AgeGroups);
    }

    /// <summary>Sagt der NAME bereits eine Klasse, ist sie die bessere Auskunft und bleibt unangetastet.</summary>
    [Fact]
    public async Task RunAsync_ANamedClassBeatsTheJuniorTag()
    {
        await CreateService(
            $"[{Row("Scottish U14 Championship", categories: "\"Junior\"")}]", Detail(null)).RunAsync();

        Assert.Equal(TournamentAgeGroups.U14,
            Assert.Single(_db.TournamentDirectoryEntries.ToList()).AgeGroups);
    }

    // ----- Kennung und Abgleich ----------------------------------------------

    [Fact]
    public void PublicIdOf_FitsTheColumnAndKeepsSimilarSlugsApart()
    {
        var a = ChessScotlandDirectorySweepService.PublicIdOf(
            "murrayfield-stadium-blitz-edinburgh-uk-blitz-qualifier");
        var b = ChessScotlandDirectorySweepService.PublicIdOf("ayr-congress-2026");

        Assert.NotEqual(a, b);
        Assert.True(a.Length <= 24, a);
        Assert.StartsWith("sc", a);
        Assert.Equal(a, ChessScotlandDirectorySweepService.PublicIdOf(
            "MURRAYFIELD-STADIUM-BLITZ-EDINBURGH-UK-BLITZ-QUALIFIER"));
    }

    [Fact]
    public async Task RunAsync_TournamentSearchCaughtUp_RetiresTheOwnEntry()
    {
        await CreateService($"[{Row("Ayr Congress 2026")}]", Detail(null)).RunAsync();
        var own = Assert.Single(_db.TournamentDirectoryEntries.ToList());

        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1477777", ChessResultsId = "1477777",
            Name = "Ayr Congress 2026", Federation = "SCO",
            StartDate = Soon, EndDate = Soon, FirstSeenAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var result = await CreateService($"[{Row("Ayr Congress 2026")}]", Detail(null)).RunAsync();

        Assert.Equal(1, result.Matched);
        Assert.Equal(1, result.Retired);
        Assert.NotNull(_db.TournamentDirectoryEntries.Single(e => e.Id == own.Id).RemovedAt);
    }

    [Fact]
    public async Task RunAsync_NotesWhereTheEntryCameFrom()
    {
        await CreateService($"[{Row("Ayr Congress 2026")}]", Detail(null)).RunAsync();

        var source = Assert.Single(_db.TournamentDirectorySources.ToList());
        Assert.Equal(DirectorySourceKind.ScottishChessFederation, source.Kind);
        // Seit 2026-09-15 der Kurzschluessel, nicht mehr der rohe Slug: ein Slug mit 71 Zeichen
        // sprengte die 60-Zeichen-Spalte und brach die ganze Quelle ab.
        Assert.Equal(ChessScotlandDirectorySweepService.PublicIdOf("ayr-congress-2026"), source.ExternalId);
        Assert.Equal("https://www.chessscotland.com/calendar/ayr-congress-2026", source.Url);
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

    /// <summary>Zwei Routen, ein Handler: die Liste und die Detailseite.</summary>
    /// <summary>
    /// Vom 2026-09-10 bis 2026-09-15 brach die GANZE Quelle jede Nacht ab: ein Slug mit 71 Zeichen,
    /// die Vermerk-Spalte haelt 60, und die gemeinsame Vermerk-Logik lehnt eine zu lange Kennung
    /// bewusst ab statt sie abzuschneiden. Vermerkt wird deshalb der Kurzschluessel.
    /// </summary>
    [Fact]
    public async Task RunAsync_SlugLongerThanTheColumn_DoesNotAbortTheSource()
    {
        const string slug = "the-scottish-chess-association-annual-congress-and-open-rapidplay-2027";
        Assert.True(slug.Length > ExternalDirectorySource.MaxExternalIdLength, slug.Length.ToString());

        await CreateService($"[{Row("Scottish Annual Congress", slug: slug)}]", Detail(null)).RunAsync();

        var note = Assert.Single(_db.TournamentDirectorySources.ToList());
        Assert.Equal(ChessScotlandDirectorySweepService.PublicIdOf(slug), note.ExternalId);
        Assert.True(note.ExternalId.Length <= ExternalDirectorySource.MaxExternalIdLength);
    }

    /// <summary>
    /// Der Altbestand trug den ROHEN Slug (Dev 43, Prod 44 Vermerke). Er muss UMGESTELLT werden —
    /// ein zweiter Vermerk derselben Quelle am selben Eintrag waere eine Dublette.
    /// </summary>
    [Fact]
    public async Task RunAsync_LegacyNoteWithTheRawSlug_IsRekeyedInsteadOfDoubled()
    {
        await CreateService($"[{Row("Ayr Congress 2026")}]", Detail(null)).RunAsync();
        _db.TournamentDirectorySources.Single().ExternalId = "ayr-congress-2026";
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("Ayr Congress 2026")}]", Detail(null)).RunAsync();

        var note = Assert.Single(_db.TournamentDirectorySources.ToList());
        Assert.Equal(ChessScotlandDirectorySweepService.PublicIdOf("ayr-congress-2026"), note.ExternalId);
    }

    /// <summary>
    /// Die gefaehrlichere Haelfte der Altlast: ein Turnier, das VOR der Umstellung schon einem
    /// chess-results-Eintrag zugeordnet war, traegt dort den rohen Vermerk. Ohne Umstellung saehe
    /// HasOtherNoteOfSameKind einen „anderen" Vermerk derselben Quelle, verweigerte die Zuordnung —
    /// und die Quelle legte das Turnier ein zweites Mal als eigenen Eintrag an.
    /// </summary>
    [Fact]
    public async Task RunAsync_AlreadyMatchedWithALegacyNote_StaysMatched()
    {
        var cr = new TournamentDirectoryEntry
        {
            PublicId = "1400001", ChessResultsId = "1400001", Federation = "SCO",
            Name = "Ayr Congress 2026", StartDate = Soon, EndDate = Soon, FirstSeenAt = DateTime.UtcNow,
        };
        cr.Sources.Add(new TournamentDirectorySource
        {
            Kind = DirectorySourceKind.ScottishChessFederation, ExternalId = "ayr-congress-2026",
            FirstSeenAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow,
        });
        _db.TournamentDirectoryEntries.Add(cr);
        await _db.SaveChangesAsync();

        var result = await CreateService($"[{Row("Ayr Congress 2026")}]", Detail(null)).RunAsync();

        Assert.Equal(0, result.Added);
        Assert.Single(_db.TournamentDirectoryEntries.ToList());
        var note = Assert.Single(_db.TournamentDirectorySources.ToList());
        Assert.Equal(ChessScotlandDirectorySweepService.PublicIdOf("ayr-congress-2026"), note.ExternalId);
    }

    [Theory]
    [InlineData("sc0123456789ab", true)]
    [InlineData("ayr-congress-2026", false)]
    [InlineData("scottish-championship", false)]
    public void IsShortKey_TellsTheHashFromARealSlug(string externalId, bool expected) =>
        Assert.Equal(expected, ChessScotlandDirectorySweepService.IsShortKey(externalId));

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
