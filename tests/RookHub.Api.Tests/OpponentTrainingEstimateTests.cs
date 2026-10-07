using Chess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Prep;

namespace RookHub.Api.Tests;

/// <summary>
/// Trainingslinien mit Schätzung (Wunsch 2026-10-07: „wenn gaaaanz wenig games vorhanden sind … nimm lichesspartien, +100 - +400
/// elo … oder kombination"): je Gegner-Stellung eigene Partien ab der Schwelle, sonst der Explorer; Mischlinien, ganz ohne
/// Gegnerpartien, Explorer ohne Daten → Auffüllregel, Budget überschritten → unvollständig; Wertungsband → Explorer-Stufen.
/// Der Explorer ist IMMER gemockt.
/// </summary>
public class OpponentTrainingEstimateTests
{
    private static string KeyAfter(params string[] sans)
    {
        var board = new ChessBoard();
        foreach (var san in sans) Assert.True(board.Move(san), san);
        return RepertoireReach.Key(board.ToFen());
    }

    private static ExplorerPositionStats Stats(params (string San, long Games)[] moves) =>
        new(moves.Sum(m => m.Games), moves.Select(m => new ExplorerMoveStat("", m.San, m.Games, null, null)).ToList());

    private static RepertoireReach.Graph Graph(char color, params string[] lines) =>
        RepertoireReach.Build(lines.Select(l => $"[Event \"x\"]\n[Black \"K\"]\n\n{l} *\n").SelectMany(PgnMoveTree.ParseSections), color);

    private static IEnumerable<OpponentTrainingLines.Game> Times(int n, string moves, bool opponentWhite) =>
        Enumerable.Range(0, n).Select(_ => new OpponentTrainingLines.Game(moves.Split(' '), opponentWhite, 2025));

    private static OpponentTrainingLines.Result Rank(RepertoireReach.Graph g, IEnumerable<OpponentTrainingLines.Game> games,
        Dictionary<string, ExplorerPositionStats> explorer, int minOwn = 5, HashSet<string>? pending = null)
    {
        var a = OpponentTrainingLines.Analyze(g, games);
        var est = new OpponentTrainingLines.Estimate(n => explorer.GetValueOrDefault(n.Key), minOwn, pending ?? new HashSet<string>());
        return OpponentTrainingLines.Rank(g, Enumerable.Repeat("K", g.Mainlines.Count).ToList(), a, est);
    }

    [Theory]
    [InlineData(4, "lichess", 0.3)]     // unter der Schwelle: der Explorer (1...c5 in 30 % der Partien)
    [InlineData(5, "own", 1.0)]         // ab 5 weitergespielten Partien: seine (immer 1...c5)
    public void Threshold_FourGamesUseTheExplorer_FiveUseHisOwn(int games, string source, double p)
    {
        var g = Graph('w', "1. e4 c5 2. Nf3");
        var explorer = new Dictionary<string, ExplorerPositionStats> { [KeyAfter("e4")] = Stats(("e5", 70), ("c5", 30)) };
        var line = Assert.Single(Rank(g, Times(games, "e4 c5 Nf3", false), explorer).Lines);
        Assert.Equal(source, line.Source);
        Assert.Equal(p, line.Probability, 6);
    }

    [Fact]
    public void Mixed_HisOwn1e4_ThenExplorerFrom1c6()
    {
        // Der Nutzer spielt Caro-Kann (Schwarz); der Gegner spielt immer 1.e4, hat aber nie gegen 1...c6 gespielt
        var g = Graph('b', "1. e4 c6 2. d4 d5 3. e5 Bf5", "1. e4 c6 2. d4 d5 3. Nc3 dxe4");
        var games = Times(6, "e4 e5 Nf3 Nc6", true);
        var explorer = new Dictionary<string, ExplorerPositionStats>
        {
            [KeyAfter("e4", "c6")] = Stats(("d4", 80), ("Nc3", 20)),
            [KeyAfter("e4", "c6", "d4", "d5")] = Stats(("e5", 50), ("Nc3", 30), ("exd5", 20)),
        };

        var r = Rank(g, games, explorer);

        Assert.Equal(new[] { "e5", "Nc3" }, r.Lines.Select(l => l.Sans[4]));
        var advance = r.Lines[0];
        Assert.Equal("mixed", advance.Source);
        Assert.Equal((1, 2), (advance.OwnMoves, advance.LichessMoves));
        Assert.Equal(2, advance.LichessFrom);                       // ab 2.d4 (Halbzug 2) geschätzt
        Assert.Equal(1.0 * 0.8 * 0.5, advance.Probability, 6);
        Assert.Equal(1.0 * 0.8 * 0.3, r.Lines[1].Probability, 6);
    }

