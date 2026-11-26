using Chess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Chessable;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Prep;
using RookHub.Api.Services.Repertoires;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Tests.Prep;

/// <summary>
/// Vorbereitung gegen „Brunner, Roman" (Prod-Repertoire 331, 2026-11-26): seine einzige Partie im Zeitraum ist 1.c4 e6 2.Sc3 b6 —
/// „alle partien von ihm sind c4 – warum sind da e4 lines dabei?". Linien, die seinem ERSTEN Zug widersprechen, fallen weg; Linien,
/// die er über eine Zugumstellung erreichen kann (1.c4 e6 2.Sc3 d5 3.d4 = Damengambit aus den 1.d4-Kapiteln), beginnen an der
/// gemeinsamen Stellung. Explorer immer gemockt.
/// </summary>
public class OpponentTranspositionsTests
{
    // Repertoire mit Schwarz wie Martinovićs Französisch + Schandorff gegen 1.d4/1.c4
    private static readonly string[] Lines =
    [
        "1. e4 e6 2. d4 d5 3. Nc3 Bb4 4. e5 c5",            // 0: Französisch — er spielt nie 1.e4
        "1. d4 d5 2. c4 e6 3. Nc3 Nf6 4. Bg5 Be7",          // 1: Damengambit 3.Sc3 — über 1.c4 e6 2.Sc3 d5 3.d4
        "1. d4 d5 2. c4 e6 3. Nf3 Nf6 4. g3 Be7",           // 2: Katalanisch mit 3.Sf3 — von 2.Sc3 aus nicht erreichbar
        "1. c4 e6 2. g3 d5 3. Bg2 Nf6",                     // 3: 1.c4 e6 2.g3 — folgt ihm einen Zug, weicht dann ab
        "1. d4 Nf6 2. c4 e6 3. Nf3 b6 4. g3 Ba6",           // 4: Damenindisch — nicht in zwei Halbzügen von 2.Sc3 aus
    ];

    private static readonly OpponentTrainingLines.Game Brunner = new("c4 e6 Nc3 b6 e4 Bb7".Split(' '), true, 2025);

    private static RepertoireReach.Graph Graph() =>
        RepertoireReach.Build(Lines.Select(l => $"[Event \"x\"]\n[Black \"K\"]\n\n{l} *\n").SelectMany(PgnMoveTree.ParseSections), 'b');

    private static string KeyAfter(params string[] sans)
    {
        var board = new ChessBoard();
        foreach (var san in sans) Assert.True(board.Move(san), san);
        return RepertoireReach.Key(board.ToFen());
    }

    private static string FenAfter(params string[] sans)
    {
        var board = new ChessBoard();
        foreach (var san in sans) Assert.True(board.Move(san), san);
        return board.ToFen();
    }

    [Fact]
    public void Brunner_OnlyC4Lines_NoFrench_QgdOverTransposition()
    {
        var g = Graph();
        var r = OpponentTrainingLines.Rank(g, Enumerable.Repeat("K", Lines.Length).ToList(), [Brunner]);

        Assert.DoesNotContain(r.Lines, l => l.Index == 0);                    // keine 1.e4-Linie
        Assert.Equal(3, r.ExcludedFirstMove);                                  // Französisch, Katalanisch, Damenindisch
        Assert.Equal(new[] { 1, 3 }, r.Lines.Select(l => l.Index).Order());

        var qgd = r.Lines.Single(l => l.Index == 1);
        Assert.Equal(FenAfter("c4", "e6", "Nc3"), qgd.StartFen);               // seine Stellung nach 2.Sc3
        Assert.Equal(new[] { "d5", "d4", "Nf6", "Bg5", "Be7" }, qgd.Sans);     // 2…d5 3.d4 = Damengambit, dann wie im Kapitel
        Assert.Equal(2, qgd.BridgeMoves);
        Assert.Equal(5, qgd.TransposedFrom);                                    // ab der Stellung nach 3.Sc3 der Ursprungslinie
        Assert.Equal(ChessableTrainedLineService.LineKeyFromSans(["d4", "d5", "c4", "e6", "Nc3", "Nf6", "Bg5", "Be7"]), qgd.Key);
        Assert.False(qgd.NeverReached);

        var g3 = r.Lines.Single(l => l.Index == 3);                             // folgt ihm einen Zug, weicht dann ab: bleibt
        Assert.Null(g3.TransposedFrom);
        Assert.Equal(1, g3.Matched);
    }

