using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Authorization;
using RookHub.Api.Controllers.League;
using RookHub.Api.Controllers.Prep;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Prep;
using RookHub.Api.Services.Repertoires;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Tests.Prep;

/// <summary>
/// „Abweichungen trainieren" (Wunsch 2026-11-07): Stellungen des Repertoires, in denen der Gegner einen Zug spielt, den das Repertoire
/// nicht kennt — Zug nicht im Repertoire, Zugumstellung, Farbe, Häufigkeit/Reihung, Fortsetzungsbaum mit Verzweigung und Ende;
/// dazu Dienst (Quellen, Positionen, Cache), Trainingslinien mit <c>positions</c> und die Rechte der Endpunkte.
/// </summary>
public class OpponentDeviationsTests
{
    private static RepertoireReach.Graph Graph(char color, params string[] lines) =>
        RepertoireReach.Build(lines.Select(l => $"[Event \"x\"]\n[Black \"K\"]\n\n{l} *\n")
            .SelectMany(PgnMoveTree.ParseSections).Where(s => s.Moves.Count > 0), color);

    private static IEnumerable<OpponentTrainingLines.Game> Times(int n, string moves, bool opponentWhite, int? year = 2024) =>
        Enumerable.Range(0, n).Select(_ => new OpponentTrainingLines.Game(moves.Split(' ', StringSplitOptions.RemoveEmptyEntries), opponentWhite, year));

    private static string Key(string sans)
    {
        var b = new Chess.ChessBoard();
        foreach (var s in sans.Split(' ', StringSplitOptions.RemoveEmptyEntries)) b.Move(s);
        return RepertoireReach.Key(b.ToFen());
    }

    [Fact]
    public void MoveNotInRepertoire_IsADeviation_WithPathCountsAndRepertoireMoves()
    {
        // Nutzer Weiß: 1.e4 c5 2.Nf3 d6 — der Gegner (Schwarz) spielt 2…Nc6 (3×, zuletzt 2025) und 2…d6 (1×, im Repertoire)
        var g = Graph('w', "1. e4 c5 2. Nf3 d6");
        var games = Times(2, "e4 c5 Nf3 Nc6 d4", false, 2023).Concat(Times(1, "e4 c5 Nf3 Nc6", false, 2025))
            .Concat(Times(1, "e4 c5 Nf3 d6", false, 2019));

        var r = OpponentDeviations.Find(g, games);

        Assert.Equal(4, r.Games);
        var d = Assert.Single(r.Deviations);
        Assert.Equal(new[] { "e4", "c5", "Nf3" }, d.Path);
        Assert.Equal("Nc6", d.San);
        Assert.Equal(3, d.Games);
        Assert.Equal(4, d.Reached);
        Assert.Equal(2025, d.LastYear);
        Assert.Equal(new[] { "d6" }, d.Repertoire);
        Assert.Equal(Key("e4 c5 Nf3"), RepertoireReach.Key(d.Fen));
        Assert.Equal(1, r.Total);
        // Stellung vor der Abweichung: erreicht 4×, seine Züge dort
        var at = r.Positions[Key("e4 c5 Nf3")];
        Assert.Equal(4, at.Reached);
        Assert.Equal(3, at.Moves["Nc6"]);
        Assert.Equal(1, at.Moves["d6"]);
        Assert.Equal(4, r.Positions[RepertoireReach.StandardStartKey].Reached);
    }

    [Fact]
    public void UserLeavingTheRepertoire_IsNoDeviation_AndOnlyTheOtherColorCounts()
    {
        // Nutzer Weiß, 1.e4 e5 2.Nf3 — in seinen Partien spielte SEIN Gegner (meine Seite) 2.Bc4: das ist meine Abweichung, nicht seine
        var g = Graph('w', "1. e4 e5 2. Nf3 Nc6");
        var games = Times(3, "e4 e5 Bc4 Nf6", false)
            .Concat(Times(5, "d4 d5 c4", true));                       // er mit Weiß: zählt gegen ein Weiß-Repertoire nicht

        var r = OpponentDeviations.Find(g, games);

        Assert.Equal(3, r.Games);
        Assert.Empty(r.Deviations);
    }

