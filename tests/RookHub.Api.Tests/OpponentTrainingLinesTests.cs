using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Prep;

namespace RookHub.Api.Tests;

/// <summary>
/// Trainingslinien gegen einen Gegner (2026-10-07): Linien des eigenen Repertoires, gereiht nach den Partien EINES Gegners —
/// Produkt über die Gegnerzüge, Zugumstellungen, nie erreichte Stellungen, falsche Farbe, Grenze + „weitere", Reihenfolge,
/// Kapitelfarben wie im Trainer. Ohne DB, bis auf den Dienst (In-Memory).
/// </summary>
public class OpponentTrainingLinesTests
{
    private static RepertoireReach.Graph Graph(char color, params string[] lines) =>
        RepertoireReach.Build(lines.Select((l, i) => $"[Event \"x\"]\n[Black \"K\"]\n\n{l} *\n")
            .SelectMany(PgnMoveTree.ParseSections).Where(s => s.Moves.Count > 0), color);

    private static OpponentTrainingLines.Game G(string moves, bool opponentWhite, int? year = 2024) =>
        new(moves.Split(' ', StringSplitOptions.RemoveEmptyEntries), opponentWhite, year);

    private static IEnumerable<OpponentTrainingLines.Game> Times(int n, string moves, bool opponentWhite, int? year = 2024) =>
        Enumerable.Range(0, n).Select(_ => G(moves, opponentWhite, year));

    private static OpponentTrainingLines.Line ByFirst(OpponentTrainingLines.Result r, string sans) =>
        r.Lines.Single(l => string.Join(' ', l.Sans) == sans);

    [Fact]
    public void Probability_IsTheProductOverTheOpponentMoves_OwnMovesCountOne()
    {
        // Nutzer Weiß, Gegner Schwarz: 1.e4 c5 (3 von 4) 2.Nf3 d6 (2 von 3 nach 2.Nf3)
        var g = Graph('w', "1. e4 c5 2. Nf3 d6", "1. e4 e5 2. Nf3 Nc6");
        var games = Times(2, "e4 c5 Nf3 d6", false, 2023)
            .Concat(Times(1, "e4 c5 Nf3 Nc6", false, 2025))
            .Concat(Times(1, "e4 e5 Nf3 Nc6", false, 2019));

        var r = OpponentTrainingLines.Rank(g, ["K", "K"], games);

        Assert.Equal(4, r.Games);
        var najdorf = ByFirst(r, "e4 c5 Nf3 d6");
        Assert.Equal(0.75 * (2.0 / 3), najdorf.Probability, 6);
        Assert.Equal(2, najdorf.Reached);
        Assert.Equal(2023, najdorf.LastYear);
        Assert.False(najdorf.NeverReached);
        var open = ByFirst(r, "e4 e5 Nf3 Nc6");
        Assert.Equal(0.25 * 1.0, open.Probability, 6);
        Assert.Equal(2019, open.LastYear);
        Assert.Equal("e4 c5 Nf3 d6", string.Join(' ', r.Lines[0].Sans));     // wahrscheinlichste zuerst
    }

    [Fact]
    public void Transposition_CountsTheSamePositionOverAnotherMoveOrder()
    {
        // Repertoire (Schwarz): 1.d4 Nf6 2.c4 e6 3.Nc3 Bb4 — der Gegner kommt über 1.c4 e6 2.Nc3 Nf6 3.d4 in dieselbe Stellung.
        var g = Graph('b', "1. e4 e5", "1. d4 Nf6 2. c4 e6 3. Nc3 Bb4");
        var games = Times(3, "c4 e6 Nc3 Nf6 d4 Bb4", true);

        var r = OpponentTrainingLines.Rank(g, ["K", "K"], games);

        // beide 0 % — die mit Partien kommt vor die nie erreichte, obwohl sie im Repertoire später steht
        Assert.Equal(new[] { "d4", "e4" }, r.Lines.Select(l => l.Sans[0]));
        Assert.True(r.Lines[1].NeverReached);
        var line = r.Lines[0];
        Assert.Equal(3, line.Reached);          // die Stellung nach 3.Nc3 erreichen alle drei, über die andere Zugfolge
        Assert.False(line.NeverReached);
        // Die Zwischenstellungen der Linie (nach 1.d4) erreicht der Gegner so nie — das Produkt ist 0, die Partien zählen trotzdem.
        Assert.Equal(0, line.Probability);
    }

