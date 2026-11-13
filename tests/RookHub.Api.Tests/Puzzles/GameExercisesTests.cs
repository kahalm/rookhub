using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using RookHub.Api.Services.Puzzles;
using Xunit;

namespace RookHub.Api.Tests.Puzzles;

/// <summary>Empfohlene Übungen (<see cref="GameExercises"/>, <see cref="PuzzleSignatureWorker"/>, 0.960.0).</summary>
public class GameExercisesTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private static GameExercises.Position P(int ply, string fen, string move, string? cands) => new(ply, fen, move, cands);

    // Weiß hat Dg7# (Bauer f6 deckt), zieht aber Kb1; Schwarz zieht Kh8, dann setzt Weiß doch matt (Dg7#, jetzt in der Ecke).
    private static readonly List<GameExercises.Position> MissedThenPlayed =
    [
        P(10, "6k1/5p1p/5P1Q/8/8/8/8/K7 w - - 0 1", "a1b1", """[{"uci":"h6g7","mate":1,"pv":["h6g7"]},{"uci":"h6h5","cp":800}]"""),
        P(11, "6k1/5p1p/5P1Q/8/8/8/8/1K6 b - - 1 1", "g8h8", """[{"uci":"g8h8","cp":-900}]"""),
        P(12, "7k/5p1p/5P1Q/8/8/8/8/1K6 w - - 2 2", "h6g7", """[{"uci":"h6g7","mate":1,"pv":["h6g7"]}]"""),
    ];

    [Fact]
    public void Occurrences_MissedAndPlayedMate_BothFound()
    {
        var occ = GameExercises.Occurrences(MissedThenPlayed);
        Assert.Collection(occ,
            o => { Assert.Equal((10, true, GameExercises.MissedMate, 1), (o.Ply, o.White, o.Reason, o.MateIn)); Assert.NotNull(o.Signature); },
            o => { Assert.Equal((12, true, GameExercises.PlayedMate, 1), (o.Ply, o.White, o.Reason, o.MateIn)); Assert.NotNull(o.Signature); });
        Assert.NotEqual(occ[0].Signature!.Strict, occ[1].Signature!.Strict);   // König g8 vs. Ecke h8
    }

    [Fact]
    public void Occurrences_MateKept_NotMissed()
    {
        // Schwarz hat Matt in 2 und zieht so, dass Weiß weiter matt steht → keine verpasste Stelle.
        var occ = GameExercises.Occurrences(
        [
            P(20, "6k1/8/8/8/8/8/5PPP/r5K1 b - - 0 1", "a1b1", """[{"uci":"a1b1","mate":2,"pv":["a1b1"]}]"""),
            P(21, "6k1/8/8/8/8/8/5PPP/1r4K1 w - - 1 2", "h2h3", """[{"uci":"h2h3","mate":-2}]"""),
        ]);
        Assert.Empty(occ);
    }

    [Fact]
    public void Occurrences_NoMate_Empty_AndBlackMissedMateCounts()
    {
        Assert.Empty(GameExercises.Occurrences([P(0, "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "e2e4", """[{"uci":"e2e4","cp":30}]""")]));
        // Schwarz verpasst Td1# (die andere Seite zählt genauso).
        var occ = GameExercises.Occurrences(
        [
            P(5, "3r2k1/8/8/8/8/8/5PPP/6K1 b - - 0 1", "g8f8", """[{"uci":"d8d1","mate":1,"pv":["d8d1"]}]"""),
            P(6, "3r1k2/8/8/8/8/8/5PPP/6K1 w - - 1 2", "h2h3", """[{"uci":"h2h3","cp":-500}]"""),
        ]);
        var o = Assert.Single(occ);
        Assert.False(o.White);
        Assert.StartsWith("MF1|back|Rlo|", o.Signature!.Family);
    }

    [Fact]
    public void Group_SameSignatureTwice_OneGroup()
    {
        var occ = GameExercises.Occurrences(MissedThenPlayed);
        var twice = new List<GameExercises.Occurrence> { occ[0], occ[0] with { Ply = 30 } };
        var g = Assert.Single(GameExercises.Group(twice));
        Assert.Equal(10, g.First.Ply);
        Assert.Equal([30], g.Also);
    }

    private int _id;
    private Puzzle AddPuzzle(string fen, string moves, int rating, string themes, int popularity = 95)
    {
        var p = new Puzzle { Id = ++_id, LichessId = $"x{_id}", Fen = fen, Moves = moves, Rating = rating, Popularity = popularity, NbPlays = 1000, Themes = themes };
        _db.Puzzles.Add(p);
        return p;
    }

    private async Task SeedGameAsync()
    {
        _db.SavedGames.Add(new SavedGame { Id = 7, UserId = 1, Pgn = "", WhiteElo = 1550, BlackElo = 1500, GameAnalysisId = 1, ShareToken = "tok7" });
        var n = 0;
        foreach (var p in MissedThenPlayed)
            _db.GameAnalysisPositions.Add(new GameAnalysisPosition { Id = ++n, GameAnalysisId = 1, Ply = p.Ply, Fen = p.Fen, GameMoveUci = p.GameMoveUci, GameMoveSan = "x", CandidatesJson = p.CandidatesJson });
        // Gleiches Bild (Dame g7, Bauer f6): drei beliebte, eine unbeliebte.
        foreach (var r in new[] { 1500, 1550, 1600 }) AddPuzzle("6k1/p4p1p/5P1Q/8/8/8/8/K7 b - - 0 1", "a7a6 h6g7", r, "mate mateIn1");
        AddPuzzle("6k1/p4p1p/5P1Q/8/8/8/8/K7 b - - 0 1", "a7a6 h6g7", 1550, "mate mateIn1", popularity: 50);
        // Gleiche Familie (Springer h5 deckt).
        foreach (var r in new[] { 1520, 1580 }) AddPuzzle("6k1/p4p1p/7Q/7N/8/8/8/K7 b - - 0 1", "a7a6 h6g7", r, "mate mateIn1");
        // Anderes Bild (Grundreihe), nur über das Thema mateIn1 erreichbar.
        var tag = new Tag { Id = 1, Name = "mateIn1" };
        _db.Tags.Add(tag);
        foreach (var r in new[] { 1540, 1560 })
        {
            var p = AddPuzzle("6k1/p4ppp/8/8/8/8/8/K2R4 b - - 0 1", "a7a6 d1d8", r, "backRankMate mate mateIn1");
            _db.PuzzleTags.Add(new PuzzleTag { PuzzleId = p.Id, TagId = 1, Rating = r });
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        while (await PuzzleSignatureWorker.RunBatchAsync(_db, 3) > 0) { }
    }

    [Fact]
    public async Task Worker_ResumesInPortions_SkipsUnpopular()
    {
        await SeedGameAsync();
        Assert.Equal(7, await _db.PuzzleSignatures.CountAsync());   // 8 Aufgaben, die unbeliebte nicht
        Assert.Equal(_id, await PuzzleSignatureWorker.CursorAsync(_db));
        Assert.True(await PuzzleSignatureWorker.DoneAsync(_db));
        Assert.Equal(0, await PuzzleSignatureWorker.RunBatchAsync(_db, 3));
    }

    [Fact]
    public async Task ForOwnGame_StrictThenFamily_ThemeOnlyWithoutSignatureMatch_Stable()
    {
        await SeedGameAsync();
        var svc = new GameExercises(_db, new EbookSuggestions(_db));
        var dto = (await svc.ForOwnGameAsync(1, 7))!;
        Assert.Collection(dto.Groups,
            g =>
            {
                Assert.Equal((10, GameExercises.MissedMate, 1), (g.Ply, g.Reason, g.MateIn));
                Assert.Equal(["strict", "strict", "strict", "family", "family"], g.Exercises.Select(e => e.Match).OrderByDescending(m => m).ToList());
                Assert.DoesNotContain(g.Exercises, e => e.Id == 4);   // unbeliebt
                Assert.Equal(["a7a6", "h6g7"], g.Exercises[0].Moves);
            },
            g =>
            {
                Assert.Equal(GameExercises.PlayedMate, g.Reason);
                Assert.All(g.Exercises, e => Assert.Equal("theme", e.Match));
                Assert.Equal(2, g.Exercises.Count);
            });
        var again = (await svc.ForOwnGameAsync(1, 7))!;
        Assert.Equal(dto.Groups.SelectMany(g => g.Exercises).Select(e => e.Id), again.Groups.SelectMany(g => g.Exercises).Select(e => e.Id));
        Assert.NotNull(await svc.ForSharedOwnGameAsync("tok7"));
    }

    [Fact]
    public async Task Rights_ForeignOrUnknown_Null()
    {
        await SeedGameAsync();
        var svc = new GameExercises(_db, new EbookSuggestions(_db));
        Assert.Null(await svc.ForOwnGameAsync(2, 7));
        Assert.Null(await svc.ForSharedOwnGameAsync("nope"));
        Assert.Null(await svc.ForSharedOwnGameAsync(""));
        Assert.Null(await svc.ForClubLinkAsync("nope"));
    }

    [Fact]
    public async Task GameWithoutTacticalMotif_NoGroups()
    {
        _db.SavedGames.Add(new SavedGame { Id = 8, UserId = 1, Pgn = "", GameAnalysisId = 2 });
        _db.GameAnalysisPositions.Add(new GameAnalysisPosition { Id = 99, GameAnalysisId = 2, Ply = 0, Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", GameMoveUci = "e2e4", GameMoveSan = "e4", CandidatesJson = """[{"uci":"e2e4","cp":30}]""" });
        AddPuzzle("6k1/p4p1p/5P1Q/8/8/8/8/K7 b - - 0 1", "a7a6 h6g7", 1500, "mate mateIn1");
        await _db.SaveChangesAsync();
        var dto = (await new GameExercises(_db, new EbookSuggestions(_db)).ForOwnGameAsync(1, 8))!;
        Assert.Empty(dto.Groups);
        // Ohne Analyse ebenso.
        _db.SavedGames.Add(new SavedGame { Id = 9, UserId = 1, Pgn = "" });
        await _db.SaveChangesAsync();
        Assert.Empty((await new GameExercises(_db, new EbookSuggestions(_db)).ForOwnGameAsync(1, 9))!.Groups);
    }

    // Weiß hätte mit Sc7+ König und Turm gegabelt (+6), zieht aber Ke2 (Fehler, danach 0.00).
    private static List<GameExercises.Position> MissedFork(string pv = "\"d5c7\",\"e8d7\",\"c7a8\"", int cp = 600) =>
    [
        P(0, "r3k3/8/8/3N4/8/8/8/4K3 w - - 0 1", "e1e2", $$"""[{"uci":"d5c7","cp":{{cp}},"pv":[{{pv}}]},{"uci":"e1e2","cp":0}]"""),
        P(1, "r3k3/8/8/3N4/8/8/4K3/8 b - - 1 1", "a8a7", """[{"uci":"a8a7","cp":0,"pv":["a8a7"]}]"""),
        P(2, "4k3/r7/8/3N4/8/8/4K3/8 w - - 2 2", "", """[{"uci":"d5c7","cp":0}]"""),
    ];

    [Fact]
    public void Occurrences_MissedFork_IsTacticStelle()
    {
        var o = Assert.Single(GameExercises.Occurrences(MissedFork()));
        Assert.Equal((0, true, GameExercises.MissedTactic), (o.Ply, o.White, o.Reason));
        Assert.Equal("fork|N|KR", o.Motif!.Strict);
    }

    [Fact]
    public void Occurrences_PositionalMistake_NoMotif_Nothing()
    {
        // Gleicher Bewertungsverlust, aber die beste Linie ist ein ruhiger Königszug → kein Motiv, keine Stelle.
        Assert.Empty(GameExercises.Occurrences(MissedFork(pv: "\"e1d2\"")));
        // Beste Linie gewinnt nicht klar (+1) → keine Taktik.
        Assert.Empty(GameExercises.Occurrences(MissedFork(cp: 100)));
    }

    [Fact]
    public async Task ForOwnGame_MissedFork_StrictForkPuzzles()
    {
        _db.SavedGames.Add(new SavedGame { Id = 11, UserId = 1, Pgn = "", WhiteElo = 1500, GameAnalysisId = 3 });
        var n = 200;
        foreach (var p in MissedFork())
            _db.GameAnalysisPositions.Add(new GameAnalysisPosition { Id = ++n, GameAnalysisId = 3, Ply = p.Ply, Fen = p.Fen, GameMoveUci = p.GameMoveUci, GameMoveSan = "x", CandidatesJson = p.CandidatesJson });
        foreach (var r in new[] { 1450, 1500, 1550 }) AddPuzzle("r3k3/p7/8/3N4/8/8/8/4K3 b - - 0 1", "a7a6 d5c7 e8d7 c7a8", r, "fork short");
        AddPuzzle("4k3/8/8/8/3n4/8/7P/R3K3 w - - 0 1", "h2h3 d4c2 e1d2 c2a1", 1500, "fork short");   // Gabel K+T, aber Schwarz: gleiche Familie
        AddPuzzle("8/4P3/8/8/8/k7/8/K7 b - - 0 1", "a3b3 e7e8q", 1500, "promotion");               // anderes Motiv
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        Assert.Equal(5, await PuzzleSignatureWorker.RunTacticBatchAsync(_db, 10));
        Assert.Equal(5, await _db.PuzzleSignatures.CountAsync(x => x.Kind != PuzzleSignatureKind.Mate));
        var dto = (await new GameExercises(_db, new EbookSuggestions(_db)).ForOwnGameAsync(1, 11))!;
        var g = Assert.Single(dto.Groups);
        Assert.Equal((GameExercises.MissedTactic, "fork", (int?)null), (g.Reason, g.Theme, g.MateIn));
        Assert.Equal(4, g.Exercises.Count);
        Assert.All(g.Exercises, e => Assert.Equal("strict", e.Match));   // Farbe egal: dieselbe strenge Signatur
    }

    [Fact]
    public void BandMid_ParsesBand() => Assert.Equal(1750, GameExercises.BandMid("1700–1799"));
}