    [Fact]
    public void Transposition_BackIntoTheRepertoire_IsNoDeviation_AndTheSamePositionOverTwoPathsCountsOnce()
    {
        // Nutzer Schwarz: 1.d4 Nf6 2.c4 e6 3.Nc3 Bb4. Gegner kommt über 1.c4 e6 2.Nc3 Nf6 3.d4 — jeder seiner Züge verlässt das Repertoire
        // zunächst (1.c4 kennt es nicht) → das ist EINE Abweichung an der Grundstellung; nach 3.d4 ist er wieder drin.
        var g = Graph('b', "1. d4 Nf6 2. c4 e6 3. Nc3 Bb4 4. Qc2 O-O");
        var games = Times(2, "c4 e6 Nc3 Nf6 d4 Bb4 a3", true)        // 4.a3: Abweichung NACH der Zugumstellung
            .Concat(Times(1, "d4 Nf6 c4 e6 Nc3 Bb4 a3", true));      // gleiche Stellung, gleicher Zug, anderer Weg

        var r = OpponentDeviations.Find(g, games);

        var c4 = r.Deviations.Single(d => d.San == "c4");
        Assert.Empty(c4.Path);
        Assert.Equal(2, c4.Games);
        var a3 = r.Deviations.Single(d => d.San == "a3");
        Assert.Equal(3, a3.Games);                                    // beide Wege zusammen
        Assert.Equal(3, a3.Reached);
        Assert.Equal(new[] { "c4", "e6", "Nc3", "Nf6", "d4", "Bb4" }, a3.Path);   // der häufigere Weg
        Assert.Equal(new[] { "Qc2" }, a3.Repertoire);
        Assert.Equal("a3", r.Deviations[0].San);                     // mehr Partien zuerst
        Assert.Equal(2, r.Deviations.Count);
    }

    [Fact]
    public void Ranking_GamesThenYearThenShorterPath_TakeAndTotal()
    {
        var g = Graph('w', "1. e4 e5 2. Nf3 Nc6 3. Bb5 a6");
        var games = Times(2, "e4 c5", false, 2020)                    // 2×, 2020
            .Concat(Times(2, "e4 e5 Nf3 d6", false, 2024))            // 2×, 2024 → vor c5
            .Concat(Times(5, "e4 e5 Nf3 Nc6 Bb5 Nf6", false, 2010));  // 5× → zuerst

        var r = OpponentDeviations.Find(g, games);
        Assert.Equal(new[] { "Nf6", "d6", "c5" }, r.Deviations.Select(d => d.San));

        var two = OpponentDeviations.Find(g, games, take: 2);
        Assert.Equal(2, two.Deviations.Count);
        Assert.Equal(3, two.Total);
        Assert.False(two.Positions.ContainsKey(Key("e4 c5")));       // nur Stellungen der gezeigten Abweichungen
    }

