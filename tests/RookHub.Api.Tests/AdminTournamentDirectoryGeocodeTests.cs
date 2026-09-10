using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Nachverortung von Hand — und vor allem ihr `force`-Schalter.
///
/// <para>Gebraucht wird er, wenn sich die Verortungs-REGELN aendern: der naechtliche Sweep
/// verortet einen bestehenden Eintrag nur neu, wenn sich sein Ortstext geaendert hat (sonst liefe
/// jede Nacht der ganze Bestand durch den Gazetteer). Ein falscher Pin aus einer alten Regel
/// bliebe damit fuer immer stehen — genau der Fall bei der Umstellung in 0.418.0.</para>
/// </summary>
public class AdminTournamentDirectoryGeocodeTests : IDisposable
{
    private readonly AppDbContext _db;

    public AdminTournamentDirectoryGeocodeTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>
    /// Nur `_db` und der Geocoder werden hier gebraucht — die uebrigen Abhaengigkeiten des
    /// Controllers gehoeren zu anderen Endpunkten und bekommen deshalb Attrappen, die nie
    /// angesprochen werden.
    /// </summary>
    private AdminTournamentDirectoryController Controller()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Crawler:BaseUrl"] = "http://crawler" }).Build();
        var geocoding = new GeocodingService(_db);
        var factory = new UnusedClientFactory();
        return new AdminTournamentDirectoryController(
            _db,
            new TournamentDirectoryService(_db, factory, geocoding,
                new NotificationService(_db, new NoOpTaskQueue()),
                new TestLogger<TournamentDirectoryService>()),
            new GazetteerImportService(new HttpClient(new UnusedHandler()), _db,
                new TestLogger<GazetteerImportService>(), config),
            geocoding,
            new VenueDisambiguationService(_db, factory,
                new TestLogger<VenueDisambiguationService>()),
            new TournamentRoundPlanService(_db, factory,
                new TestLogger<TournamentRoundPlanService>()),
            new FideDirectorySweepService(_db, factory, geocoding,
                new TestLogger<FideDirectorySweepService>()),
            new FideEventDetailService(_db, factory, geocoding,
                new TestLogger<FideEventDetailService>()),
            new TournamentCalendarSweepService(_db, factory,
                new TestLogger<TournamentCalendarSweepService>()),
            new FsiDirectorySweepService(_db, factory, geocoding,
                new TestLogger<FsiDirectorySweepService>()),
            new SzsDirectorySweepService(_db, factory, geocoding,
                new TestLogger<SzsDirectorySweepService>()),
            new ChessSkDirectorySweepService(_db, factory, geocoding,
                new TestLogger<ChessSkDirectorySweepService>()),
            new ChessHuDirectorySweepService(_db, factory, geocoding,
                new TestLogger<ChessHuDirectorySweepService>()),
            new ChessCzDirectorySweepService(_db, factory, geocoding,
                new TestLogger<ChessCzDirectorySweepService>()),
            new ChessArbiterDirectorySweepService(_db, factory, geocoding, config,
                new TestLogger<ChessArbiterDirectorySweepService>()),
            new SchachbundDirectorySweepService(_db, factory, geocoding,
                new TestLogger<SchachbundDirectorySweepService>()),
            new EcfDirectorySweepService(_db, factory, geocoding,
                new TestLogger<EcfDirectorySweepService>()),
            new IcuDirectorySweepService(_db, factory, geocoding, config,
                new TestLogger<IcuDirectorySweepService>()),
            new FfeDirectorySweepService(_db, factory, geocoding, config,
                new TestLogger<FfeDirectorySweepService>()),
            new SjakkDirectorySweepService(_db, factory, geocoding, config,
                new TestLogger<SjakkDirectorySweepService>()),
            new ChessScotlandDirectorySweepService(_db, factory, geocoding, config,
                new TestLogger<ChessScotlandDirectorySweepService>()),
            new FrsahDirectorySweepService(_db, factory, geocoding,
                new TestLogger<FrsahDirectorySweepService>()),
            new WcuDirectorySweepService(_db, factory, geocoding,
                new TestLogger<WcuDirectorySweepService>()),
            new CfcDirectorySweepService(_db, factory, geocoding,
                new TestLogger<CfcDirectorySweepService>()),
            new KnsbDirectorySweepService(_db, factory,
                new TestLogger<KnsbDirectorySweepService>()));
    }

    /// <summary>
    /// Der Nachtrag fuer den Altbestand. Publikum und Format stehen im NAMEN, der schon in der
    /// Datenbank liegt — es braucht also kein Netz, um 4000 bestehende Eintraege einzuordnen. Ohne
    /// diesen Knopf blieben sie bis zum naechsten naechtlichen Sweep unklassifiziert, und der
    /// Filter „nur Erwachsene" liesse eine halb leere Liste zurueck.
    /// </summary>
    [Fact]
    public async Task Classify_ExistingEntries_AreCategorisedFromTheirNames()
    {
        _db.TournamentDirectoryEntries.AddRange(
            new TournamentDirectoryEntry
            {
                PublicId = "1", ChessResultsId = "1", Name = "Landesmeisterschaft U12 weiblich", Federation = "AUT",
                StartDate = new DateOnly(2026, 10, 3), EndDate = new DateOnly(2026, 10, 4),
            },
            new TournamentDirectoryEntry
            {
                PublicId = "2", ChessResultsId = "2", Name = "Tiroler Landesliga", Federation = "AUT",
                StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 4, 1),
            },
            new TournamentDirectoryEntry
            {
                PublicId = "3", ChessResultsId = "3", Name = "Open Braunau 2026 A", Federation = "AUT",
                StartDate = new DateOnly(2026, 12, 18), EndDate = new DateOnly(2026, 12, 20),
            });
        await _db.SaveChangesAsync();

        var result = await Controller().Classify();

        Assert.IsType<OkObjectResult>(result);
        var byId = await _db.TournamentDirectoryEntries.ToDictionaryAsync(e => e.ChessResultsId);
        Assert.Equal(TournamentAgeGroups.U12, byId["1"].AgeGroups);
        Assert.Equal(TournamentGender.Female, byId["1"].Gender);
        Assert.True(byId["2"].IsLeague);
        Assert.Equal(TournamentAgeGroups.None, byId["3"].AgeGroups);
        Assert.False(byId["3"].IsLeague);
    }

    /// <summary>
    /// Die Turnier<b>art</b> bleibt unangetastet: sie kommt aus einer zweiten
    /// chess-results-Abfrage, nicht aus dem Namen. Wuerde dieser Knopf sie mitschreiben, machte er
    /// aus „noch nicht geklaert" ein falsches „Einzelturnier".
    /// </summary>
    [Fact]
    public async Task Classify_LeavesTheTournamentKindAlone()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1", ChessResultsId = "1", Name = "Steirischer Mannschaftscup", Federation = "AUT",
            StartDate = new DateOnly(2026, 10, 3), EndDate = new DateOnly(2026, 10, 4),
            Kind = TournamentKind.Unknown,
        });
        await _db.SaveChangesAsync();

        await Controller().Classify();

        var entry = await _db.TournamentDirectoryEntries.SingleAsync();
        Assert.Equal(TournamentKind.Unknown, entry.Kind);
        // Und ohne bekannte Art greift die Dauerregel nicht — „Mannschaftscup" allein ist keine Liga.
        Assert.False(entry.IsLeague);
    }

    /// <summary>
    /// Der Herkunftsvermerk fuer den Altbestand. Der naechtliche Sweep traegt ihn von selbst
    /// nach, aber erst wenn er die Foederation wieder vornimmt — die Rotation braucht dafuer
    /// eine Woche. Der Vermerk uebernimmt die Zeitstempel des EINTRAGS, nicht „jetzt": seit wann
    /// die Quelle das Turnier fuehrt, ist bekannt.
    /// </summary>
    [Fact]
    public async Task BackfillSources_ExistingEntries_GetTheirChessResultsOrigin()
    {
        var seen = new DateTime(2026, 5, 1, 3, 0, 0, DateTimeKind.Utc);
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1457129", ChessResultsId = "1457129", Name = "Open Braunau", Federation = "AUT",
            FirstSeenAt = seen, LastSeenAt = seen.AddDays(30),
        });
        await _db.SaveChangesAsync();

        await Controller().BackfillSources();

        var source = Assert.Single(await _db.TournamentDirectorySources.ToListAsync());
        Assert.Equal(DirectorySourceKind.ChessResults, source.Kind);
        Assert.Equal("1457129", source.ExternalId);
        Assert.Equal(seen, source.FirstSeenAt);
    }

    /// <summary>Ein zweiter Aufruf legt nichts doppelt an.</summary>
    [Fact]
    public async Task BackfillSources_RunTwice_AddsNothingTheSecondTime()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1", ChessResultsId = "1", Name = "Open", Federation = "AUT",
        });
        await _db.SaveChangesAsync();

        await Controller().BackfillSources();
        _db.ChangeTracker.Clear();
        await Controller().BackfillSources();

        Assert.Single(await _db.TournamentDirectorySources.ToListAsync());
    }

    /// <summary>Wird in diesen Tests nie benutzt — ein Aufruf ist ein Fehler, kein Zufall.</summary>
    private sealed class UnusedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("Diese Tests erwarten keinen HTTP-Aufruf.");
    }

    private sealed class UnusedClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new UnusedHandler());
    }

    private async Task<TournamentDirectoryEntry> EntryAsync(
        string id, string location, double? lat, double? lon, GeoSource source)
    {
        var entry = new TournamentDirectoryEntry
        {
            PublicId = id, ChessResultsId = id, Name = "Turnier " + id, Federation = "DEU_unused",
            LocationText = location, Lat = lat, Lon = lon, GeoSource = source,
        };
        entry.Federation = "GER";
        _db.TournamentDirectoryEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    private void SeedPlaces(params GeoPlace[] places)
    {
        foreach (var p in places) p.NameNormalized = GeoTextNormalizer.Normalize(p.Name);
        _db.GeoPlaces.AddRange(places);
        _db.SaveChanges();
    }

    [Fact]
    public async Task GeocodeMissing_WithoutForce_LeavesExistingPinsAlone()
    {
        // Ohne den Schalter ist der Endpunkt, was sein Name sagt: er fuellt nur Luecken.
        await EntryAsync("1", "Münster", 51.96, 7.63, GeoSource.City);
        SeedPlaces(new GeoPlace { Country = "DE", PostalCode = "48143", Name = "Münster", Lat = 51.96, Lon = 7.63, Kind = GeoPlaceKind.PostalCode },
                   new GeoPlace { Country = "DE", PostalCode = "84579", Name = "Münster", Lat = 48.26, Lon = 12.71, Kind = GeoPlaceKind.PostalCode });

        var result = await Controller().GeocodeMissing();
        var body = Assert.IsType<OkObjectResult>(result).Value!;

        Assert.Equal(0, Prop(body, "examined"));
        var entry = await _db.TournamentDirectoryEntries.FirstAsync();
        Assert.Equal(51.96, entry.Lat);
    }

    [Fact]
    public async Task GeocodeMissing_WithForce_RemovesAPinThatIsNowConsideredAGuess()
    {
        // „Münster" gibt es mehrfach, weit auseinander — nach der neuen Regel gibt es dafuer
        // keinen Pin mehr. Der alte muss WEG, nicht bloss unveraendert bleiben.
        await EntryAsync("1", "Münster", 51.96, 7.63, GeoSource.City);
        SeedPlaces(new GeoPlace { Country = "DE", PostalCode = "48143", Name = "Münster", Lat = 51.96, Lon = 7.63, Kind = GeoPlaceKind.PostalCode },
                   new GeoPlace { Country = "DE", PostalCode = "84579", Name = "Münster", Lat = 48.26, Lon = 12.71, Kind = GeoPlaceKind.PostalCode });

        var result = await Controller().GeocodeMissing(limit: 100, force: true);
        var body = Assert.IsType<OkObjectResult>(result).Value!;

        Assert.Equal(1, Prop(body, "cleared"));
        var entry = await _db.TournamentDirectoryEntries.FirstAsync();
        Assert.Null(entry.Lat);
        Assert.Equal(GeoSource.Ambiguous, entry.GeoSource);
    }

    [Fact]
    public async Task GeocodeMissing_WithForce_NeverTouchesAManualCorrection()
    {
        // Eine von Hand gesetzte Koordinate ist die Korrektur eines Fehlgriffs — sie darf auch
        // ein Regelwechsel nicht ueberschreiben.
        await EntryAsync("1", "Münster", 10.0, 20.0, GeoSource.Manual);
        SeedPlaces(new GeoPlace { Country = "DE", PostalCode = "48143", Name = "Münster", Lat = 51.96, Lon = 7.63, Kind = GeoPlaceKind.PostalCode });

        await Controller().GeocodeMissing(limit: 100, force: true);

        var entry = await _db.TournamentDirectoryEntries.FirstAsync();
        Assert.Equal(10.0, entry.Lat);
        Assert.Equal(GeoSource.Manual, entry.GeoSource);
    }

    [Fact]
    public async Task GeocodeMissing_WithForce_NeverTouchesAClubNameResolution()
    {
        // Der ueber die Vereinsnamen aufgeloeste Spielort ist BELEGT (ein Seitenabruf hat gezeigt,
        // welches „St. Veit" gemeint ist) — die Namensregel kann diesen Fall gar nicht loesen und
        // wuerde daraus wieder „mehrdeutig, kein Pin" machen. Weil `TeamHintCheckedAt` einen
        // zweiten Abruf verhindert, waere die Arbeit unwiederbringlich weg.
        await EntryAsync("1", "St. Veit", 46.77, 14.36, GeoSource.TeamHint);
        SeedPlaces(new GeoPlace { Country = "AT", PostalCode = "9300", Name = "St. Veit", Lat = 46.77, Lon = 14.36, Kind = GeoPlaceKind.PostalCode },
                   new GeoPlace { Country = "AT", PostalCode = "6373", Name = "St. Veit", Lat = 47.29, Lon = 12.35, Kind = GeoPlaceKind.PostalCode });

        var result = await Controller().GeocodeMissing(limit: 100, force: true);
        var body = Assert.IsType<OkObjectResult>(result).Value!;

        Assert.Equal(0, Prop(body, "examined"));   // gar nicht erst vorgenommen
        Assert.Equal(0, Prop(body, "cleared"));
        var entry = await _db.TournamentDirectoryEntries.FirstAsync();
        Assert.Equal(46.77, entry.Lat);
        Assert.Equal(GeoSource.TeamHint, entry.GeoSource);
    }

    private static int Prop(object body, string name) =>
        Convert.ToInt32(body.GetType().GetProperty(name)!.GetValue(body));
}
