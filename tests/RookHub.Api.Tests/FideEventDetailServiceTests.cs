using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Detailangaben der FIDE-Eintraege.
///
/// <para>Der Anlass: die FIDE-Jahresansicht, aus der der Sweep liest, traegt je Ereignis genau
/// einen Textabschnitt („01 May - 07 May / Malmo (SWE)"). Am Dev-Stand hiess das, von 144
/// FIDE-eigenen Eintraegen hatte KEIN EINZIGER eine Bedenkzeit, eine Rundenzahl oder eine
/// Teilnehmerzahl — waehrend die chess-results-Eintraege daneben 4444 von 4923 mit Bedenkzeit
/// fuehren. Die Angaben stehen auf der Ereignisseite und kosten einen Abruf je Ereignis.</para>
/// </summary>
public class FideEventDetailServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly string _dbName = Guid.NewGuid().ToString();

    public FideEventDetailServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options);
    }

    public void Dispose() => _db.Dispose();

    private FideEventDetailService CreateService(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHandler(json, status);
        return new FideEventDetailService(_db, new StubClientFactory(handler),
            new GeocodingService(_db), new TestLogger<FideEventDetailService>());
    }

    /// <summary>Ein FIDE-Eintrag mit Herkunftsvermerk — nur so ist er ueberhaupt Kandidat.</summary>
    private async Task<TournamentDirectoryEntry> AddFideEntryAsync(
        string publicId = "f5437", string fideId = "5437", string? chessResultsId = null)
    {
        var entry = new TournamentDirectoryEntry
        {
            PublicId = publicId,
            ChessResultsId = chessResultsId,
            Name = "European Women's Rapid Chess Championship",
            Federation = "MNC",
            StartDate = new DateOnly(2026, 1, 8),
            EndDate = new DateOnly(2026, 1, 12),
        };
        _db.TournamentDirectoryEntries.Add(entry);
        await _db.SaveChangesAsync();

        _db.TournamentDirectorySources.Add(new TournamentDirectorySource
        {
            TournamentDirectoryEntryId = entry.Id,
            Kind = DirectorySourceKind.Fide,
            ExternalId = fideId,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        return entry;
    }

    private const string FullDetail = """
        {"eventId":"17954","eventType":"Over-the-Board Tournament","timeControl":"Standard",
         "timeControlText":"90 minutes with 30 second increment from move 1",
         "system":"Round-Robin","rounds":9,"players":10,"country":"Spain","city":"Zaragoza",
         "venueAddress":"Via Iberica, 69, 77, 50012 Zaragoza, Spain","website":null}
        """;

    /// <summary>Die 46. Schacholympiade: acht Zeilen, davon die Haelfte leer.</summary>
    private const string SparseDetail = """
        {"eventId":"5072","eventType":"Over-the-Board Tournament","timeControl":"Standard",
         "timeControlText":null,"system":"Other","rounds":null,"players":null,
         "country":"Uzbekistan","city":"Samarkand","venueAddress":null,"website":null}
        """;

    [Fact]
    public async Task RunAsync_FullDetail_FillsTheEmptyFields()
    {
        var entry = await AddFideEntryAsync();

        var result = await CreateService(FullDetail).RunAsync(10);

        Assert.Equal(1, result.Checked);
        Assert.Equal(1, result.WithDetails);

        await _db.Entry(entry).ReloadAsync();
        Assert.Equal("90 minutes with 30 second increment from move 1", entry.TimeControlText);
        Assert.Equal(TournamentSpeed.Standard, entry.Speed);
        Assert.Equal(TournamentSystem.RoundRobin, entry.System);
        Assert.Equal(9, entry.Rounds);
        Assert.Equal(10, entry.PlayerCount);
        Assert.NotNull(entry.FideDetailCheckedAt);
    }

    /// <summary>
    /// Und der karge Fall. Fehlende Angaben duerfen nichts ueberschreiben — aber der Vermerk muss
    /// trotzdem gesetzt werden, sonst wird dieselbe Seite jede Nacht erneut geholt. Das ist
    /// dieselbe Regel wie beim Rundenplan: „nachgesehen, nichts hinterlegt" ist ein ERGEBNIS.
    /// </summary>
    /// <summary>
    /// Aendert sich der Parser oder der Weg zum Detail-Fragment, muss der BESTAND mit — der
    /// Zeitstempel allein sagt nur, DASS geholt wurde. Eine aeltere Fassung holt der naechste
    /// Durchgang von selbst nach, ohne dass jemand `retryEmpty` von Hand ausloest.
    /// </summary>
    [Fact]
    public async Task RunAsync_AnOlderVersion_IsFetchedAgain()
    {
        var entry = await AddFideEntryAsync();
        entry.FideDetailCheckedAt = DateTime.UtcNow.AddDays(-1);
        entry.FideDetailVersion = 0;
        await _db.SaveChangesAsync();

        var result = await CreateService(FullDetail).RunAsync(10);

        Assert.Equal(1, result.Checked);
        var after = await _db.TournamentDirectoryEntries.SingleAsync();
        Assert.Equal(FideEventDetailService.CurrentVersion, after.FideDetailVersion);
    }

    /// <summary>Auf dem aktuellen Stand bleibt es liegen — sonst waere die Fassung wertlos.</summary>
    [Fact]
    public async Task RunAsync_TheCurrentVersion_IsLeftAlone()
    {
        var entry = await AddFideEntryAsync();
        entry.FideDetailCheckedAt = DateTime.UtcNow.AddDays(-1);
        entry.FideDetailVersion = FideEventDetailService.CurrentVersion;
        await _db.SaveChangesAsync();

        Assert.Equal(0, (await CreateService(FullDetail).RunAsync(10)).Checked);
    }

    [Fact]
    public async Task RunAsync_SparseDetail_StillMarksItChecked()
    {
        var entry = await AddFideEntryAsync();

        var result = await CreateService(SparseDetail).RunAsync(10);

        Assert.Equal(1, result.Checked);
        await _db.Entry(entry).ReloadAsync();
        Assert.NotNull(entry.FideDetailCheckedAt);
        Assert.Equal(TournamentSystem.Other, entry.System);
        // Keine Bedenkzeit-Beschreibung, aber FIDEs Klasse traegt trotzdem.
        Assert.Equal(TournamentSpeed.Standard, entry.Speed);
        Assert.Null(entry.TimeControlText);
        Assert.Null(entry.Rounds);
    }

    /// <summary>
    /// Ein NETZfehler darf den Vermerk NICHT setzen — sonst gilt das Ereignis als nachgesehen,
    /// obwohl niemand hingesehen hat, und die Angaben fehlen fuer immer. Derselbe Unterschied wie
    /// beim Rundenplan zwischen „kein Plan hinterlegt" und „Abruf gescheitert".
    /// </summary>
    [Fact]
    public async Task RunAsync_CrawlerError_LeavesTheMarkUnset()
    {
        var entry = await AddFideEntryAsync();

        var result = await CreateService("boom", HttpStatusCode.InternalServerError).RunAsync(10);

        Assert.Equal(1, result.Checked);          // versucht …
        Assert.Equal(0, result.WithDetails);      // … aber nichts bekommen
        await _db.Entry(entry).ReloadAsync();
        Assert.Null(entry.FideDetailCheckedAt);   // … und deshalb weiter offen
    }

    /// <summary>Ein Eintrag ohne FIDE-Herkunft geht diesen Weg gar nicht.</summary>
    [Fact]
    public async Task RunAsync_EntryWithoutAFideSource_IsNotFetched()
    {
        _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
        {
            PublicId = "1405166", ChessResultsId = "1405166", Name = "Landesliga",
            Federation = "AUT", StartDate = new DateOnly(2026, 9, 26), EndDate = new DateOnly(2027, 4, 17),
        });
        await _db.SaveChangesAsync();

        Assert.Equal(0, (await CreateService(FullDetail).RunAsync(10)).Checked);
    }

    /// <summary>
    /// Ein Turnier, das auf BEIDEN Quellen steht, wird ebenfalls vorgenommen: die ausgeschriebene
    /// Bedenkzeit von FIDE ist auch dort eine Bereicherung. Kandidat ist die FIDE-HERKUNFT, nicht
    /// die fehlende chess-results-Nummer.
    /// </summary>
    [Fact]
    public async Task RunAsync_EntryOnBothSources_IsStillFetched()
    {
        await AddFideEntryAsync(publicId: "1405166", fideId: "5437", chessResultsId: "1405166");

        Assert.Equal(1, (await CreateService(FullDetail).RunAsync(10)).Checked);
    }

    /// <summary>
    /// Die Anschrift traegt eine Postleitzahl, und das ist der genaueste Weg des Geocoders — bei
    /// FIDE-Eintraegen greift er sonst nie. Hier mit einem Lexikoneintrag fuer 50012 Zaragoza.
    /// </summary>
    [Fact]
    public async Task RunAsync_VenueAddressWithPostalCode_GeocodesTheEntry()
    {
        var entry = await AddFideEntryAsync();
        entry.Federation = "ESP";
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "ES", PostalCode = "50012", Name = "Zaragoza",
            NameNormalized = "zaragoza", Lat = 41.65, Lon = -0.91,
            Kind = GeoPlaceKind.PostalCode, Population = 0,
        });
        await _db.SaveChangesAsync();

        var result = await CreateService(FullDetail).RunAsync(10);

        Assert.Equal(1, result.Geocoded);
        await _db.Entry(entry).ReloadAsync();
        Assert.Equal(GeoSource.PostalCode, entry.GeoSource);
        Assert.Equal(41.65, entry.Lat!.Value, 2);
    }

    /// <summary>
    /// Eine von Hand gesetzte Koordinate ueberlebt alles — dieselbe Zusage wie beim Sweep und
    /// beim erzwungenen Geocoding-Lauf.
    /// </summary>
    [Fact]
    public async Task RunAsync_ManualCoordinates_AreNeverOverwritten()
    {
        var entry = await AddFideEntryAsync();
        entry.Federation = "ESP";
        entry.Lat = 1.0;
        entry.Lon = 2.0;
        entry.GeoSource = GeoSource.Manual;
        _db.GeoPlaces.Add(new GeoPlace
        {
            Country = "ES", PostalCode = "50012", Name = "Zaragoza",
            NameNormalized = "zaragoza", Lat = 41.65, Lon = -0.91,
            Kind = GeoPlaceKind.PostalCode, Population = 0,
        });
        await _db.SaveChangesAsync();

        await CreateService(FullDetail).RunAsync(10);

        await _db.Entry(entry).ReloadAsync();
        Assert.Equal(1.0, entry.Lat);
        Assert.Equal(GeoSource.Manual, entry.GeoSource);
    }

    /// <summary>Ein zweiter Durchgang holt dieselbe Seite nicht noch einmal.</summary>
    [Fact]
    public async Task RunAsync_AlreadyChecked_IsNotFetchedAgain()
    {
        await AddFideEntryAsync();
        await CreateService(SparseDetail).RunAsync(10);

        Assert.Equal(0, (await CreateService(SparseDetail).RunAsync(10)).Checked);
    }

    /// <summary>
    /// … ausser mit <c>retryEmpty</c>, und dann in der Reihenfolge des Vermerk-ALTERS. Sortiert
    /// nach Termin naehme jeder Wiederholungslauf wieder dieselben vordersten Ereignisse — der
    /// Fehler, der beim Rundenplan-Nachtrag gemessen wurde (159 von 200 gerade erst geprueft).
    /// </summary>
    [Fact]
    public async Task RunAsync_WithRetryEmpty_TakesTheLeastRecentlyCheckedFirst()
    {
        var justChecked = await AddFideEntryAsync("f1", "1");
        justChecked.StartDate = new DateOnly(2026, 1, 1);        // frueherer Termin
        justChecked.FideDetailCheckedAt = DateTime.UtcNow;

        var stale = await AddFideEntryAsync("f2", "2");
        stale.StartDate = new DateOnly(2026, 6, 1);              // spaeterer Termin
        stale.FideDetailCheckedAt = DateTime.UtcNow.AddHours(-5);
        await _db.SaveChangesAsync();

        await CreateService(FullDetail).RunAsync(1, retryEmpty: true);

        await _db.Entry(stale).ReloadAsync();
        await _db.Entry(justChecked).ReloadAsync();
        Assert.Equal("90 minutes with 30 second increment from move 1", stale.TimeControlText);
        Assert.Null(justChecked.TimeControlText);
    }

    [Theory]
    [InlineData("Round-Robin", TournamentSystem.RoundRobin)]
    [InlineData("Swiss-System", TournamentSystem.Swiss)]
    [InlineData("swiss system", TournamentSystem.Swiss)]
    [InlineData("Other", TournamentSystem.Other)]
    // Unverstandener Text bleibt „noch nicht geklaert" und wird NICHT zu „ausdruecklich anderes".
    [InlineData("Knockout", TournamentSystem.Unknown)]
    [InlineData("", TournamentSystem.Unknown)]
    [InlineData(null, TournamentSystem.Unknown)]
    public void MapSystem_MapsTheSourceWording(string? text, TournamentSystem expected) =>
        Assert.Equal(expected, FideEventDetailService.MapSystem(text));

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://crawler:8080") };
    }

    private sealed class StubHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