    [Fact]
    public void ContinuationTree_BranchesOnHisMoves_AndEnds()
    {
        // Nutzer Weiß, 1.e4 e5; er spielt 1…c5. Danach in seinen Partien: 2.Nf3 d6 (3×) bzw. 2.Nf3 Nc6 (1×), 2.c3 d5 (2×)
        var g = Graph('w', "1. e4 e5 2. Nf3");
        var games = Times(3, "e4 c5 Nf3 d6 d4", false).Concat(Times(1, "e4 c5 Nf3 Nc6", false))
            .Concat(Times(2, "e4 c5 c3 d5", false));

        var r = OpponentDeviations.Find(g, games);

        var d = Assert.Single(r.Deviations);
        Assert.Equal("c5", d.San);
        var afterNf3 = r.Positions[Key("e4 c5 Nf3")];
        Assert.Equal(4, afterNf3.Reached);
        Assert.Equal(3, afterNf3.Moves["d6"]);                        // mehrere Gegnerzüge → mehrere Linien
        Assert.Equal(1, afterNf3.Moves["Nc6"]);
        Assert.Equal(2, r.Positions[Key("e4 c5 c3")].Moves["d5"]);
        // Stellung nach meiner Antwort (Weiß am Zug nach 1…c5) trägt KEINE Züge (nur seine verzweigen)
        Assert.Empty(r.Positions[Key("e4 c5")].Moves);
        // Ende: nach 2…Nc6 hat er nichts mehr gespielt; eine nie erreichte Stellung fehlt ganz
        Assert.Empty(r.Positions[Key("e4 c5 Nf3 Nc6")].Moves);
        Assert.False(r.Positions.ContainsKey(Key("e4 c5 Nc3")));
    }

    [Fact]
    public void ContinuationTree_StopsAfterTailPlies()
    {
        var g = Graph('w', "1. e4 e5");
        var tail = "Nf3 d6 d4 cxd4 Nxd4 Nf6 Nc3 a6 Be3 e5 Nb3 Be6 f3 Be7 Qd2 O-O O-O-O Nbd7 g4 b5";
        var r = OpponentDeviations.Find(g, Times(1, "e4 c5 " + tail, false));
        var sans = tail.Split(' ');
        Assert.True(r.Positions.ContainsKey(Key("e4 c5 " + string.Join(' ', sans.Take(OpponentDeviations.TailPlies)))));
        Assert.False(r.Positions.ContainsKey(Key("e4 c5 " + string.Join(' ', sans.Take(OpponentDeviations.TailPlies + 1)))));
    }

    // ── Dienst + Endpunkte ───────────────────────────────────────────────────────────────────────

    private sealed class Db : IDisposable
    {
        public readonly AppDbContext Ctx = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly MemoryCache Cache = new(new MemoryCacheOptions());
        public TrainingLinesService Service()
        {
            var notifications = new NotificationService(Ctx);
            var reps = new RepertoireService(Ctx, new RepertoireAnalyzeService(Ctx, new MemoryCache(new MemoryCacheOptions())),
                new FriendService(Ctx, notifications), notifications);
            return new TrainingLinesService(Ctx, reps, new ConfigurationBuilder().Build(), cache: Cache);
        }

        public async Task<int> RepertoireAsync(int userId, string name, string pgn)
        {
            if (!Ctx.AppUsers.Any(u => u.Id == userId)) Ctx.AppUsers.Add(new AppUser { Id = userId, Username = "u" + userId, PasswordHash = "x" });
            var rep = new Repertoire { UserId = userId, Name = name, UseForExtension = true };
            Ctx.Repertoires.Add(rep);
            await Ctx.SaveChangesAsync();
            Ctx.RepertoireFiles.Add(new RepertoireFile { RepertoireId = rep.Id, FileName = "a.pgn", PgnContent = pgn, FileSize = pgn.Length });
            await Ctx.SaveChangesAsync();
            return rep.Id;
        }

        public void Dispose() { Ctx.Dispose(); Cache.Dispose(); }
    }

    private static TrainingLinesService.Query Q(int? rep = null, string? color = null) =>
        new(rep, color, TrainingLinesService.ParseOverrides(null), null);

    private static Task<List<OpponentTrainingLines.Game>> GamesOf(IEnumerable<OpponentTrainingLines.Game> g) => Task.FromResult(g.ToList());

