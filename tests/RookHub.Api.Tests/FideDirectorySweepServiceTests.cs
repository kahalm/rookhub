using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der FIDE-Kalender als zweite Turnierquelle.
///
/// <para>Am 2026-09-07 gemessen: von 139 FIDE-Ereignissen des Jahres 2026 fanden sich 132 nicht
/// im Bestand — Tata Steel, Rilton Cup, Prague Masters, Aeroflot Open, das
/// Frauen-Kandidatenturnier. Die grossen internationalen Turniere schreiben nicht oder erst spaet
/// auf chess-results aus.</para>
/// </summary>
public class FideDirectorySweepServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly YearHandler _handler = new();

    public FideDirectorySweepServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private FideDirectorySweepService CreateService() =>
        new(_db, new ClientFactory(_handler), new GeocodingService(_db),
            new TestLogger<FideDirectorySweepService>());

    private static string Event(
        string id, string name, string start, string end, string? city = "Wijk aan Zee",
        string country = "NED") =>
        $$"""
          {"eventId":"{{id}}","name":"{{name}}","startDate":"{{start}}","endDate":"{{end}}",
           "city":{{(city is null ? "null" : $"\"{city}\"")}},"country":"{{country}}"}
          """;

    // ----- Neue Eintraege ---------------------------------------------------

    [Fact]
    public async Task RunAsync_NewEvent_BecomesADirectoryEntry()
    {
        _handler.Years[2026] = $"[{Event("12684", "Tata Steel Chess 2026 Masters", "2026-01-16", "2026-02-01")}]";

        var result = await CreateService().RunAsync([2026]);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Added);

        var entry = await _db.TournamentDirectoryEntries.Include(e => e.Sources).SingleAsync();
        Assert.Equal("f12684", entry.PublicId);
        Assert.Equal("Tata Steel Chess 2026 Masters", entry.Name);
        Assert.Equal(new DateOnly(2026, 1, 16), entry.StartDate);
        Assert.Equal(new DateOnly(2026, 2, 1), entry.EndDate);
        Assert.Equal("Wijk aan Zee, NED", entry.LocationText);
        Assert.Equal("NED", entry.Federation);

        var source = Assert.Single(entry.Sources);
        Assert.Equal(DirectorySourceKind.Fide, source.Kind);
        Assert.Equal("12684", source.ExternalId);
    }

    /// <summary>
    /// Ein FIDE-Eintrag hat KEINE chess-results-Nummer. Daran haengt, dass die Anzeige „merken"
    /// und „Ergebnisse holen" nicht anbietet — Knoepfe, die ins Leere fuehren, sind schlimmer als
    /// fehlende Knoepfe.
    /// </summary>
    [Fact]
    public async Task RunAsync_NewEvent_HasNoChessResultsId()
    {
        _handler.Years[2026] = $"[{Event("12684", "Tata Steel Masters", "2026-01-16", "2026-02-01")}]";

        await CreateService().RunAsync([2026]);

        Assert.Null((await _db.TournamentDirectoryEntries.SingleAsync()).ChessResultsId);
    }

    /// <summary>
    /// Verortet wird ueber Stadt UND Land — anders als bei chess-results, wo der Ort ein Freitext
    /// mit Adresse ist. Hier sind beide getrennt und sauber.
    /// </summary>
    [Fact]
    public async Task RunAsync_KnownCity_IsPinned()
    {
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "NL", Name = "Wijk aan Zee", NameNormalized = "wijk aan zee",
            Lat = 52.49, Lon = 4.59, Kind = GeoPlaceKind.City, Population = 2400,
        });
        await _db.SaveChangesAsync();
        _handler.Years[2026] = $"[{Event("12684", "Tata Steel Masters", "2026-01-16", "2026-02-01")}]";

        await CreateService().RunAsync([2026]);

        var entry = await _db.TournamentDirectoryEntries.SingleAsync();
        Assert.Equal(52.49, entry.Lat);
        Assert.Equal("Wijk aan Zee", entry.GeoPlaceName);
    }

    /// <summary>
    /// Ein ONLINE-Ereignis bekommt KEINEN Pin: ein Punkt auf der Karte fuer ein Turnier, das
    /// nirgends stattfindet, waere eine Falschaussage.
    /// </summary>
    [Fact]
    public async Task RunAsync_OnlineEvent_GetsNoCoordinates()
    {
        _handler.Years[2026] =
            $"[{Event("12795", "African Chess Confederation Meet and Greet", "2026-01-31", "2026-01-31", null, "ONL")}]";

        await CreateService().RunAsync([2026]);

        var entry = await _db.TournamentDirectoryEntries.SingleAsync();
        Assert.Null(entry.Lat);
        Assert.Equal("ONL", entry.LocationText);
    }

    /// <summary>Publikum und Format stehen wie ueberall im NAMEN — FIDE nennt sie nicht.</summary>
    [Fact]
    public async Task RunAsync_AudienceComesFromTheName()
    {
        _handler.Years[2026] = "[" +
            Event("5078", "FIDE World Youth U14, U16 & U18 Championships", "2026-06-15", "2026-06-28",
                "Batumi", "GEO") + "," +
            Event("13838", "Norway Chess Women 2026", "2026-05-25", "2026-06-05", "Oslo", "NOR") + "]";

        await CreateService().RunAsync([2026]);

        var byId = await _db.TournamentDirectoryEntries.ToDictionaryAsync(e => e.PublicId);
        Assert.Equal(
            TournamentAgeGroups.U14 | TournamentAgeGroups.U16 | TournamentAgeGroups.U18,
            byId["f5078"].AgeGroups);
        Assert.Equal(TournamentGender.Female, byId["f13838"].Gender);
    }

    // ----- Zusammenfuehren --------------------------------------------------

    /// <summary>
    /// Steht dasselbe Turnier schon als chess-results-Eintrag da, bekommt ES den
    /// Herkunftsvermerk — ein zweiter Eintrag waere sichtbares Rauschen im Kalender.
    /// </summary>
    [Fact]
    public async Task RunAsync_SameTournamentFromChessResults_GetsTheSourceInsteadOfADuplicate()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1468887", ChessResultsId = "1468887",
            Name = "Sparkassen Chess Trophy Dortmund 2026", Federation = "GER",
            StartDate = new DateOnly(2026, 6, 20), EndDate = new DateOnly(2026, 6, 28),
            LocationText = "Dortmund",
        });
        await _db.SaveChangesAsync();

        _handler.Years[2026] =
            $"[{Event("777", "Sparkassen Dortmund Chess Days", "2026-06-20", "2026-06-28", "Dortmund", "GER")}]";

        var result = await CreateService().RunAsync([2026]);

        Assert.Equal(1, result.MergedIntoExisting);
        Assert.Equal(0, result.Added);
        var entry = await _db.TournamentDirectoryEntries.Include(e => e.Sources).SingleAsync();
        Assert.Equal("1468887", entry.PublicId);
        Assert.Contains(entry.Sources, s => s.Kind == DirectorySourceKind.Fide && s.ExternalId == "777");
    }

    /// <summary>
    /// Und die andere Richtung, die WICHTIGERE: ein falsches Zusammenfuehren macht aus zwei
    /// Turnieren eines und ist schlimmer als ein Duplikat. Zwei Turniere am selben Tag in
    /// verschiedenen Staedten ohne gemeinsames unterscheidendes Wort bleiben getrennt.
    /// </summary>
    [Fact]
    public async Task RunAsync_DifferentTournamentOnTheSameDay_StaysSeparate()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1", ChessResultsId = "1", Name = "Vereinsmeisterschaft Hietzing",
            Federation = "AUT", StartDate = new DateOnly(2026, 1, 16),
            EndDate = new DateOnly(2026, 1, 18), LocationText = "Wien",
        });
        await _db.SaveChangesAsync();

        _handler.Years[2026] = $"[{Event("12684", "Tata Steel Masters", "2026-01-16", "2026-02-01")}]";

        var result = await CreateService().RunAsync([2026]);

        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.MergedIntoExisting);
        Assert.Equal(2, await _db.TournamentDirectoryEntries.CountAsync());
    }

    /// <summary>
    /// EIN gemeinsames Wort genuegt nicht, wenn der Ort nicht passt: „Prague Masters" und
    /// „Prague Challengers" derselben Woche sind zwei Turniere, und „Masters"/„Challengers"
    /// stehen ohnehin in der Fuellwortliste.
    /// </summary>
    [Fact]
    public async Task RunAsync_OneSharedWordWithoutTheCity_IsNotEnough()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1", ChessResultsId = "1", Name = "Aeroflot Open 2026",
            Federation = "RUS", StartDate = new DateOnly(2026, 2, 27),
            EndDate = new DateOnly(2026, 3, 6), LocationText = "Sankt Petersburg",
        });
        await _db.SaveChangesAsync();

        _handler.Years[2026] =
            $"[{Event("888", "Aeroflot Cup", "2026-02-27", "2026-03-06", "Moscow", "RUS")}]";

        var result = await CreateService().RunAsync([2026]);

        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.MergedIntoExisting);
    }

    // ----- Der Jahreswechsel ------------------------------------------------

    /// <summary>
    /// Die FIDE-Jahresansicht nennt nur Tag und Monat, das Jahr kommt aus der Anfrage. Ein
    /// Ereignis ueber den Jahreswechsel steht deshalb in ZWEI Ansichten, und nur die fruehere
    /// liest es richtig. Der ERSTE Treffer je Ereignisnummer gewinnt — deshalb aufsteigend.
    /// </summary>
    [Fact]
    public async Task RunAsync_AscendingYears_KeepsTheFirstReading()
    {
        _handler.Years[2025] = $"[{Event("12683", "Rilton Cup", "2025-12-27", "2026-01-05", "Stockholm", "SWE")}]";
        _handler.Years[2026] = $"[{Event("12683", "Rilton Cup", "2026-12-27", "2027-01-05", "Stockholm", "SWE")}]";

        // Absichtlich in falscher Reihenfolge uebergeben: der Dienst sortiert selbst.
        var result = await CreateService().RunAsync([2026, 2025]);

        Assert.Equal(1, result.Fetched);
        var entry = await _db.TournamentDirectoryEntries.SingleAsync();
        Assert.Equal(new DateOnly(2025, 12, 27), entry.StartDate);
    }

    // ----- Fehlerverhalten --------------------------------------------------

    /// <summary>
    /// Ein Ausfall bei FIDE meldet sich als Fehler, laesst aber die schon uebernommenen
    /// Ereignisse stehen — und der naechtliche Sweep gilt deshalb nicht als gescheitert.
    /// </summary>
    [Fact]
    public async Task RunAsync_SourceDown_ReportsTheErrorAndKeepsWhatItHas()
    {
        _handler.Years[2025] = $"[{Event("1", "Rilton Cup", "2025-12-27", "2026-01-05", "Stockholm", "SWE")}]";
        _handler.FailFrom = 2026;

        var result = await CreateService().RunAsync([2025, 2026]);

        Assert.False(result.Succeeded);
        Assert.Equal(1, result.Added);
        Assert.Single(await _db.TournamentDirectoryEntries.ToListAsync());
    }

    [Fact]
    public async Task RunAsync_SecondRun_UpdatesInsteadOfDuplicating()
    {
        _handler.Years[2026] = $"[{Event("12684", "Tata Steel Masters", "2026-01-16", "2026-02-01")}]";
        await CreateService().RunAsync([2026]);
        _db.ChangeTracker.Clear();

        _handler.Years[2026] = $"[{Event("12684", "Tata Steel Masters 2026", "2026-01-17", "2026-02-01")}]";
        var result = await CreateService().RunAsync([2026]);

        Assert.Equal(1, result.Updated);
        var entry = await _db.TournamentDirectoryEntries.Include(e => e.Sources).SingleAsync();
        Assert.Equal("Tata Steel Masters 2026", entry.Name);
        Assert.Equal(new DateOnly(2026, 1, 17), entry.StartDate);
        Assert.Single(entry.Sources);
    }

    /// <summary>Eine Zeile ohne verwertbares Datum wird uebergangen, nicht geraten.</summary>
    [Fact]
    public async Task RunAsync_RowWithoutADate_IsSkipped()
    {
        _handler.Years[2026] = """[{"eventId":"1","name":"Ohne Datum","startDate":"","endDate":""}]""";

        var result = await CreateService().RunAsync([2026]);

        Assert.Equal(0, result.Fetched);
        Assert.Empty(await _db.TournamentDirectoryEntries.ToListAsync());
    }

    // ----- Attrappen --------------------------------------------------------

    /// <summary>Antwortet je nach abgefragtem Jahr.</summary>
    private sealed class YearHandler : HttpMessageHandler
    {
        public Dictionary<int, string> Years { get; } = [];
        /// <summary>Ab diesem Jahr antwortet die Attrappe mit einem Fehler.</summary>
        public int? FailFrom { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri?.ToString() ?? "";
            var match = System.Text.RegularExpressions.Regex.Match(url, @"year=(\d+)");
            var year = match.Success ? int.Parse(match.Groups[1].Value) : 0;

            if (FailFrom is { } fail && year >= fail)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("boom", Encoding.UTF8, "text/plain"),
                });
            }

            var body = Years.GetValueOrDefault(year, "[]");
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
