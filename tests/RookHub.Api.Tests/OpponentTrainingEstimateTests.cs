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

    [Fact]
    public void DefaultThreshold_IsOne_HisMovesHavePriority()
    {
        Assert.Equal(1, TrainingLinesService.DefaultMinOwn);
        var g = Graph('w', "1. e4 c5 2. Nf3");
        var explorer = new Dictionary<string, ExplorerPositionStats> { [KeyAfter("e4")] = Stats(("e5", 70), ("c5", 30)) };
        var line = Assert.Single(Rank(g, Times(1, "e4 c5 Nf3", false), explorer, minOwn: 1).Lines);
        Assert.Equal("own", line.Source);                          // EINE Partie reicht: seine Züge, nicht der Explorer
        Assert.Equal(1.0, line.Probability, 6);
    }

    [Fact]
    public void UsersExample_OneGame_DeviatingLineGoesBehind_LineHeFollowsIsEstimatedWhereHisGameEnds()
    {
        // „wenn es nur eine partie gibt und die nicht so tief geht": er hat einmal 1.e4 c5 2.Sf3 gespielt (als Schwarz)
        var g = Graph('w', "1. e4 e5 2. Nf3 Nc6 3. Bb5", "1. e4 c5 2. Nf3 d6 3. d4");
        var explorer = new Dictionary<string, ExplorerPositionStats>
        {
            [KeyAfter("e4")] = Stats(("e5", 45), ("c5", 35), ("e6", 20)),
            [KeyAfter("e4", "e5", "Nf3")] = Stats(("Nc6", 80), ("d6", 20)),
            [KeyAfter("e4", "c5", "Nf3")] = Stats(("d6", 40), ("Nc6", 35), ("e6", 25)),
        };

        var r = OpponentTrainingLines.Rank(g, ["K", "K"], OpponentTrainingLines.Analyze(g, Times(1, "e4 c5 Nf3", false)),
            new OpponentTrainingLines.Estimate(n => explorer.GetValueOrDefault(n.Key), 1, new HashSet<string>()));

        var najdorf = r.Lines[0];                                  // folgt seiner Partie: Hauptreihung
        Assert.Equal("c5", najdorf.Sans[1]);
        Assert.Equal("mixed", najdorf.Source);
        Assert.Equal(3, najdorf.LichessFrom);                      // ab 2...d6 geschätzt
        Assert.Equal(1.0 * 0.4, najdorf.Probability, 6);
        var open = r.Lines[1];                                     // widerspricht ihm (er spielt 1...c5): eigene Stufe dahinter
        Assert.Equal("deviates", open.Source);
        Assert.Equal(1, open.DeviationPly);
        Assert.Equal("c5", open.DeviationSan);
        Assert.Equal(1, open.DeviationGames);
        Assert.Equal(0.45 * 0.8, open.Probability, 6);              // Schätzung ab dem Widerspruch
    }

    [Fact]
    public void Tiers_NotContradicting_ThenContradicting_ByMatchedMovesThenEstimate_ThenNoSource()
    {
        var g = Graph('w',
            "1. c4 e5 2. Nc3",                       // 0: keine Quelle (nach 1.c4 war er nie, der Explorer gibt nichts)
            "1. d4 d5 2. c4",                        // 1: nach 1.d4 war er nie — reine Explorer-Linie, widerspricht ihm nicht
            "1. e4 e5 2. Nf3",                       // 2: widerspricht bei 1...e5 (0 übereinstimmende Züge)
            "1. e4 c5 2. Nf3 Nc6 3. d4",             // 3: widerspricht bei 2...Sc6 (1 übereinstimmender Zug)
            "1. e4 c5 2. Nf3 d6 3. d4");             // 4: folgt ihm — Hauptreihung
        var explorer = new Dictionary<string, ExplorerPositionStats>
        {
            [KeyAfter("e4")] = Stats(("c5", 50), ("e5", 50)),
            [KeyAfter("d4")] = Stats(("d5", 60), ("Nf6", 40)),
            [KeyAfter("e4", "c5", "Nf3")] = Stats(("d6", 50), ("Nc6", 50)),
        };
        var r = Rank(g, Times(2, "e4 c5 Nf3 d6 d4", false), explorer, minOwn: 1);
        Assert.Equal(new[] { 4, 1, 3, 2, 0 }, r.Lines.Select(l => l.Index));
        Assert.Equal(new[] { "own", "lichess", "deviates", "deviates", "none" }, r.Lines.Select(l => l.Source));
        Assert.Equal(1, r.Lines[2].OwnMoves);                      // 1...c5 stimmte noch
        Assert.Equal("d6", r.Lines[2].DeviationSan);
        Assert.Equal(2, r.Lines[2].DeviationGames);
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
        // nach 1.e4 und 2.Sf3 hat er 5× weitergespielt (eigene Daten) — zu schätzen ist nur, wo er der Linie widerspricht:
        // nach 2.Sf3 spielt er nie 2...Sc6 (Linie 2), dort springt der Explorer ein
        Assert.Equal(new[] { KeyAfter("e4", "c5", "Nf3") }, need);
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

    private sealed class FakeExplorer(Dictionary<string, ExplorerPositionStats> stats, HashSet<string>? pending = null, bool available = true,
        Action? onCall = null) : ITrainingExplorer
    {
        public bool Available => available;
        public List<(List<string> Keys, int Elo)> Calls { get; } = new();
        public List<TimeSpan> Budgets { get; } = new();
        public List<int> MaxNews { get; } = new();
        public Task<TrainingExplorerResult> StatsAsync(int userId, IReadOnlyList<RepertoireReach.Node> positions, int elo, TimeSpan budget,
            int maxNew, CancellationToken ct, int? parallelism = null)
        {
            Budgets.Add(budget);
            MaxNews.Add(maxNew);
            onCall?.Invoke();
            Calls.Add((positions.Select(p => p.Key).ToList(), elo));
            var found = positions.Where(p => stats.ContainsKey(p.Key)).ToDictionary(p => p.Key, p => stats[p.Key]);
            // wie BatchStatsAsync: ohne Frist nichts Neues, sonst höchstens maxNew der fehlenden — der Rest bleibt offen
            var missing = positions.Where(p => !stats.ContainsKey(p.Key)).ToList();
            var tried = budget <= TimeSpan.Zero ? 0 : Math.Max(0, maxNew);
            var open = positions.Where(p => pending?.Contains(p.Key) == true).Select(p => p.Key)
                .Concat(missing.Skip(tried).Select(p => p.Key)).ToHashSet();
            return Task.FromResult(new TrainingExplorerResult(found, open, TrainingExplorer.Band(elo)));
        }
    }

    private static async Task<(AppDbContext Db, TrainingLinesService Svc, int Rep)> ServiceAsync(ITrainingExplorer explorer, string pgn,
        IMemoryCache? cache = null, Dictionary<string, string?>? config = null, TrainingLinesContinuation? continuation = null)
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
        return (db, new TrainingLinesService(db, reps, new ConfigurationBuilder().AddInMemoryCollection(config ?? new()).Build(), explorer, cache,
            null, continuation), rep.Id);
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

    [Fact]
    public async Task Service_WithoutLocalExplorer_NoQuery_NoEstimate_FillRule_HisMovesStillFirst()
    {
        var explorer = new FakeExplorer(new() { [KeyAfter("e4", "c5", "Nf3")] = Stats(("d6", 100)) }, available: false);
        var (db, svc, rep) = await ServiceAsync(explorer,
            "[Event \"x\"]\n[Black \"K\"]\n\n1. e4 c5 2. Nf3 d6 3. d4 *\n\n[Event \"x\"]\n[Black \"K\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 *\n");
        using var _ = db;
        var eloAsked = false;
        var r = (await svc.LinesAsync(1, new(rep, "w", TrainingLinesService.ChapterOverrides.None, null),
            () => Task.FromResult(Times(1, "e4 c5 Nf3", false).ToList()), default,
            () => { eloAsked = true; return Task.FromResult<int?>(2000); }))!;

        Assert.Empty(explorer.Calls);                               // keine Abfrage
        Assert.False(eloAsked);
        Assert.Null(r["lichessBand"]);
        Assert.False(r["explorerIncomplete"]!.GetValue<bool>());
        var lines = r["lines"]!.AsArray();
        Assert.All(lines, l => Assert.False(l!["pending"]!.GetValue<bool>()));
        Assert.DoesNotContain(lines, l => l!["source"]!.GetValue<string>() is "lichess" or "mixed");
        // seine Partie geht bis 2.Sf3: die Najdorf-Linie hat danach keine Quelle (Auffüllregel), die 1...e5-Linie widerspricht ihm
        Assert.Equal("none", lines.Single(l => l!["moves"]![1]!.GetValue<string>() == "c5")!["source"]!.GetValue<string>());
        Assert.Equal("deviates", lines.Single(l => l!["moves"]![1]!.GetValue<string>() == "e5")!["source"]!.GetValue<string>());
    }

    [Fact]
    public async Task Cache_SecondCallComputesNothing_ChangedRepertoireRecomputes_OtherScopeToo()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var explorer = new FakeExplorer(new() { [KeyAfter("e4", "c5", "Nf3")] = Stats(("d6", 100)) });
        var (db, svc, rep) = await ServiceAsync(explorer, "[Event \"x\"]\n[Black \"K\"]\n\n1. e4 c5 2. Nf3 d6 3. d4 *\n", memory);
        using var _ = db;
        var loads = 0;
        Task<List<OpponentTrainingLines.Game>> Games() { loads++; return Task.FromResult(Times(1, "e4 c5 Nf3", false).ToList()); }
        var q = new TrainingLinesService.Query(rep, "w", TrainingLinesService.ChapterOverrides.None, null);

        var first = (await svc.LinesAsync(1, q, Games, default, scope: "prep:42"))!;
        var second = (await svc.LinesAsync(1, q, Games, default, scope: "prep:42"))!;
        Assert.Equal(1, loads);                                    // zweiter Aufruf ohne Rechnung
        Assert.Single(explorer.Calls);
        Assert.Equal(first.ToJsonString(), second.ToJsonString());

        await svc.LinesAsync(1, q, Games, default, scope: "prep:43");   // anderer Gegner: eigene Rechnung
        Assert.Equal(2, loads);

        // Repertoire geändert (UpdatedAt): sofort neu
        var r = db.Repertoires.Single(x => x.Id == rep);
        r.UpdatedAt = r.UpdatedAt.AddSeconds(1);
        await db.SaveChangesAsync();
        await svc.LinesAsync(1, q, Games, default, scope: "prep:42");
        Assert.Equal(3, loads);

        // ohne scope (z. B. Tests) wird nichts gehalten
        await svc.LinesAsync(1, q, Games, default);
        await svc.LinesAsync(1, q, Games, default);
        Assert.Equal(5, loads);
    }

    // ── Hotfix 2026-10-08: Frist, Deckel, Reihenfolge, kein Halten unvollständiger Ergebnisse ─────────────────────────

    private const string ThreeLines = "[Event \"x\"]\n[Black \"K\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 *\n\n"
        + "[Event \"x\"]\n[Black \"K\"]\n\n1. e4 c5 2. Nf3 d6 3. d4 *\n\n"
        + "[Event \"x\"]\n[Black \"K\"]\n\n1. e4 c6 2. d4 d5 3. e5 *\n";

    private static TrainingLinesService.Query Qw(int rep) => new(rep, "w", TrainingLinesService.ChapterOverrides.None, null);

    [Fact]
    public async Task Priority_GapsOfTheLinesHeFollows_First_ThenTheDeviating()
    {
        var explorer = new FakeExplorer(new());
        var (db, svc, rep) = await ServiceAsync(explorer, ThreeLines);
        using var _ = db;
        // er hat 3× 1...c5 2.Sf3 gespielt (Partie endet dort): die Najdorf-Linie folgt ihm, die beiden anderen weichen ab
        await svc.LinesAsync(1, Qw(rep), () => Task.FromResult(Times(3, "e4 c5 Nf3", false).ToList()), default);
        var keys = Assert.Single(explorer.Calls).Keys;
        // die Lücke der Linie, der er folgt, vor den Stellungen, die nur die abweichenden Linien brauchen
        var najdorf = keys.IndexOf(KeyAfter("e4", "c5", "Nf3"));
        Assert.True(najdorf >= 0);
        Assert.True(najdorf < keys.IndexOf(KeyAfter("e4", "e5", "Nf3")), string.Join(" | ", keys));
        Assert.True(najdorf < keys.IndexOf(KeyAfter("e4", "c6", "d4")), string.Join(" | ", keys));
    }

    [Fact]
    public async Task Cap_OnlyTheFirstNPositions_RestPending_AndIncomplete()
    {
        var explorer = new FakeExplorer(new());
        var (db, svc, rep) = await ServiceAsync(explorer, ThreeLines,
            config: new() { [TrainingLinesService.MaxPositionsKey] = "1" });
        using var _ = db;
        var r = (await svc.LinesAsync(1, Qw(rep), () => Task.FromResult(new List<OpponentTrainingLines.Game>()), default))!;
        // ALLE Stellungen gehen mit (gespeicherte kosten nichts), der Deckel gilt nur für neue
        Assert.True(Assert.Single(explorer.Calls).Keys.Count >= 3);
        Assert.Equal(1, Assert.Single(explorer.MaxNews));
        Assert.True(r["explorerIncomplete"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Deadline_UsedUp_NoQuery_HonestPartialAnswer_NotCached()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var explorer = new FakeExplorer(new());
        var (db, svc, rep) = await ServiceAsync(explorer, ThreeLines, memory,
            new() { [TrainingLinesService.ExplorerBudgetKey] = "0" });
        using var _ = db;
        var loads = 0;
        Task<List<OpponentTrainingLines.Game>> Games() { loads++; return Task.FromResult(new List<OpponentTrainingLines.Game>()); }
        var r = (await svc.LinesAsync(1, Qw(rep), Games, default, scope: "league:1"))!;
        Assert.Equal(TimeSpan.Zero, Assert.Single(explorer.Budgets)); // keine Zeit: nur der Speicher, nichts Neues
        Assert.True(r["explorerIncomplete"]!.GetValue<bool>());
        await svc.LinesAsync(1, Qw(rep), Games, default, scope: "league:1");
        Assert.Equal(2, loads);                                       // unvollständig wird NICHT gehalten
    }

    [Fact]
    public async Task ExplorerBudget_IsTheSmallerOfConfigAndWhatTheDeadlineLeaves()
    {
        var explorer = new FakeExplorer(new());
        var (db, svc, rep) = await ServiceAsync(explorer, ThreeLines);
        using var _ = db;
        await svc.LinesAsync(1, Qw(rep), () => Task.FromResult(new List<OpponentTrainingLines.Game>()), default);
        var budget = Assert.Single(explorer.Budgets);
        Assert.True(budget <= TimeSpan.FromSeconds(TrainingLinesService.DefaultExplorerSeconds), budget.ToString());
        Assert.True(budget > TimeSpan.FromSeconds(5), budget.ToString());
    }

    [Fact]
    public async Task AbortedRequest_StillAnswers_NoException()
    {
        // nginx kappt mitten in der Schätzung → Kestrel bricht die Anfrage ab: danach darf nichts mehr werfen
        using var aborted = new CancellationTokenSource();
        var explorer = new FakeExplorer(new() { [KeyAfter("e4")] = Stats(("e5", 50), ("c5", 50)) }, onCall: aborted.Cancel);
        var (db, svc, rep) = await ServiceAsync(explorer, ThreeLines);
        using var _ = db;
        var r = await svc.LinesAsync(1, Qw(rep), () => Task.FromResult(new List<OpponentTrainingLines.Game>()), aborted.Token);
        Assert.True(aborted.IsCancellationRequested);
        Assert.NotNull(r);
        Assert.NotEmpty(r!["lines"]!.AsArray());
    }

    // ── Fortsetzung im Hintergrund (0.725.2) ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Explorer, der im Vordergrund nichts schafft (alles offen) und im Hintergrund alles liefert — bei Bedarf erst nach
    /// <see cref="Gate"/>.</summary>
    private sealed class SlowExplorer(Dictionary<string, ExplorerPositionStats> stats) : ITrainingExplorer
    {
        public bool Available => true;
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Blocking { get; set; }
        public bool Hang { get; set; }
        public int BackgroundCalls;
        public readonly List<(int MaxNew, TimeSpan Budget, int? Parallelism)> Calls = new();
        private readonly HashSet<string> _stored = new();

        public async Task<TrainingExplorerResult> StatsAsync(int userId, IReadOnlyList<RepertoireReach.Node> positions, int elo, TimeSpan budget,
            int maxNew, CancellationToken ct, int? parallelism = null)
        {
            lock (Calls) Calls.Add((maxNew, budget, parallelism));
            if (parallelism is not null)                           // die Fortsetzung
            {
                Interlocked.Increment(ref BackgroundCalls);
                if (Hang) await Task.Delay(Timeout.Infinite, ct);
                if (Blocking) await Gate.Task;
                lock (_stored) foreach (var p in positions) _stored.Add(p.Key);
            }
            lock (_stored)
            {
                var found = positions.Where(p => _stored.Contains(p.Key) && stats.ContainsKey(p.Key)).ToDictionary(p => p.Key, p => stats[p.Key]);
                var open = positions.Where(p => !_stored.Contains(p.Key)).Select(p => p.Key).ToHashSet();
                return new TrainingExplorerResult(found, open, TrainingExplorer.Band(elo));
            }
        }
    }

    private static TrainingLinesContinuation Continuation(ITrainingExplorer explorer, IMemoryCache memory)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, explorer);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, memory);
        var sp = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var scopes = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<
            Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(sp);
        return new TrainingLinesContinuation(scopes, Microsoft.Extensions.Logging.Abstractions.NullLogger<TrainingLinesContinuation>.Instance);
    }

    private static async Task WaitUntil(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) await Task.Delay(20);
        Assert.True(done());
    }

    private static Dictionary<string, ExplorerPositionStats> AllStats() => new()
    {
        [KeyAfter("e4")] = Stats(("e5", 40), ("c5", 40), ("c6", 20)),
        [KeyAfter("e4", "e5", "Nf3")] = Stats(("Nc6", 100)),
        [KeyAfter("e4", "c5", "Nf3")] = Stats(("d6", 100)),
        [KeyAfter("e4", "c6", "d4")] = Stats(("d5", 100)),
    };

    [Fact]
    public async Task Continuation_StartsOnce_FillsTheStore_PutsTheCompleteResultIntoTheCache()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var explorer = new SlowExplorer(AllStats());
        var continuation = Continuation(explorer, memory);
        var (db, svc, rep) = await ServiceAsync(explorer, ThreeLines, memory, continuation: continuation);
        using var _ = db;
        var loads = 0;
        Task<List<OpponentTrainingLines.Game>> Games() { loads++; return Task.FromResult(new List<OpponentTrainingLines.Game>()); }

        var first = (await svc.LinesAsync(1, Qw(rep), Games, default, scope: "league:1"))!;
        Assert.True(first["explorerIncomplete"]!.GetValue<bool>());
        Assert.True(first["explorerRunning"]!.GetValue<bool>());
        await WaitUntil(() => explorer.BackgroundCalls == 1 && continuation.RunningCount == 0);
        Assert.Equal(3, explorer.Calls.Count);                        // Vordergrund, Fortsetzung, Reihung aus dem Speicher
        Assert.Equal(TrainingLinesContinuation.Parallelism, explorer.Calls[1].Parallelism);
        Assert.Equal(int.MaxValue, explorer.Calls[1].MaxNew);

        // danach: vollständig aus dem Speicher (ohne Rechnung)
        var second = (await svc.LinesAsync(1, Qw(rep), Games, default, scope: "league:1"))!;
        Assert.Equal(1, loads);                                       // aus dem 15-min-Speicher
        Assert.False(second["explorerIncomplete"]!.GetValue<bool>());
        Assert.False(second["explorerRunning"]!.GetValue<bool>());
        Assert.Equal(1, explorer.BackgroundCalls);
    }

    [Fact]
    public async Task Continuation_SecondRequestDuringTheRun_StartsNoSecond_AndAsksNothingNew()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var explorer = new SlowExplorer(AllStats()) { Blocking = true };
        var continuation = Continuation(explorer, memory);
        var (db, svc, rep) = await ServiceAsync(explorer, ThreeLines, memory, continuation: continuation);
        using var _ = db;
        Task<List<OpponentTrainingLines.Game>> Games() => Task.FromResult(new List<OpponentTrainingLines.Game>());

        await svc.LinesAsync(1, Qw(rep), Games, default, scope: "league:1");
        await WaitUntil(() => explorer.BackgroundCalls == 1);
        var during = (await svc.LinesAsync(1, Qw(rep), Games, default, scope: "league:1"))!;
        Assert.True(during["explorerRunning"]!.GetValue<bool>());
        var last = explorer.Calls[^1];
        Assert.Equal((0, TimeSpan.Zero), (last.MaxNew, last.Budget));    // nur der Speicher, nichts doppelt
        Assert.Equal(1, explorer.BackgroundCalls);

        // ein anderer Gegner desselben Nutzers während des Laufs: keine zweite Fortsetzung
        var other = (await svc.LinesAsync(1, Qw(rep), Games, default, scope: "league:2"))!;
        Assert.False(other["explorerRunning"]!.GetValue<bool>());
        Assert.Equal(1, explorer.BackgroundCalls);

        explorer.Gate.SetResult();
        await WaitUntil(() => explorer.Calls.Count(c => c.Budget == TimeSpan.Zero && c.Parallelism is null && c.MaxNew == 0) >= 2);
    }

    [Fact]
    public async Task Continuation_TimeLimit_EndsTheRun_NothingCached()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var explorer = new SlowExplorer(AllStats()) { Hang = true };
        var continuation = Continuation(explorer, memory);
        continuation.Limit = TimeSpan.FromMilliseconds(100);
        var (db, svc, rep) = await ServiceAsync(explorer, ThreeLines, memory, continuation: continuation);
        using var _ = db;
        var loads = 0;
        Task<List<OpponentTrainingLines.Game>> Games() { loads++; return Task.FromResult(new List<OpponentTrainingLines.Game>()); }

        var first = (await svc.LinesAsync(1, Qw(rep), Games, default, scope: "league:1"))!;
        Assert.True(first["explorerRunning"]!.GetValue<bool>());
        await WaitUntil(() => explorer.BackgroundCalls == 1 && continuation.RunningCount == 0);
        var after = (await svc.LinesAsync(1, Qw(rep), Games, default, scope: "league:1"))!;
        Assert.Equal(2, loads);                                       // nichts gecacht
        Assert.True(after["explorerIncomplete"]!.GetValue<bool>());
        Assert.True(after["explorerRunning"]!.GetValue<bool>());      // der Lauf war zu Ende — eine neue Fortsetzung durfte starten
        await WaitUntil(() => explorer.BackgroundCalls == 2);
    }
}