    [Fact]
    public void Brunner_WithExplorer_HisFirstMoveWins_ExplorerCannotBringBackE4_QgdRanksFirst()
    {
        var g = Graph();
        var a = OpponentTrainingLines.Analyze(g, [Brunner]);
        // der Explorer kennt 1.e4 als häufigsten Zug — zählt nicht, er hat im Zeitraum 1.c4 gespielt
        var explorer = new Dictionary<string, ExplorerPositionStats>
        {
            [RepertoireReach.StandardStartKey] = Stats(("e4", 60), ("d4", 30), ("c4", 10)),
            [KeyAfter("c4", "e6", "Nc3", "d5")] = Stats(("d4", 50), ("cxd5", 30), ("e4", 20)),
            [KeyAfter("d4", "d5", "c4", "e6", "Nc3", "Nf6")] = Stats(("Bg5", 60), ("cxd5", 40)),
            [KeyAfter("c4", "e6", "g3", "d5")] = Stats(("Bg2", 100)),
        };
        var need = OpponentTrainingLines.NeedsExplorer(g, a, 1);
        Assert.Contains(need, n => n.Key == KeyAfter("c4", "e6", "Nc3", "d5"));  // die Brückenstellung wird geschätzt
        Assert.DoesNotContain(need, n => n.Key == KeyAfter("e4", "e6", "d4", "d5")); // Französisch fragt niemand mehr an

        var r = OpponentTrainingLines.Rank(g, Enumerable.Repeat("K", Lines.Length).ToList(), a,
            new OpponentTrainingLines.Estimate(n => explorer.GetValueOrDefault(n.Key), 1, new HashSet<string>()));

        Assert.Equal(new[] { 1, 3 }, r.Lines.Select(l => l.Index));
        var qgd = r.Lines[0];
        Assert.Equal("lichess", qgd.Source);                                    // 3.d4 und 4.Lg5 geschätzt
        Assert.Equal(0.5 * 0.6, qgd.Probability, 6);
        Assert.Equal("deviates", r.Lines[1].Source);                             // 2.g3 spielt er nicht (2.Sc3)
    }

    [Fact]
    public void NoGamesInTheWindow_EverythingAsBefore()
    {
        var g = Graph();
        var r = OpponentTrainingLines.Rank(g, Enumerable.Repeat("K", Lines.Length).ToList(), []);
        Assert.Equal(Lines.Length, r.Lines.Count);
        Assert.Equal(0, r.ExcludedFirstMove);
        Assert.All(r.Lines, l => Assert.Null(l.TransposedFrom));
    }

    [Fact]
    public void TwoMoveOrdersIntoTheSamePosition_SameContinuation_OneLine()
    {
        // 1.d4 d5 2.c4 e6 3.Sc3 Sf6 4.Lg5 und 1.d4 e6 2.c4 d5 3.Sc3 Sf6 4.Lg5: dieselbe Fortsetzung ab der gemeinsamen Stellung
        // (die 2.g3-Linie sorgt dafür, dass er das Repertoire erst mit 2.Sc3 verlässt — beide erreichen von dort dieselbe Stellung)
        var g = RepertoireReach.Build(new[] { "1. d4 d5 2. c4 e6 3. Nc3 Nf6 4. Bg5 Be7", "1. d4 e6 2. c4 d5 3. Nc3 Nf6 4. Bg5 Be7",
                "1. c4 e6 2. g3 d5" }
            .Select(l => $"[Event \"x\"]\n[Black \"K\"]\n\n{l} *\n").SelectMany(PgnMoveTree.ParseSections), 'b');
        var r = OpponentTrainingLines.Rank(g, ["K", "K", "K"], [Brunner]);
        var line = Assert.Single(r.Lines, l => l.TransposedFrom is not null);
        Assert.Equal(2, r.Lines.Count);
        Assert.Equal(new[] { "d5", "d4", "Nf6", "Bg5", "Be7" }, line.Sans);
    }

