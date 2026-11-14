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
    public async Task ForOwnGame_StrictThenFamily_ThenThemeFillsUp_Stable()
    {
        await SeedGameAsync();
        var svc = new GameExercises(_db, new EbookSuggestions(_db));
        var dto = (await svc.ForOwnGameAsync(1, 7))!;
        Assert.Collection(dto.Groups,
            g =>
            {
                Assert.Equal((10, GameExercises.MissedMate, 1), (g.Ply, g.Reason, g.MateIn));
                // Drei mit gleichem Bild, zwei aus der Familie — die sechste füllt das Thema mateIn1 auf (0.976.0).
                Assert.Equal(["theme", "strict", "strict", "strict", "family", "family"], g.Exercises.Select(e => e.Match).OrderByDescending(m => m).ToList());
                Assert.Equal(["strict", "strict", "strict", "family", "family", "theme"], g.Exercises.Select(e => e.Match).ToList());
                Assert.DoesNotContain(g.Exercises, e => e.Id == 4);   // unbeliebt
                Assert.Equal(["a7a6", "h6g7"], g.Exercises[0].Moves);
            },
            g =>
            {
                Assert.Equal(GameExercises.PlayedMate, g.Reason);
                Assert.All(g.Exercises, e => Assert.Equal("theme", e.Match));
                Assert.Single(g.Exercises);                             // die andere Thema-Aufgabe steht schon in der ersten Gruppe
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

    // ---- 0.976.0: mehr Motive (Mattdrohung, Matt am Schluss, Grundreihe, Abstand zum zweitbesten Zug) ----

    /// <summary>Die echte Hauptanalyse der Vereinspartie 251 (Prod, Stand 14.11.2026): Halbzug, FEN, Zug, Tiefe, Kandidaten.</summary>
    private static List<GameExercises.Position> Game251() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "GameExercises", "club-game-251.tsv"))
            .Select(l => l.Split('\t')).Select(p => P(int.Parse(p[0]), p[1], p[2], p[4] == "" ? null : p[4])).ToList();

    /// <summary>Die Zeilen ihrer Fehler-Vergleiche mit Mattbewertung: Halbzug, Stellung des Vergleichs, Art, Stellung der Zeile,
    /// bester Zug, Antwort, Ergebnis.</summary>
    private static List<GameExercises.ComparisonRow> Game251Comparisons() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "GameExercises", "club-game-251-comparisons.tsv"))
            .Select(l => l.Split('\t'))
            .Select(p => new GameExercises.ComparisonRow(int.Parse(p[0]), p[1], (MoveComparisonLineKind)int.Parse(p[2]), p[3], p[6], p[4], p[5] == "" ? null : p[5])).ToList();

    [Fact]
    public void Game251_Fork_MateThreat_BackRank()
    {
        // Befund 14.11.2026: nur EINE Gruppe (Gabel). Erwartet: Gabel, Matt(drohung), Grundreihe.
        var positions = Game251();
        var groups = GameExercises.Group(GameExercises.Occurrences(positions, GameExercises.SideLinesOf(Game251Comparisons(), positions)));
        Assert.Collection(groups,
            g =>
            {
                // 45.Ta8+ Lf8 46.Lxc3+ (statt 45.Lb6??) — und schon 41.Td8+ Lf8 42.Lb4: Grundreihe, Bewertung −0,1 bzw. −1,4.
                Assert.Equal((88, true, GameExercises.MissedTactic, TacticMotifs.BackRank), (g.First.Ply, g.First.White, g.First.Reason, g.First.Theme));
                Assert.Equal("backRank|R", g.First.Motif!.Strict);
                Assert.Equal([80], g.Also);
            },
            g =>
            {
                // 37.Sd6 (statt 37.Td8) gabelt die Türme; einer verkauft sich auf g4. Dieselbe Gabel schon im 29. Zug.
                Assert.Equal((72, true, GameExercises.MissedTactic, "fork|N|RR"), (g.First.Ply, g.First.White, g.First.Reason, g.First.Motif!.Strict));
                Assert.Equal([56], g.Also);
            },
            g =>
            {
                // „Auf 37...Tg8 folgt 38.Sf7#“ — ersticktes Matt als Drohung; im 29. Zug dasselbe Bild (29.Sd6 Tg8 30.Sxf7#).
                Assert.Equal((GameExercises.MateThreat, true, 1, MatePattern.SmotheredMate), (g.First.Reason, g.First.White, g.First.MateIn, g.First.Theme));
                Assert.Equal(new[] { 56, 72 }, new[] { g.First.Ply }.Concat(g.Also).OrderBy(x => x));
                Assert.StartsWith("M1|corner|N", g.First.Signature!.Strict);
                Assert.Equal(["smotheredMate", "mateIn1"], GameExercises.ThemeTags(g.First));
            });
        // Ohne die Zeilen der Vergleiche dasselbe (das Matt in 1 findet das Brett selbst).
        Assert.Equal(3, GameExercises.Group(GameExercises.Occurrences(positions)).Count);
    }

    [Fact]
    public void SideLinesOf_ReadsMateLines_OfRepliesTriesAndDefences()
    {
        var positions = Game251();
        var line = Assert.Single(GameExercises.SideLinesOf(Game251Comparisons(), positions));
        Assert.Equal((72, true, 1), (line.Ply, line.White, line.MateIn));
        Assert.Equal(["d6f7"], line.Pv);

        const string at72 = "4r2k/6bp/p7/Bp2p1p1/2r1N1Pn/2P3K1/P3RP2/3R4 w - - 0 37";
        const string afterRg8 = "6rk/6bp/p2N4/Bp2p1p1/2r3Pn/2P3K1/P3RP2/3R4 w - - 2 38";
        const string mate1 = """{"depth":30,"pvs":[{"moves":["d6f7"],"mate":1}]}""";
        GameExercises.ComparisonRow Row(MoveComparisonLineKind kind, string fen = afterRg8, string json = mate1, string? reply = "e8g8", int ply = 72, string cfen = at72) =>
            new(ply, cfen, kind, fen, json, "e4d6", reply);
        // Verlockende Antwort (0.974.0) zählt wie der Antwort-Test.
        Assert.Single(GameExercises.SideLinesOf([Row(MoveComparisonLineKind.BestMoveTry)], positions));
        // Antwort-Test mit einer Antwort, die nach dem besten Zug niemand erwägt → nichts.
        Assert.Empty(GameExercises.SideLinesOf([Row(MoveComparisonLineKind.Reply, reply: "h7h6")], positions));
        // Kandidatenlinie, Matt zu weit weg, der Gegner setzt matt (Weiß-Sicht −1), Vergleich einer anderen Stellung → nichts.
        Assert.Empty(GameExercises.SideLinesOf([Row(MoveComparisonLineKind.Candidate)], positions));
        Assert.Empty(GameExercises.SideLinesOf([Row(MoveComparisonLineKind.BestMoveTry, json: """{"pvs":[{"moves":["d6f7"],"mate":6}]}""")], positions));
        Assert.Empty(GameExercises.SideLinesOf([Row(MoveComparisonLineKind.BestMoveTry, json: """{"pvs":[{"moves":["d6f7"],"mate":-1}]}""")], positions));
        Assert.Empty(GameExercises.SideLinesOf([Row(MoveComparisonLineKind.BestMoveTry, ply: 70)], positions));
        // Andere Verteidigung in der Widerlegung des Fehlzugs: dort ist der WIDERLEGENDE am Zug (hier Schwarz, Weiß-Sicht −1 =
        // Schwarz setzt matt) — die Stelle ist die Stellung nach dem Fehlzug (Halbzug 73), seine Seite.
        var defence = Assert.Single(GameExercises.SideLinesOf(
            [Row(MoveComparisonLineKind.Defence, fen: "3r2k1/8/8/8/8/8/5PPP/6K1 b - - 0 1", json: """{"pvs":[{"moves":["d8d1"],"mate":-1}]}""")], positions));
        Assert.Equal((73, false, 1), (defence.Ply, defence.White, defence.MateIn));
        // Dieselbe Zeile mit Weiß am Zug passt nicht zur Art → nichts.
        Assert.Empty(GameExercises.SideLinesOf([Row(MoveComparisonLineKind.Defence)], positions));
    }

    [Fact]
    public void Occurrences_SideLineMate_IsMateThreat_OnePerPositionAndSide()
    {
        // Schwarz verpasst die Gabel nicht, aber eine Zusatzlinie des Vergleichs endet für Weiß matt (Td8#, Grundreihe).
        var positions = MissedFork();
        var lines = new List<GameExercises.SideLine>
        {
            new(0, true, "6k1/5ppp/8/8/8/8/8/3R2K1 w - - 0 1", ["d1d8"], 1),
            new(0, true, "6k1/5ppp/8/8/8/8/8/3RR1K1 w - - 0 1", ["e1e8", "g8h7x"], 2),   // länger und nicht nachspielbar bis zum Matt
            new(9, true, "6k1/5ppp/8/8/8/8/8/3R2K1 w - - 0 1", ["d1d8"], 1),             // Stelle gibt es nicht
        };
        var occ = GameExercises.Occurrences(positions, lines);
        var threat = Assert.Single(occ, o => o.Reason == GameExercises.MateThreat);
        Assert.Equal((0, true, 1, MatePattern.BackRankMate), (threat.Ply, threat.White, threat.MateIn, threat.Theme));
        Assert.Equal(["backRankMate", "mateIn1"], GameExercises.ThemeTags(threat));
        // Die Mattdrohung steht vor der Taktik desselben Fehlers.
        var groups = GameExercises.Group(occ);
        Assert.Equal([GameExercises.MateThreat, GameExercises.MissedTactic], groups.Select(g => g.First.Reason));
    }

    [Fact]
    public void Occurrences_GameEndsWithForcedMateOnBoard_IsForcedMate()
    {
        // Vereinspartie 240: 30.De6+ und Schwarz gibt auf (Kh8 31.Dxf6+ Kg8 32.Dg7#). Kein gespieltes, kein verpasstes Matt.
        var occ = GameExercises.Occurrences(
        [
            P(58, "r1r3k1/1pqR3p/pN3pp1/8/P1P2b2/1P2Q3/6PP/2R4K w - - 4 30", "e3e6", """[{"uci":"e3e6","mate":3,"pv":["e3e6","g8h8","e6f6","h8g8","f6g7"]}]"""),
            P(59, "r1r3k1/1pqR3p/pN2Qpp1/8/P1P2b2/1P6/6PP/2R4K b - - 5 30", "", """[{"uci":"g8h8","mate":-2,"pv":["g8h8","e6f6","h8g8","f6g7"]}]"""),
        ]);
        var o = Assert.Single(occ);
        Assert.Equal((58, true, GameExercises.ForcedMate, 3), (o.Ply, o.White, o.Reason, o.MateIn));
        Assert.StartsWith("M1|back|Q", o.Signature!.Strict);
        // Vereinspartie 248: Schwarz zieht 37...Sd3 und gibt auf — Weiß am Zug hat Dxg7#.
        var end = GameExercises.Occurrences(
        [
            P(73, "7k/1p4p1/6Q1/3p1N2/1n2q3/6PK/PP5P/8 b - - 4 37", "b4d3", """[{"uci":"e4f5","cp":-1509,"pv":["e4f5","g6f5"]}]"""),
            P(74, "7k/1p4p1/6Q1/3p1N2/4q3/3n2PK/PP5P/8 w - - 5 38", "", """[{"uci":"g6g7","mate":1,"pv":["g6g7"]}]"""),
        ]);
        var last = Assert.Single(end, x => x.Reason == GameExercises.ForcedMate);
        Assert.Equal((74, true, 1), (last.Ply, last.White, last.MateIn));
        // Matt in 9 am Schluss ist kein Motiv.
        Assert.Empty(GameExercises.Occurrences(
        [
            P(58, "r1r3k1/1pqR3p/pN3pp1/8/P1P2b2/1P2Q3/6PP/2R4K w - - 4 30", "e3e6", """[{"uci":"e3e6","mate":9}]"""),
            P(59, "r1r3k1/1pqR3p/pN2Qpp1/8/P1P2b2/1P6/6PP/2R4K b - - 5 30", "", """[{"uci":"g8h8","mate":-8}]"""),
        ]));
    }

    [Fact]
    public void Occurrences_TacticBelowTwoPawns_CountsWhenFarAheadOfSecondMove()
    {
        // Die Gabel bringt nur +1,0, der zweitbeste Zug steht aber bei −3,0 (Abstand 4,0) → Stelle.
        string Cands(int best, int second) =>
            $$"""[{"uci":"d5c7","cp":{{best}},"pv":["d5c7","e8d7","c7a8"]},{"uci":"e1e2","cp":{{second}}}]""";
        List<GameExercises.Position> Game(int best, int second) =>
        [
            P(0, "r3k3/8/8/3N4/8/8/8/4K3 w - - 0 1", "e1e2", Cands(best, second)),
            P(1, "r3k3/8/8/3N4/8/8/4K3/8 b - - 1 1", "a8a7", $$"""[{"uci":"a8a7","cp":{{-second}},"pv":["a8a7"]}]"""),
            P(2, "4k3/r7/8/3N4/8/8/4K3/8 w - - 2 2", "", $$"""[{"uci":"d5c7","cp":{{second}}}]"""),
        ];
        Assert.Equal("fork|N|KR", Assert.Single(GameExercises.Occurrences(Game(100, -300))).Motif!.Strict);
        Assert.Equal(200, GameExercises.MinTacticGap);
        // Abstand nur 1,9 → keine Taktik (wie bisher unter +2,0).
        Assert.Empty(GameExercises.Occurrences(Game(100, -90)));
    }

    [Fact]
    public async Task ForClubGame_251_ThreeGroups_FilledFromThemes_AndComparisonLinesRead()
    {
        // Die echte Analyse samt Vergleichszeile in der Datenbank; Aufgaben nur über die Lichess-Themen (keine Signaturen gerechnet).
        _db.GameAnalyses.Add(new GameAnalysis { Id = 5, UserId = 1, Pgn = "", Origin = GameAnalysisOrigin.Club, LeagueClubGameId = 251, Status = GameAnalysisStatus.Done });
        var n = 1000;
        foreach (var p in Game251())
            _db.GameAnalysisPositions.Add(new GameAnalysisPosition { Id = ++n, GameAnalysisId = 5, Ply = p.Ply, Fen = p.Fen, GameMoveUci = p.GameMoveUci, GameMoveSan = "x", CandidatesJson = p.CandidatesJson });
        var row = Game251Comparisons()[0];
        _db.MoveComparisons.Add(new MoveComparison { Id = 9, UserId = 1, Fen = row.ComparisonFen, GameAnalysisId = 5, Ply = row.Ply, BestUci = row.BestUci });
        _db.MoveComparisonLines.Add(new MoveComparisonLine
        {
            Id = 1, MoveComparisonId = 9, Kind = row.Kind, CandidateUci = "d1d8", ReplyUci = row.ReplyUci, Fen = row.Fen, State = MoveComparisonLineState.Done, ResultJson = row.ResultJson,
        });
        var tagId = 0;
        foreach (var (name, fen, moves) in new[]
                 {
                     ("backRankMate", "6k1/p4ppp/8/8/8/8/8/K2R4 b - - 0 1", "a7a6 d1d8"),
                     ("smotheredMate", "6rk/p5pp/8/6N1/8/8/8/K7 b - - 0 1", "a7a6 g5f7"),
                     ("fork", "r3k3/p7/8/3N4/8/8/8/4K3 b - - 0 1", "a7a6 d5c7 e8d7 c7a8"),
                 })
        {
            _db.Tags.Add(new Tag { Id = ++tagId, Name = name });
            foreach (var r in new[] { 1400, 1450, 1500, 1550, 1600, 1650, 1700 })
                _db.PuzzleTags.Add(new PuzzleTag { PuzzleId = AddPuzzle(fen, moves, r, name).Id, TagId = tagId, Rating = r });
        }
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var dto = await new GameExercises(_db, new EbookSuggestions(_db)).ForClubGameAsync(251);
        Assert.Equal(
            [(GameExercises.MissedTactic, "backRankMate"), (GameExercises.MissedTactic, "fork"), (GameExercises.MateThreat, "smotheredMate")],
            dto.Groups.Select(g => (g.Reason, g.Theme!)));
        Assert.All(dto.Groups, g =>
        {
            Assert.Equal(GameExercises.PerGroup, g.Exercises.Count);
            Assert.All(g.Exercises, e => { Assert.Equal("theme", e.Match); Assert.Contains(g.Theme!, e.Themes); });
        });
        Assert.Equal((int?)1, dto.Groups[2].MateIn);
        Assert.Null(dto.Groups[0].MateIn);
        Assert.Equal(18, dto.Groups.SelectMany(g => g.Exercises).Select(e => e.Id).Distinct().Count());
    }
}
