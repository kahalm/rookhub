using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Lesereihenfolge eines Buchs ist EINE Regel (<c>Round.Length, Round, Id</c>) mit zwei Gesichtern:
/// <see cref="ChapterOrder.InReadingOrder"/> sortiert in der DB, <see cref="ChapterOrder.Compare"/> vergleicht
/// in-memory (Weiter-Cursor). Bis 2026-09-29 stand die SQL-Seite 15-mal ausgeschrieben; die In-Memory-Seite
/// driftete schon einmal weg („Überspringen" zeigte bei Runden 9/10 dieselbe Aufgabe endlos). Dieser
/// Spiegeltest hält beide Seiten auf derselben Folge.
/// </summary>
public class ChapterOrderTests : IDisposable
{
    private readonly AppDbContext _db;

    public ChapterOrderTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Runden bewusst NICHT in Lesereihenfolge angelegt (Insert-Reihenfolge = Id): ungepolstert
    /// („9"/„10"), Chessable-gepolstert („001.003"), lang („9001"), leer, und eine doppelte Runde, bei der
    /// die Id entscheidet.</summary>
    private async Task<List<BookPuzzle>> SeedAsync()
    {
        var book = new Book
        {
            FileName = "order.pgn", DisplayName = "Order", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Source = new BookSource(),
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        var rounds = new[] { "10", "9001", "9", "001.003", "", "2", "10", "001.002", "1" };
        var lines = rounds.Select((r, i) => new BookPuzzle
        {
            LineId = $"order:{i}", BookFileName = book.FileName, BookId = book.Id, Round = r,
            Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", Moves = "",
        }).ToList();
        foreach (var l in lines)
        {
            _db.BookPuzzles.Add(l);
            await _db.SaveChangesAsync();   // einzeln → aufsteigende Ids in Insert-Reihenfolge
        }
        return lines;
    }

    [Fact]
    public async Task InReadingOrder_SortsByLengthThenRoundThenId()
    {
        var lines = await SeedAsync();

        var rounds = await _db.BookPuzzles.InReadingOrder().Select(bp => bp.Round).ToListAsync();

        Assert.Equal(new[] { "", "1", "2", "9", "10", "10", "9001", "001.002", "001.003" }, rounds);
        // Doppelte Runde „10": die kleinere Id zuerst.
        var tens = await _db.BookPuzzles.Where(bp => bp.Round == "10").InReadingOrder().Select(bp => bp.Id).ToListAsync();
        Assert.Equal(lines.Where(l => l.Round == "10").Select(l => l.Id).OrderBy(id => id), tens);
    }

    [Fact]
    public async Task InReadingOrder_MirrorsCompare()
    {
        await SeedAsync();

        var fromDb = await _db.BookPuzzles.InReadingOrder().Select(bp => bp.Id).ToListAsync();
        var inMemory = await _db.BookPuzzles.Select(bp => new { bp.Id, bp.Round }).ToListAsync();
        inMemory.Sort((a, b) => ChapterOrder.Compare(a.Round, a.Id, b.Round, b.Id));

        Assert.Equal(inMemory.Select(x => x.Id), fromDb);
    }

    [Fact]
    public void InReadingOrder_TranslatesToLengthRoundIdOnMySql()
    {
        // Gleiches SQL wie die frühere ausgeschriebene Kopie: ORDER BY CHAR_LENGTH(Round), Round, Id.
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql("server=localhost;database=x;user=x;password=x", DbServerVersion.Current)
            .Options);

        var sql = db.BookPuzzles.Where(bp => bp.BookId == 1).InReadingOrder().ToQueryString();

        Assert.Matches(@"ORDER BY CHAR_LENGTH\(`b`\.`Round`\), `b`\.`Round`, `b`\.`Id`", sql);
    }
}
