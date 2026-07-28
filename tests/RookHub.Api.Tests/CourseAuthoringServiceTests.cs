using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Kurs-Detailseite + Inhaltspflege: Detailbild (inkl. reiner Stellungs-Kapitel), Stellungen als
/// Text einfügen, Kapitel umbenennen/löschen, Linie löschen, Einzel-Kapitel-Reset des eigenen
/// Fortschritts — samt Rechteprüfung (lesen = Kurs-Zugriff, ändern = Besitzer/Admin).
/// </summary>
public class CourseAuthoringServiceTests : IDisposable
{
    private const string Fen1 = "r2q1r1k/1pp1bppb/p1np4/4p1Pp/2B1P2N/2PPB2P/PP1Q1P2/R3R1K1 w - - 0 18";
    private const string Fen2 = "r2qk2r/p3bppp/Q4n2/4p1N1/3n4/8/PPPP1PPP/RNB2RK1 b kq - 0 12";
    private const string Fen3 = "1k6/p1p2ppp/1p6/1Qn2q2/8/P2r1B2/1P4PP/K1R5 w - - 0 28";

    private readonly AppDbContext _db;
    private readonly CourseAuthoringService _svc;

    public CourseAuthoringServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _svc = new CourseAuthoringService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<AppUser> CreateUserAsync(string name = "owner")
    {
        var user = new AppUser { Username = name, PasswordHash = "x", CreatedAt = DateTime.UtcNow };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<Book> SeedBookAsync(int? ownerUserId, bool isCalculation = false)
    {
        var book = new Book
        {
            FileName = $"b-{Guid.NewGuid():N}.pgn", DisplayName = "Kurs", OwnerUserId = ownerUserId,
            IsCalculation = isCalculation, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    private async Task<BookPuzzle> SeedLineAsync(Book book, string round, string? chapter, bool infoOnly,
        string fen = "8/8/8/4k3/8/8/4K3/8 w - - 0 1")
    {
        var line = new BookPuzzle
        {
            LineId = $"{book.FileName}:{round}", BookFileName = book.FileName, BookId = book.Id, Round = round,
            Fen = fen, Moves = infoOnly ? "" : "e2e3", StartPly = -1, Chapter = chapter, IsInfoOnly = infoOnly,
        };
        _db.BookPuzzles.Add(line);
        await _db.SaveChangesAsync();
        return line;
    }

    private static AddCourseLinesDto Paste(string? chapter, params string[] fens) => new()
    {
        Chapter = chapter,
        Text = string.Join('\n', fens.Select((f, i) => $"{i + 1}: {f}")),
    };

    // ===== Detailbild =========================================================

    [Fact]
    public async Task GetDetail_WithoutAccess_Throws404()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(ownerUserId: 999);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.GetDetailAsync(user.Id, book.Id, isAdmin: false));
    }

    [Fact]
    public async Task GetDetail_ListsAllChapters_IncludingPositionOnlyOnes()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id);
        var quiz = await SeedLineAsync(book, "1", "Taktik", infoOnly: false);
        await SeedLineAsync(book, "2", "Nur Stellungen", infoOnly: true);
        await SeedLineAsync(book, "3", "Nur Stellungen", infoOnly: true);

        var detail = await _svc.GetDetailAsync(user.Id, book.Id, isAdmin: false);

