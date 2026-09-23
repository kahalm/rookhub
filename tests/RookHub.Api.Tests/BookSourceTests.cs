using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Umgang mit dem ausgelagerten Roh-PGN (<see cref="BookSource"/>, Tabellensplitting auf <c>Books</c>):
/// Anlegen, Lesen, Schreiben, Löschen.
///
/// <para>Wichtig ist der FRISCHE Kontext je Schritt: die übrigen Tests legen Buch und Aufruf im selben
/// DbContext an — dort ist die Source schon getrackt und ein vergessenes <c>.Include(b =&gt; b.Source)</c>
/// fiele nie auf. Hier kennt der Kontext des Aufrufs das Buch nicht vorher; fehlt das Include, knallt
/// es (NullReference) bzw. der Text fehlt.</para>
/// </summary>
public class BookSourceTests : IDisposable
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    // Roh-PGN mit Variante + Kommentar: kommt es verbatim zurück, stammt es aus BookSource (die
    // Rekonstruktion aus den BookPuzzles kennt keine Varianten).
    private const string RawPgn = "[Event \"Kurs\"]\n[Round \"001.001\"]\n[Black \"Kapitel A\"]\n[FEN \"" + StartFen + "\"]\n\n"
        + "1. e4 (1. d4 {Damengambit}) e5 *\n";

    private const string ReprocessPgn = @"
[Event ""X""]
[Round ""1""]
[FEN ""rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2""]

