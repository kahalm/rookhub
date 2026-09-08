using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Terminkalender des tschechischen Verbands (chess.cz). Klein im Volumen — sein Wert sind die
/// LIGARUNDEN: Spieltermine, fuer die sonst je Turnier eine eigene chess-results-Seite geholt
/// wird.
/// </summary>
public class ChessCzDirectorySweepServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public ChessCzDirectorySweepServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private ChessCzDirectorySweepService CreateService(string json,
        HttpStatusCode status = HttpStatusCode.OK) =>
        new(_db, new StubClientFactory(new StubHandler(json, status)),
            new GeocodingService(_db), new TestLogger<ChessCzDirectorySweepService>());

    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    private static string Row(string name, string eventId, DateOnly date, string? place = "Turnov",
        string? chessResultsId = null, string? country = "CZ", bool youth = false,
        bool nonTournament = false, int? round = null, string? series = null, DateOnly? end = null) =>
        $$"""
          {"eventId":"{{eventId}}","name":"{{name}}",
           "startDate":"{{date:yyyy-MM-dd}}","endDate":"{{(end ?? date):yyyy-MM-dd}}",
           "place":{{Json(place)}},"country":{{Json(country)}},
           "chessResultsId":{{Json(chessResultsId)}},
           "youth":{{Lower(youth)}},"nonTournament":{{Lower(nonTournament)}},
           "roundNumber":{{(round is null ? "null" : round.ToString())}},
           "seriesName":{{Json(series)}}}
          """;

    private static string Json(string? value) => value is null ? "null" : $"\"{value}\"";
    private static string Lower(bool value) => value ? "true" : "false";

    private static string Rounds(int count, string? chessResultsId = "1464172",
        string series = "šachy.cz Extraliga") =>
        string.Join(",", Enumerable.Range(1, count).Select(n =>
            Row($"{series} – {n}. kolo", $"extraliga-{n}-kolo", Soon.AddDays(7 * n),
                place: null, chessResultsId: chessResultsId, round: n, series: series)));

    // ----- Einzeltermine ----------------------------------------------------

    [Fact]
    public async Task RunAsync_UnknownTournament_IsAdded()
    {
        var result = await CreateService($"[{Row("Turnovský Granát", "turnovsky-granat-5", Soon)}]")
            .RunAsync();

        Assert.Equal(1, result.Added);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("CZE", entry.Federation);
        Assert.Equal("Turnov", entry.LocationText);
        Assert.Equal(Soon, entry.StartDate);
    }

    /// <summary>
    /// Die Kennung dieser Quelle ist ein Wort von bis zu 57 Zeichen, die Spalte fasst 24. Gekuerzt
    /// wird NICHT: „1-ligy-1-kolo" und „1-ligy-10-kolo" unterscheiden sich am ENDE.
    /// </summary>
    [Fact]
    public void PublicIdOf_FitsTheColumnAndKeepsLongSlugsApart()
    {
        var a = ChessCzDirectorySweepService.PublicIdOf(
            "mistrovstvi-cr-junioru-a-dorostencu-v-rapid-sachu-2026");
        var b = ChessCzDirectorySweepService.PublicIdOf(
            "mistrovstvi-cr-junioru-a-dorostencu-v-rapid-sachu-2027");

        Assert.NotEqual(a, b);
        Assert.True(a.Length <= 24, a);
        Assert.StartsWith("cz", a);
        // Derselbe Slug ergibt IMMER dieselbe Kennung — sonst gaebe es jede Nacht ein Duplikat.
        Assert.Equal(a, ChessCzDirectorySweepService.PublicIdOf(
            "mistrovstvi-cr-junioru-a-dorostencu-v-rapid-sachu-2026"));
    }

    /// <summary>Jeder fuenfte Eintrag ist eine Schulung oder ein Trainingslager.</summary>
    [Fact]
    public async Task RunAsync_DoesNotAddTrainingCourses()
    {
        var result = await CreateService(
            $"[{Row("Školení a seminář rozhodčích R1", "skoleni-r1", Soon, nonTournament: true)}]")
            .RunAsync();

        Assert.Equal(0, result.Added);
        Assert.Empty(_db.TournamentDirectoryEntries.ToList());
    }

    /// <summary>Die Zeile fuehrt ihr Land als Faehnchen — auch ein auslaendisches.</summary>
    [Theory]
    [InlineData("CZ", "CZE")]
    [InlineData("SK", "SVK")]
    [InlineData(null, "CZE")]
    [InlineData("XX", "CZE")]
    public async Task RunAsync_TakesTheFederationFromTheFlag(string? iso2, string expected)
    {
        await CreateService($"[{Row("Turnaj", "turnaj", Soon, country: iso2)}]").RunAsync();

        Assert.Equal(expected, Assert.Single(_db.TournamentDirectoryEntries.ToList()).Federation);
    }

    /// <summary>
    /// Die Jugend-Lasche traegt einen Fall, den kein Namensmuster faengt: „Mistrovství Čech
    /// 8 – 10 let" nennt seine Altersklasse als Spanne, ohne das Wort „mládež".
    /// </summary>
    [Fact]
    public async Task RunAsync_YouthTab_MarksWhatTheNameDoesNotSay()
    {
        await CreateService($"[{Row("Mistrovství Čech 8 - 10 let", "mcech-8-10", Soon, youth: true)}]")
            .RunAsync();

        Assert.Equal(TournamentAgeGroups.YouthUnspecified,
            Assert.Single(_db.TournamentDirectoryEntries.ToList()).AgeGroups);
    }

    /// <summary>Sagt der Name eine KLASSE, ist sie die bessere Auskunft als „irgendwie Jugend".</summary>
    [Fact]
    public async Task RunAsync_ANamedClassBeatsTheYouthTab()
    {
        await CreateService($"[{Row("Mistrovství ČR mládeže U16", "mcr-u16", Soon, youth: true)}]")
            .RunAsync();

        Assert.Equal(TournamentAgeGroups.U16,
            Assert.Single(_db.TournamentDirectoryEntries.ToList()).AgeGroups);
    }

    // ----- Ligarunden -------------------------------------------------------

    /// <summary>
    /// Der Grund fuer diese Quelle: aus elf Kalenderzeilen „… – N. kolo" wird EIN Eintrag mit elf
    /// Spielterminen. Elf Eintraege waeren schlicht falsch — es ist dieselbe Veranstaltung.
    /// </summary>
    [Fact]
    public async Task RunAsync_ElevenRoundRows_BecomeOneEntryWithElevenPlayingDates()
    {
        var result = await CreateService($"[{Rounds(11, chessResultsId: null)}]").RunAsync();

        Assert.Equal(1, result.Added);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.Include(e => e.RoundDates).ToList());
        Assert.Equal("šachy.cz Extraliga", entry.Name);
        Assert.Equal(11, entry.RoundDates.Count);
        Assert.Equal(Enumerable.Range(1, 11), entry.RoundDates.OrderBy(r => r.Number).Select(r => r.Number));
        Assert.Equal(Soon.AddDays(7), entry.StartDate);
        Assert.Equal(Soon.AddDays(77), entry.EndDate);
        Assert.Equal(11, result.Updated);   // gezaehlt werden die ergaenzten Spieltermine
    }

    /// <summary>
    /// Nennen die Runden die chess-results-Nummer ihrer Meisterschaft (22 von 33 tun das), landen
    /// die Termine direkt am bestehenden Eintrag — dort, wo sie sonst einen Seitenabruf je Turnier
    /// kosten.
    /// </summary>
    [Fact]
    public async Task RunAsync_RoundsLandOnTheExistingChessResultsEntry()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1464172", ChessResultsId = "1464172", Name = "Extraliga 2026/27",
            Federation = "CZE", StartDate = Soon, EndDate = Soon.AddDays(200),
        });
        await _db.SaveChangesAsync();

        var result = await CreateService($"[{Rounds(11)}]").RunAsync();

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Matched);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.Include(e => e.RoundDates).ToList());
        Assert.Equal(11, entry.RoundDates.Count);
        // Der bestehende Eintrag bleibt, wie er ist — die Quelle steuert nur die Termine bei.
        Assert.Equal("Extraliga 2026/27", entry.Name);
    }

    /// <summary>
    /// Was der Rundenplan-Dienst schon von chess-results geholt hat, bleibt stehen: dort steht
    /// auch die Uhrzeit, und ein Wettlauf zweier Quellen um dieselbe Zeile waere jede Nacht ein
    /// anderes Ergebnis.
    /// </summary>
    [Fact]
    public async Task RunAsync_NeverOverwritesARoundThatIsAlreadyThere()
    {
        var entry = new TournamentDirectoryEntry
        {
            PublicId = "1464172", ChessResultsId = "1464172", Name = "Extraliga",
            Federation = "CZE", StartDate = Soon, EndDate = Soon.AddDays(200),
        };
        entry.RoundDates.Add(new TournamentDirectoryRound
        {
            Number = 1, Date = Soon.AddDays(1), TimeText = "10:00",
        });
        _db.TournamentDirectoryEntries.Add(entry);
        await _db.SaveChangesAsync();

        var result = await CreateService($"[{Rounds(3)}]").RunAsync();

        var rounds = _db.TournamentDirectoryEntries.Include(e => e.RoundDates)
            .Single().RoundDates.OrderBy(r => r.Number).ToList();
        Assert.Equal(3, rounds.Count);
        Assert.Equal(Soon.AddDays(1), rounds[0].Date);      // unveraendert
        Assert.Equal("10:00", rounds[0].TimeText);
        Assert.Equal(2, result.Updated);                    // nur die zwei fehlenden
    }

    /// <summary>
    /// Ein zweiter Durchgang ergaenzt nichts mehr — sonst waechst die Rundenliste jede Nacht, und
    /// der eindeutige Schluessel (Eintrag, Rundennummer) wuerde brechen.
    /// </summary>
    [Fact]
    public async Task RunAsync_IsIdempotent()
    {
        var json = $"[{Rounds(5, chessResultsId: null)}]";
        await CreateService(json).RunAsync();
        var second = await CreateService(json).RunAsync();

        Assert.Equal(0, second.Added);
        Assert.Equal(0, second.Updated);
        Assert.Equal(5, _db.TournamentDirectoryEntries.Include(e => e.RoundDates)
            .Single().RoundDates.Count);
    }

    /// <summary>Eine Ligarunde wird dezentral gespielt — die Meisterschaft bekommt keinen Pin.</summary>
    [Fact]
    public async Task RunAsync_ASeriesWithoutVenues_GetsNoCoordinates()
    {
        await CreateService($"[{Rounds(3, chessResultsId: null)}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Null(entry.Lat);
        Assert.Null(entry.LocationText);
    }

    // ----- Herkunft und Ausfaelle -------------------------------------------

    [Fact]
    public async Task RunAsync_NotesTheReadableSlugAsItsSource()
    {
        await CreateService($"[{Row("Turnovský Granát", "turnovsky-granat-5", Soon)}]").RunAsync();

        var source = Assert.Single(_db.TournamentDirectorySources.ToList());
        Assert.Equal(DirectorySourceKind.CzechChessFederation, source.Kind);
        Assert.Equal("turnovsky-granat-5", source.ExternalId);
        Assert.Equal("https://www.chess.cz/akce/turnovsky-granat-5/", source.Url);
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
