using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der ANKUENDIGUNGS-Kalender von chess-results — der zweite Datenbestand derselben Seite.
///
/// <para>Der Anlass: die Turniersuche, aus der das Verzeichnis lebt, fuellt sich erst beim
/// Swiss-Manager-Upload — typisch Tage bis Wochen vorher. Am 2026-09-07 fuer AUT gemessen kannte
/// sie 8 im November beginnende Turniere und 7 im Dezember, der Kalender 23 und 16. Von 143
/// kuenftigen Kalendereintraegen fehlten 93 in der Suche.</para>
/// </summary>
public class TournamentCalendarSweepServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public TournamentCalendarSweepServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private TournamentCalendarSweepService CreateService(string json,
        HttpStatusCode status = HttpStatusCode.OK) =>
        new(_db, new StubClientFactory(new StubHandler(json, status)),
            new TestLogger<TournamentCalendarSweepService>());

    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(60);

    private static string Row(string name, DateOnly start, string? calendarId = "10814",
        string fed = "AUT", string? tnr = null) =>
        $$"""
          {"name":"{{name}}","federation":"{{fed}}",
           "startDate":"{{start:yyyy-MM-dd}}","endDate":"{{start.AddDays(2):yyyy-MM-dd}}",
           "calendarId":{{(calendarId is null ? "null" : $"\"{calendarId}\"")}},
           "url":"http://verein.example","chessResultsId":{{(tnr is null ? "null" : $"\"{tnr}\"")}}}
          """;

    private async Task<TournamentDirectoryEntry> AddSearchEntryAsync(
        string tnr, string name, DateOnly start, string fed = "AUT")
    {
        var entry = new TournamentDirectoryEntry
        {
            PublicId = tnr, ChessResultsId = tnr, Name = name, Federation = fed,
            StartDate = start, EndDate = start.AddDays(2),
        };
        _db.TournamentDirectoryEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    [Fact]
    public async Task RunAsync_UnknownTournament_IsAdded()
    {
        var result = await CreateService($"[{Row("Advent-Open Krems 2026", Soon)}]").RunAsync();

        Assert.Equal(1, result.Added);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("k10814", entry.PublicId);
        Assert.Equal("Advent-Open Krems 2026", entry.Name);
        Assert.Equal("AUT", entry.Federation);
        // Der Kalender kennt keine Turniernummer — alles, was chess-results wirklich braucht,
        // bleibt deshalb aus, bis die Turniersuche das Turnier einholt.
        Assert.Null(entry.ChessResultsId);
    }

    /// <summary>
    /// Kennt die Turniersuche das Turnier schon, wird nur der Herkunftsvermerk gesetzt — der
    /// Kalender hat kein Feld, das die Suche nicht besser fuehrt.
    /// </summary>
    [Fact]
    public async Task RunAsync_AlreadyKnownTournament_IsOnlyNoted()
    {
        await AddSearchEntryAsync("1490241", "Advent-Open Krems 2026", Soon);

        var result = await CreateService($"[{Row("Advent Open Krems", Soon)}]").RunAsync();

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Matched);
        var entry = Assert.Single(_db.TournamentDirectoryEntries.Include(e => e.Sources).ToList());
        Assert.Equal("1490241", entry.PublicId);
        Assert.Contains(entry.Sources, s => s.Kind == DirectorySourceKind.ChessResultsCalendar
                                            && s.ExternalId == "10814");
    }

    /// <summary>
    /// Der Fall, der sonst jede Nacht ein Duplikat erzeugt haette: wir legen das Turnier aus dem
    /// Kalender an, Wochen spaeter holt die Turniersuche es ein — und dann steht dieselbe
    /// Veranstaltung unter zwei Kennungen. Der eigene Eintrag wird zurueckgezogen.
    /// </summary>
    [Fact]
    public async Task RunAsync_OwnEntryOvertakenByTheSearch_IsRetired()
    {
        // Erster Durchgang: nur der Kalender kennt es.
        await CreateService($"[{Row("Advent-Open Krems 2026", Soon)}]").RunAsync();
        var own = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.Equal("k10814", own.PublicId);

        // Die Turniersuche holt es ein.
        await AddSearchEntryAsync("1490241", "Advent-Open Krems 2026", Soon);

        var result = await CreateService($"[{Row("Advent-Open Krems 2026", Soon)}]").RunAsync();

        Assert.Equal(1, result.Merged);
        await _db.Entry(own).ReloadAsync();
        Assert.NotNull(own.RemovedAt);
        Assert.Null(_db.TournamentDirectoryEntries.Single(e => e.PublicId == "1490241").RemovedAt);
    }

    /// <summary>
    /// Ohne Kalender-Nummer wird NICHTS angelegt. Sie ist die einzige stabile Kennung dieser
    /// Quelle (an der echten Seite: 147 von 209 kuenftigen tragen eine). Ohne sie liesse sich
    /// „neues Turnier" nicht von „umbenanntes Turnier" unterscheiden, und der Bestand bekaeme
    /// jede Nacht ein Duplikat mehr.
    /// </summary>
    [Fact]
    public async Task RunAsync_EntryWithoutACalendarId_IsNotAdded()
    {
        var result = await CreateService($"[{Row("Namenloses Turnier", Soon, calendarId: null)}]").RunAsync();

        Assert.Equal(0, result.Added);
        Assert.Empty(_db.TournamentDirectoryEntries.ToList());
    }

    /// <summary>Zweimal laufen darf nicht zweimal anlegen.</summary>
    [Fact]
    public async Task RunAsync_TwiceInARow_AddsOnlyOnce()
    {
        var json = $"[{Row("Advent-Open Krems 2026", Soon)}]";
        await CreateService(json).RunAsync();
        var second = await CreateService(json).RunAsync();

        Assert.Equal(0, second.Added);
        Assert.Single(_db.TournamentDirectoryEntries.ToList());
        // Und der Herkunftsvermerk bleibt EINER, statt je Nacht eine Zeile anzulegen.
        Assert.Single(_db.TournamentDirectorySources.ToList());
    }

    /// <summary>
    /// Vergangenes bringt nichts: der Wert dieser Quelle ist der VORLAUF, und ein gespieltes
    /// Turnier steht ohnehin laengst in der Turniersuche.
    /// </summary>
    [Fact]
    public async Task RunAsync_PastTournament_IsIgnored()
    {
        var past = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30);
        var result = await CreateService($"[{Row("Vergangenes Open", past)}]").RunAsync();

        Assert.Equal(0, result.Added);
        Assert.Empty(_db.TournamentDirectoryEntries.ToList());
    }

    /// <summary>
    /// EIN gemeinsames Wort reicht fuer die Zuordnung NICHT. „Open" und „Meisterschaft" sind schon
    /// weggefiltert, aber ein Ortsname trifft auch das andere Turnier derselben Woche am selben
    /// Ort — und aus zwei Turnieren eines zu machen ist schlimmer als ein Duplikat.
    /// </summary>
    [Fact]
    public async Task RunAsync_OnlyOneSharedWord_DoesNotMerge()
    {
        await AddSearchEntryAsync("1490241", "Klagenfurt Blitzturnier", Soon);

        var result = await CreateService($"[{Row("Klagenfurt Jugendmeisterschaft", Soon)}]").RunAsync();

        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Matched);
        Assert.Equal(2, _db.TournamentDirectoryEntries.Count());
    }

    /// <summary>
    /// Traegt der Kalendereintrag ausnahmsweise eine Turniernummer (an AUT gemessen 6 von 146),
    /// ist sie eindeutig und schlaegt den Namensvergleich.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithATournamentNumber_MatchesExactly()
    {
        await AddSearchEntryAsync("1490241", "Voellig anderer Name", Soon.AddDays(40));

        var result = await CreateService($"[{Row("Advent-Open", Soon, tnr: "1490241")}]").RunAsync();

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Matched);
    }

    /// <summary>Publikum und Format stehen auch hier nur im Namen — dieselbe Ableitung wie im Sweep.</summary>
    [Fact]
    public async Task RunAsync_DerivesAudienceFromTheName()
    {
        await CreateService($"[{Row("Landesmeisterschaft U12 weiblich", Soon)}]").RunAsync();

        var entry = Assert.Single(_db.TournamentDirectoryEntries.ToList());
        Assert.True(entry.AgeGroups.HasFlag(TournamentAgeGroups.U12));
        Assert.Equal(TournamentGender.Female, entry.Gender);
        // Die Turnierart bleibt unbekannt: der Kalender sagt nichts darueber, und Raten waere
        // schlechter als Schweigen.
        Assert.Equal(TournamentKind.Unknown, entry.Kind);
    }

    [Fact]
    public async Task RunAsync_CrawlerError_Throws()
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            CreateService("boom", HttpStatusCode.InternalServerError).RunAsync());
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