2. Nf3 {Develops.} Nc6 3. Bb5 {The pin.} a6 *
";

    private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private readonly List<AppDbContext> _contexts = new();

    /// <summary>Neuer Kontext auf DERSELBEN InMemory-DB — ohne getrackte Entitäten.</summary>
    private AppDbContext Fresh()
    {
        var db = new AppDbContext(_options);
        _contexts.Add(db);
        return db;
    }

    public void Dispose()
    {
        foreach (var db in _contexts) db.Dispose();
    }

    private async Task<int> SeedBookAsync(string? sourcePgn, int importVersion = 0, string fileName = "kurs.pgn")
    {
        var db = Fresh();
        var book = new Book
        {
            FileName = fileName, DisplayName = "Kurs", OwnerUserId = 1, ImportVersion = importVersion,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Source = new BookSource { SourcePgn = sourcePgn },
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book.Id;
    }

    private async Task<int> SeedLineAsync(int bookId, string round, string? chapter, string moves)
    {
        var db = Fresh();
        var line = new BookPuzzle
        {
            LineId = $"kurs.pgn:{round}", BookId = bookId, BookFileName = "kurs.pgn", Round = round,
            Chapter = chapter, Title = $"Linie {round}", Fen = StartFen, Moves = moves, StartPly = -1,
        };
        db.BookPuzzles.Add(line);
        await db.SaveChangesAsync();
        return line.Id;
    }

    // ===== Anlegen / Laden =====

    [Fact]
    public async Task Anlegen_MitSource_SpeichertDenTextUnterDerBuchId()
    {
        var id = await SeedBookAsync(RawPgn);

        var source = await Fresh().BookSources.SingleAsync();
        Assert.Equal(id, source.Id);                 // PK = FK = Books.Id (dieselbe Zeile in MariaDB)
        Assert.Equal(RawPgn, source.SourcePgn);
    }

    [Fact]
    public async Task Laden_OhneInclude_SourceBleibtNull()
    {
        var id = await SeedBookAsync(RawPgn);

        var book = await Fresh().Books.SingleAsync(b => b.Id == id);

        Assert.Null(book.Source);                    // der Text kommt NICHT beiläufig mit
    }

    [Fact]
    public async Task Laden_MitInclude_LiefertInstanz_AuchOhneText()
    {
        var mitText = await SeedBookAsync(RawPgn, fileName: "a.pgn");
        var ohneText = await SeedBookAsync(null, fileName: "b.pgn");

        var db = Fresh();
        var a = await db.Books.Include(b => b.Source).SingleAsync(b => b.Id == mitText);
        var b = await db.Books.Include(b => b.Source).SingleAsync(b => b.Id == ohneText);

        Assert.Equal(RawPgn, a.Source.SourcePgn);
        Assert.NotNull(b.Source);                    // Pflicht-Navigation: Instanz auch bei SourcePgn = NULL
        Assert.Null(b.Source.SourcePgn);
    }

    [Fact]
    public async Task Schreiben_UeberInclude_AendertNurDenText()
    {
        var id = await SeedBookAsync(RawPgn);

        var db = Fresh();
        var book = await db.Books.Include(b => b.Source).SingleAsync(b => b.Id == id);
        book.Source.SourcePgn = "neu";
        await db.SaveChangesAsync();

        var check = await Fresh().Books.Include(b => b.Source).SingleAsync(b => b.Id == id);
        Assert.Equal("neu", check.Source.SourcePgn);
        Assert.Equal("Kurs", check.DisplayName);
    }

    // ===== Löschen =====

    [Fact]
    public async Task DeleteBook_OhneGeladeneSource_EntferntBuchUndSource()
    {
        var id = await SeedBookAsync(RawPgn);
        await SeedLineAsync(id, "001.001", "Kapitel A", "e2e4 e7e5");

        await new BookAdminService(Fresh()).DeleteBookAsync(id);

        var db = Fresh();
        Assert.False(await db.Books.AnyAsync());
        Assert.False(await db.BookSources.AnyAsync());   // InMemory kaskadiert nicht → Stub-Pfad nötig
        Assert.False(await db.BookPuzzles.AnyAsync());
    }

    [Fact]
    public async Task DeleteBook_MitSchonGetrackterSource_EntferntBeides()
    {
        // Wie „Kurs → Repertoire": GetBookPgnAsync hat Buch + Source im selben Kontext schon geladen.
        var id = await SeedBookAsync(RawPgn);
        var db = Fresh();
        _ = await db.Books.Include(b => b.Source).SingleAsync(b => b.Id == id);

        await new BookAdminService(db).DeleteBookAsync(id);   // kein „already tracked" durch den Stub

        var check = Fresh();
        Assert.False(await check.Books.AnyAsync());
        Assert.False(await check.BookSources.AnyAsync());
    }

    // ===== Lesende Pfade im frischen Kontext =====

    [Fact]
    public async Task GetBookPgn_FrischerKontext_LiefertRohPgnVerbatim()
    {
        var id = await SeedBookAsync(RawPgn);
        await SeedLineAsync(id, "001.001", "Kapitel A", "e2e4 e7e5");

        var (pgn, _) = await TestServices.Course(Fresh()).GetBookPgnAsync(userId: 1, id, isAdmin: true);

        Assert.Equal(RawPgn, pgn);
    }

    [Fact]
    public async Task ChapterUndLinePgn_FrischerKontext_KommenAusDemRohPgn()
    {
        var id = await SeedBookAsync(RawPgn);
        var lineId = await SeedLineAsync(id, "001.001", "Kapitel A", "e2e4 e7e5");

        var (chapter, _) = await TestServices.Course(Fresh()).GetChapterPgnAsync(1, id, "Kapitel A", isAdmin: true);
        var (line, _) = await TestServices.Course(Fresh()).GetLinePgnAsync(1, id, lineId, isAdmin: true);

        Assert.Contains("(1. d4 {Damengambit})", chapter);   // Variante nur im Roh-PGN
        Assert.Contains("(1. d4 {Damengambit})", line);
    }

    [Fact]
    public async Task ImportFile_Teilimport_FrischerKontext_BehaeltVollesRohPgn()
    {
        var voll = "[Event \"X\"]\n[Round \"1\"]\n[FEN \"" + StartFen + "\"]\n\n1. e4 e5 2. Nf3 *\n";
        await new PgnImportService(Fresh()).ImportFileAsync("src.pgn", voll, CancellationToken.None);

        var teil = "[Event \"X\"]\n[Round \"2\"]\n[FEN \"" + StartFen + "\"]\n\n1. d4 d5 2. c4 *\n";
        await new PgnImportService(Fresh()).ImportFileAsync("src.pgn", teil, CancellationToken.None, partial: true);

        var book = await Fresh().Books.Include(b => b.Source).SingleAsync(b => b.FileName == "src.pgn");
        Assert.Equal(voll, book.Source.SourcePgn);
    }

    [Fact]
    public async Task ImportFile_NeuesBuch_LegtSourceMitAn()
    {
        var pgn = "[Event \"X\"]\n[Round \"1\"]\n[FEN \"" + StartFen + "\"]\n\n1. e4 e5 2. Nf3 *\n";

        await new PgnImportService(Fresh()).ImportFileAsync("neu.pgn", pgn, CancellationToken.None);

        var book = await Fresh().Books.Include(b => b.Source).SingleAsync(b => b.FileName == "neu.pgn");
        Assert.Equal(pgn, book.Source.SourcePgn);
    }

    [Fact]
    public async Task ReprocessCourses_FrischerKontext_LaedtDenTextJeBuch()
    {
        var id = await SeedBookAsync(ReprocessPgn, importVersion: 0, fileName: "manual-loc.pgn");
        var db = Fresh();
        db.BookPuzzles.Add(new BookPuzzle
        {
            LineId = "manual-loc.pgn:1", BookFileName = "manual-loc.pgn", BookId = id, Round = "1",
            Fen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2",
            Moves = "g1f3 b8c6 f1b5 a7a6", StartPly = -1,
        });
        await db.SaveChangesAsync();

        var result = await ReprocessTestHelper.Build(Fresh()).ReprocessCoursesAsync(userId: 1, isAdmin: false);

        Assert.Equal(1, result.Reprocessed);
        Assert.Equal(0, result.Failed);
        var check = Fresh();
        Assert.Equal(ImportPipeline.CurrentVersion, (await check.Books.SingleAsync(b => b.Id == id)).ImportVersion);
        Assert.Contains("Develops.", (await check.BookPuzzles.SingleAsync()).MoveComments);
    }
}
