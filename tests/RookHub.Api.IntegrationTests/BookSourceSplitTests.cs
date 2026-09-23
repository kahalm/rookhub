using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>Merkt sich den Text aller abgesetzten Lese-Befehle.</summary>
public sealed class CommandTextRecorder : DbCommandInterceptor
{
    public List<string> Commands { get; } = new();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken ct = default)
    {
        Commands.Add(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, ct);
    }
}

/// <summary>
/// Das Roh-PGN liegt seit 0.508.2 per TABELLENSPLITTING in <see cref="BookSource"/> — dieselbe Zeile
/// in <c>Books</c>, aber eine eigene Entität, damit <c>.Include(bp =&gt; bp.Book)</c> es nicht mehr
/// mitlädt (vorher 11 GB aus der DB für EINEN Kurs-Puzzle-Request).
///
/// <para>Warum gegen MariaDB: InMemory kennt kein Tabellensplitting (dort sind Book und BookSource
/// zwei getrennte „Tabellen"). Ob Anlegen, Ändern und Löschen auf der GETEILTEN Zeile richtig
/// ankommen — insbesondere, dass ein Buch-Update ohne geladene Source den Text NICHT überschreibt
/// und ein Löschen ohne geladene Source die Zeile trotzdem entfernt —, zeigt sich nur hier.</para>
/// </summary>
public class BookSourceSplitTests(BookSourceSplitFixture fixture)
    : IAsyncLifetime, IClassFixture<BookSourceSplitFixture>
{
    private readonly List<AppDbContext> _contexts = new();

    public async Task InitializeAsync() => await fixture.ResetAsync();

    public async Task DisposeAsync()
    {
        foreach (var db in _contexts) await db.DisposeAsync();
    }

    private AppDbContext Fresh(CommandTextRecorder? recorder = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(fixture.Schema.ConnectionString, new MySqlServerVersion(new Version(11, 0, 0)));
        if (recorder != null) builder.AddInterceptors(recorder);
        var db = new AppDbContext(builder.Options);
        _contexts.Add(db);
        return db;
    }

    /// <summary>Spalte direkt per SQL lesen — am Mapping vorbei.</summary>
    private async Task<string?> RawSourcePgnAsync(int bookId) =>
        await Fresh().Database
            .SqlQuery<string?>($"SELECT SourcePgn AS Value FROM Books WHERE Id = {bookId}")
            .SingleAsync();

    private async Task<int> SeedAsync(string fileName, string? sourcePgn)
    {
        var db = Fresh();
        var book = new Book
        {
            FileName = fileName, DisplayName = fileName, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Source = new BookSource { SourcePgn = sourcePgn },
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book.Id;
    }

    [MySqlFact]
    public async Task Anlegen_SchreibtDenTextInDieSpalteDerBuchzeile()
    {
        var mit = await SeedAsync("mit.pgn", "1. e4 e5 *");
        var ohne = await SeedAsync("ohne.pgn", null);

        Assert.Equal("1. e4 e5 *", await RawSourcePgnAsync(mit));
        Assert.Null(await RawSourcePgnAsync(ohne));
        Assert.Equal(2, await Fresh().Books.CountAsync());   // eine Zeile je Buch, keine Extratabelle
    }

    [MySqlFact]
    public async Task Laden_OhneInclude_Null_MitInclude_InstanzAuchOhneText()
    {
        var mit = await SeedAsync("mit.pgn", "1. d4 *");
        var ohne = await SeedAsync("ohne.pgn", null);

        Assert.Null((await Fresh().Books.SingleAsync(b => b.Id == mit)).Source);

        var db = Fresh();
        var a = await db.Books.Include(b => b.Source).SingleAsync(b => b.Id == mit);
        var b = await db.Books.Include(b => b.Source).SingleAsync(b => b.Id == ohne);
        Assert.Equal("1. d4 *", a.Source.SourcePgn);
        Assert.NotNull(b.Source);
        Assert.Null(b.Source.SourcePgn);
    }

    [MySqlFact]
    public async Task BuchAendern_OhneGeladeneSource_LaesstDenTextStehen()
    {
        var id = await SeedAsync("k.pgn", "1. c4 *");

        var db = Fresh();
        var book = await db.Books.SingleAsync(b => b.Id == id);
        book.DisplayName = "Umbenannt";
        await db.SaveChangesAsync();

        Assert.Equal("1. c4 *", await RawSourcePgnAsync(id));
    }

    [MySqlFact]
    public async Task Schreiben_UeberInclude_AendertNurDenText()
    {
        var id = await SeedAsync("k.pgn", "alt");

        var db = Fresh();
        var book = await db.Books.Include(b => b.Source).SingleAsync(b => b.Id == id);
        book.Source.SourcePgn = "neu";
        await db.SaveChangesAsync();

        Assert.Equal("neu", await RawSourcePgnAsync(id));
        Assert.Equal("k.pgn", (await Fresh().Books.SingleAsync(b => b.Id == id)).DisplayName);
    }

    [MySqlFact]
    public async Task DeleteBook_OhneGeladeneSource_EntferntDieZeile()
    {
        var id = await SeedAsync("weg.pgn", "1. f4 *");
        var bleibt = await SeedAsync("bleibt.pgn", "1. b3 *");

        await new BookAdminService(Fresh()).DeleteBookAsync(id);

        var db = Fresh();
        Assert.False(await db.Books.AnyAsync(b => b.Id == id));
        Assert.Equal("1. b3 *", await RawSourcePgnAsync(bleibt));
    }

    [MySqlFact]
    public async Task KursPuzzleAbfrage_UebertraegtKeinSourcePgn()
    {
        var id = await SeedAsync("kurs.pgn", new string('x', 100_000));
        var seed = Fresh();
        seed.BookPuzzles.Add(new BookPuzzle { LineId = "kurs.pgn:1", BookId = id, BookFileName = "kurs.pgn", Round = "1", Fen = "f", Moves = "e2e4" });
        seed.BookPuzzles.Add(new BookPuzzle { LineId = "kurs.pgn:2", BookId = id, BookFileName = "kurs.pgn", Round = "2", Fen = "f", Moves = "d2d4" });
        await seed.SaveChangesAsync();

        var recorder = new CommandTextRecorder();
        var puzzles = await CourseService.PuzzlesWithBookInReadingOrder(Fresh(recorder), id).ToListAsync();

        Assert.Equal(2, puzzles.Count);
        Assert.All(puzzles, p => Assert.Equal("kurs.pgn", p.Book!.DisplayName));
        Assert.All(puzzles, p => Assert.Null(p.Book!.Source));
        Assert.DoesNotContain(recorder.Commands, c => c.Contains("SourcePgn"));
    }
}
