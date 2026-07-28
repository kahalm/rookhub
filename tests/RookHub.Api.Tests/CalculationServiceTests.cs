using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Kalkulations-Modus (Stellungen ohne Lösung + eigener Analysebaum je Nutzer):
/// Zugriffs-Gating, „bearbeitet"-Markierung, Upsert/Validierung des Baums und — die wichtigste
/// Eigenschaft — dass die gespeicherten Lösungszüge des Buchs NICHT ausgeliefert werden.
/// </summary>
public class CalculationServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CalculationService _svc;

    public CalculationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _svc = new CalculationService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<AppUser> CreateUserAsync(string name = "u1")
    {
        var user = new AppUser { Username = name, PasswordHash = "x", CreatedAt = DateTime.UtcNow };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<Book> SeedBookAsync(int? ownerUserId = null, bool isCalculation = true)
    {
        var book = new Book
        {
            FileName = $"calc-{Guid.NewGuid():N}.pgn", DisplayName = "Kalkulation", OwnerUserId = ownerUserId,
            IsCalculation = isCalculation, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        return book;
    }

    private async Task<BookPuzzle> SeedPositionAsync(Book book, string round = "1", string? comment = "Weiß am Zug",
        string moves = "", int startPly = -1, string? chapter = null)
    {
        var p = new BookPuzzle
        {
            LineId = $"{book.FileName}:{round}:{Guid.NewGuid():N}",
            BookFileName = book.FileName,
            BookId = book.Id,
            Round = round,
            Fen = "r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4",
            Moves = moves,
            StartPly = startPly,
            Comment = comment,
            Chapter = chapter,
            IsInfoOnly = moves.Length == 0,
        };
        _db.BookPuzzles.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    // ---- Zugriff --------------------------------------------------------------

    [Fact]
    public async Task GetBook_WithoutAccess_Throws404()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(ownerUserId: 999);   // fremdes persönliches Buch
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.GetBookAsync(user.Id, book.Id, isAdmin: false));
    }

    [Fact]
    public async Task GetBook_AsAdmin_Works()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(ownerUserId: 999);
        await SeedPositionAsync(book);
        var dto = await _svc.GetBookAsync(user.Id, book.Id, isAdmin: true);
        Assert.Single(dto.Positions);
        Assert.True(dto.IsCalculation);
    }

    [Fact]
    public async Task GetPosition_WithoutAccess_Throws404()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(ownerUserId: 999);
        var pos = await SeedPositionAsync(book);
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.GetPositionAsync(user.Id, pos.Id, isAdmin: false));
    }

    [Fact]
    public async Task SaveTree_WithoutAccess_Throws404()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(ownerUserId: 999);
        var pos = await SeedPositionAsync(book);
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.SaveTreeAsync(user.Id, pos.Id, new SaveCalcTreeDto { TreeJson = "{\"v\":1}" }, isAdmin: false));
        Assert.Empty(_db.CalculationTrees);
    }

    // ---- Stellungsliste ------------------------------------------------------

    [Fact]
    public async Task GetBook_OrdersByRound_AndMarksOwnTrees()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(ownerUserId: null);
        book.IsPublic = true;                                  // Zugriff für jeden
        await _db.SaveChangesAsync();
        var p1 = await SeedPositionAsync(book, round: "1");
        var p2 = await SeedPositionAsync(book, round: "2", chapter: "Kapitel A");
        await _svc.SaveTreeAsync(user.Id, p2.Id, new SaveCalcTreeDto { TreeJson = "{\"v\":1}" }, isAdmin: false);

        var dto = await _svc.GetBookAsync(user.Id, book.Id, isAdmin: false);

        Assert.Equal(new[] { p1.Id, p2.Id }, dto.Positions.Select(p => p.Id));
        Assert.False(dto.Positions[0].HasTree);
        Assert.True(dto.Positions[1].HasTree);
        Assert.Equal("Kapitel A", dto.Positions[1].Chapter);
    }

    [Fact]
    public async Task GetBook_TreesOfOtherUsers_DoNotCount()
    {
        var mine = await CreateUserAsync("mine");
        var other = await CreateUserAsync("other");
        var book = await SeedBookAsync(ownerUserId: null);
        book.IsPublic = true;
        await _db.SaveChangesAsync();
        var pos = await SeedPositionAsync(book);
        await _svc.SaveTreeAsync(other.Id, pos.Id, new SaveCalcTreeDto { TreeJson = "{\"v\":1}" }, isAdmin: false);

        var dto = await _svc.GetBookAsync(mine.Id, book.Id, isAdmin: false);
        Assert.False(dto.Positions.Single().HasTree);
    }

    // ---- Keine Lösung ausliefern ---------------------------------------------

    [Fact]
    public async Task GetPosition_NeverLeaksSolutionMoves()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(ownerUserId: null);
        book.IsPublic = true;
        await _db.SaveChangesAsync();
        // Klassische Puzzle-Linie: Trainingsstart nach Halbzug 1, danach kommt die LÖSUNG.
        var pos = await SeedPositionAsync(book, moves: "e2e4 e7e5 g1f3 b8c6", startPly: 1);

        var dto = await _svc.GetPositionAsync(user.Id, pos.Id, isAdmin: false);

        // Nur der Vorlauf bis zum Trainingsstart (Halbzüge 0..1), nichts danach.
        Assert.Equal("e2e4 e7e5", dto.SetupMoves);
        Assert.DoesNotContain("g1f3", dto.SetupMoves);
        Assert.DoesNotContain("b8c6", dto.SetupMoves);
    }

    [Fact]
    public async Task GetPosition_PurePositionLine_HasNoSetupMoves()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(ownerUserId: null);
        book.IsPublic = true;
        await _db.SaveChangesAsync();
        var pos = await SeedPositionAsync(book, comment: "Wie bewertest du die Stellung?");

        var dto = await _svc.GetPositionAsync(user.Id, pos.Id, isAdmin: false);

        Assert.Equal(string.Empty, dto.SetupMoves);
        Assert.Equal("Wie bewertest du die Stellung?", dto.Comment);
        Assert.False(string.IsNullOrEmpty(dto.Fen));
        Assert.Null(dto.TreeJson);
    }

    [Fact]
    public void SetupMoves_ClampsToAvailableMoves()
    {
        // StartPly zeigt über das Ende hinaus (defekte Altzeile) → keine Exception, nur alles was da ist.
        var puzzle = new BookPuzzle { Moves = "e2e4 e7e5", StartPly = 9, LineId = "x", BookFileName = "b", Fen = "f" };
        Assert.Equal("e2e4 e7e5", CalculationService.SetupMoves(puzzle));
    }

    // ---- Baum speichern/laden/löschen ---------------------------------------

    [Fact]
    public async Task SaveTree_ThenGetPosition_ReturnsTree()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(ownerUserId: null);
        book.IsPublic = true;
        await _db.SaveChangesAsync();
        var pos = await SeedPositionAsync(book);
        const string json = "{\"v\":1,\"nodes\":[{\"i\":0,\"p\":null}]}";

        var saved = await _svc.SaveTreeAsync(user.Id, pos.Id, new SaveCalcTreeDto { TreeJson = json }, isAdmin: false);
        var dto = await _svc.GetPositionAsync(user.Id, pos.Id, isAdmin: false);

        Assert.Equal(pos.Id, saved.BookPuzzleId);
        Assert.Equal(json, dto.TreeJson);
        Assert.Equal(saved.UpdatedAt, dto.TreeUpdatedAt);
        // BookId wird denormalisiert mitgeschrieben (Zähler der Kursübersicht ohne Join).
        Assert.Equal(book.Id, _db.CalculationTrees.Single().BookId);
    }

    [Fact]
    public async Task SaveTree_Twice_UpdatesInPlace()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(ownerUserId: user.Id);
        var pos = await SeedPositionAsync(book);

        var first = await _svc.SaveTreeAsync(user.Id, pos.Id, new SaveCalcTreeDto { TreeJson = "{\"a\":1}" }, isAdmin: false);
        var second = await _svc.SaveTreeAsync(user.Id, pos.Id, new SaveCalcTreeDto { TreeJson = "{\"a\":2}" }, isAdmin: false);

        var row = Assert.Single(_db.CalculationTrees);
        Assert.Equal("{\"a\":2}", row.TreeJson);
        Assert.True(second.UpdatedAt >= first.UpdatedAt);
        Assert.True(row.CreatedAt <= row.UpdatedAt);
    }

    [Fact]
    public async Task SaveTree_InvalidJson_Throws400()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(ownerUserId: user.Id);
        var pos = await SeedPositionAsync(book);
        await Assert.ThrowsAsync<ArgumentException>(
            () => _svc.SaveTreeAsync(user.Id, pos.Id, new SaveCalcTreeDto { TreeJson = "{nope" }, isAdmin: false));
    }

    [Fact]
    public async Task SaveTree_Empty_Throws400()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(ownerUserId: user.Id);
        var pos = await SeedPositionAsync(book);
        await Assert.ThrowsAsync<ArgumentException>(
            () => _svc.SaveTreeAsync(user.Id, pos.Id, new SaveCalcTreeDto { TreeJson = "  " }, isAdmin: false));
    }

    [Fact]
    public async Task SaveTree_TooLarge_Throws400()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(ownerUserId: user.Id);
        var pos = await SeedPositionAsync(book);
        var huge = "\"" + new string('x', CalculationService.MaxTreeJsonLength) + "\"";
        await Assert.ThrowsAsync<ArgumentException>(
            () => _svc.SaveTreeAsync(user.Id, pos.Id, new SaveCalcTreeDto { TreeJson = huge }, isAdmin: false));
    }

    [Fact]
    public async Task SaveTree_UnknownPosition_Throws404()
    {
        var user = await CreateUserAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.SaveTreeAsync(user.Id, 12345, new SaveCalcTreeDto { TreeJson = "{}" }, isAdmin: true));
    }

    [Fact]
    public async Task DeleteTree_RemovesOnlyOwnTree()
    {
        var mine = await CreateUserAsync("mine");
        var other = await CreateUserAsync("other");
        var book = await SeedBookAsync(ownerUserId: null);
        book.IsPublic = true;
        await _db.SaveChangesAsync();
        var pos = await SeedPositionAsync(book);
        await _svc.SaveTreeAsync(mine.Id, pos.Id, new SaveCalcTreeDto { TreeJson = "{\"m\":1}" }, isAdmin: false);
        await _svc.SaveTreeAsync(other.Id, pos.Id, new SaveCalcTreeDto { TreeJson = "{\"o\":1}" }, isAdmin: false);

        await _svc.DeleteTreeAsync(mine.Id, pos.Id, isAdmin: false);

        var remaining = Assert.Single(_db.CalculationTrees);
        Assert.Equal(other.Id, remaining.UserId);
    }

    [Fact]
    public async Task DeleteTree_WithoutTree_IsNoOp()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(ownerUserId: user.Id);
        var pos = await SeedPositionAsync(book);
        await _svc.DeleteTreeAsync(user.Id, pos.Id, isAdmin: false);   // wirft nicht
        Assert.Empty(_db.CalculationTrees);
    }
}
