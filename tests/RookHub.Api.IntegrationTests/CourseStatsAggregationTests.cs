using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Die Kursstatistik zaehlt den „ersten Versuch" je Puzzle. Das lief frueher im Speicher ueber
/// ALLE Versuchszeilen des Users in diesem Buch — bei jedem /next und /results, wachsend mit der
/// Historie. Jetzt macht das eine Gruppierung mit korrelierter Unterabfrage
/// (ORDER BY AttemptedAt, Id LIMIT 1).
///
/// <para>Warum das hier und nicht in der Unit-Suite steht: EF InMemory wertet solche Abfragen
/// klaglos im Speicher aus. Ob MySQL sie uebersetzen KANN und ob der Reset-Filter in die
/// Unterabfrage mitwandert, zeigt sich nur gegen eine echte Datenbank.</para>
/// </summary>
[Collection(ApiFactoryCollection.Name)]
public class CourseStatsAggregationTests : IAsyncLifetime
{
    private MariaDbSchema _schema = null!;
    private ApiFactory _factory = null!;
    private IServiceScope _scope = null!;

    public async Task InitializeAsync()
    {
        _schema = await MariaDbSchema.CreateAsync("stats");
        await using (var m = _schema.NewContext()) await m.Database.MigrateAsync();
        _factory = new ApiFactory(_schema.ConnectionString);
        _scope = _factory.Services.CreateScope();
    }

    public async Task DisposeAsync()
    {
        _scope?.Dispose();
        _factory?.Dispose();
        if (_schema is not null) await _schema.DisposeAsync();
    }

    private AppDbContext Db => _scope.ServiceProvider.GetRequiredService<AppDbContext>();

    private async Task<(int userId, int bookId, List<int> puzzleIds)> SeedAsync(int puzzles)
    {
        var user = new AppUser { Username = "s", Email = "s@t.local", PasswordHash = "x" };
        Db.AppUsers.Add(user);
        await Db.SaveChangesAsync();
        var book = new Book { FileName = "stats.pgn", DisplayName = "S", OwnerUserId = user.Id };
        Db.Books.Add(book);
        await Db.SaveChangesAsync();
        var ids = new List<int>();
        for (var i = 0; i < puzzles; i++)
        {
            var bp = new BookPuzzle
            {
                BookId = book.Id, BookFileName = book.FileName, Chapter = "A",
                LineId = Guid.NewGuid().ToString("N"), Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", Moves = "a1",
            };
            Db.BookPuzzles.Add(bp);
            await Db.SaveChangesAsync();
            ids.Add(bp.Id);
        }
        return (user.Id, book.Id, ids);
    }

    private void AddAttempt(int userId, int bookId, int puzzleId, bool solved, int seconds, DateTime at)
        => Db.CourseAttempts.Add(new CourseAttempt
        {
            UserId = userId, BookId = bookId, BookPuzzleId = puzzleId,
            Solved = solved, TimeSeconds = seconds, AttemptedAt = at,
        });

    [MySqlFact]
    public async Task ErsterVersuchZaehlt_NichtDerBeste()
    {
        var (userId, bookId, ids) = await SeedAsync(2);
        var t0 = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
        // Puzzle 1: erst falsch (30 s), dann richtig (20 s) -> Erst-Versuch FALSCH.
        AddAttempt(userId, bookId, ids[0], false, 30, t0);
        AddAttempt(userId, bookId, ids[0], true, 20, t0.AddMinutes(5));
        // Puzzle 2: gleich richtig (10 s).
        AddAttempt(userId, bookId, ids[1], true, 10, t0.AddMinutes(1));
        await Db.SaveChangesAsync();

        var next = await _scope.ServiceProvider.GetRequiredService<CourseService>()
            .GetNextAsync(userId, bookId, "sequential", null, null, isAdmin: true);

        Assert.Equal(60, next.Book!.TotalSeconds);     // alle Versuche zaehlen
        Assert.Equal(2, next.Book.AttemptedCount);     // zwei Puzzles angefasst
        Assert.Equal(1, next.Book.FirstTryCorrect);    // nur Puzzle 2
        Assert.Equal(50, next.Book.AccuracyPercent);
    }

    [MySqlFact]
    public async Task NachEinemReset_ZaehltDerErsteVersuchDANACH()
    {
        // DER kritische Fall: der Reset-Filter MUSS in die korrelierte Unterabfrage mitwandern.
        // Tut er es nicht, holt sie den Versuch von VOR dem Zuruecksetzen und die Trefferquote
        // bliebe fuer immer auf dem alten Fehlversuch stehen.
        var (userId, bookId, ids) = await SeedAsync(1);
        var t0 = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
        AddAttempt(userId, bookId, ids[0], false, 30, t0);                 // vor dem Reset: falsch
        var reset = t0.AddHours(1);
        Db.CourseProgresses.Add(new CourseProgress { UserId = userId, BookId = bookId, ResetAt = reset });
        AddAttempt(userId, bookId, ids[0], true, 7, reset.AddMinutes(1));  // danach: richtig
        await Db.SaveChangesAsync();

        var next = await _scope.ServiceProvider.GetRequiredService<CourseService>()
            .GetNextAsync(userId, bookId, "sequential", null, null, isAdmin: true);

        Assert.Equal(7, next.Book!.TotalSeconds);      // der Versuch von vorher zaehlt nicht mehr
        Assert.Equal(1, next.Book.AttemptedCount);
        Assert.Equal(1, next.Book.FirstTryCorrect);    // waere 0, wenn der alte Versuch durchschlaegt
        Assert.Equal(100, next.Book.AccuracyPercent);
    }

    [MySqlFact]
    public async Task GleicheZeitstempel_EntscheidetDieId()
    {
        // Die Sortierung ist (AttemptedAt, Id) - bei identischem Zeitstempel gibt die Id den
        // Ausschlag. Ohne das zweite Kriterium waere das Ergebnis von der Datenbank abhaengig.
        var (userId, bookId, ids) = await SeedAsync(1);
        var t = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
        AddAttempt(userId, bookId, ids[0], false, 5, t);   // kleinere Id -> das ist der erste
        AddAttempt(userId, bookId, ids[0], true, 5, t);
        await Db.SaveChangesAsync();

        var next = await _scope.ServiceProvider.GetRequiredService<CourseService>()
            .GetNextAsync(userId, bookId, "sequential", null, null, isAdmin: true);

        Assert.Equal(0, next.Book!.FirstTryCorrect);
        Assert.Equal(0, next.Book.AccuracyPercent);
    }
}
