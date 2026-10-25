using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Courses;

namespace RookHub.Api.Tests.Courses;

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

    /// <summary>EINE Kapitel-Normalisierung (Entscheidung User 25.10.: überall trimmen) — in-memory
    /// (<see cref="ChapterOrder.NormalizeChapter"/>) und als SQL-Filter (<see cref="ChapterOrder.WhereChapter"/>)
    /// dieselbe Gruppe; die Kalk-Sperre vergleicht ebenso getrimmt (<see cref="CalcVisibility.IsHidden"/>).</summary>
    [Fact]
    public async Task NormalizeChapter_Trims_AndWhereChapterMatchesTheSameLines()
    {
        Assert.Null(ChapterOrder.NormalizeChapter(null));
        Assert.Null(ChapterOrder.NormalizeChapter("  "));
        Assert.Equal("Kapitel A", ChapterOrder.NormalizeChapter(" Kapitel A "));

        var book = new Book { FileName = "c.pgn", Source = new BookSource() };
        string?[] chapters = ["Kapitel A", " Kapitel A ", "Kapitel B", null, "", "  "];
        foreach (var (c, i) in chapters.Select((c, i) => (c, i)))
            _db.BookPuzzles.Add(new BookPuzzle { Book = book, LineId = $"c:{i}", BookFileName = "c.pgn", Round = $"{i}", Chapter = c, Fen = "x", Moves = "" });
        await _db.SaveChangesAsync();

        Assert.Equal(2, await _db.BookPuzzles.WhereChapter("Kapitel A").CountAsync());
        Assert.Equal(3, await _db.BookPuzzles.WhereChapter(null).CountAsync());
        Assert.Equal([null, "Kapitel A", "Kapitel B"],
            (await ChapterOrder.GetOrderedChapterNamesAsync(_db, book.Id)).Order());

        var hidden = new HashSet<string>(StringComparer.Ordinal) { "Woche 3" };
        Assert.True(CalcVisibility.IsHidden(hidden, " Woche 3 "));
        Assert.False(CalcVisibility.IsHidden(hidden, "Woche 2"));
        Assert.False(CalcVisibility.IsHidden(hidden, null));
    }

    [Fact]
    public void WhereChapter_TrimsTheColumnOnMySql()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql("server=localhost;database=x;user=x;password=x", DbServerVersion.Current)
            .Options);

        var sql = db.BookPuzzles.WhereChapter("Kapitel A").ToQueryString();

        Assert.Contains("TRIM(`b`.`Chapter`)", sql);
    }
}
