using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

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
            ChessResultsId = crId,
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

        var filled = await TournamentGroupingBackfillService.BackfillAsync(_db);

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
        await TournamentGroupingBackfillService.BackfillAsync(_db);

        var reference = new TournamentDirectoryEntry
        {
            ChessResultsId = "1",
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

    [Fact]
    public async Task Backfill_LeavesExistingKeysAlone_AndIsIdempotent()
    {
        var untouched = Add("1", "Open Braunau 2026 A", groupKey: "handgesetzt", baseName: "von Hand");
        Add("2", "Open Braunau 2026 B");
        await _db.SaveChangesAsync();

        Assert.Equal(1, await TournamentGroupingBackfillService.BackfillAsync(_db));
        Assert.Equal(0, await TournamentGroupingBackfillService.BackfillAsync(_db)); // zweiter Start: nichts zu tun
        Assert.Equal("handgesetzt", untouched.GroupKey);
        Assert.Equal("von Hand", untouched.BaseName);
    }

    [Fact]
    public async Task Backfill_WorksBeyondOneBatch()
    {
        for (var i = 0; i < TournamentGroupingBackfillService.BatchSize + 7; i++)
            Add(i.ToString(), $"Turnier {i} Gruppe A", location: $"Ort {i}");
        await _db.SaveChangesAsync();

        Assert.Equal(TournamentGroupingBackfillService.BatchSize + 7,
            await TournamentGroupingBackfillService.BackfillAsync(_db));
        Assert.Empty(await _db.TournamentDirectoryEntries.Where(e => e.GroupKey == null).ToListAsync());
    }
}
