using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.TournamentDirectory;

namespace RookHub.Api.Tests.TournamentDirectory;

/// <summary>
/// Die Verschwunden-Erkennung der ZUSATZQUELLEN, diesmal ueber den GANZEN Durchgang
/// (<see cref="ExternalSweepRunner.RunAsync"/>) statt nur ueber
/// <see cref="ExternalDirectorySource.RetireVanishedAsync"/> (dafuer <see cref="RetireVanishedTests"/>).
/// TODO 2026-09-08: ein Turnier, das aus einem Verbandskalender verschwindet, darf nach der
/// Karenz zurueckgezogen werden — aber NUR nach einem Durchgang, der wirklich Daten hatte. Ein
/// leerer und ein mittendrin abgebrochener Durchgang duerfen nichts zurueckziehen.
///
/// <para>Zehn eigene Eintraege, weil die 90-%-Bremse einen einzelnen Fehlenden erst ab zehn
/// Kandidaten durchlaesst. Die Karenz (<see cref="ExternalDirectorySource.MissCooldown"/>) haengt an
/// der Uhr des Runners; die „naechste Nacht" entsteht, indem <c>LastMissAt</c> zurueckgestellt wird.</para>
/// </summary>
public class ExternalSweepRunnerRetireTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options);

    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    private sealed record Row(string Id, string Name, DateOnly Start);

    /// <summary>Zehn Zeilen mit festen Kennungen 990001..990010 und Namen ohne gemeinsames Wort.</summary>
    private static readonly Row[] All = [.. Enumerable.Range(1, 10)
        .Select(i => new Row($"9900{i:00}", $"Qx{i}wort Zy{i}pokal", Soon))];

    private static Row[] AllBut(int missing) => [.. All.Where(r => r.Id != $"9900{missing:00}")];

    private sealed class StubSource(AppDbContext db, string? throwAt = null) : IDirectorySourceAdapter<Row>
    {
        public DirectorySourceKind Kind => DirectorySourceKind.PolishChessFederation;
        public string LogName => "stub";
        public string? SourceKey(Row row) => row.Id;
        public DateOnly? Start(Row row) => row.Start;
        public string Name(Row row) => row.Name;
        public string PublicId(Row row) => "pl" + row.Id;
        public string? Federation(Row row) => "POL";
        public string? MatchPlace(Row row) => null;
        public string? Url(Row row) => null;

        public async Task<bool> ApplyAsync(TournamentDirectoryEntry own, Row row, DateOnly start, DateTime now,
            CancellationToken ct)
        {
            if (row.Id == throwAt) throw new HttpRequestException("Quelle bricht mitten im Durchgang ab");
            own.Name = row.Name;
            own.Federation = "POL";
            own.StartDate = start;
            own.EndDate = start;
            ExternalSweepRunner.MarkSeen(own, now);
            await ExternalDirectorySource.NoteSourceAsync(db, own, Kind, row.Id, null, now, ct);
            return false;
        }

        public void LogSummary(ILogger log, int read, int added, int updated, int matched, int retired) { }
    }

    private async Task<ExternalSweepResult> RunAsync(Row[] rows, string? throwAt = null)
    {
        await using var db = NewDb();
        return await ExternalSweepRunner.RunAsync(db, NullLogger.Instance, new StubSource(db, throwAt), rows,
            CancellationToken.None);
    }

    private async Task<TournamentDirectoryEntry> EntryAsync(int i)
    {
        await using var db = NewDb();
        return await db.TournamentDirectoryEntries.SingleAsync(e => e.PublicId == $"pl9900{i:00}");
    }

    /// <summary>Den letzten gezaehlten Fehlschlag um eine Nacht zurueckstellen.</summary>
    private async Task NextNightAsync()
    {
        await using var db = NewDb();
        foreach (var e in await db.TournamentDirectoryEntries.Where(e => e.LastMissAt != null).ToListAsync())
            e.LastMissAt = e.LastMissAt!.Value - ExternalDirectorySource.MissCooldown - TimeSpan.FromHours(1);
        await db.SaveChangesAsync();
    }

    /// <summary>Erstlauf mit allen zehn, dann eine Nacht ohne Nummer 10: ein Fehlschlag.</summary>
    private async Task SeedWithOneMissAsync()
    {
        var first = await RunAsync(All);
        Assert.Equal(10, first.Added);
        await RunAsync(AllBut(10));
        var missing = await EntryAsync(10);
        Assert.Equal(1, missing.MissedSweeps);
        Assert.Null(missing.RemovedAt);
        await NextNightAsync();
    }

    [Fact]
    public async Task Verschwunden_wirdNachDerSchwelleZurueckgezogen()
    {
        await SeedWithOneMissAsync();

        var result = await RunAsync(AllBut(10));

        Assert.Equal(1, result.Retired);
        var gone = await EntryAsync(10);
        Assert.Equal(ExternalDirectorySource.MissesUntilRetired, gone.MissedSweeps);
        Assert.NotNull(gone.RemovedAt);
        Assert.Null((await EntryAsync(9)).RemovedAt);
    }

    [Fact]
    public async Task LeererDurchgang_ziehtNichtsZurueck()
    {
        await SeedWithOneMissAsync();

        var result = await RunAsync([]);

        Assert.Equal(0, result.Retired);
        var entry = await EntryAsync(10);
        Assert.Equal(1, entry.MissedSweeps);
        Assert.Null(entry.RemovedAt);
        // Und die neun anderen sammeln auch keinen Fehlschlag.
        Assert.Equal(0, (await EntryAsync(1)).MissedSweeps);
    }

    [Fact]
    public async Task HalberDurchgang_FehlerMittendrin_ziehtNichtsZurueck()
    {
        await SeedWithOneMissAsync();

        // Der Lauf liefert neun Zeilen, bricht aber bei der fuenften ab.
        await Assert.ThrowsAsync<HttpRequestException>(() => RunAsync(AllBut(10), throwAt: "990005"));

        var entry = await EntryAsync(10);
        Assert.Equal(1, entry.MissedSweeps);
        Assert.Null(entry.RemovedAt);
        Assert.Equal(0, (await EntryAsync(6)).MissedSweeps);
        Assert.Null((await EntryAsync(6)).RemovedAt);
    }

    [Fact]
    public async Task HalberDurchgang_NurEinTeilGeliefert_ziehtNichtsZurueck()
    {
        await SeedWithOneMissAsync();

        // Eine Antwort, die nach drei Zeilen abreisst (Seite 1 von 3): die 90-%-Bremse.
        var result = await RunAsync([.. All.Take(3)]);

        Assert.Equal(0, result.Retired);
        Assert.Null((await EntryAsync(10)).RemovedAt);
        Assert.Equal(1, (await EntryAsync(10)).MissedSweeps);
        Assert.Equal(0, (await EntryAsync(4)).MissedSweeps);
    }

    [Fact]
    public async Task WiederAufgetaucht_setztMissedSweepsZurueck()
    {
        await SeedWithOneMissAsync();

        await RunAsync(All);

        var back = await EntryAsync(10);
        Assert.Equal(0, back.MissedSweeps);
        Assert.Null(back.LastMissAt);
        Assert.Null(back.RemovedAt);

        // Und die naechste Luecke beginnt wieder bei eins, nicht bei der Schwelle.
        await NextNightAsync();
        await RunAsync(AllBut(10));
        Assert.Equal(1, (await EntryAsync(10)).MissedSweeps);
        Assert.Null((await EntryAsync(10)).RemovedAt);
    }
}