    [Fact]
    public void Transposition_InsideTheProduct_MergesBothMoveOrders()
    {
        // Nutzer Schwarz: 1.d4 Nf6 2.c4 e6 — der Gegner spielt 1.c4 (2) und 1.d4 (2); nach 1.c4 Nf6 2.d4 steht dieselbe Stellung.
        var g = Graph('b', "1. d4 Nf6 2. c4 e6", "1. c4 Nf6 2. d4 e6");
        var games = Times(2, "d4 Nf6 c4 e6", true).Concat(Times(2, "c4 Nf6 d4 e6", true));

        var r = OpponentTrainingLines.Rank(g, ["K", "K"], games);

        Assert.Equal(1, r.Lines.Count(l => l.Sans[0] == "d4"));
        Assert.Equal(0.5, ByFirst(r, "d4 Nf6 c4 e6").Probability, 6);
        Assert.Equal(4, ByFirst(r, "d4 Nf6 c4 e6").Reached);           // vier Partien erreichen die Stellung nach 2.c4
        Assert.Equal(0.5, ByFirst(r, "c4 Nf6 d4 e6").Probability, 6);
    }

    [Fact]
    public void NeverReachedPosition_StaysInTheList_AtTheEnd()
    {
        var g = Graph('w', "1. e4 c6 2. d4 d5", "1. e4 c5 2. Nf3 d6", "1. e4 e5 2. Nf3 Nc6");
        var games = Times(5, "e4 c5 Nf3 d6", false).Concat(Times(1, "e4 e5 Nf3 Nc6", false));

        var r = OpponentTrainingLines.Rank(g, ["K", "K", "K"], games);

        Assert.Equal(3, r.Lines.Count);
        var caro = r.Lines[^1];
        Assert.Equal("e4 c6 d4 d5", string.Join(' ', caro.Sans));
        Assert.True(caro.NeverReached);
        Assert.Equal(0, caro.Probability);
        Assert.Equal(0, caro.Reached);
        Assert.Null(caro.LastYear);
    }

    [Fact]
    public void WrongColor_CountsNoGames()
    {
        // Repertoire Weiß → gezählt werden nur Partien, in denen der Gegner SCHWARZ hatte; seine Weißpartien zählen nicht.
        var g = Graph('w', "1. e4 c5 2. Nf3 d6");
        var r = OpponentTrainingLines.Rank(g, ["K"], Times(4, "e4 c5 Nf3 d6", opponentWhite: true));

        Assert.Equal(0, r.Games);
        Assert.True(Assert.Single(r.Lines).NeverReached);
    }

    [Fact]
    public void Order_ProbabilityThenGamesThenRepertoireOrder()
    {
        // zwei gleich wahrscheinliche Linien (je 1/2 nach 1.e4, gleich viele Partien) in Repertoire-Reihenfolge, die nie
        // erreichten dahinter, ebenfalls in Repertoire-Reihenfolge (mehr Partien bei gleicher Wahrscheinlichkeit: Transpositionstest)
        var g = Graph('w', "1. d4 d5", "1. c4 e5", "1. e4 c5 2. Nf3", "1. e4 e5 2. Nf3 Nc6");
        var games = Times(2, "e4 c5 Nc3", false).Concat(Times(2, "e4 e5 Nf3 Nc6", false));

        var r = OpponentTrainingLines.Rank(g, ["K", "K", "K", "K"], games);

        Assert.Equal(new[] { "e4 c5 Nf3", "e4 e5 Nf3 Nc6", "d4 d5", "c4 e5" }, r.Lines.Select(l => string.Join(' ', l.Sans)));
        Assert.Equal(0.5, r.Lines[0].Probability, 6);
        Assert.Equal(0.5, r.Lines[1].Probability, 6);
    }

    [Fact]
    public void OnlyTheFirst40Plies_Count()
    {
        var shuffle = string.Join(' ', Enumerable.Repeat("Nf3 Nf6 Ng1 Ng8", 10));        // 40 Halbzüge zurück in die Grundstellung
        var g = Graph('w', "1. e4 c5");
        var r = OpponentTrainingLines.Rank(g, ["K"], [G(shuffle + " e4 c5", false)]);
        Assert.True(Assert.Single(r.Lines).NeverReached);
    }

    [Fact]
    public void Key_IsTheTrainerLineKey_AndEndIsTheEndPosition()
    {
        var g = Graph('w', "1. e4 c5 2. Nf3 d6");
        var line = Assert.Single(OpponentTrainingLines.Rank(g, ["K"], []).Lines);
        Assert.Equal(ChessableTrainedLineService.LineKeyFromSans(["e4", "c5", "Nf3", "d6"]), line.Key);
        Assert.Equal(g.Mainlines[0][^1].Key, line.End);
        Assert.Null(line.StartFen);
        Assert.Equal("K", line.Chapter);
    }

