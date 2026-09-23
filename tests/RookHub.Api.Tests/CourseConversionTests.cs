using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>„Kurs → Repertoire umwandeln" und umgekehrt: der Kurs-PGN wird zum Repertoire, der
/// Repertoire-PGN zum persönlichen Kurs (nur bei Puzzle-PGN im Chessable-Stil).</summary>
public class CourseConversionTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CourseService _courses;
    private readonly RepertoireService _repertoires;
    private readonly CourseRepertoireConversionService _conversion;

    // Chessable-Stil-Puzzle-PGN (FEN + [%tqu]-Trainingsmarker) → erzeugt beim Import ein Kurs-Puzzle.
    private const string PuzzlePgn = @"
[Event ""Test Book""]
[Round ""1.1""]
[White ""Italian Idea""]
[Result ""*""]
[SetUp ""1""]
[FEN ""rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2""]

{ [%tqu ""En"",""Finde den Zug""] Pointe. } 2.Nf3 Nc6 3. Bb5 $1 a6 *
";

    // Reines Eröffnungs-PGN ohne Puzzle-Marker → kein quiz-barer Kurs-Inhalt.
    private const string PlainPgn = "[Event \"Opening\"]\n[Result \"*\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 *";

    public CourseConversionTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _repertoires = TestServices.Repertoire(_db);
        _courses = TestServices.Course(_db);
        _conversion = TestServices.Conversion(_db, _courses, _repertoires);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ConvertCourseToRepertoire_CreatesRepertoireWithBookPgn()
    {
        var book = new Book
        {
            FileName = "chessable-u1-x.pgn", DisplayName = "My Course", OwnerUserId = 1,
            Source = new BookSource { SourcePgn = PlainPgn }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();

        var rep = await _conversion.ConvertCourseToRepertoireAsync(userId: 1, bookId: book.Id, isAdmin: false);

        var saved = await _db.Repertoires.Include(r => r.Files).FirstAsync(r => r.Id == rep.Id);
        Assert.Equal(1, saved.UserId);
        Assert.Equal("My Course", saved.Name);
        Assert.False(saved.UseForExtension);              // wie importierte Kurse: Extension-Nutzung aus
        var file = Assert.Single(saved.Files);
        Assert.Contains("1. e4 e5", file.PgnContent);      // Kurs-PGN im Repertoire gelandet
        // Verschieben: der EIGENE Original-Kurs ist nach der Umwandlung entfernt.
        Assert.False(await _db.Books.AnyAsync(b => b.Id == book.Id));
    }

    [Fact]
    public async Task ConvertCourseToRepertoire_SharedCourse_KeepsOriginal()
    {
        // Geteiltes Gruppen-Buch (kein OwnerUserId), für User 1 über eine Gruppe freigegeben.
        var book = new Book { FileName = "group.pgn", DisplayName = "Group Course", OwnerUserId = null, Source = new BookSource { SourcePgn = PlainPgn }, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        _db.UserGroups.Add(new UserGroup { UserId = 1, GroupId = 7 });
        await _db.SaveChangesAsync();
        _db.BookGroupAccesses.Add(new BookGroupAccess { BookId = book.Id, GroupId = 7 });
        await _db.SaveChangesAsync();

        var rep = await _conversion.ConvertCourseToRepertoireAsync(userId: 1, bookId: book.Id, isAdmin: false);

        Assert.True(await _db.Repertoires.AnyAsync(r => r.Id == rep.Id));
        // Geteilter Kurs gehört dem User nicht → bleibt bestehen (nicht gelöscht).
        Assert.True(await _db.Books.AnyAsync(b => b.Id == book.Id));
    }

    [Fact]
    public async Task ConvertCourseToRepertoire_NoAccess_Throws()
    {
        var book = new Book { FileName = "chessable-u1-y.pgn", DisplayName = "Foreign", OwnerUserId = 1, Source = new BookSource { SourcePgn = PlainPgn } };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _conversion.ConvertCourseToRepertoireAsync(userId: 2, bookId: book.Id, isAdmin: false));
    }

    [Fact]
    public async Task ConvertRepertoireToCourse_WithPuzzlePgn_CreatesCourse()
    {
        // Repertoire mit Chessable-Stil-Puzzle-PGN anlegen (wie es aus „Kurs → Repertoire" entstünde).
        var rep = await _repertoires.CreateFromPgnAsync(userId: 1, name: "Puzzles", fileName: "p.pgn", pgn: PuzzlePgn);

        // Reverse (wie im Controller): kombiniertes PGN → persönlicher Kurs.
        var pgn = await _repertoires.GetCombinedPgnAsync(rep.Id, userId: 1);
        var course = await _courses.UploadPersonalCourseAsync(userId: 1, "Puzzles.pgn", pgn, "Puzzles");

        Assert.True(course.PuzzleCount >= 1);
        Assert.True(course.IsOwned);
        Assert.Equal("Puzzles", course.DisplayName);
    }

    [Fact]
    public async Task ConvertRepertoireToCourse_PlainPgn_ThrowsNoPlayableLines()
    {
        var rep = await _repertoires.CreateFromPgnAsync(userId: 1, name: "Opening", fileName: "o.pgn", pgn: PlainPgn);
        var pgn = await _repertoires.GetCombinedPgnAsync(rep.Id, userId: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _courses.UploadPersonalCourseAsync(userId: 1, "Opening.pgn", pgn, "Opening"));
    }

    // ===== Die Gegenrichtung als SERVICE ======================================================
    // „Repertoire → Kurs" stand bis v0.499.4 im RepertoireController ausgeschrieben — Besitzer-Pruefung,
    // Leer-Fall mit eigenem Code und die Reihenfolge „erst anlegen, dann loeschen" waren damit nur ueber
    // einen Controller-Test erreichbar. Die Controller-Tests (RepertoireControllerTests) pruefen weiter
    // die HTTP-Abbildung; hier steht die Regel selbst.

    [Fact]
    public async Task ConvertRepertoireToCourse_WithPuzzlePgn_CreatesCourse_AndMovesOriginal()
    {
        var rep = await _repertoires.CreateFromPgnAsync(userId: 1, name: "Puzzles", fileName: "p.pgn", pgn: PuzzlePgn);

        var course = await _conversion.ConvertRepertoireToCourseAsync(userId: 1, repertoireId: rep.Id);

        Assert.True(course.PuzzleCount >= 1);
        Assert.Equal("Puzzles", course.DisplayName);
        // Verschieben: das Original-Repertoire ist nach der Umwandlung weg.
        Assert.False(await _db.Repertoires.AnyAsync(r => r.Id == rep.Id));
    }

    /// <summary>Angelegt, aber nie eine PGN importiert: EIGENER Code, damit die Oberflaeche nicht
    /// faelschlich „keine Puzzle-Linien" meldet, wo in Wahrheit noch gar nichts drin ist. Und das
    /// Repertoire bleibt — es gibt nichts, was den Verlust rechtfertigte.</summary>
    [Fact]
    public async Task ConvertRepertoireToCourse_EmptyRepertoire_ThrowsWithCode_AndKeepsRepertoire()
    {
        var rep = await _repertoires.CreateAsync(userId: 1, new DTOs.CreateRepertoireDto { Name = "Nimzo" });

        var ex = await Assert.ThrowsAsync<CourseConversionException>(
            () => _conversion.ConvertRepertoireToCourseAsync(userId: 1, repertoireId: rep.Id));

        Assert.Equal("repertoire_empty", ex.Code);
        // Erbt von InvalidOperationException — sonst faenge der 400-Zweig der Controller sie nicht.
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
        Assert.True(await _db.Repertoires.AnyAsync(r => r.Id == rep.Id));
        Assert.False(await _db.Books.AnyAsync(b => b.OwnerUserId == 1));   // kein Kurs-Torso
    }

    /// <summary>Kein quiz-barer Inhalt ist etwas anderes als „leer": KEIN Code (die Oberflaeche zeigt
    /// dort einen anderen Satz), und das Repertoire bleibt ebenfalls stehen.</summary>
    [Fact]
    public async Task ConvertRepertoireToCourse_PlainPgn_ThrowsWithoutCode_AndKeepsRepertoire()
    {
        var rep = await _repertoires.CreateFromPgnAsync(userId: 1, name: "Opening", fileName: "o.pgn", pgn: PlainPgn);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _conversion.ConvertRepertoireToCourseAsync(userId: 1, repertoireId: rep.Id));

        Assert.IsNotType<CourseConversionException>(ex);
        Assert.True(await _db.Repertoires.AnyAsync(r => r.Id == rep.Id));
    }

    /// <summary>Umwandeln VERSCHIEBT — deshalb darf es nur der Besitzer, nicht ein Freigabe-Empfaenger.
    /// 404 statt 403: sonst waere die Antwort ein Existenz-Orakel fuer fremde Repertoires.</summary>
    [Fact]
    public async Task ConvertRepertoireToCourse_ForeignRepertoire_Throws_AndKeepsIt()
    {
        var rep = await _repertoires.CreateFromPgnAsync(userId: 1, name: "Puzzles", fileName: "p.pgn", pgn: PuzzlePgn);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _conversion.ConvertRepertoireToCourseAsync(userId: 2, repertoireId: rep.Id));

        Assert.True(await _db.Repertoires.AnyAsync(r => r.Id == rep.Id));
        Assert.False(await _db.Books.AnyAsync(b => b.OwnerUserId == 2));
    }

    // ===== Info-Linien überleben die Rundreise Kurs → Repertoire → Kurs =====================

    // Eine Quiz-Linie + eine Info-Linie MIT Zügen ([%info]) + ein zugloses Kapitel-Intro.
    private const string CourseWithInfoPgn = @"
