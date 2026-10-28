using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Leihe der Diagramm-Erkennung (0.870.0) gegen ECHTES MariaDB: das bedingte <c>UPDATE … WHERE Pending AND Leihe frei</c>
/// (ExecuteUpdate, InMemory kennt es nicht) lässt von zwei Workern, die dasselbe Buch offen geladen haben, nur einen gewinnen;
/// die Warteschlange (Vorrang zuerst) und die Auswahl der externen Worker übersetzen sich.
/// </summary>
public class EbookDiagramLeaseSqlTests(EbookDiagramLeaseSqlFixture fixture) : IAsyncLifetime, IClassFixture<EbookDiagramLeaseSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static Ebook Book(string title, bool priority = false, string format = "pdf") => new()
    {
        Title = title, Source = EbookSource.Library, DiagramState = EbookDiagramState.Pending, DiagramPriority = priority,
        Files = [new EbookFile { Format = format, RelativePath = title + "." + format, FileName = title + "." + format }],
    };

    [MySqlFact]
    public async Task Claim_OnlyOneOfTwoWorkersWins_ExpiredLeaseIsFree_QueueAndClaimableTranslate()
    {
        await using var db = fixture.Schema.NewContext();
        var plain = Book("Bibliothek");
        var urgent = Book("Vorrang", priority: true);
        var djvu = Book("Nur DJVU", format: "djvu");
        db.Ebooks.AddRange(plain, urgent, djvu);
        await db.SaveChangesAsync();

        var order = await EbookDiagramWorker.QueueOrder(EbookDiagramLease.Free(db.Ebooks.Where(b => b.DiagramState == EbookDiagramState.Pending), DateTime.UtcNow))
            .Select(b => b.Title).ToListAsync();
        Assert.Equal(["Vorrang", "Bibliothek", "Nur DJVU"], order);
        var claimable = await EbookDiagramWorkerService.Claimable(db.Ebooks, DateTime.UtcNow).OrderBy(b => b.Id).Select(b => b.Title).ToListAsync();
        Assert.Equal(["Bibliothek", "Vorrang"], claimable);

        // Server-Worker hat das Buch getrackt geladen; ein externer Worker leiht es vorher.
        var tracked = await db.Ebooks.SingleAsync(b => b.Id == urgent.Id);
        await using (var other = fixture.Schema.NewContext())
            Assert.NotNull(await EbookDiagramLease.TryClaimAsync(other, urgent.Id, "pat:1", EbookDiagramLease.ExternalLease, CancellationToken.None));
        Assert.Null(await EbookDiagramLease.TryClaimAsync(db, urgent.Id, EbookDiagramLease.ServerOwner, EbookDiagramLease.ServerLease, CancellationToken.None, tracked));

        await using (var check = fixture.Schema.NewContext())
        {
            var row = await check.Ebooks.SingleAsync(b => b.Id == urgent.Id);
            Assert.Equal("pat:1", row.DiagramLeaseOwner);
            row.DiagramLeaseUntil = DateTime.UtcNow.AddMinutes(-1);   // abgelaufen
            await check.SaveChangesAsync();
        }
        Assert.NotNull(await EbookDiagramLease.TryClaimAsync(db, urgent.Id, EbookDiagramLease.ServerOwner, EbookDiagramLease.ServerLease, CancellationToken.None, tracked));

        // Löschen über den Tracker schreibt, obwohl die Leihe am Tracker vorbei gesetzt wurde.
        EbookDiagramLease.Clear(db, tracked);
        await db.SaveChangesAsync();
        await using var final = fixture.Schema.NewContext();
        Assert.Null((await final.Ebooks.SingleAsync(b => b.Id == urgent.Id)).DiagramLeaseOwner);
    }
}

public sealed class EbookDiagramLeaseSqlFixture() : MariaDbClassFixture("ebooklease", withApp: false);