    [Fact]
    public void ThousandsOfGames_StayFast()
    {
        var g = Graph('w', "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6", "1. e4 e5 2. Nf3 Nc6 3. Bb5 a6", "1. e4 e6 2. d4 d5");
        var rnd = new Random(7);
        var openings = new[]
        {
            "e4 c5 Nf3 d6 d4 cxd4 Nxd4 Nf6 Nc3 a6 Be3 e5 Nb3 Be6 f3 Be7 Qd2 O-O O-O-O Nbd7 g4 b5",
            "e4 e5 Nf3 Nc6 Bb5 a6 Ba4 Nf6 O-O Be7 Re1 b5 Bb3 d6 c3 O-O h3 Nb8 d4 Nbd7",
            "e4 e6 d4 d5 Nc3 Bb4 e5 c5 a3 Bxc3+ bxc3 Ne7 Qg4 O-O Bd3 Nbc6 Qh5 Ng6",
        };
        // 3000 Partien, je eine Abweichung irgendwo — der Präfixbaum hat dann viele Äste
        var games = Enumerable.Range(0, 3000).Select(i =>
        {
            var m = openings[i % 3].Split(' ').ToList();
            var cut = 6 + rnd.Next(m.Count - 6);
            return new OpponentTrainingLines.Game(m.Take(cut).ToList(), false, 2000 + i % 25);
        }).ToList();

        var sw = Stopwatch.StartNew();
        var r = OpponentTrainingLines.Rank(g, ["K", "K", "K"], games);
        sw.Stop();

        Assert.Equal(3000, r.Games);
        Assert.True(sw.ElapsedMilliseconds < 3000, $"{sw.ElapsedMilliseconds} ms");
    }

    // ── Dienst: Repertoire-Auswahl, Farben, Grenze ───────────────────────────────────────────────

    private sealed class Db : IDisposable
    {
        public readonly AppDbContext Ctx = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public TrainingLinesService Service(Dictionary<string, string?>? cfg = null)
        {
            var notifications = new NotificationService(Ctx);
            var reps = new RepertoireService(Ctx, new RepertoireAnalyzeService(Ctx, new MemoryCache(new MemoryCacheOptions())),
                new FriendService(Ctx, notifications), notifications);
            return new TrainingLinesService(Ctx, reps, new ConfigurationBuilder().AddInMemoryCollection(cfg ?? new()).Build());
        }

        public async Task<int> RepertoireAsync(int userId, string name, string pgn, bool forExtension = true)
        {
            if (!Ctx.AppUsers.Any(u => u.Id == userId)) Ctx.AppUsers.Add(new AppUser { Id = userId, Username = "u" + userId, PasswordHash = "x" });
            var rep = new Repertoire { UserId = userId, Name = name, UseForExtension = forExtension };
            Ctx.Repertoires.Add(rep);
            await Ctx.SaveChangesAsync();
            Ctx.RepertoireFiles.Add(new RepertoireFile { RepertoireId = rep.Id, FileName = "a.pgn", PgnContent = pgn, FileSize = pgn.Length });
            await Ctx.SaveChangesAsync();
            return rep.Id;
        }

        public void Dispose() => Ctx.Dispose();
    }

    private static string Section(string chapter, string moves, string? white = null) =>
        $"[Event \"x\"]\n[White \"{white ?? "?"}\"]\n[Black \"{chapter}\"]\n\n{moves} *\n\n";

    private static Task<List<OpponentTrainingLines.Game>> NoGames() => Task.FromResult(new List<OpponentTrainingLines.Game>());

    [Fact]
    public async Task Service_ListsOnlyOwnRepertoiresForExtension_ForeignOrUnflaggedIs404()
    {
        using var db = new Db();
        var mine = await db.RepertoireAsync(1, "B-Sizilianisch", Section("Sizilianisch", "1. e4 c5 2. Nf3 d6"));
        var off = await db.RepertoireAsync(1, "A-Aus", Section("x", "1. d4 d5"), forExtension: false);
        var foreign = await db.RepertoireAsync(2, "Fremd", Section("x", "1. d4 d5"));
        var svc = db.Service();

        var r = await svc.LinesAsync(1, null, null, null, null, NoGames, default);
        Assert.NotNull(r);
        var reps = r!["repertoires"]!.AsArray();
        Assert.Equal(mine, Assert.Single(reps)!["id"]!.GetValue<int>());
        Assert.Equal(mine, r["repertoire"]!.GetValue<int>());
        Assert.Null(await svc.LinesAsync(1, off, null, null, null, NoGames, default));
        Assert.Null(await svc.LinesAsync(1, foreign, null, null, null, NoGames, default));
    }