    [Fact]
    public async Task Service_Deviations_FromMarkedRepertoires_WithPositionsElo_CachedPerScope_ForeignIs404()
    {
        using var db = new Db();
        // Kapitel endet mit einem Weißzug → als Weiß trainiert
        var id = await db.RepertoireAsync(1, "Weiß", "[Event \"x\"]\n[White \"?\"]\n[Black \"Offen\"]\n\n1. e4 e5 2. Nf3 *\n");
        var foreign = await db.RepertoireAsync(2, "Fremd", "[Event \"x\"]\n[White \"?\"]\n[Black \"Offen\"]\n\n1. d4 *\n");
        var svc = db.Service();
        var calls = 0;
        Task<List<OpponentTrainingLines.Game>> games() { calls++; return GamesOf(Times(3, "e4 c5 Nf3 d6", false, 2022)); }

        var r = (await svc.DeviationsAsync(1, Q(), games, default, () => Task.FromResult<int?>(2150), "prep:9"))!;
        Assert.Equal("w", r["color"]!.GetValue<string>());
        Assert.Equal(3, r["games"]!.GetValue<int>());
        Assert.Equal(2150, r["elo"]!.GetValue<int>());
        var d = Assert.Single(r["deviations"]!.AsArray())!;
        Assert.Equal("c5", d["san"]!.GetValue<string>());
        Assert.Equal(new[] { "e4" }, d["path"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Equal(new[] { "e5" }, d["repertoire"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Equal(3, r["positions"]![Key("e4 c5 Nf3")]!["m"]!["d6"]!.GetValue<int>());
        Assert.Equal(0, r["more"]!.GetValue<int>());

        await svc.DeviationsAsync(1, Q(), games, default, null, "prep:9");
        Assert.Equal(1, calls);                                       // zweite Abfrage aus dem Speicher

        var noElo = (await svc.DeviationsAsync(1, Q(), games, default))!;
        Assert.Equal(TrainingExplorer.DefaultElo, noElo["elo"]!.GetValue<int>());
        Assert.Null(await svc.DeviationsAsync(1, Q(foreign), games, default));
        Assert.Equal(id, r["repertoires"]!.AsArray().Single()!["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task Service_TrainingLines_WithPositions_CarriesHisGamesPerRepertoirePosition()
    {
        using var db = new Db();
        await db.RepertoireAsync(1, "Weiß", "[Event \"x\"]\n[White \"?\"]\n[Black \"Offen\"]\n\n1. e4 e5 2. Nf3 *\n");
        var svc = db.Service();
        var games = Times(2, "e4 e5 Nf3 Nc6", false, 2021).Concat(Times(1, "e4 c5", false, 2024));

        var without = (await svc.LinesAsync(1, Q(), () => GamesOf(games), default))!;
        Assert.Null(without["positions"]);
        var r = (await svc.LinesAsync(1, Q(), () => GamesOf(games), default, positions: true))!;
        var start = r["positions"]![RepertoireReach.StandardStartKey]!;
        Assert.Equal(3, start["n"]!.GetValue<int>());
        Assert.Equal(2024, start["y"]!.GetValue<int>());
        var afterE4 = r["positions"]![Key("e4")]!;
        Assert.Equal(2, afterE4["m"]!["e5"]!.GetValue<int>());
        Assert.Equal(1, afterE4["m"]!["c5"]!.GetValue<int>());
        Assert.Equal(2, r["positions"]![Key("e4 e5 Nf3")]!["n"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(typeof(PrepController), nameof(PrepController.Deviations), "player/{id:int}/deviations", Permissions.PrepView)]
    [InlineData(typeof(LeagueTrainingController), nameof(LeagueTrainingController.Deviations), "player/{fide}/deviations", Permissions.LeagueView)]
    public void Endpoints_RouteAndPermission_LikeTrainingLines(Type controller, string method, string route, string permission)
    {
        var m = controller.GetMethod(method)!;
        Assert.Equal(route, m.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpGetAttribute>()!.Template);
        Assert.Equal(PermissionPolicyProvider.Prefix + permission, m.GetCustomAttribute<HasPermissionAttribute>()!.Policy);
    }
}