    [Fact]
    public void NoGamesAtAll_EverythingFromTheExplorer()
    {
        var g = Graph('w', "1. e4 c5 2. Nf3 d6 3. d4", "1. e4 e5 2. Nf3 Nc6 3. Bb5");
        var explorer = new Dictionary<string, ExplorerPositionStats>
        {
            [KeyAfter("e4")] = Stats(("c5", 40), ("e5", 60)),
            [KeyAfter("e4", "c5", "Nf3")] = Stats(("d6", 50), ("Nc6", 50)),
            [KeyAfter("e4", "e5", "Nf3")] = Stats(("Nc6", 90), ("Nf6", 10)),
        };
        var r = Rank(g, [], explorer);
        Assert.Equal(0, r.Games);
        Assert.All(r.Lines, l => Assert.Equal("lichess", l.Source));
        Assert.Equal("e5", r.Lines[0].Sans[1]);                     // 0,6 × 0,9 vor 0,4 × 0,5
        Assert.Equal(0.54, r.Lines[0].Probability, 6);
        Assert.Equal(1, r.Lines[0].LichessFrom);                    // ab 1...e5 (Halbzug 1)
    }

    [Fact]
    public void ExplorerWithoutData_FillRuleForExactlyThoseLines_BehindAllWithASource()
    {
        var g = Graph('w', "1. e4 c6 2. d4 d5", "1. e4 c5 2. Nf3 d6");
        var explorer = new Dictionary<string, ExplorerPositionStats>
        {
            [KeyAfter("e4")] = Stats(("c5", 50), ("c6", 50)),
            [KeyAfter("e4", "c5", "Nf3")] = Stats(("d6", 100)),
            [KeyAfter("e4", "c6", "d4")] = Stats(("d5", 5)),          // unter 10 Partien: keine Aussage
        };
        var r = Rank(g, [], explorer);
        Assert.Equal("lichess", r.Lines[0].Source);
        Assert.Equal("c5", r.Lines[0].Sans[1]);
        Assert.Equal("none", r.Lines[1].Source);                    // Auffüllregel, hinten
        Assert.Equal((0, 1), (r.Lines[1].OwnMoves, r.Lines[1].LichessMoves));
        Assert.False(r.Lines[1].Pending);
    }

    [Fact]
    public void BudgetExceeded_LinesArePending()
    {
        var g = Graph('w', "1. e4 c5 2. Nf3 d6");
        var key = KeyAfter("e4");
        var r = Rank(g, [], new Dictionary<string, ExplorerPositionStats>(), pending: [key, KeyAfter("e4", "c5", "Nf3")]);
        Assert.True(Assert.Single(r.Lines).Pending);
        Assert.Equal("none", r.Lines[0].Source);
    }

    [Fact]
    public void NeedsExplorer_OnlyOpponentNodesBelowTheThreshold_OncePerPosition()
    {
        var g = Graph('w', "1. e4 c5 2. Nf3 d6 3. d4", "1. e4 c5 2. Nf3 Nc6 3. d4");
        var a = OpponentTrainingLines.Analyze(g, Times(5, "e4 c5 Nf3 d6", false));
        var need = OpponentTrainingLines.NeedsExplorer(g, a, 5).Select(n => n.Key).ToList();
        // nach 1.e4 hat er 5× weitergespielt (eigene Daten), nach 2.Sf3 auch — nichts zu schätzen
        Assert.Empty(need);
        var few = OpponentTrainingLines.NeedsExplorer(g, OpponentTrainingLines.Analyze(g, Times(2, "e4 c5 Nf3 d6", false)), 5).Select(n => n.Key).ToList();
        Assert.Equal(new[] { KeyAfter("e4"), KeyAfter("e4", "c5", "Nf3") }, few);
    }

    [Theory]
    [InlineData(1900, false, new[] { 2000, 2200 })]
    [InlineData(2400, false, new[] { 2500 })]
    [InlineData(1500, true, new[] { 1600, 1800 })]      // [1600, 1900] schneidet die lokalen Stufen 1600 UND 1800
    [InlineData(1000, true, new[] { 1600 })]            // Band ganz unter den lokalen Stufen: die nächstgelegene
    [InlineData(1100, false, new[] { 1200, 1400 })]
    public void RatingBand_ToExplorerStages(int elo, bool local, int[] expected)
    {
        var available = local ? LocalExplorerClient.LocalRatings : ExplorerQuery.AllowedRatings;
        Assert.Equal(expected, TrainingExplorer.Stages(elo, available));
    }

    // ── Dienst mit gemocktem Explorer ────────────────────────────────────────────────────────────

