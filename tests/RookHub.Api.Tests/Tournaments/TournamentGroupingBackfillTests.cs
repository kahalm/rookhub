using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.TournamentDirectory;
using RookHub.Api.Services.Tournaments;

namespace RookHub.Api.Tests.Tournaments;

/// <summary>
/// Nachtragen der Gruppenschluessel im Altbestand. Ohne diesen Lauf steht ein bereits gefuelltes
/// Verzeichnis nach dem Deploy ungruppiert da (die Abfrage faellt auf <c>"id:" + Id</c> zurueck),
/// und Eintraege, die aus der chess-results-Suche verschwunden sind, bekaemen nie einen Schluessel.
/// </summary>
public class TournamentGroupingBackfillTests : IDisposable
{
    private readonly AppDbContext _db;

    public TournamentGroupingBackfillTests()
        => _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private TournamentDirectoryEntry Add(string crId, string name, string? location = "Braunau",
        string? groupKey = null, string? baseName = null)
    {
        var entry = new TournamentDirectoryEntry
        {
            PublicId = crId, ChessResultsId = crId,
            Name = name,
            Federation = "AUT",
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 7, 5),
            LocationText = location,
            GroupKey = groupKey,
            BaseName = baseName,
        };
        _db.TournamentDirectoryEntries.Add(entry);
        return entry;
    }

    [Fact]
    public async Task Backfill_GroupsSectionsOfOneTournament()
    {
        var a = Add("1", "Open Braunau 2026 A");
        var b = Add("2", "Open Braunau 2026 B");
        var other = Add("3", "Sommerturnier Linz", location: "Linz");
        await _db.SaveChangesAsync();

        var filled = await TournamentGroupingBackfillService.RegroupAsync(_db);

        Assert.Equal(3, filled);
        Assert.Equal("Open Braunau 2026", a.BaseName);
        Assert.Equal(a.GroupKey, b.GroupKey);          // dieselbe Veranstaltung
        Assert.NotEqual(a.GroupKey, other.GroupKey);   // ein anderes Turnier bleibt getrennt
    }

    [Fact]
    public async Task Backfill_MatchesWhatTheSweepWouldCompute()
    {
        // Der Bestand MUSS dieselbe Rechnung bekommen wie der naechste Sweep, sonst zerfaellt
        // ein Turnier nach der Nacht in zwei Eintraege (Bestand mit altem, Zulauf mit neuem Wert).
        var entry = Add("1", "Dekron Cup 2026 Gruppe A");
        await _db.SaveChangesAsync();
        await TournamentGroupingBackfillService.RegroupAsync(_db);
        entry = await _db.TournamentDirectoryEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);

        var reference = new TournamentDirectoryEntry
        {
            PublicId = "1", ChessResultsId = "1",
            Name = entry.Name,
            Federation = entry.Federation,
            StartDate = entry.StartDate,
            EndDate = entry.EndDate,
            LocationText = entry.LocationText,
        };
        TournamentDirectoryService.ApplyGrouping(reference);

        Assert.Equal(reference.GroupKey, entry.GroupKey);
        Assert.Equal(reference.BaseName, entry.BaseName);
    }

    /// <summary>
    /// UI-Sweep 2026-10-11 (r2-t-dup-cards): der Lauf GLEICHT AB statt nur aufzufuellen — ein
    /// veralteter Schluessel (alte Regel, oder vor dem Verorten gerechnet) wird nachgezogen.
    /// Idempotent: der zweite Lauf findet nichts mehr.
    /// </summary>
    [Fact]
    public async Task Regroup_RecomputesStaleKeys_AndIsIdempotent()
    {
        var stale = Add("1", "Open Braunau 2026 A", groupKey: "veraltet", baseName: "von Hand");
        Add("2", "Open Braunau 2026 B");
        await _db.SaveChangesAsync();

        Assert.Equal(2, await TournamentGroupingBackfillService.RegroupAsync(_db));
        Assert.Equal(0, await TournamentGroupingBackfillService.RegroupAsync(_db)); // zweiter Lauf: nichts zu tun

        var reloaded = await _db.TournamentDirectoryEntries.AsNoTracking().SingleAsync(e => e.Id == stale.Id);
        Assert.Equal("Open Braunau 2026", reloaded.BaseName);
        Assert.NotEqual("veraltet", reloaded.GroupKey);
    }

    /// <summary>
    /// Der Prod-Fall nach dem Deploy: zwei Gruppen eines Turniers mit unterschiedlich
    /// geschriebenem Ortstext trugen verschiedene (Text-)Schluessel. Nach dem Abgleich stehen sie
    /// unter EINEM Schluessel — ueber den verorteten Ort.
    /// </summary>
    [Fact]
    public async Task Regroup_RekeysExistingRows_ByGeocodedPlace()
    {
        var a = Add("1", "O přeborníka Šluknovského výběžku, skupina A", location: "Otáhalova 1260 Varnsdorf");
        var c = Add("2", "O přeborníka Šluknovského výběžku, skupina C", location: "Varnsdorf Otáhalova 1260");
        foreach (var e in new[] { a, c })
        {
            e.Federation = "CZE";
            // Alter Stand: Schluessel aus dem ROHEN Ortstext (vor dem Verorten gerechnet).
            TournamentDirectoryService.ApplyGrouping(e);
            e.Lat = 50.9115; e.Lon = 14.6182; e.GeoSource = GeoSource.City; e.GeoPlaceName = "Varnsdorf";
        }
        await _db.SaveChangesAsync();
        Assert.NotEqual(a.GroupKey, c.GroupKey);

        Assert.Equal(2, await TournamentGroupingBackfillService.RegroupAsync(_db));

        var keys = await _db.TournamentDirectoryEntries.AsNoTracking().Select(e => e.GroupKey).Distinct().ToListAsync();
        Assert.Single(keys);
    }

    [Fact]
    public async Task Backfill_WorksBeyondOneBatch()
    {
        for (var i = 0; i < TournamentGroupingBackfillService.BatchSize + 7; i++)
            Add(i.ToString(), $"Turnier {i} Gruppe A", location: $"Ort {i}");
        await _db.SaveChangesAsync();

        Assert.Equal(TournamentGroupingBackfillService.BatchSize + 7,
            await TournamentGroupingBackfillService.RegroupAsync(_db));
        Assert.Empty(await _db.TournamentDirectoryEntries.Where(e => e.GroupKey == null).ToListAsync());
    }
}
