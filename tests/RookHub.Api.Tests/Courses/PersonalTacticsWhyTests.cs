using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Puzzles;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;
using RookHub.Api.Services.Ebooks;
using RookHub.Api.Services.Puzzles;
using Xunit;

namespace RookHub.Api.Tests.Courses;

/// <summary>Vermerk „Warum diese Aufgabe“ im Kurs „Persönliche Taktiken“ (<see cref="PersonalTacticsWhy"/>, 1.001.0) und sein
/// Nachtrag im Bestand (<see cref="PersonalTacticsWhyBackfill"/>).</summary>
public class PersonalTacticsWhyTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    // ---------- Bau des Vermerks (rein)

    private static GameExerciseGroupDto Group(string reason, int ply = 41, bool white = false, int? mateIn = null, string? theme = null,
        string? motif = null, List<int>? also = null) =>
        new(ply, white, reason, mateIn, also ?? [], [], theme, motif, MoveSan: "b6", PrevMoveSan: "Qd2");

    private static GameExerciseDto Exercise(string match, List<string>? themes = null, bool book = false) =>
        new(1, "8/8/8/8/8/8/8/8 w - - 0 1", ["a1a2"], book ? 0 : 1500, themes ?? [], match, book ? "book" : "lichess",
            book ? new GameExerciseBookDto(3, "Buch", null, null, 12, true, null, true) : null);

    [Fact]
    public void Build_MissedTactic_StrictSignature_NamesTheMoveAndThePieces()
    {
        var why = PersonalTacticsWhy.Build(Group(GameExercises.MissedTactic, theme: "fork", motif: "fork|N|KQ"), Exercise("strict"));
        Assert.Equal((41, false, "missedTactic", 41, "b6"), (why.Ply, why.White, why.Reason, why.MovePly, why.MoveSan));
        Assert.Equal(("fork", "strict", "N", "KQ"), (why.Theme, why.Match, why.Piece, why.Targets));
        Assert.False(why.Book);
        Assert.Null(why.MatchTheme);
        Assert.Null(why.MateIn);
    }

    [Fact]
    public void Build_AllowedTactic_RefersToTheMistakeBefore()
    {
        var why = PersonalTacticsWhy.Build(Group(GameExercises.AllowedTactic, ply: 42, white: true, theme: "pin", motif: "pin|B|N>K"), Exercise("strict"));
        Assert.Equal((42, true, 41, "Qd2"), (why.Ply, why.White, why.MovePly, why.MoveSan));
        Assert.Equal(("B", "NK"), (why.Piece, why.Targets));   // Fesselung: vorn, dann hinten
    }

    [Fact]
    public void Build_Family_SameMotifOtherPieces_NoPieces()
    {
        var why = PersonalTacticsWhy.Build(Group(GameExercises.MissedTactic, theme: "fork", motif: "fork|N|KQ"), Exercise("family"));
        Assert.Equal("family", why.Match);
        Assert.Null(why.Piece);
        Assert.Null(why.Targets);
    }

    [Fact]
    public void Build_ThemeOnly_NamesTheThemeItWasFilledFrom()
    {
        var tactic = PersonalTacticsWhy.Build(Group(GameExercises.MissedTactic, theme: "discoveredAttack", motif: "discoveredAttack|B|Q|N"), Exercise("theme"));
        Assert.Equal(("theme", "discoveredAttack"), (tactic.Match, tactic.MatchTheme));
        Assert.Null(tactic.Piece);   // keine Figuren — weder gleiche Signatur noch Gabel/Fesselung/Spieß

        // Matt mit benanntem Bild: aus dem Bild, wenn die Aufgabe es trägt — sonst aus mateInN (Reihenfolge von GameExercises.ThemeTags).
        var named = Group(GameExercises.MissedMate, mateIn: 2, theme: "backRankMate");
        Assert.Equal("backRankMate", PersonalTacticsWhy.Build(named, Exercise("theme", ["backRankMate", "mateIn2"])).MatchTheme);
        Assert.Equal("mateIn2", PersonalTacticsWhy.Build(named, Exercise("theme", ["mateIn2"])).MatchTheme);
        Assert.Equal("mateIn5", PersonalTacticsWhy.Build(Group(GameExercises.MissedMate, mateIn: 7), Exercise("theme", ["mateIn5"])).MatchTheme);
    }

    [Fact]
    public void Build_Mates_KeepMateInAndFurtherPlaces()
    {
        var missed = PersonalTacticsWhy.Build(Group(GameExercises.MissedMate, ply: 10, white: true, mateIn: 1, also: [12]), Exercise("strict"));
        Assert.Equal(("missedMate", 1, 10, "b6"), (missed.Reason, missed.MateIn, missed.MovePly, missed.MoveSan));
        Assert.Equal([12], missed.Also!);
        Assert.Null(missed.Piece);

        var played = PersonalTacticsWhy.Build(Group(GameExercises.PlayedMate, mateIn: 3), Exercise("family"));
        Assert.Equal(("playedMate", 3, "family"), (played.Reason, played.MateIn, played.Match));
        var threat = PersonalTacticsWhy.Build(Group(GameExercises.MateThreat, mateIn: 1, also: Enumerable.Range(1, 20).ToList()), Exercise("strict"));
        Assert.Equal(PersonalTacticsWhy.MaxAlso, threat.Also!.Count);
    }

    [Fact]
    public void Build_BookDiagram_IsMarked()
    {
        var why = PersonalTacticsWhy.Build(Group(GameExercises.MissedTactic, theme: "skewer", motif: "skewer|R|K>Q"), Exercise("strict", book: true));
        Assert.True(why.Book);
        Assert.Equal(("R", "KQ"), (why.Piece, why.Targets));
    }

    [Fact]
    public void Serialize_Parse_RoundTrip_FitsTheColumn_BrokenIsNull()
    {
        var why = PersonalTacticsWhy.Build(Group(GameExercises.MateThreat, mateIn: 5, theme: "smotheredMate", also: Enumerable.Range(100, 30).ToList()),
            Exercise("theme", ["smotheredMate"], book: true));
        var json = PersonalTacticsWhy.Serialize(why);
        Assert.True(json.Length <= 600, json);
        Assert.DoesNotContain("null", json);
        var back = PersonalTacticsWhy.Parse(json)!;
        Assert.Equal((why.Reason, why.Match, why.MatchTheme, why.MateIn, why.Book), (back.Reason, back.Match, back.MatchTheme, back.MateIn, back.Book));
        Assert.Equal(why.Also, back.Also);
        Assert.Null(PersonalTacticsWhy.Parse("{kaputt"));
        Assert.Null(PersonalTacticsWhy.Parse("{}"));
        Assert.Null(PersonalTacticsWhy.Parse(null));
    }

    [Fact]
    public void MapToDto_HandsTheNoteToTheSolver()
    {
        var json = PersonalTacticsWhy.Serialize(PersonalTacticsWhy.Build(Group(GameExercises.MissedTactic, theme: "fork", motif: "fork|N|KQ"), Exercise("strict")));
        var dto = BookPuzzleService.MapToDto(new BookPuzzle { LineId = "x", Fen = "f", Moves = "m", Why = json });
        Assert.Equal(("missedTactic", "N"), (dto.Why!.Reason, dto.Why.Piece));
        Assert.Null(BookPuzzleService.MapToDto(new BookPuzzle { LineId = "y", Fen = "f", Moves = "m" }).Why);
    }

    // ---------- Anlegen + Nachtrag im Bestand

    private PersonalTacticsCourse Service() => new(_db, new GameExercises(_db, new EbookSuggestions(_db)));
    private static readonly EbookViewer Viewer1 = new(1, false);
    private static Task<EbookViewer> ViewerOf(int userId, CancellationToken _) => Task.FromResult(new EbookViewer(userId, false));

    private int _positionId, _puzzleId;
    private void AddPosition(int analysisId, int ply, string fen, string move, string san, string cands) =>
        _db.GameAnalysisPositions.Add(new GameAnalysisPosition { Id = ++_positionId, GameAnalysisId = analysisId, Ply = ply, Fen = fen, GameMoveUci = move, GameMoveSan = san, CandidatesJson = cands });
    private void AddPuzzle(string fen, string moves, int rating, string themes) =>
        _db.Puzzles.Add(new Puzzle { Id = ++_puzzleId, LichessId = $"L{_puzzleId}", Fen = fen, Moves = moves, Rating = rating, Popularity = 95, NbPlays = 1000, Themes = themes });

    /// <summary>Partie 7: Weiß verpasst Dg7# (Halbzug 10, gespielt Kb1) und setzt bei Halbzug 12 doch matt. Partie 11: Weiß verpasst
    /// die Springergabel Sc7+ (gespielt Ke2). Dazu je drei Lichess-Aufgaben mit demselben Bild.</summary>
    private async Task SeedAsync()
    {
        _db.SavedGames.Add(new SavedGame { Id = 7, UserId = 1, Pgn = "", White = "Anna", Black = "Bert", WhiteElo = 1550, BlackElo = 1500, GameAnalysisId = 1 });
        AddPosition(1, 10, "6k1/5p1p/5P1Q/8/8/8/8/K7 w - - 0 1", "a1b1", "Kb1", """[{"uci":"h6g7","mate":1,"pv":["h6g7"]},{"uci":"h6h5","cp":800}]""");
        AddPosition(1, 11, "6k1/5p1p/5P1Q/8/8/8/8/1K6 b - - 1 1", "g8h8", "Kh8", """[{"uci":"g8h8","cp":-900}]""");
        AddPosition(1, 12, "7k/5p1p/5P1Q/8/8/8/8/1K6 w - - 2 2", "h6g7", "Qg7#", """[{"uci":"h6g7","mate":1,"pv":["h6g7"]}]""");
        _db.SavedGames.Add(new SavedGame { Id = 11, UserId = 1, Pgn = "", White = "Anna", Black = "Cleo", WhiteElo = 1500, GameAnalysisId = 3 });
        AddPosition(3, 0, "r3k3/8/8/3N4/8/8/8/4K3 w - - 0 1", "e1e2", "Ke2", """[{"uci":"d5c7","cp":600,"pv":["d5c7","e8d7","c7a8"]},{"uci":"e1e2","cp":0}]""");
        AddPosition(3, 1, "r3k3/8/8/3N4/8/8/4K3/8 b - - 1 1", "a8a7", "Ra7", """[{"uci":"a8a7","cp":0,"pv":["a8a7"]}]""");
        AddPosition(3, 2, "4k3/r7/8/3N4/8/8/4K3/8 w - - 2 2", "", "", """[{"uci":"d5c7","cp":0}]""");
        foreach (var r in new[] { 1500, 1550, 1600 }) AddPuzzle("6k1/p4p1p/5P1Q/8/8/8/8/K7 b - - 0 1", "a7a6 h6g7", r, "mate mateIn1");
        foreach (var r in new[] { 1450, 1500, 1550 }) AddPuzzle("r3k3/p7/8/3N4/8/8/8/4K3 b - - 0 1", "a7a6 d5c7 e8d7 c7a8", r, "fork short");
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        while (await PuzzleSignatureWorker.RunBatchAsync(_db, 10) > 0) { }
        while (await PuzzleSignatureWorker.RunTacticBatchAsync(_db, 10) > 0) { }
    }

    private Task<RookHub.Api.DTOs.Courses.PersonalTacticsChapterDto> OpenAsync(int gameId) =>
        Service().OpenAsync(1, new PersonalTacticsCourse.Game(PersonalTacticsCourse.GameKind.Own, gameId, true), Viewer1, "de");

    [Fact]
    public async Task Open_EveryLineGetsItsNote_FromTheGameAndTheKindOfHit()
    {
        await SeedAsync();
        await OpenAsync(7);
        await OpenAsync(11);
        var lines = await _db.BookPuzzles.AsNoTracking().ToListAsync();
        Assert.NotEmpty(lines);
        Assert.All(lines, l => Assert.NotNull(PersonalTacticsWhy.Parse(l.Why)));

        var mate = lines.Where(l => l.LineId.Contains(":own7:")).Select(l => PersonalTacticsWhy.Parse(l.Why)!).ToList();
        Assert.NotEmpty(mate);
        Assert.All(mate, w => Assert.Equal(("missedMate", true, 1, 10, "Kb1", "strict"), (w.Reason, w.White, w.MateIn, w.MovePly, w.MoveSan, w.Match)));

        var fork = lines.Where(l => l.LineId.Contains(":own11:")).Select(l => PersonalTacticsWhy.Parse(l.Why)!).ToList();
        Assert.NotEmpty(fork);
        Assert.All(fork, w =>
        {
            Assert.Equal(("missedTactic", true, "fork", 0, "Ke2", "strict", "N"), (w.Reason, w.White, w.Theme, w.MovePly, w.MoveSan, w.Match, w.Piece));
            Assert.Equal("KR", string.Concat(w.Targets!.Order()));   // Springergabel auf König und Turm
        });
    }

    [Fact]
    public async Task Backfill_OldLinesGetTheNoteOfTheSameExercise_UnmatchedStayWithout_RunsOnce()
    {
        await SeedAsync();
        await OpenAsync(7);
        await OpenAsync(11);
        var expected = await _db.BookPuzzles.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Why);

        // Bestand von vor 1.001.0: ohne Vermerk. Dazu eine Linie, die es so nicht mehr gibt (andere Stellung), eine mit
        // falscher Zugnummer im Titel und eine mit fremder LineId — alle drei bleiben ohne.
        var all = await _db.BookPuzzles.ToListAsync();
        foreach (var l in all) l.Why = null;
        var src = all.First(l => l.LineId.Contains(":own11:"));
        _db.BookPuzzles.AddRange(
            new BookPuzzle { LineId = $"{src.LineId[..^2]}97", BookFileName = src.BookFileName, BookId = src.BookId, Round = "97", Fen = "4k3/8/8/8/8/8/8/4K3 w - - 0 1", Moves = src.Moves, Title = src.Title, Source = src.Source },
            new BookPuzzle { LineId = $"{src.LineId[..^2]}98", BookFileName = src.BookFileName, BookId = src.BookId, Round = "98", Fen = src.Fen, Moves = src.Moves, Title = "9. · Lichess L1", Source = src.Source },
            new BookPuzzle { LineId = "personal-tactics-uX.pgn:own11:99", BookFileName = src.BookFileName, BookId = src.BookId, Round = "99", Fen = src.Fen, Moves = src.Moves, Title = src.Title, Source = src.Source });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var r = await PersonalTacticsWhyBackfill.RunOnceAsync(_db, Service(), ViewerOf);
        Assert.Equal(new PersonalTacticsWhyBackfill.Result(expected.Count + 3, expected.Count, 3), r);
        _db.ChangeTracker.Clear();
        var after = await _db.BookPuzzles.AsNoTracking().ToListAsync();
        Assert.All(after.Where(l => expected.ContainsKey(l.Id)), l => Assert.Equal(expected[l.Id], l.Why));
        Assert.All(after.Where(l => !expected.ContainsKey(l.Id)), l => Assert.Null(l.Why));

        // Zweiter Start: Marke gesetzt → nichts. Und auch ohne Marke ändert ein zweiter Lauf nichts (idempotent).
        Assert.Null(await PersonalTacticsWhyBackfill.RunOnceAsync(_db, Service(), ViewerOf));
        Assert.Equal(new PersonalTacticsWhyBackfill.Result(3, 0, 3), await PersonalTacticsWhyBackfill.BackfillAsync(_db, Service(), ViewerOf));
        _db.ChangeTracker.Clear();
        Assert.Equal(after.Select(l => (l.Id, l.Why)), (await _db.BookPuzzles.AsNoTracking().ToListAsync()).Select(l => (l.Id, l.Why)));
    }

    [Theory]
    [InlineData("personal-tactics-u5.pgn:club250:01", 5, "Club", 250)]
    [InlineData("personal-tactics-u12.pgn:own237:06", 12, "Own", 237)]
    public void GameOf_ReadsOwnerAndGameFromTheLineId(string lineId, int user, string kind, int game)
    {
        var g = PersonalTacticsWhyBackfill.GameOf(lineId)!.Value;
        Assert.Equal((user, kind, game), (g.UserId, g.Game.Kind.ToString(), g.Game.Id));
    }

    [Theory]
    [InlineData("personal-tactics-u5.pgn:foo250:01")]
    [InlineData("personal-tactics-uX.pgn:own1:01")]
    [InlineData("tactics-u5.pgn:own1:01")]
    [InlineData("personal-tactics-u5.pgn:own1")]
    public void GameOf_ForeignFormat_IsNull(string lineId) => Assert.Null(PersonalTacticsWhyBackfill.GameOf(lineId));
}
