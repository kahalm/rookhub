using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>
/// Prod 30.10.2026: <c>PUT /api/ebooks/{id}/progress</c> → 500 „Duplicate entry '461-25' for key 'PRIMARY'“. Zwei gleichzeitige
/// Speichervorgänge desselben Buchs und Lesers fanden beide noch keine Zeile und legten beide die erste an. Nachgestellt:
/// zwischen Lesen und Speichern schreibt ein „paralleler Request“ (zweiter Kontext auf derselben InMemory-DB) die Zeile, und
/// das Speichern scheitert wie bei MariaDB mit „Duplicate entry“. Erwartet: kein Fehler, eine Zeile, letzter Stand gewinnt.
/// </summary>
public class EbookProgressRaceTests : IDisposable
{
    private const string Anon = "0123456789abcdef0123456789abcdef";
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rh-ebooks-race-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* egal */ }
    }

    private AppDbContext NewDb(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(_dbName).AddInterceptors(interceptors).Options);

    private EbookService Service(AppDbContext db)
    {
        Directory.CreateDirectory(_root);
        var options = Microsoft.Extensions.Options.Options.Create(new EbookOptions { Root = _root });
        return new EbookService(db, new EbookStorage(options, NullLogger<EbookStorage>.Instance), NullLogger<EbookService>.Instance, options);
    }

    private async Task<(int UserId, int BookId)> SeedAsync(bool anonymousBook = false)
    {
        await using var db = NewDb();
        var u = new AppUser { Username = "leser", PasswordHash = "x", CreatedAt = DateTime.UtcNow };
        db.AppUsers.Add(u);
        await db.SaveChangesAsync();
        var b = new Ebook
        {
            Title = "Buch", OwnerUserId = anonymousBook ? null : u.Id, AnonymousSessionId = anonymousBook ? Anon : null,
            Tags = string.Empty, Source = EbookSource.Upload, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        b.Files.Add(new EbookFile { Format = "pdf", RelativePath = "files/x.pdf", FileName = "x.pdf", SizeBytes = 10 });
        db.Ebooks.Add(b);
        await db.SaveChangesAsync();
        return (u.Id, b.Id);
    }

    /// <summary>Vor dem ERSTEN Speichern mit einer neuen Lesestand-Zeile: der parallele Request legt sie an, dann scheitert
    /// das Speichern wie MariaDB mit „Duplicate entry … for key 'PRIMARY'“.</summary>
    private sealed class ParallelInsertFirst(Func<Task> parallel) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired && eventData.Context!.ChangeTracker.Entries()
                    .Any(e => e.State == EntityState.Added && e.Entity is EbookProgress or EbookAnonymousProgress))
            {
                Fired = true;
                await parallel();
                throw new DbUpdateException("An error occurred while saving the entity changes.",
                    new InvalidOperationException("Duplicate entry '461-25' for key 'PRIMARY'"));
            }
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task SetProgress_ConcurrentFirstInsert_UpdatesExistingRowInsteadOf500()
    {
        var (userId, bookId) = await SeedAsync();
        var race = new ParallelInsertFirst(async () =>
        {
            await using var other = NewDb();
            other.EbookProgresses.Add(new EbookProgress
            {
                EbookId = bookId, UserId = userId, Page = 3, PageCount = 100, Locator = "epubcfi(/6/2)", UpdatedAt = DateTime.UtcNow,
            });
            await other.SaveChangesAsync();
        });
        await using var db = NewDb(race);

        var dto = await Service(db).SetProgressAsync(userId, false, bookId, 7, 100);

        Assert.True(race.Fired);
        Assert.Equal(7, dto.Page);
        await using var check = NewDb();
        var row = await check.EbookProgresses.SingleAsync();
        Assert.Equal(7, row.Page);
        Assert.Equal(100, row.PageCount);
        Assert.Equal("epubcfi(/6/2)", row.Locator);   // ohne neue Fundstelle bleibt die des parallelen Requests
    }

    [Fact]
    public async Task SetProgress_Anonymous_ConcurrentFirstInsert_UpdatesExistingRow()
    {
        var (_, bookId) = await SeedAsync(anonymousBook: true);
        var race = new ParallelInsertFirst(async () =>
        {
            await using var other = NewDb();
            other.EbookAnonymousProgresses.Add(new EbookAnonymousProgress { EbookId = bookId, SessionId = Anon, Page = 2, UpdatedAt = DateTime.UtcNow });
            await other.SaveChangesAsync();
        });
        await using var db = NewDb(race);

        var dto = await Service(db).SetProgressAsync(EbookCaller.Anonymous(Anon), false, bookId, 9, 50, locator: "epubcfi(/6/8)");

        Assert.True(race.Fired);
        Assert.Equal(9, dto.Page);
        await using var check = NewDb();
        var row = await check.EbookAnonymousProgresses.SingleAsync();
        Assert.Equal(9, row.Page);
        Assert.Equal(50, row.PageCount);
        Assert.Equal("epubcfi(/6/8)", row.Locator);
    }

    [Fact]
    public async Task SetProgress_OtherDbUpdateError_IsNotSwallowed()
    {
        var (userId, bookId) = await SeedAsync();
        await using var db = NewDb(new RejectAddedInterceptorWithMessage("Data too long for column 'Locator'"));
        await Assert.ThrowsAsync<DbUpdateException>(() => Service(db).SetProgressAsync(userId, false, bookId, 7, 100));
    }

    private sealed class RejectAddedInterceptorWithMessage(string message) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries().Any(e => e.State == EntityState.Added && e.Entity is EbookProgress))
                throw new DbUpdateException("save failed", new InvalidOperationException(message));
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