    private sealed class FakeExplorer(Dictionary<string, ExplorerPositionStats> stats, HashSet<string>? pending = null) : ITrainingExplorer
    {
        public List<(List<string> Keys, int Elo)> Calls { get; } = new();
        public Task<TrainingExplorerResult> StatsAsync(int userId, IReadOnlyList<RepertoireReach.Node> positions, int elo, CancellationToken ct)
        {
            Calls.Add((positions.Select(p => p.Key).ToList(), elo));
            var found = positions.Where(p => stats.ContainsKey(p.Key)).ToDictionary(p => p.Key, p => stats[p.Key]);
            var open = positions.Where(p => pending?.Contains(p.Key) == true).Select(p => p.Key).ToHashSet();
            return Task.FromResult(new TrainingExplorerResult(found, open, TrainingExplorer.Band(elo)));
        }
    }

    private static async Task<(AppDbContext Db, TrainingLinesService Svc, int Rep)> ServiceAsync(ITrainingExplorer explorer, string pgn)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.AppUsers.Add(new AppUser { Id = 1, Username = "u1", PasswordHash = "x" });
        var rep = new Repertoire { UserId = 1, Name = "Weiß", UseForExtension = true };
        db.Repertoires.Add(rep);
        await db.SaveChangesAsync();
        db.RepertoireFiles.Add(new RepertoireFile { RepertoireId = rep.Id, FileName = "a.pgn", PgnContent = pgn, FileSize = pgn.Length });
        await db.SaveChangesAsync();
        var notifications = new NotificationService(db);
        var reps = new RepertoireService(db, new RepertoireAnalyzeService(db, new MemoryCache(new MemoryCacheOptions())),
            new FriendService(db, notifications), notifications);
        return (db, new TrainingLinesService(db, reps, new ConfigurationBuilder().Build(), explorer), rep.Id);
    }

    [Fact]
    public async Task Service_AsksOnlyForTheGaps_WithTheOpponentsElo_AndSaysWhatIsEstimated()
    {
        var explorer = new FakeExplorer(new() { [KeyAfter("e4", "c6", "d4")] = Stats(("d5", 90), ("g6", 10)) },
            pending: [KeyAfter("e4", "c6", "d4", "d5", "e5")]);
        var (db, svc, rep) = await ServiceAsync(explorer,
            "[Event \"x\"]\n[Black \"Gegen Caro\"]\n\n1. e4 c6 2. d4 d5 3. e5 Bf5 4. Nf3 *\n\n[Event \"x\"]\n[Black \"Gegen Caro\"]\n\n1. e4 c6 2. d4 d5 3. e5 *\n");
        using var _ = db;
        // Repertoire Weiß; Gegner (Schwarz) hat 6× 1...c6 gespielt, aber danach nie 2.d4 gesehen
        var games = () => Task.FromResult(Times(6, "e4 c6 Nc3 d5", false).ToList());

        var r = (await svc.LinesAsync(1, new(rep, "w", TrainingLinesService.ChapterOverrides.None, null), games, default, () => Task.FromResult<int?>(1900)))!;

        var call = Assert.Single(explorer.Calls);
        Assert.Equal(1900, call.Elo);
        Assert.DoesNotContain(KeyAfter("e4"), call.Keys);            // dort reichen seine 6 Partien
        Assert.Contains(KeyAfter("e4", "c6", "d4"), call.Keys);
        Assert.Equal("2000–2300", r["lichessBand"]!.GetValue<string>());
        Assert.True(r["explorerIncomplete"]!.GetValue<bool>());
        Assert.Equal(6, r["ownGames"]!.GetValue<int>());
        var lines = r["lines"]!.AsArray();
        var shortLine = lines.Single(l => l!["moves"]!.AsArray().Count == 5)!;
        Assert.Equal("mixed", shortLine["source"]!.GetValue<string>());
        Assert.Equal(1, shortLine["ownMoves"]!.GetValue<int>());
        Assert.Equal(1, shortLine["lichessMoves"]!.GetValue<int>());
        Assert.Equal(3, shortLine["lichessFrom"]!.GetValue<int>());  // ab 2...d5
        Assert.Equal(0.9, shortLine["probability"]!.GetValue<double>(), 6);
        var longLine = lines.Single(l => l!["moves"]!.AsArray().Count == 7)!;
        Assert.True(longLine["pending"]!.GetValue<bool>());
        Assert.Equal("none", longLine["source"]!.GetValue<string>());
    }

    [Fact]
    public async Task Service_WithoutElo_Uses1800()
    {
        var explorer = new FakeExplorer(new());
        var (db, svc, rep) = await ServiceAsync(explorer, "[Event \"x\"]\n[Black \"K\"]\n\n1. e4 c5 2. Nf3 *\n");
        using var _ = db;
        await svc.LinesAsync(1, new(rep, "w", TrainingLinesService.ChapterOverrides.None, null),
            () => Task.FromResult(new List<OpponentTrainingLines.Game>()), default);
        Assert.Equal(1800, Assert.Single(explorer.Calls).Elo);
    }
}