    [Fact]
    public async Task Service_WithoutRepertoire_AnswersEmpty_AndDoesNotLoadGames()
    {
        using var db = new Db();
        var called = false;
        var r = await db.Service().LinesAsync(5, null, null, null, null, () => { called = true; return NoGames(); }, default);
        Assert.Empty(r!["repertoires"]!.AsArray());
        Assert.Null(r["repertoire"]);
        Assert.Empty(r["lines"]!.AsArray());
        Assert.False(called);
    }

    [Fact]
    public async Task Service_ColorPerChapterLikeTheTrainer_DefaultIsTheColorWithMoreLines_OverridesWin()
    {
        using var db = new Db();
        // Kapitel „Weiß": Linien enden mit einem Weißzug; Kapitel „Französisch": mit einem Schwarzzug (2 Linien)
        var pgn = Section("Weiß", "1. e4 c5 2. Nf3") + Section("Französisch", "1. e4 e6") + Section("Französisch", "1. d4 e6 2. c4 d5");
        var id = await db.RepertoireAsync(1, "Gemischt", pgn);
        var svc = db.Service();

        var r = (await svc.LinesAsync(1, id, null, null, null, NoGames, default))!;
        Assert.Equal("b", r["color"]!.GetValue<string>());
        Assert.Equal(new[] { "w", "b" }, r["colors"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Equal(2, r["total"]!.GetValue<int>());

        var w = (await svc.LinesAsync(1, id, "w", null, null, NoGames, default))!;
        Assert.Equal("w", w["color"]!.GetValue<string>());
        Assert.Equal("Weiß", Assert.Single(w["lines"]!.AsArray())!["chapter"]!.GetValue<string>());

        // eigene Festlegung: „Weiß" doch als Schwarz → dann gibt es nur noch Schwarz
        var o = (await svc.LinesAsync(1, id, null, "{\"Weiß\":\"b\"}", null, NoGames, default))!;
        Assert.Equal(new[] { "b" }, o["colors"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Equal(3, o["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task Service_LimitsTo50ByDefault_RestAsMore_TakeAndConfigChangeIt()
    {
        using var db = new Db();
        // 60 verschiedene Weiß-Linien: 1.e4 mit je eigener Antwort und eigenem zweiten Zug
        var firsts = new[] { "a6", "a5", "b6", "b5", "c6", "c5", "d6", "d5", "e6", "e5", "f6", "f5", "g6", "g5", "h6", "h5", "Na6", "Nc6", "Nf6", "Nh6" };
        var seconds = new[] { "Nf3", "d4", "Nc3" };
        var pgn = string.Concat(firsts.SelectMany(a => seconds.Select(b => Section("Weiß", $"1. e4 {a} 2. {b}"))));
        var id = await db.RepertoireAsync(1, "Viel", pgn);

        var r = (await db.Service().LinesAsync(1, id, "w", null, null, NoGames, default))!;
        Assert.Equal(60, r["total"]!.GetValue<int>());
        Assert.Equal(50, r["lines"]!.AsArray().Count);
        Assert.Equal(10, r["more"]!.GetValue<int>());

        var all = (await db.Service().LinesAsync(1, id, "w", null, 5000, NoGames, default))!;
        Assert.Equal(60, all["lines"]!.AsArray().Count);
        Assert.Equal(0, all["more"]!.GetValue<int>());

        var cfg = (await db.Service(new() { [TrainingLinesService.TakeKey] = "7" }).LinesAsync(1, id, "w", null, null, NoGames, default))!;
        Assert.Equal(7, cfg["lines"]!.AsArray().Count);
        Assert.Equal(53, cfg["more"]!.GetValue<int>());
    }

    [Fact]
    public void Sections_DropInfoLines_LikeTheTrainer()
    {
        var pgn = Section("K", "1. e4 c5") + Section("K", "1. e4 e5", white: "Info | Erklärung") + Section("K", "1. d4 { [%info] } d5");
        var s = TrainingLinesService.Sections(pgn);
        Assert.Single(s);
        Assert.Equal("e4", s[0].Moves[0].San);
    }

    [Fact]
    public void ChapterColors_TieFallsToTheOtherSideOfTheRoot()
    {
        var s = TrainingLinesService.Sections(Section("A", "1. e4 c5") + Section("A", "1. e4 c5 2. Nf3"));
        Assert.Equal('b', TrainingLinesService.ChapterColors(s, new Dictionary<string, char>())["A"]);
        Assert.Equal('w', TrainingLinesService.ChapterColors(s, TrainingLinesService.ParseOverrides("{\"A\":\"w\"}"))["A"]);
        Assert.Empty(TrainingLinesService.ParseOverrides("kaputt"));
    }
}
