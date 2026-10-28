using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Wer ein Buch gerade erkennt (0.870.0): der eigene <see cref="EbookDiagramWorker"/> oder ein externer Diagramm-Worker
/// (<c>/api/diagram-worker</c>, Laptop). Ein Buch bleibt währenddessen <see cref="EbookDiagramState.Pending"/>; die Leihe
/// (<see cref="Ebook.DiagramLeaseId"/>/<see cref="Ebook.DiagramLeaseOwner"/>/<see cref="Ebook.DiagramLeaseUntil"/>) sperrt es nur
/// für die anderen. Läuft sie ab (Worker abgestürzt, Laptop zugeklappt), ist das Buch von selbst wieder frei — kein
/// Aufräumlauf nötig. Übernommen wird ATOMAR (<c>UPDATE … WHERE</c> auf freie Leihe), damit nie zwei dasselbe Buch rechnen.
/// </summary>
public static class EbookDiagramLease
{
    /// <summary>Besitzer der Leihe des eigenen Workers.</summary>
    public const string ServerOwner = "server";

    /// <summary>Der eigene Worker leiht lang (Riesenbände brauchen Stunden); nach einem Neustart gibt er seine Leihen zurück.</summary>
    public static readonly TimeSpan ServerLease = TimeSpan.FromHours(12);

    /// <summary>Externe Worker leihen kurz und verlängern per Lebenszeichen (<c>…/heartbeat</c>).</summary>
    public static readonly TimeSpan ExternalLease = TimeSpan.FromMinutes(30);

    /// <summary>Bücher ohne gültige Leihe.</summary>
    public static IQueryable<Ebook> Free(IQueryable<Ebook> books, DateTime now)
        => books.Where(b => b.DiagramLeaseUntil == null || b.DiagramLeaseUntil < now);

    /// <summary>Leiht ein noch wartendes, freies Buch für <paramref name="owner"/> → die Kennung der Leihe, oder <c>null</c>,
    /// wenn ein anderer schneller war. Auf MariaDB ein einziges <c>UPDATE … WHERE</c> (echte Atomarität); die InMemory-Tests
    /// prüfen dieselbe Bedingung getrackt. Ein getracktes <paramref name="tracked"/> bekommt die Werte mit.</summary>
    public static async Task<Guid?> TryClaimAsync(AppDbContext db, int ebookId, string owner, TimeSpan duration, CancellationToken ct,
        Ebook? tracked = null)
    {
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        var until = now + duration;
        if (db.Database.IsRelational())
        {
            var rows = await db.Ebooks
                .Where(b => b.Id == ebookId && b.DiagramState == EbookDiagramState.Pending && (b.DiagramLeaseUntil == null || b.DiagramLeaseUntil < now))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.DiagramLeaseId, id)
                    .SetProperty(b => b.DiagramLeaseOwner, owner)
                    .SetProperty(b => b.DiagramLeaseUntil, until), ct);
            if (rows != 1) return null;
            if (tracked != null) { tracked.DiagramLeaseId = id; tracked.DiagramLeaseOwner = owner; tracked.DiagramLeaseUntil = until; }
            return id;
        }
        var book = tracked ?? await db.Ebooks.FirstOrDefaultAsync(b => b.Id == ebookId, ct);
        if (book == null || book.DiagramState != EbookDiagramState.Pending || (book.DiagramLeaseUntil != null && book.DiagramLeaseUntil >= now))
            return null;
        book.DiagramLeaseId = id; book.DiagramLeaseOwner = owner; book.DiagramLeaseUntil = until;
        await db.SaveChangesAsync(ct);
        return id;
    }

    /// <summary>Ist <paramref name="leaseId"/> noch die gültige Leihe von <paramref name="owner"/> an diesem Buch?</summary>
    public static bool Holds(Ebook book, Guid leaseId, string owner, DateTime now)
        => book.DiagramState == EbookDiagramState.Pending && book.DiagramLeaseId == leaseId && book.DiagramLeaseOwner == owner
           && book.DiagramLeaseUntil is { } until && until >= now;

    /// <summary>Leihe löschen (getrackt; die Eigenschaften werden ausdrücklich als geändert markiert, damit das Löschen auch
    /// dann geschrieben wird, wenn die Leihe per <c>ExecuteUpdate</c> am Tracker vorbei gesetzt wurde).</summary>
    public static void Clear(AppDbContext db, Ebook book)
    {
        book.DiagramLeaseId = null; book.DiagramLeaseOwner = null; book.DiagramLeaseUntil = null;
        var e = db.Entry(book);
        if (e.State is EntityState.Detached or EntityState.Added) return;
        e.Property(b => b.DiagramLeaseId).IsModified = true;
        e.Property(b => b.DiagramLeaseOwner).IsModified = true;
        e.Property(b => b.DiagramLeaseUntil).IsModified = true;
    }

    /// <summary>Alle Leihen eines Besitzers zurückgeben (der eigene Worker nach einem Neustart) → Anzahl.</summary>
    public static async Task<int> ReleaseOwnerAsync(AppDbContext db, string owner, CancellationToken ct)
    {
        var books = await db.Ebooks.Where(b => b.DiagramLeaseOwner == owner).ToListAsync(ct);
        foreach (var b in books) Clear(db, b);
        if (books.Count > 0) await db.SaveChangesAsync(ct);
        return books.Count;
    }
}