[Event ""Info Book""]
[Round ""1.1""]
[White ""Intro""]
[Black ""Kapitel 1""]
[SetUp ""1""]
[FEN ""rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1""]

{[%info]} 1. -- {Worum es in diesem Kapitel geht.} *

[Event ""Info Book""]
[Round ""1.2""]
[White ""Idee""]
[Black ""Kapitel 1""]
[SetUp ""1""]
[FEN ""rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1""]

{[%info] Der Plan.} 1. e4 e5 2. Nf3 Nc6 *

[Event ""Info Book""]
[Round ""1.3""]
[White ""Aufgabe""]
[Black ""Kapitel 1""]
[SetUp ""1""]
[FEN ""rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2""]

{ [%tqu ""En"",""Finde den Zug""] Pointe. } 2.Nf3 Nc6 3. Bb5 $1 a6 *
";

    private async Task<Dictionary<string, bool>> InfoFlagsByTitle(int bookId)
        => await _db.BookPuzzles.Where(bp => bp.BookId == bookId)
            .ToDictionaryAsync(bp => bp.Title ?? bp.Round, bp => bp.IsInfoOnly);

    private async Task<Dictionary<string, bool>> RoundTrip(int bookId)
    {
        var rep = await _conversion.ConvertCourseToRepertoireAsync(userId: 1, bookId: bookId, isAdmin: false);
        var back = await _conversion.ConvertRepertoireToCourseAsync(userId: 1, repertoireId: rep.Id);
        return await InfoFlagsByTitle(back.BookId);
    }

    [Fact]
    public async Task RoundTrip_ImportedCourse_KeepsInfoLines()
    {
        var course = await _courses.UploadPersonalCourseAsync(userId: 1, "info.pgn", CourseWithInfoPgn, "Info Book");
        var before = await InfoFlagsByTitle(course.BookId);
        Assert.True(before["Idee"]);
        Assert.False(before["Aufgabe"]);

        var after = await RoundTrip(course.BookId);

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task RoundTrip_CourseWithoutSourcePgn_KeepsInfoLines()
    {
        // Altbestand ohne Roh-PGN: der Export baut die Linien aus den gespeicherten BookPuzzles nach.
        var course = await _courses.UploadPersonalCourseAsync(userId: 1, "info.pgn", CourseWithInfoPgn, "Info Book");
        var book = await _db.Books.Include(b => b.Source).FirstAsync(b => b.Id == course.BookId);
        book.Source.SourcePgn = null;
        await _db.SaveChangesAsync();
        var before = await InfoFlagsByTitle(course.BookId);

        var after = await RoundTrip(course.BookId);

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task RoundTrip_RepertoireDownloadedWithInfoPrefix_KeepsInfoLines()
    {
        // Repertoire aus einem RookHub-Download (Marker weg, „Info | " im White-Header) → Kurs.
        var downloaded = PgnParser.ForDownload(CourseWithInfoPgn);
        var rep = await _repertoires.CreateFromPgnAsync(userId: 1, name: "DL", fileName: "dl.pgn", pgn: downloaded);

        var course = await _conversion.ConvertRepertoireToCourseAsync(userId: 1, repertoireId: rep.Id);
        var flags = await InfoFlagsByTitle(course.BookId);

        Assert.True(flags["Idee"]);
        Assert.False(flags["Aufgabe"]);
    }

    [Fact]
    public async Task RoundTrip_HandAddedInfoPosition_InCourseWithSourcePgn_Survives()
    {
        // „Stellungen hinzufügen" (CourseAuthoringService.AddLinesAsync) legt Info-Linien OHNE Eintrag
        // im Roh-PGN an. Der Kurs-Export lieferte bisher nur das Roh-PGN → die Stellung ging beim
        // Umwandeln verloren.
        var course = await _courses.UploadPersonalCourseAsync(userId: 1, "info.pgn", CourseWithInfoPgn, "Info Book");
        var book = await _db.Books.FirstAsync(b => b.Id == course.BookId);
        _db.BookPuzzles.Add(new BookPuzzle
        {
            LineId = $"{book.FileName}:2.1", BookFileName = book.FileName, BookId = book.Id, Round = "2.1",
            Title = "Stellung", Chapter = "Kapitel 2",
            Fen = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3",
            Moves = string.Empty, StartPly = -1, Comment = "Merken.", IsInfoOnly = true,
        });
        await _db.SaveChangesAsync();
        var before = await InfoFlagsByTitle(course.BookId);

        var after = await RoundTrip(course.BookId);

        Assert.Equal(before, after);
    }
}