        Assert.Equal(new[] { "Taktik", "Nur Stellungen" }, detail.Chapters.Select(c => c.Name));
        // Solver kennt nur Kapitel MIT Quiz-Linien — das Stellungs-Kapitel hat keinen Solver-Index.
        Assert.Equal(0, detail.Chapters[0].SolverIndex);
        Assert.Null(detail.Chapters[1].SolverIndex);
        Assert.Equal(2, detail.Chapters[1].LineCount);
        Assert.Equal(0, detail.Chapters[1].QuizCount);
        Assert.Equal(3, detail.TotalLines);
        Assert.Equal(2, detail.InfoLineCount);
        Assert.Equal(1, detail.PuzzleCount);             // normales Buch: nur Quiz-Linien
        Assert.True(detail.CanManage);
        Assert.True(detail.IsOwned);
        Assert.Equal(quiz.Id, detail.Chapters[0].FirstLineId);
    }

    [Fact]
    public async Task GetDetail_CalculationBook_CountsPositionsAndTrees()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id, isCalculation: true);
        var a = await SeedLineAsync(book, "1", "Kapitel 1", infoOnly: true, fen: Fen1);
        await SeedLineAsync(book, "2", "Kapitel 1", infoOnly: true, fen: Fen2);
        _db.CalculationTrees.Add(new CalculationTree
        {
            UserId = user.Id, BookId = book.Id, BookPuzzleId = a.Id, TreeJson = "{\"v\":1}",
        });
        await _db.SaveChangesAsync();

        var detail = await _svc.GetDetailAsync(user.Id, book.Id, isAdmin: false);

        Assert.True(detail.IsCalculation);
        Assert.Equal(2, detail.PuzzleCount);             // ALLE Stellungen
        Assert.Equal(1, detail.SolvedCount);             // eine bearbeitet
        Assert.Equal(50, detail.ProgressPercent);
        Assert.Equal(1, detail.Chapters.Single().SolvedCount);
    }

    [Fact]
    public async Task GetDetail_SharedRecipient_MayReadButNotManage()
    {
        var owner = await CreateUserAsync("owner");
        var guest = await CreateUserAsync("guest");
        var book = await SeedBookAsync(owner.Id);
        await SeedLineAsync(book, "1", null, infoOnly: false);
        _db.CourseShares.Add(new CourseShare
        {
            BookId = book.Id, OwnerId = owner.Id, RecipientId = guest.Id, SharedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var detail = await _svc.GetDetailAsync(guest.Id, book.Id, isAdmin: false);

        Assert.True(detail.IsShared);
        Assert.Equal("owner", detail.SharedByUsername);
        Assert.False(detail.CanManage);
        Assert.False(detail.IsOwned);
    }

    [Fact]
    public async Task GetChapterLines_ReturnsLineCountButNeverTheMoves()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id);
        await SeedLineAsync(book, "1", "Taktik", infoOnly: false);   // Moves = "e2e3"
        await SeedLineAsync(book, "2", "Andere", infoOnly: true);

        var lines = await _svc.GetChapterLinesAsync(user.Id, book.Id, "Taktik", isAdmin: false);

        var line = Assert.Single(lines);
        Assert.Equal(1, line.MoveCount);
        Assert.False(line.IsInfoOnly);
        // Das DTO hat gar kein Zug-Feld — die Detailseite verrät keine Lösung.
        Assert.DoesNotContain("Moves", typeof(CourseLineDto).GetProperties().Select(p => p.Name));
    }

    // ===== Stellungen einfügen ===============================================

    [Fact]
    public async Task AddLines_CreatesChapterAndPositionLines()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id, isCalculation: true);

        var result = await _svc.AddLinesAsync(user.Id, book.Id,
            Paste("Kapitel 1", Fen1, Fen2, Fen3), isAdmin: false);

        Assert.Equal(3, result.Added);
        Assert.Equal("Kapitel 1", result.Chapter);
        Assert.Empty(result.Issues);

        var lines = await _db.BookPuzzles.Where(bp => bp.BookId == book.Id).OrderBy(bp => bp.Round).ToListAsync();
        Assert.Equal(new[] { "1", "2", "3" }, lines.Select(l => l.Round));
        Assert.All(lines, l =>
        {
            Assert.True(l.IsInfoOnly);                    // trägt keine Lösung → nie abgefragt
            Assert.Equal(string.Empty, l.Moves);
            Assert.Equal(-1, l.StartPly);
            Assert.Equal("Kapitel 1", l.Chapter);
            Assert.StartsWith(book.FileName + ":", l.LineId);
        });
        // Handgepflegtes Buch gilt nicht als „veraltet" (kein Quell-PGN zum Aufbereiten).
        Assert.Equal(ImportPipeline.CurrentVersion, (await _db.Books.FindAsync(book.Id))!.ImportVersion);
    }

    [Fact]
    public async Task AddLines_KeepsPastedComments()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id);

        var result = await _svc.AddLinesAsync(user.Id, book.Id, new AddCourseLinesDto
        {
            Chapter = "Rechnen",
            Text = $"1: {Fen1} | Weiß am Zug — reicht der Angriff?\n2: {Fen2} {{Wie bewertest du?}}",
        }, isAdmin: false);

        Assert.Equal(2, result.Added);
        var comments = await _db.BookPuzzles.Where(bp => bp.BookId == book.Id)
            .OrderBy(bp => bp.Round).Select(bp => bp.Comment).ToListAsync();
        Assert.Equal(new[] { "Weiß am Zug — reicht der Angriff?", "Wie bewertest du?" }, comments);
    }

    [Fact]
    public async Task AddLines_AppendsAfterExistingRounds_KeepingTextOrder()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id);
        // Bestehende Runden vierstellig → neue Runde übernimmt die Breite (sonst sortiert „10" vor „9").
        await SeedLineAsync(book, "0001", "Alt", infoOnly: false);
        await SeedLineAsync(book, "0009", "Alt", infoOnly: false);

        await _svc.AddLinesAsync(user.Id, book.Id, Paste("Neu", Fen1, Fen2), isAdmin: false);

        var rounds = await _db.BookPuzzles.Where(bp => bp.BookId == book.Id)
            .OrderBy(bp => bp.Round).Select(bp => bp.Round).ToListAsync();
        Assert.Equal(new[] { "0001", "0009", "0010", "0011" }, rounds);
    }

    [Fact]
    public async Task AddLines_SkipsPositionsAlreadyInTheBook()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id);
        await _svc.AddLinesAsync(user.Id, book.Id, Paste("K", Fen1, Fen2), isAdmin: false);

        // Dieselbe Liste erneut einfügen (plus eine neue Stellung).
        var again = await _svc.AddLinesAsync(user.Id, book.Id, Paste("K", Fen1, Fen2, Fen3), isAdmin: false);

        Assert.Equal(1, again.Added);
        Assert.Equal(2, again.Issues.Count);
        Assert.All(again.Issues, i => Assert.Equal("duplicate", i.Reason));
        Assert.Equal(3, await _db.BookPuzzles.CountAsync(bp => bp.BookId == book.Id));
    }

    [Fact]
    public async Task AddLines_ReportsUnusableLinesButKeepsTheRest()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id);

        var result = await _svc.AddLinesAsync(user.Id, book.Id, new AddCourseLinesDto
        {
            Text = $"1: {Fen1}\n2: völliger Unsinn\n3: {Fen2}",
        }, isAdmin: false);

        Assert.Equal(2, result.Added);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("invalid_fen", issue.Reason);
        Assert.Equal(2, issue.LineNumber);
        Assert.Null(result.Chapter);                      // ohne Kapitel
    }

    [Fact]
    public async Task AddLines_EmptyText_Throws400()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id);
        await Assert.ThrowsAsync<ArgumentException>(
            () => _svc.AddLinesAsync(user.Id, book.Id, new AddCourseLinesDto { Text = "  \n " }, isAdmin: false));
    }

    [Fact]
    public async Task AddLines_NonOwnerWithAccess_Throws403()
    {
        var owner = await CreateUserAsync("owner");
        var guest = await CreateUserAsync("guest");
        var book = await SeedBookAsync(owner.Id);
        _db.CourseShares.Add(new CourseShare
        {
            BookId = book.Id, OwnerId = owner.Id, RecipientId = guest.Id, SharedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _svc.AddLinesAsync(guest.Id, book.Id, Paste("K", Fen1), isAdmin: false));
        Assert.Empty(_db.BookPuzzles);
    }

    [Fact]
    public async Task AddLines_AdminMayEditForeignBook()
    {
        var admin = await CreateUserAsync("admin");
        var book = await SeedBookAsync(ownerUserId: 999);
        var result = await _svc.AddLinesAsync(admin.Id, book.Id, Paste("K", Fen1), isAdmin: true);
        Assert.Equal(1, result.Added);
    }

    [Fact]
    public void NextRound_TakesMaxAndWidth()
    {
        Assert.Equal((1, 1), CourseAuthoringService.NextRound(Array.Empty<string>()));
        Assert.Equal((3, 1), CourseAuthoringService.NextRound(new[] { "1", "2" }));
        Assert.Equal((10, 4), CourseAuthoringService.NextRound(new[] { "0001", "0009" }));
        // Nicht-numerische Runden (Chessable-Kennungen) beeinflussen die Zählung nicht.
        Assert.Equal((6, 1), CourseAuthoringService.NextRound(new[] { "abc", "5" }));
    }

    // ===== Kapitel pflegen ====================================================

    [Fact]
    public async Task RenameChapter_MovesAllItsLines()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id);
        await SeedLineAsync(book, "1", "Alt", infoOnly: true, fen: Fen1);
        await SeedLineAsync(book, "2", "Alt", infoOnly: true, fen: Fen2);
        await SeedLineAsync(book, "3", "Andere", infoOnly: true, fen: Fen3);

        var moved = await _svc.RenameChapterAsync(user.Id, book.Id,
            new RenameCourseChapterDto { Chapter = "Alt", NewName = "Neu" }, isAdmin: false);

        Assert.Equal(2, moved);
        Assert.Equal(2, await _db.BookPuzzles.CountAsync(bp => bp.Chapter == "Neu"));
        Assert.Equal(1, await _db.BookPuzzles.CountAsync(bp => bp.Chapter == "Andere"));
    }

    [Fact]
    public async Task RenameChapter_EmptyNewName_MovesToNoChapter()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id);
        await SeedLineAsync(book, "1", "Alt", infoOnly: true, fen: Fen1);

        await _svc.RenameChapterAsync(user.Id, book.Id,
            new RenameCourseChapterDto { Chapter = "Alt", NewName = "   " }, isAdmin: false);

        Assert.Null((await _db.BookPuzzles.FirstAsync()).Chapter);
    }

    [Fact]
    public async Task RenameChapter_ToExistingName_Throws400()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id);
        await SeedLineAsync(book, "1", "A", infoOnly: true, fen: Fen1);
        await SeedLineAsync(book, "2", "B", infoOnly: true, fen: Fen2);

        await Assert.ThrowsAsync<ArgumentException>(() => _svc.RenameChapterAsync(user.Id, book.Id,
            new RenameCourseChapterDto { Chapter = "A", NewName = "B" }, isAdmin: false));
    }

    [Fact]
    public async Task RenameChapter_UnknownChapter_Throws404()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id);
        await SeedLineAsync(book, "1", "A", infoOnly: true, fen: Fen1);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.RenameChapterAsync(user.Id, book.Id,
            new RenameCourseChapterDto { Chapter = "gibtsnicht", NewName = "X" }, isAdmin: false));
    }

    [Fact]
    public async Task DeleteChapter_RemovesItsLinesAndDependentUserData()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id, isCalculation: true);
        var a = await SeedLineAsync(book, "1", "Weg", infoOnly: true, fen: Fen1);
        await SeedLineAsync(book, "2", "Bleibt", infoOnly: true, fen: Fen2);
        _db.CalculationTrees.Add(new CalculationTree
        {
            UserId = user.Id, BookId = book.Id, BookPuzzleId = a.Id, TreeJson = "{\"v\":1}",
        });
        _db.CourseAttempts.Add(new CourseAttempt
        {
            UserId = user.Id, BookId = book.Id, BookPuzzleId = a.Id, AttemptedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var deleted = await _svc.DeleteChapterAsync(user.Id, book.Id, "Weg", isAdmin: false);

        Assert.Equal(1, deleted);
        Assert.Equal(1, await _db.BookPuzzles.CountAsync(bp => bp.BookId == book.Id));
        Assert.Empty(_db.CalculationTrees);
        Assert.Empty(_db.CourseAttempts);
    }

    [Fact]
    public async Task DeleteLine_RemovesOnlyThatLine()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id);
        var a = await SeedLineAsync(book, "1", "K", infoOnly: true, fen: Fen1);
        await SeedLineAsync(book, "2", "K", infoOnly: true, fen: Fen2);

        await _svc.DeleteLineAsync(user.Id, book.Id, a.Id, isAdmin: false);

        var rest = Assert.Single(await _db.BookPuzzles.ToListAsync());
        Assert.Equal(Fen2, rest.Fen);
    }

    [Fact]
    public async Task DeleteLine_OfAnotherBook_Throws404()
    {
        var user = await CreateUserAsync();
        var mine = await SeedBookAsync(user.Id);
        var other = await SeedBookAsync(user.Id);
        var foreignLine = await SeedLineAsync(other, "1", null, infoOnly: true, fen: Fen1);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.DeleteLineAsync(user.Id, mine.Id, foreignLine.Id, isAdmin: false));
    }

    [Fact]
    public async Task DeleteChapter_NonOwner_Throws403()
    {
        var owner = await CreateUserAsync("owner");
        var guest = await CreateUserAsync("guest");
        var book = await SeedBookAsync(owner.Id);
        book.IsPublic = true;                             // Zugriff ja, Schreibrecht nein
        await SeedLineAsync(book, "1", "K", infoOnly: true, fen: Fen1);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _svc.DeleteChapterAsync(guest.Id, book.Id, "K", isAdmin: false));
    }

    // ===== Einzel-Kapitel-Reset ==============================================

    [Fact]
    public async Task ResetChapterProgress_ClearsOnlyThatChapterAndOnlyForMe()
    {
        var me = await CreateUserAsync("me");
        var other = await CreateUserAsync("other");
        var book = await SeedBookAsync(ownerUserId: null);
        book.IsPublic = true;
        await _db.SaveChangesAsync();
        var a = await SeedLineAsync(book, "1", "Reset", infoOnly: false, fen: Fen1);
        var b = await SeedLineAsync(book, "2", "Bleibt", infoOnly: false, fen: Fen2);

        foreach (var (userId, line) in new[] { (me.Id, a), (me.Id, b), (other.Id, a) })
        {
            _db.CoursePuzzleResults.Add(new CoursePuzzleResult
            {
                UserId = userId, BookId = book.Id, BookPuzzleId = line.Id, SolvedAt = DateTime.UtcNow,
            });
            _db.CourseAttempts.Add(new CourseAttempt
            {
                UserId = userId, BookId = book.Id, BookPuzzleId = line.Id, AttemptedAt = DateTime.UtcNow,
            });
        }
        _db.CourseInfoViews.Add(new CourseInfoView
        {
            UserId = me.Id, BookId = book.Id, BookPuzzleId = a.Id, SeenAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var cleared = await _svc.ResetChapterProgressAsync(me.Id, book.Id, "Reset", isAdmin: false);

        Assert.Equal(1, cleared);
        // Eigenes Kapitel „Reset" weg, eigenes „Bleibt" da, fremder Fortschritt unangetastet.
        Assert.False(await _db.CoursePuzzleResults.AnyAsync(r => r.UserId == me.Id && r.BookPuzzleId == a.Id));
        Assert.True(await _db.CoursePuzzleResults.AnyAsync(r => r.UserId == me.Id && r.BookPuzzleId == b.Id));
        Assert.True(await _db.CoursePuzzleResults.AnyAsync(r => r.UserId == other.Id && r.BookPuzzleId == a.Id));
        Assert.Empty(_db.CourseInfoViews);
        Assert.Equal(2, await _db.CourseAttempts.CountAsync());   // eigener Versuch weg, fremder + „Bleibt" da
    }

    [Fact]
    public async Task ResetChapterProgress_KeepsMyCalculationTrees()
    {
        // Ein Analysebaum ist eigene Arbeit — ein Fortschritts-Reset darf ihn nicht wegwerfen.
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id, isCalculation: true);
        var line = await SeedLineAsync(book, "1", "K", infoOnly: true, fen: Fen1);
        _db.CalculationTrees.Add(new CalculationTree
        {
            UserId = user.Id, BookId = book.Id, BookPuzzleId = line.Id, TreeJson = "{\"v\":1}",
        });
        _db.CourseInfoViews.Add(new CourseInfoView
        {
            UserId = user.Id, BookId = book.Id, BookPuzzleId = line.Id, SeenAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        await _svc.ResetChapterProgressAsync(user.Id, book.Id, "K", isAdmin: false);

        Assert.Single(_db.CalculationTrees);
        Assert.Empty(_db.CourseInfoViews);
    }

    [Fact]
    public async Task ResetChapterProgress_SharedRecipientMayResetOwnProgress()
    {
        var owner = await CreateUserAsync("owner");
        var guest = await CreateUserAsync("guest");
        var book = await SeedBookAsync(owner.Id);
        var line = await SeedLineAsync(book, "1", "K", infoOnly: false, fen: Fen1);
        _db.CourseShares.Add(new CourseShare
        {
            BookId = book.Id, OwnerId = owner.Id, RecipientId = guest.Id, SharedAt = DateTime.UtcNow,
        });
        _db.CoursePuzzleResults.Add(new CoursePuzzleResult
        {
            UserId = guest.Id, BookId = book.Id, BookPuzzleId = line.Id, SolvedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var cleared = await _svc.ResetChapterProgressAsync(guest.Id, book.Id, "K", isAdmin: false);

        Assert.Equal(1, cleared);
        Assert.Empty(_db.CoursePuzzleResults);
    }

    [Fact]
    public async Task ResetChapterProgress_UnknownChapter_Throws404()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(user.Id);
        await SeedLineAsync(book, "1", "K", infoOnly: false, fen: Fen1);
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.ResetChapterProgressAsync(user.Id, book.Id, "gibtsnicht", isAdmin: false));
    }
}
