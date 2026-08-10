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

    // ---- Rechenzeit (Delta, wird addiert) ------------------------------------

    [Fact]
    public async Task PatchMeta_Seconds_AreAddedNotOverwritten()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();

        await _svc.PatchMetaAsync(user.Id, pos.Id, new PatchCalcMetaDto { AddSeconds = 90 }, isAdmin: false);
        var state = await _svc.PatchMetaAsync(user.Id, pos.Id, new PatchCalcMetaDto { AddSeconds = 30 }, isAdmin: false);

        // Zweiter Besuch schickt sein Delta — die Zeit des ersten darf dabei nicht verlorengehen.
        Assert.Equal(120, state.SecondsSpent);
        Assert.Equal(120, _db.CalculationTrees.Single().SecondsSpent);
    }

    [Fact]
    public async Task PatchMeta_Seconds_AreClampedPerTransmission()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();

        // Hängender Client meldet ein absurdes Delta → je Übertragung gedeckelt.
        await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { AddSeconds = 5_000_000 }, isAdmin: false);
        var state = await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { AddSeconds = 5_000_000 }, isAdmin: false);

        Assert.Equal(2 * CalculationService.MaxSecondsPerFlush, state.SecondsSpent);
    }

    [Fact]
    public async Task PatchMeta_GrownOverCapPatch_CreditsRemainderAcrossRetries()
    {
        // Review-Fund 2026-08-09: ein beim Wiedereinreihen über MaxSecondsPerFlush hinaus
        // gewachsener Patch (Server war lange tot, mergeReviewPatch addierte unter DERSELBEN Marke)
        // wurde je Übertragung auf den Cap geklemmt — der Server merkte sich aber `requested`
        // statt des tatsächlich Angerechneten, sodass der Rest nie nachkam. Jetzt: applied =
        // already + delta ⇒ der Rest wird über weitere Retries derselben Marke nachgeholt.
        var (user, _, pos) = await SeedOwnPositionAsync();
        var cap = CalculationService.MaxSecondsPerFlush;
        var patch = new PatchCalcMetaDto { AddSeconds = cap + 1400, SecondsToken = "t-grown" };

        var first = await _svc.PatchMetaAsync(user.Id, pos.Id, patch, isAdmin: false);
        Assert.Equal(cap, first.SecondsSpent);                 // erste Übertragung: nur der Cap

        // Gleiche Marke, gleicher (immer noch über-Cap) Wert: der Rest bis requested kommt nach.
        var second = await _svc.PatchMetaAsync(user.Id, pos.Id, patch, isAdmin: false);
        Assert.Equal(cap + 1400, second.SecondsSpent);          // Rest angerechnet, nicht unterschlagen

        // Und jetzt ist es fertig — ein weiterer identischer Retry addiert nichts mehr (idempotent).
        var third = await _svc.PatchMetaAsync(user.Id, pos.Id, patch, isAdmin: false);
        Assert.Equal(cap + 1400, third.SecondsSpent);
    }

    [Fact]
    public async Task PatchMeta_NegativeSeconds_DoNotSubtract()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();

        await _svc.PatchMetaAsync(user.Id, pos.Id, new PatchCalcMetaDto { AddSeconds = 60 }, isAdmin: false);
        var state = await _svc.PatchMetaAsync(user.Id, pos.Id, new PatchCalcMetaDto { AddSeconds = -600 }, isAdmin: false);

        Assert.Equal(60, state.SecondsSpent);
    }

    [Fact]
    public async Task PatchMeta_Seconds_HaveATotalCeiling()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();
        var tree = await _svc.PatchMetaAsync(user.Id, pos.Id, new PatchCalcMetaDto { AddSeconds = 1 }, isAdmin: false);
        var row = _db.CalculationTrees.Single();
        row.SecondsSpent = CalculationService.MaxSecondsSpent - 10;
        await _db.SaveChangesAsync();

        var state = await _svc.PatchMetaAsync(user.Id, pos.Id, new PatchCalcMetaDto { AddSeconds = 3600 }, isAdmin: false);

        Assert.Equal(CalculationService.MaxSecondsSpent, state.SecondsSpent);
        Assert.Equal(pos.Id, tree.BookPuzzleId);
    }

    // ---- Rechenzeit: Wiederholungen dürfen nicht doppelt zählen ---------------
    // Zeit ist der einzige Wert, der ADDIERT statt SETZT — und damit der einzige, der eine
    // Idempotenz-Marke braucht. Der Fall, um den es geht: die Anfrage KAM AN, nur die Antwort ging
    // verloren (Timeout/502) — der Client wiederholt sie dann mit derselben Marke.

    [Fact]
    public async Task PatchMeta_SamePatchTwice_AddsSecondsOnlyOnce()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();
        var patch = new PatchCalcMetaDto { AddSeconds = 90, SecondsToken = "t-abc" };

        var first = await _svc.PatchMetaAsync(user.Id, pos.Id, patch, isAdmin: false);
        var retry = await _svc.PatchMetaAsync(user.Id, pos.Id, patch, isAdmin: false);

        Assert.Equal(90, first.SecondsSpent);
        Assert.Equal(90, retry.SecondsSpent);              // NICHT 180
        Assert.Equal(90, _db.CalculationTrees.Single().SecondsSpent);
    }

    [Fact]
    public async Task PatchMeta_DifferentSecondsTokens_AddBothDeltas()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();

        await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { AddSeconds = 90, SecondsToken = "t-1" }, isAdmin: false);
        var state = await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { AddSeconds = 30, SecondsToken = "t-2" }, isAdmin: false);

        // Zwei echte Messungen sind zwei Deltas — die Marke unterdrückt nur die WIEDERHOLUNG.
        Assert.Equal(120, state.SecondsSpent);
    }

    [Fact]
    public async Task PatchMeta_RepeatedPatch_StillUpdatesGradeAndChoice()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();
        await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { AddSeconds = 60, SecondsToken = "t-x" }, isAdmin: false);

        // Der Wiederholversuch trägt dieselbe Zeit-Marke, aber neue Stufe/Festlegung: Stufe und
        // Festlegung SETZEN (sind also von sich aus idempotent) und müssen durchlaufen.
        var state = await _svc.PatchMetaAsync(user.Id, pos.Id, new PatchCalcMetaDto
        {
            AddSeconds = 60,
            SecondsToken = "t-x",
            Grade = (int)CalculationGrade.MoveNoSideLines,
            ChosenSan = "Nd5",
            ChosenUci = "c3d5",
        }, isAdmin: false);

        Assert.Equal(60, state.SecondsSpent);              // Zeit: einmal
        Assert.Equal((int)CalculationGrade.MoveNoSideLines, state.Grade);
        Assert.Equal("Nd5", state.ChosenSan);
        Assert.Equal("c3d5", state.ChosenUci);
    }

    [Fact]
    public async Task PatchMeta_GrownRetryWithSameToken_AddsOnlyTheDifference()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();
        await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { AddSeconds = 40, SecondsToken = "t-grow" }, isAdmin: false);

        // Scheitert die Antwort, legt der Client den Patch zurück in die Warteschlange und lässt ihn
        // um inzwischen gemessene Zeit WACHSEN — mit derselben Marke.
        var state = await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { AddSeconds = 65, SecondsToken = "t-grow" }, isAdmin: false);

        Assert.Equal(65, state.SecondsSpent);              // 40 + 25, nicht 105
    }

    [Fact]
    public async Task SaveTree_SamePatchTwice_AddsSecondsOnlyOnce()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();
        var dto = new SaveCalcTreeDto { TreeJson = "{\"v\":1}", AddSeconds = 30, SecondsToken = "t-tree" };

        await _svc.SaveTreeAsync(user.Id, pos.Id, dto, isAdmin: false);
        var retry = await _svc.SaveTreeAsync(user.Id, pos.Id, dto, isAdmin: false);

        Assert.Equal(30, retry.SecondsSpent);
        Assert.True(retry.HasTree);
    }

    [Fact]
    public async Task PatchMeta_SecondsToken_TooLong_Throws400()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto
            {
                AddSeconds = 10,
                SecondsToken = new string('x', CalculationService.MaxSecondsTokenLength + 1),
            }, isAdmin: false));
    }

    [Fact]
    public async Task PatchMeta_WithoutSecondsToken_KeepsAddingAsBefore()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();

        await _svc.PatchMetaAsync(user.Id, pos.Id, new PatchCalcMetaDto { AddSeconds = 20 }, isAdmin: false);
        var state = await _svc.PatchMetaAsync(user.Id, pos.Id, new PatchCalcMetaDto { AddSeconds = 20 }, isAdmin: false);

        Assert.Equal(40, state.SecondsSpent);
        Assert.Null(_db.CalculationTrees.Single().SecondsToken);
    }

    // ---- Bewertung (benannte Stufe, null ≠ Stufe 0) --------------------------

    [Theory]
    [InlineData(CalculationGrade.NotSolved, 0)]
    [InlineData(CalculationGrade.SomeIdeas, 1)]
    [InlineData(CalculationGrade.MoveNoMainLine, 2)]
    [InlineData(CalculationGrade.MoveNoSideLines, 3)]
    [InlineData(CalculationGrade.Solved, 4)]
    public async Task PatchMeta_EachGrade_DerivesItsPoints(CalculationGrade grade, int expectedPoints)
    {
        var (user, _, pos) = await SeedOwnPositionAsync();

        var state = await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { Grade = (int)grade }, isAdmin: false);

        // Gespeichert wird die STUFE, die Punkte sind nur abgeleitet (heute linear 0..4).
        Assert.Equal((int)grade, state.Grade);
        Assert.Equal((int)grade, _db.CalculationTrees.Single().Grade);
        Assert.Equal(expectedPoints, state.Points);
        Assert.Equal(expectedPoints, CalculationGrades.PointsFor(grade));
    }

    [Fact]
    public async Task PatchMeta_Grade_CanBeSetChangedAndTakenBack()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();

        var set = await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { Grade = (int)CalculationGrade.SomeIdeas }, isAdmin: false);
        Assert.Equal((int)CalculationGrade.SomeIdeas, set.Grade);
        Assert.Equal(1, set.Points);

        // Umbewerten ersetzt die Stufe (es gibt genau eine je Stellung).
        var changed = await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { Grade = (int)CalculationGrade.MoveNoSideLines }, isAdmin: false);
        Assert.Equal((int)CalculationGrade.MoveNoSideLines, changed.Grade);
        Assert.Equal(3, changed.Points);
        Assert.Equal((int)CalculationGrade.MoveNoSideLines, _db.CalculationTrees.Single().Grade);

        // Zurücknehmen = wieder „noch nicht bewertet".
        var cleared = await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { ClearGrade = true }, isAdmin: false);
        Assert.Null(cleared.Grade);
        Assert.Null(cleared.Points);
        Assert.Null(_db.CalculationTrees.Single().Grade);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(-1)]
    [InlineData(99)]
    public async Task PatchMeta_UnknownGrade_Throws400_AndStoresNothing(int grade)
    {
        var (user, _, pos) = await SeedOwnPositionAsync();

        // Eine unbekannte Stufe ist ein Client-Fehler — NICHT still auf 0 („nicht gelöst") klemmen.
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { Grade = grade }, isAdmin: false));
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.SaveTreeAsync(user.Id, pos.Id,
            new SaveCalcTreeDto { TreeJson = "{\"v\":1}", Grade = grade }, isAdmin: false));
        Assert.Empty(_db.CalculationTrees);
    }

    [Fact]
    public async Task PatchMeta_UnratedIsNull_AndNotSolvedIsAValueOfItsOwn()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();

        // Zeit buchen, ohne zu bewerten → Stufe bleibt null („noch nicht bewertet").
        var afterTime = await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { AddSeconds = 30 }, isAdmin: false);
        Assert.Null(afterTime.Grade);
        Assert.Null(afterTime.Points);

        // Stufe 0 = geprüft und nicht gelöst — ein echter Wert, nicht „unbewertet".
        var zero = await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { Grade = (int)CalculationGrade.NotSolved }, isAdmin: false);
        Assert.Equal(0, zero.Grade);
        Assert.Equal(0, zero.Points);
        Assert.NotNull(_db.CalculationTrees.Single().Grade);

        // Weiterer PATCH ohne Stufen-Feld lässt die Bewertung stehen.
        var untouched = await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { AddSeconds = 10 }, isAdmin: false);
        Assert.Equal(0, untouched.Grade);

        // Erst ClearGrade nimmt sie zurück — und das ist etwas anderes als Stufe 0.
        var cleared = await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { ClearGrade = true }, isAdmin: false);
        Assert.Null(cleared.Grade);
        Assert.Null(_db.CalculationTrees.Single().Grade);
    }

    // ---- Festlegung (genau eine je Stellung) ---------------------------------

    [Fact]
    public async Task PatchMeta_Choice_SetIsIdempotent_NotToggle()
    {
        // Review-Fund 2026-08-09: dasselbe SAN erneut zu senden ist ein SETZEN (No-op), KEIN
        // Toggle. Früher togglete der Server auf null — nicht wiederholungsfest: kam die Anfrage
        // an und ging nur die Antwort verloren, löschte der identische Retry die Festlegung. Das
        // Zurücknehmen macht der Client über ClearChoice (eigener Test unten).
        var (user, _, pos) = await SeedOwnPositionAsync();

        var set = await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { ChosenSan = "Nd5", ChosenUci = "c3d5" }, isAdmin: false);
        Assert.Equal("Nd5", set.ChosenSan);
        Assert.Equal("c3d5", set.ChosenUci);

        // Retry desselben Set-Patches (verlorene Antwort) → Festlegung BLEIBT, nicht weg.
        var again = await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { ChosenSan = "Nd5", ChosenUci = "c3d5" }, isAdmin: false);
        Assert.Equal("Nd5", again.ChosenSan);
        Assert.Equal("c3d5", again.ChosenUci);
        Assert.Equal("Nd5", _db.CalculationTrees.Single().ChosenSan);
    }

    [Fact]
    public async Task PatchMeta_Choice_MovesInsteadOfAccumulating()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();

        await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { ChosenSan = "Nd5", ChosenUci = "c3d5" }, isAdmin: false);
        var moved = await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { ChosenSan = "Bxf7+", ChosenUci = "c4f7" }, isAdmin: false);

        Assert.Equal("Bxf7+", moved.ChosenSan);
        Assert.Equal("c4f7", moved.ChosenUci);
        var row = Assert.Single(_db.CalculationTrees);     // genau EINE Festlegung je Stellung
        Assert.Equal("Bxf7+", row.ChosenSan);
    }

    [Fact]
    public async Task PatchMeta_ClearChoice_RemovesItWithoutKnowingTheMove()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();
        await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { ChosenSan = "Nd5", ChosenUci = "c3d5" }, isAdmin: false);

        var cleared = await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { ClearChoice = true }, isAdmin: false);

        Assert.Null(cleared.ChosenSan);
        Assert.Null(cleared.ChosenUci);
    }

    [Fact]
    public async Task PatchMeta_ChoiceTooLong_Throws400()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { ChosenSan = new string('N', 21) }, isAdmin: false));
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { ChosenSan = "Nd5", ChosenUci = new string('a', 11) }, isAdmin: false));
        Assert.Empty(_db.CalculationTrees);
    }

    // ---- Werte ohne Baum / Baum mit Werten -----------------------------------

    [Fact]
    public async Task PatchMeta_WithoutAnyTree_CreatesRowButNotProgress()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();

        var state = await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto
            {
                AddSeconds = 45, Grade = (int)CalculationGrade.MoveNoMainLine,
                ChosenSan = "Nd5", ChosenUci = "c3d5",
            },
            isAdmin: false);

        Assert.False(state.HasTree);                       // festlegen/bewerten geht ohne Baum
        Assert.Equal(45, state.SecondsSpent);
        Assert.Equal((int)CalculationGrade.MoveNoMainLine, state.Grade);
        Assert.Equal(2, state.Points);
        Assert.Equal(string.Empty, _db.CalculationTrees.Single().TreeJson);

        var dto = await _svc.GetPositionAsync(user.Id, pos.Id, isAdmin: false);
        Assert.Null(dto.TreeJson);                         // leerer Baum ≠ gespeicherter Baum
        Assert.Null(dto.TreeUpdatedAt);
        Assert.Equal((int)CalculationGrade.MoveNoMainLine, dto.Grade);
        Assert.Equal(2, dto.Points);
        Assert.Equal("Nd5", dto.ChosenSan);
    }

    [Fact]
    public async Task PatchMeta_WithNothingToChange_CreatesNoRow()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();

        var state = await _svc.PatchMetaAsync(user.Id, pos.Id, new PatchCalcMetaDto(), isAdmin: false);

        Assert.Empty(_db.CalculationTrees);                // keine Karteileichen
        Assert.Equal(0, state.SecondsSpent);
        Assert.Null(state.Grade);
        Assert.Null(state.Points);
    }

    [Fact]
    public async Task SaveTree_CanCarryTheThreeValues()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();

        var saved = await _svc.SaveTreeAsync(user.Id, pos.Id, new SaveCalcTreeDto
        {
            TreeJson = "{\"v\":1}", AddSeconds = 120, Grade = (int)CalculationGrade.MoveNoSideLines,
            ChosenSan = "Nd5", ChosenUci = "c3d5",
        }, isAdmin: false);

        Assert.True(saved.HasTree);
        Assert.Equal(120, saved.SecondsSpent);
        Assert.Equal((int)CalculationGrade.MoveNoSideLines, saved.Grade);
        Assert.Equal(3, saved.Points);
        Assert.Equal("Nd5", saved.ChosenSan);

        // Ein zweites Speichern addiert wieder nur das Delta.
        var again = await _svc.SaveTreeAsync(user.Id, pos.Id,
            new SaveCalcTreeDto { TreeJson = "{\"v\":2}", AddSeconds = 60 }, isAdmin: false);
        Assert.Equal(180, again.SecondsSpent);
        Assert.Equal((int)CalculationGrade.MoveNoSideLines, again.Grade);   // unverändert mitgeschleppt
    }

    [Fact]
    public async Task PatchMeta_WithoutAccess_Throws404()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(ownerUserId: 999);   // fremdes persönliches Buch
        var pos = await SeedPositionAsync(book);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto { Grade = (int)CalculationGrade.Solved, AddSeconds = 60 }, isAdmin: false));
        Assert.Empty(_db.CalculationTrees);
    }

    [Fact]
    public async Task PatchMeta_UnknownPosition_Throws404()
    {
        var user = await CreateUserAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _svc.PatchMetaAsync(user.Id, 12345,
            new PatchCalcMetaDto { Grade = (int)CalculationGrade.MoveNoSideLines }, isAdmin: true));
    }

    [Fact]
    public async Task PatchMeta_OfAnotherUser_LeavesMyValuesAlone()
    {
        var mine = await CreateUserAsync("mine");
        var other = await CreateUserAsync("other");
        var book = await SeedBookAsync(ownerUserId: null);
        book.IsPublic = true;
        await _db.SaveChangesAsync();
        var pos = await SeedPositionAsync(book);

        await _svc.PatchMetaAsync(mine.Id, pos.Id,
            new PatchCalcMetaDto { Grade = (int)CalculationGrade.Solved, AddSeconds = 100 }, isAdmin: false);
        await _svc.PatchMetaAsync(other.Id, pos.Id,
            new PatchCalcMetaDto { Grade = (int)CalculationGrade.SomeIdeas, AddSeconds = 10 }, isAdmin: false);

        var dto = await _svc.GetPositionAsync(mine.Id, pos.Id, isAdmin: false);
        Assert.Equal((int)CalculationGrade.Solved, dto.Grade);
        Assert.Equal(4, dto.Points);
        Assert.Equal(100, dto.SecondsSpent);
    }

    [Fact]
    public async Task DeleteTree_KeepsTimeAndRating_ButDropsTheTree()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();
        await _svc.SaveTreeAsync(user.Id, pos.Id, new SaveCalcTreeDto
        {
            TreeJson = "{\"v\":1}", AddSeconds = 300, Grade = (int)CalculationGrade.MoveNoMainLine,
            ChosenSan = "Nd5", ChosenUci = "c3d5",
        }, isAdmin: false);

        await _svc.DeleteTreeAsync(user.Id, pos.Id, isAdmin: false);

        var row = Assert.Single(_db.CalculationTrees);
        Assert.Equal(string.Empty, row.TreeJson);          // Analyse neu anfangen …
        Assert.Equal(300, row.SecondsSpent);               // … aber Zeit/Bewertung bleiben
        Assert.Equal((int)CalculationGrade.MoveNoMainLine, row.Grade);
        Assert.Equal("Nd5", row.ChosenSan);
    }

    [Fact]
    public async Task DeleteTree_WithoutAnyValues_RemovesTheRow()
    {
        var (user, _, pos) = await SeedOwnPositionAsync();
        await _svc.SaveTreeAsync(user.Id, pos.Id, new SaveCalcTreeDto { TreeJson = "{\"v\":1}" }, isAdmin: false);

        await _svc.DeleteTreeAsync(user.Id, pos.Id, isAdmin: false);

        Assert.Empty(_db.CalculationTrees);
    }

    // ---- Kapitelsummen (serverseitig) ----------------------------------------

    [Fact]
    public async Task GetBook_SumsPointsWithTheirMaximumAndTimePerChapter()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(ownerUserId: user.Id);
        var a1 = await SeedPositionAsync(book, round: "1", chapter: "Kapitel A");
        var a2 = await SeedPositionAsync(book, round: "2", chapter: "Kapitel A");
        var a3 = await SeedPositionAsync(book, round: "3", chapter: "Kapitel A");
        var b1 = await SeedPositionAsync(book, round: "4", chapter: "Kapitel B");
        var ohne = await SeedPositionAsync(book, round: "5", chapter: null);

        await _svc.SaveTreeAsync(user.Id, a1.Id, new SaveCalcTreeDto
        {
            TreeJson = "{\"v\":1}", AddSeconds = 100, Grade = (int)CalculationGrade.Solved,
            ChosenSan = "Nd5", ChosenUci = "c3d5",
        }, isAdmin: false);
        await _svc.PatchMetaAsync(user.Id, a2.Id,
            new PatchCalcMetaDto { AddSeconds = 50, Grade = (int)CalculationGrade.MoveNoMainLine },
            isAdmin: false);                                                             // ohne Baum
        await _svc.PatchMetaAsync(user.Id, a3.Id,
            new PatchCalcMetaDto { AddSeconds = 25 }, isAdmin: false);                    // unbewertet
        await _svc.PatchMetaAsync(user.Id, b1.Id,
            new PatchCalcMetaDto
            {
                Grade = (int)CalculationGrade.NotSolved, ChosenSan = "Qh5", ChosenUci = "d1h5",
            }, isAdmin: false);

        var dto = await _svc.GetBookAsync(user.Id, book.Id, isAdmin: false);

        var chA = dto.Chapters.Single(c => c.Chapter == "Kapitel A");
        Assert.Equal(3, chA.PositionCount);
        Assert.Equal(1, chA.TreeCount);          // nur a1 hat einen echten Baum
        Assert.Equal(1, chA.ChosenCount);
        Assert.Equal(2, chA.RatedCount);         // a3 ist unbewertet (null zählt nicht)
        Assert.Equal(6, chA.Points);             // gelöst (4) + Hauptfolge nicht gesehen (2)
        // Das Maximum hängt an ALLEN Stellungen des Kapitels, auch der unbewerteten: 3 × 4.
        Assert.Equal(12, chA.MaxPoints);
        Assert.Equal(175, chA.SecondsSum);

        var chB = dto.Chapters.Single(c => c.Chapter == "Kapitel B");
        Assert.Equal(1, chB.RatedCount);         // Stufe „nicht gelöst" ist bewertet …
        Assert.Equal(0, chB.Points);             // … zählt aber 0 zur Summe
        Assert.Equal(4, chB.MaxPoints);
        Assert.Equal(1, chB.ChosenCount);

        var chNone = dto.Chapters.Single(c => c.Chapter == null);
        Assert.Equal(1, chNone.PositionCount);
        Assert.Equal(0, chNone.RatedCount);
        Assert.Equal(0, chNone.Points);
        Assert.Equal(4, chNone.MaxPoints);       // unbewertet heißt nicht „nichts zu holen"
        Assert.Equal(0, chNone.SecondsSum);

        // Buchsummen = Summe der Kapitel; Reihenfolge = erstes Auftreten in der Stellungsliste.
        Assert.Equal(6, dto.Points);
        Assert.Equal(20, dto.MaxPoints);         // 5 Stellungen × 4
        Assert.Equal(175, dto.SecondsSum);
        Assert.Equal(new string?[] { "Kapitel A", "Kapitel B", null }, dto.Chapters.Select(c => c.Chapter));
        Assert.Equal(ohne.Id, dto.Positions.Last().Id);
    }

    [Fact]
    public async Task GetBook_ListsTheThreeValuesPerPosition()
    {
        var (user, book, pos) = await SeedOwnPositionAsync();
        await _svc.PatchMetaAsync(user.Id, pos.Id,
            new PatchCalcMetaDto
            {
                AddSeconds = 42, Grade = (int)CalculationGrade.MoveNoSideLines,
                ChosenSan = "Nd5", ChosenUci = "c3d5",
            }, isAdmin: false);

        var item = (await _svc.GetBookAsync(user.Id, book.Id, isAdmin: false)).Positions.Single();

        Assert.Equal(42, item.SecondsSpent);
        Assert.Equal((int)CalculationGrade.MoveNoSideLines, item.Grade);
        Assert.Equal(3, item.Points);
        Assert.Equal("Nd5", item.ChosenSan);
        Assert.Equal("c3d5", item.ChosenUci);
        Assert.False(item.HasTree);
    }

    [Fact]
    public async Task GetBook_TreesOfOtherUsers_DoNotContributeToSums()
    {
        var mine = await CreateUserAsync("mine");
        var other = await CreateUserAsync("other");
        var book = await SeedBookAsync(ownerUserId: null);
        book.IsPublic = true;
        await _db.SaveChangesAsync();
        var pos = await SeedPositionAsync(book, chapter: "K");
        await _svc.PatchMetaAsync(other.Id, pos.Id,
            new PatchCalcMetaDto { Grade = (int)CalculationGrade.Solved, AddSeconds = 600 }, isAdmin: false);

        var dto = await _svc.GetBookAsync(mine.Id, book.Id, isAdmin: false);

        Assert.Equal(0, dto.Points);
        Assert.Equal(4, dto.MaxPoints);          // das Maximum hängt an der Stellung, nicht am Nutzer
        Assert.Equal(0, dto.SecondsSum);
        Assert.Equal(0, dto.Chapters.Single().RatedCount);
    }

    // ---- Weiterhin KEINE Lösung ----------------------------------------------

    [Fact]
    public async Task WithTrainingValues_StillNoSolutionLeavesTheServer()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(ownerUserId: user.Id);
        // Puzzle-Linie mit echter Lösung ab Halbzug 2.
        var pos = await SeedPositionAsync(book, moves: "e2e4 e7e5 g1f3 b8c6 f1b5", startPly: 1, chapter: "K");
        await _svc.SaveTreeAsync(user.Id, pos.Id, new SaveCalcTreeDto
        {
            TreeJson = "{\"v\":1}", AddSeconds = 60, Grade = (int)CalculationGrade.Solved,
            ChosenSan = "Nf3", ChosenUci = "g1f3",
        }, isAdmin: false);

        var listJson = System.Text.Json.JsonSerializer.Serialize(
            await _svc.GetBookAsync(user.Id, book.Id, isAdmin: false));
        var positionJson = System.Text.Json.JsonSerializer.Serialize(
            await _svc.GetPositionAsync(user.Id, pos.Id, isAdmin: false));

        // Die Bewertung ist reine SELBSTeinschätzung — sie schaltet keine Lösung frei.
        foreach (var json in new[] { listJson, positionJson })
        {
            Assert.DoesNotContain("b8c6", json);
            Assert.DoesNotContain("f1b5", json);
        }
        // Der Vorlauf bis zum Trainingsstart bleibt erlaubt, die Zugliste selbst kommt nie mit.
        Assert.DoesNotContain("Moves", listJson);
        Assert.Contains("e2e4 e7e5", positionJson);
        // …und „g1f3" steht nur als eigene Festlegung des Nutzers drin, nicht als Buchlösung.
        Assert.DoesNotContain("g1f3 b8c6", positionJson);
    }

    /// <summary>Nutzer + eigenes Kalkulationsbuch + eine Stellung — der Normalfall dieser Tests.</summary>
    private async Task<(AppUser User, Book Book, BookPuzzle Position)> SeedOwnPositionAsync()
    {
        var user = await CreateUserAsync();
        var book = await SeedBookAsync(ownerUserId: user.Id);
        var pos = await SeedPositionAsync(book);
        return (user, book, pos);
    }
}