    [Fact]
    public void ManyGames_TranspositionSearch_StaysFast()
    {
        // 600 Partien mit vielen verschiedenen Ausstiegen aus dem Repertoire — die Brückensuche darf die Reihung nicht ausbremsen
        var g = Graph();
        var seconds = new[] { "Nc3", "Nf3", "e3", "b3", "d3", "a3", "h3", "Qc2", "e4", "Qb3" };
        var replies = new[] { "b6", "c5", "Nf6", "f5", "Be7", "a6" };
        var games = Enumerable.Range(0, 600).Select(i =>
            new OpponentTrainingLines.Game($"c4 e6 {seconds[i % seconds.Length]} {replies[i / seconds.Length % replies.Length]}".Split(' '), true, 2025)).ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var a = OpponentTrainingLines.Analyze(g, games);
        var r = OpponentTrainingLines.Rank(g, Enumerable.Repeat("K", Lines.Length).ToList(), a, null);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"{sw.ElapsedMilliseconds} ms");
        Assert.Contains(r.Lines, l => l.Index == 1 && l.TransposedFrom is not null);
    }

    [Fact]
    public async Task CreateRepertoire_Brunner_OnlyC4AndTransposedQgd_SectionStartsAtHisPosition()
    {
        using var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        ctx.AppUsers.Add(new AppUser { Id = 1, Username = "u1", PasswordHash = "x" });
        var pgn = string.Concat(Lines.Select((l, i) =>
            $"[Event \"x\"]\n[White \"?\"]\n[Black \"Kapitel {i}\"]\n[ChessableOid \"{100 + i}\"]\n\n{l} {{Kommentar}} *\n\n"));
        var rep = new Repertoire { UserId = 1, Name = "Schwarz", UseForExtension = true };
        ctx.Repertoires.Add(rep);
        await ctx.SaveChangesAsync();
        ctx.RepertoireFiles.Add(new RepertoireFile { RepertoireId = rep.Id, FileName = "a.pgn", PgnContent = pgn, FileSize = pgn.Length });
        await ctx.SaveChangesAsync();
        var notifications = new NotificationService(ctx);
        var reps = new RepertoireService(ctx, new RepertoireAnalyzeService(ctx, new MemoryCache(new MemoryCacheOptions())),
            new FriendService(ctx, notifications), notifications);
        var svc = new TrainingLinesService(ctx, reps, new ConfigurationBuilder().Build());
        var q = new TrainingLinesService.Query(null, "b", TrainingLinesService.ChapterOverrides.None, null);
        Task<List<OpponentTrainingLines.Game>> games() => Task.FromResult(new List<OpponentTrainingLines.Game> { Brunner });

        var created = (await svc.CreateRepertoireAsync(1, "Brunner, Roman", q, games, default))!;
        Assert.Equal(2, created.Lines);
        var saved = ctx.Repertoires.AsNoTracking().Single(r => r.Id == created.Id);
        Assert.Contains("1 Partien gezählt, 2 von 2 Linien übernommen. 3 Linien ausgelassen", saved.Description);
        var file = ctx.RepertoireFiles.AsNoTracking().Single(f => f.RepertoireId == created.Id).PgnContent;
        Assert.DoesNotContain("e4 e6", file);                                    // kein Französisch
        Assert.Contains($"[FEN \"{FenAfter("c4", "e6", "Nc3")}\"]", file);
        Assert.Contains("[SetUp \"1\"]", file);
        Assert.Contains("2... d5 3. d4 Nf6 4. Bg5 Be7", file);
        Assert.Contains("[Black \"Kapitel 1\"]", file);
        Assert.DoesNotContain("[ChessableOid \"101\"]", file);                   // keine Chessable-Linie mehr
        Assert.Contains("1. c4 e6 2. g3 d5", file);                               // die 2.g3-Linie unverändert (Kommentar bleibt)

        // Der Trainer liest das „Prep: …" wie jedes Repertoire: zwei Linien, die erste ab seiner Stellung
        var copied = RepertoireReach.Build(TrainingLinesService.Sections(file), 'b');
        Assert.Equal(2, copied.Mainlines.Count);

        // Liste: dieselben zwei, die Zugumstellung markiert
        var list = (await svc.LinesAsync(1, q, games, default))!;
        Assert.Equal(3, list["excludedFirstMove"]!.GetValue<int>());
        var lines = list["lines"]!.AsArray();
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l!["transposedFrom"]?.GetValue<int>() == 5 && l["bridgeMoves"]!.GetValue<int>() == 2);
    }

    [Fact]
    public async Task CreateRepertoire_AllLinesContradictHisFirstMove_ClearMessage()
    {
        using var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        ctx.AppUsers.Add(new AppUser { Id = 1, Username = "u1", PasswordHash = "x" });
        const string pgn = "[Event \"x\"]\n[Black \"K\"]\n\n1. e4 e6 2. d4 d5 *\n";
        var rep = new Repertoire { UserId = 1, Name = "Schwarz", UseForExtension = true };
        ctx.Repertoires.Add(rep);
        await ctx.SaveChangesAsync();
        ctx.RepertoireFiles.Add(new RepertoireFile { RepertoireId = rep.Id, FileName = "a.pgn", PgnContent = pgn, FileSize = pgn.Length });
        await ctx.SaveChangesAsync();
        var notifications = new NotificationService(ctx);
        var reps = new RepertoireService(ctx, new RepertoireAnalyzeService(ctx, new MemoryCache(new MemoryCacheOptions())),
            new FriendService(ctx, notifications), notifications);
        var svc = new TrainingLinesService(ctx, reps, new ConfigurationBuilder().Build());
        var q = new TrainingLinesService.Query(null, "b", TrainingLinesService.ChapterOverrides.None, null);

        var e = await Assert.ThrowsAnyAsync<Exception>(() => svc.CreateRepertoireAsync(1, "Brunner, Roman", q,
            () => Task.FromResult(new List<OpponentTrainingLines.Game> { Brunner }), default));
        Assert.Contains("passt zu seinen Partien", e.Message);
    }

    private static ExplorerPositionStats Stats(params (string San, long Games)[] moves) =>
        new(moves.Sum(m => m.Games), moves.Select(m => new ExplorerMoveStat("", m.San, m.Games, null, null)).ToList());
}
