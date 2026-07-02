using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Erkennung + Tagging von Standard-Puzzles, in deren Lösung ein En-passant-Schlag möglich war,
/// aber nicht gespielt wurde (Theme <c>enPassantPossible</c>). Die Detektion spielt die UCI-Lösung
/// ab der FEN durch — validiert damit auch, dass die e.p.-Legalität aus der Zugfolge rekonstruiert
/// wird (nicht nur aus dem FEN-e.p.-Feld).
/// </summary>
public class PuzzleEnPassantTagTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PuzzleTaggingService _service;

    public PuzzleEnPassantTagTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _service = new PuzzleTaggingService(_db, NullLogger<PuzzleTaggingService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    // Klassisches Puzzle-Format: moves[0] = Gegner-Setup, Löser zieht ab moves[1] (ungerade Indizes).
    // Löser = Schwarz. Linie: 1.e4 d5 2.e5 d4 3.c4 (Gegner-Doppelschritt → e.p. d4xc3 verfügbar) …
    // An Index 5 (Löser) steht der e.p.-Schlag offen.
    private const string EpAtSolverButOther = "e2e4 d7d5 e4e5 d5d4 c2c4 g8f6"; // 3…Nf6 statt d4xc3
    private const string EpAtSolverAndPlayed = "e2e4 d7d5 e4e5 d5d4 c2c4 d4c3"; // 3…dxc3 e.p. gespielt
    // e.p. steht NUR an einem GEGNER-Zug offen (Index 4, gerade): 1.e4 a6 2.e5 d5 3.Nf3.
    private const string EpAtOpponentOnly = "e2e4 a7a6 e4e5 d7d5 g1f3";

    [Fact]
    public void HasUnplayedEnPassant_EpAtSolverTurn_NotPlayed_True()
        => Assert.True(PuzzleTaggingService.HasUnplayedEnPassant(Start, EpAtSolverButOther));

    [Fact]
    public void HasUnplayedEnPassant_EpAtSolverTurn_Played_False()
        => Assert.False(PuzzleTaggingService.HasUnplayedEnPassant(Start, EpAtSolverAndPlayed));

    [Fact]
    public void HasUnplayedEnPassant_EpOnlyAtOpponentTurn_False()
        // e.p. war nur bei einem Gegnerzug verfügbar → zählt NICHT (nur Löser-Züge).
        => Assert.False(PuzzleTaggingService.HasUnplayedEnPassant(Start, EpAtOpponentOnly));

    [Fact]
    public void HasUnplayedEnPassant_NoEpAvailable_False()
        => Assert.False(PuzzleTaggingService.HasUnplayedEnPassant(Start, "e2e4 e7e5 g1f3"));

    [Fact]
    public void HasUnplayedEnPassant_InvalidFen_False()
        => Assert.False(PuzzleTaggingService.HasUnplayedEnPassant("not-a-fen", "e2e4 e7e5"));

    [Fact]
    public async Task TagEnPassantPossible_TagsThemesAndPuzzleTags()
    {
        _db.Puzzles.Add(new Puzzle { LichessId = "aaaaa", Fen = Start, Moves = EpAtSolverButOther, Rating = 1500, Themes = "middlegame" });
        _db.Puzzles.Add(new Puzzle { LichessId = "bbbbb", Fen = Start, Moves = EpAtSolverAndPlayed, Rating = 1600, Themes = "enPassant" });
        _db.Puzzles.Add(new Puzzle { LichessId = "ccccc", Fen = Start, Moves = EpAtOpponentOnly, Rating = 1400, Themes = "opening" });
        await _db.SaveChangesAsync();

        var (scanned, tagged) = await _service.TagEnPassantPossibleAsync();

        Assert.Equal(3, scanned);
        Assert.Equal(1, tagged);

        var qualifying = await _db.Puzzles.SingleAsync(p => p.LichessId == "aaaaa");
        Assert.Contains(PuzzleTaggingService.EnPassantPossibleTheme, qualifying.Themes!);   // Themes-String
        var tag = await _db.Tags.SingleAsync(t => t.Name == PuzzleTaggingService.EnPassantPossibleTheme);
        Assert.True(await _db.PuzzleTags.AnyAsync(pt => pt.PuzzleId == qualifying.Id && pt.TagId == tag.Id)); // 2. Tabelle
        // Die anderen zwei bleiben ungetaggt.
        Assert.Equal(1, await _db.PuzzleTags.CountAsync(pt => pt.TagId == tag.Id));
    }

    [Fact]
    public async Task TagEnPassantPossible_IsIdempotent()
    {
        _db.Puzzles.Add(new Puzzle { LichessId = "aaaaa", Fen = Start, Moves = EpAtSolverButOther, Rating = 1500, Themes = "middlegame" });
        await _db.SaveChangesAsync();

        await _service.TagEnPassantPossibleAsync();
        var (_, taggedSecondRun) = await _service.TagEnPassantPossibleAsync();

        Assert.Equal(0, taggedSecondRun);   // beim 2. Lauf nichts Neues
        var tag = await _db.Tags.SingleAsync(t => t.Name == PuzzleTaggingService.EnPassantPossibleTheme);
        Assert.Equal(1, await _db.PuzzleTags.CountAsync(pt => pt.TagId == tag.Id));   // kein Duplikat
        var p = await _db.Puzzles.SingleAsync();
        Assert.Equal(1, p.Themes!.Split(' ').Count(t => t == PuzzleTaggingService.EnPassantPossibleTheme)); // Token nicht doppelt
    }
}
