using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Terminkalender des niederlaendischen Verbands (KNSB): 177 kuenftige Eintraege gegen 12 auf
/// chess-results — aber ohne Enddatum, Ort, Rundenzahl oder Teilnehmerzahl. Der eine Lichtblick ist
/// die Bedenkzeit-Klasse, die strukturiert aus der Liste kommt.
/// </summary>
public class KnsbDirectorySweepServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public KnsbDirectorySweepServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private KnsbDirectorySweepService CreateService(string json,
        HttpStatusCode status = HttpStatusCode.OK) =>
        new(_db, new StubClientFactory(new StubHandler(json, status)),
            new TestLogger<KnsbDirectorySweepService>());

    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    private const string LongSlug =
        "knsb-d-competitie-4e-ronde-tog-maasstad-87-voor-doven-en-slechthorende-2027-04-10";

    private static string Row(string name, string slug = "zomeravondcompetitie-2027-07-19",
        string? speed = "Rapidschaak", bool online = false, string? url = null,
        DateOnly? start = null) =>
        $$"""
          {"slug":"{{slug}}","name":"{{name}}",
           "startDate":"{{(start ?? Soon):yyyy-MM-dd}}",
           "url":{{Json(url ?? $"https://schaakbond.nl/event/{slug}/")}},
           "speed":{{Json(speed)}},"online":{{(online ? "true" : "false")}}}
          """;

    private static string Json(string? v) => v is null ? "null" : $"\"{v}\"";

    // ----- Anlegen -----------------------------------------------------------

    [Fact]
    public async Task RunAsync_UnknownTournament_IsAdded()
    {
        var result = await CreateService($"[{Row("Zomeravondcompetitie")}]").RunAsync();

        Assert.Equal(1, result.Added);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.StartsWith("nl", entry.PublicId);
        Assert.True(entry.PublicId.Length <= 24);
        Assert.Equal("NED", entry.Federation);
        Assert.Equal(Soon, entry.StartDate);
        Assert.Null(entry.ChessResultsId);
    }

    /// <summary>Diese Quelle hat kein Enddatum-Feld — Start=Ende ist der Fallback fuer eine unbekannte Dauer.</summary>
    [Fact]
    public async Task RunAsync_HasNoEndDate_SoEndEqualsStart()
    {
        await CreateService($"[{Row("Zomeravondcompetitie")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(entry.StartDate, entry.EndDate);
    }

    /// <summary>
    /// Zwei verschiedene Slugs muessen verschiedene Kennungen ergeben — sonst wuerden zwei
    /// Turniere zu einem gemacht, der teuerste Fehler dieser Stelle.
    /// </summary>
    [Fact]
    public async Task RunAsync_TwoDifferentTournaments_GetDifferentPublicIds()
    {
        var row1 = Row("Zomeravondcompetitie", slug: "zomeravondcompetitie-2027-07-19");
        var row2 = Row("Zomeravondcompetitie", slug: "zomeravondcompetitie-2027-07-26",
            start: Soon.AddDays(7));
        await CreateService($"[{row1},{row2}]").RunAsync();

        var entries = _db.TournamentDirectoryEntries.ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal(2, entries.Select(e => e.PublicId).Distinct().Count());
    }

    /// <summary>
    /// Der Slug wird bis zu 81 Zeichen lang (gemessen: 5 von 177 ueber den 60 Zeichen von
    /// <c>ExternalId</c>) — muss trotzdem eine gueltige, kurze PublicId ergeben.
    /// </summary>
    [Fact]
    public async Task RunAsync_ALongSlug_StillFitsThePublicIdColumn()
    {
        await CreateService(
            $"[{Row("KNSB D-Competitie 4e ronde TOG - Maasstad 87", slug: LongSlug)}]")
            .RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.True(entry.PublicId.Length <= 24);

        var source = Assert.Single(_db.TournamentDirectorySources.ToList());
        Assert.True(source.ExternalId.Length <= 60);
    }

    // ----- Bedenkzeit ----------------------------------------------------------

    [Theory]
    [InlineData("Normaalschaak", TournamentSpeed.Standard)]
    [InlineData("Rapidschaak", TournamentSpeed.Rapid)]
    [InlineData("Snelschaak", TournamentSpeed.Blitz)]
    [InlineData(null, TournamentSpeed.Unknown)]
    [InlineData("etwas-unbekanntes", TournamentSpeed.Unknown)]
    public void SpeedOf_MapsTheDutchTaxonomyName(string? speed, TournamentSpeed expected) =>
        Assert.Equal(expected, KnsbDirectorySweepService.SpeedOf(speed));

    /// <summary>
    /// Die "speed"-Taxonomie ist der eine Lichtblick dieser Quelle: sie steht STRUKTURIERT in der
    /// Liste und muss nicht wie sonst ueberall aus Freitext geraten werden.
    /// </summary>
    [Fact]
    public async Task RunAsync_TakesTheSpeedDirectlyFromTheSource()
    {
        var result = await CreateService(
            $"[{Row("Zomeravond Snelschaaktoernooi", speed: "Snelschaak")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(TournamentSpeed.Blitz, entry.Speed);
        Assert.Equal(1, result.Updated); // Updated zaehlt die neu bekannt gewordene Bedenkzeit
    }

    /// <summary>Eine fehlende Bedenkzeit-Angabe darf eine schon bekannte NICHT auf Unknown zuruecksetzen.</summary>
    [Fact]
    public async Task RunAsync_DoesNotDowngradeAnAlreadyKnownSpeedToUnknown()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = KnsbDirectorySweepService.PublicIdOf("zomeravondcompetitie-2027-07-19"),
            Name = "Zomeravondcompetitie", Federation = "NED",
            StartDate = Soon, EndDate = Soon, Speed = TournamentSpeed.Rapid,
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("Zomeravondcompetitie", speed: null)}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal(TournamentSpeed.Rapid, entry.Speed);
    }

    // ----- Kein Ort --------------------------------------------------------

    /// <summary>
    /// Diese Quelle kennt nie einen Spielort. Ein bereits gesetzter (von Hand oder aus einer
    /// frueheren Quelle) darf deshalb nicht geloescht werden.
    /// </summary>
    [Fact]
    public async Task RunAsync_NeverTouchesAnExistingLocation()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = KnsbDirectorySweepService.PublicIdOf("zomeravondcompetitie-2027-07-19"),
            Name = "Zomeravondcompetitie", Federation = "NED",
            StartDate = Soon, EndDate = Soon, LocationText = "Utrecht", GeoSource = GeoSource.Manual,
        });
        await _db.SaveChangesAsync();

        await CreateService($"[{Row("Zomeravondcompetitie")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("Utrecht", entry.LocationText);
        Assert.Equal(GeoSource.Manual, entry.GeoSource);
    }

    [Fact]
    public async Task RunAsync_ANewTournament_HasNoLocationAtAll()
    {
        await CreateService($"[{Row("Zomeravondcompetitie")}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Null(entry.LocationText);
        Assert.Null(entry.Lat);
        Assert.Equal(GeoSource.None, entry.GeoSource);
    }

    // ----- Zuordnung und Herkunft -------------------------------------------

    [Fact]
    public async Task RunAsync_RetiresItsOwnEntryOnceTheSearchCatchesUp()
    {
        _db.TournamentDirectoryEntries.AddRange(
            new TournamentDirectoryEntry
            {
                PublicId = KnsbDirectorySweepService.PublicIdOf("rotterdams-schaakfestival-2027-07-19"),
                Name = "Rotterdams Schaakfestival", Federation = "NED",
                StartDate = Soon, EndDate = Soon,
            },
            new TournamentDirectoryEntry
            {
                PublicId = "123456", ChessResultsId = "123456",
                Name = "Rotterdams Schaakfestival",
                Federation = "NED", StartDate = Soon, EndDate = Soon,
            });
        await _db.SaveChangesAsync();

        var result = await CreateService(
            $"[{Row("Rotterdams Schaakfestival", slug: "rotterdams-schaakfestival-2027-07-19")}]")
            .RunAsync();

        Assert.Equal(1, result.Matched);
        Assert.Equal(1, result.Retired);
        Assert.NotNull(_db.TournamentDirectoryEntries.Single(e => e.PublicId ==
            KnsbDirectorySweepService.PublicIdOf("rotterdams-schaakfestival-2027-07-19")).RemovedAt);
    }

    [Fact]
    public async Task RunAsync_NotesWhereTheEntryCameFrom()
    {
        await CreateService($"[{Row("Zomeravondcompetitie")}]").RunAsync();

        var source = Assert.Single(_db.TournamentDirectorySources.ToList());
        Assert.Equal(DirectorySourceKind.DutchChessFederation, source.Kind);
        Assert.True(source.ExternalId.Length <= 60);
        Assert.Equal("https://schaakbond.nl/event/zomeravondcompetitie-2027-07-19/", source.Url);
    }

    [Fact]
    public async Task RunAsync_CrawlerFailure_Throws() =>
        await Assert.ThrowsAsync<RookHub.Api.Exceptions.CrawlerRequestException>(
            () => CreateService("kaputt", HttpStatusCode.BadGateway).RunAsync());

    // ----- PublicId ------------------------------------------------------------

    [Fact]
    public void PublicIdOf_IsDeterministicAndPrefixed()
    {
        var a = KnsbDirectorySweepService.PublicIdOf("zomeravondcompetitie-2027-07-19");
        var b = KnsbDirectorySweepService.PublicIdOf("zomeravondcompetitie-2027-07-19");
        var c = KnsbDirectorySweepService.PublicIdOf("zomeravondcompetitie-2027-07-26");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.StartsWith("nl", a);
        Assert.True(a.Length <= 24);
    }

    [Fact]
    public void PublicIdOf_FitsEvenForTheLongestMeasuredSlug()
    {
        var id = KnsbDirectorySweepService.PublicIdOf(LongSlug);

        Assert.True(id.Length <= 24, $"PublicId zu lang: {id.Length} Zeichen");
    }

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
