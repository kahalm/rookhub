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
        // von vorne weg trifft er keinen Zug der Linie (1.d4 spielt er nie) — „nie erreicht", aber mit Partien vor den leeren
        Assert.True(line.NeverReached);
        Assert.Equal(0, line.Matched);
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
    public void FillRule_FullLinesFirst_ThenOneOpponentMoveMissing_ThenTwo_NeverReachedLast()
    {
        // Nutzer Weiß; der Gegner hat zweimal Schwarz gespielt
        var g = Graph('w',
            "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be3",   // 0: voll
            "1. e4 e5 2. Nf3 Nc6 3. Bb5 Nf6 4. O-O",                          // 1: ein Gegnerzug fehlt (a6 statt Nf6), Anfang 1/2
            "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 g6 6. Be3",    // 2: ein Gegnerzug fehlt (der letzte)
            "1. e4 c5 2. Nf3 e6 3. d4 d5",                                     // 3: zwei fehlen
            "1. e4 c6 2. d4 d5",                                               // 4: nie
            "1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4");                          // 5: voll
        var games = new[] { G("e4 c5 Nf3 d6 d4 cxd4 Nxd4 Nf6 Nc3 a6", false), G("e4 e5 Nf3 Nc6 Bb5 a6", false) };

        var r = OpponentTrainingLines.Rank(g, ["K", "K", "K", "K", "K", "K"], games);

        Assert.Equal(new[] { 0, 5, 1, 2, 3, 4 }, r.Lines.Select(l => l.Index));
        var najdorf = r.Lines[0];
        Assert.Equal((5, 0), (najdorf.Matched, najdorf.Missing));
        Assert.Equal(0.5, najdorf.Probability, 6);
        Assert.Equal(0.5, najdorf.PrefixProbability, 6);
        var spanish = r.Lines[2];
        Assert.Equal((2, 1), (spanish.Matched, spanish.Missing));
        Assert.Equal(0, spanish.Probability);
        Assert.Equal(0.5, spanish.PrefixProbability, 6);
        Assert.Equal(1, spanish.PrefixReached);           // die Partie, die bis 2...Sc6 dabei war
        Assert.False(spanish.NeverReached);
        var dragon = r.Lines[3];
        Assert.Equal((4, 1), (dragon.Matched, dragon.Missing));
        var twoMissing = r.Lines[4];
        Assert.Equal((1, 2), (twoMissing.Matched, twoMissing.Missing));
        Assert.True(r.Lines[5].NeverReached);
        Assert.Equal(0, r.Lines[5].Matched);
    }

    [Fact]
    public void FillRule_UsersExample_OneGame_FewFullLines_TheNextComeFromOneMoveEarlier()
    {
        // Wunsch des Users: er hat nur EINMAL gespielt — voll getroffen ist nur die eine Linie; aufgefüllt wird, „als ob er
        // den letzten Zug nicht gespielt hätte, sondern eins vorher abgewichen wäre", dann zwei vorher …
        var tails = new[] { "a6", "g6", "e6", "Nc6", "e5", "Bd7", "Qb6", "h6" };
        var lines = tails.Select(t => $"1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 {t} 6. h3").ToList();
        lines.Add("1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 e5 5. Nb5");             // weicht einen Zug früher ab
        lines.Add("1. e4 c5 2. Nf3 d6 3. d4 Nf6 4. Nc3");                          // zwei früher
        lines.Add("1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 g6 5. Nc3 Bg7 6. Be3");  // weicht früher ab UND geht weiter: zwei fehlen
        var g = Graph('w', lines.ToArray());
        var r = OpponentTrainingLines.Rank(g, Enumerable.Repeat("K", lines.Count).ToList(),
            [G("e4 c5 Nf3 d6 d4 cxd4 Nxd4 Nf6 Nc3 a6 Be3 e5", false, 2026)]);

        Assert.Equal(0, r.Lines[0].Missing);
        Assert.Equal("a6", r.Lines[0].Sans[9]);
        // sieben Linien weichen erst beim letzten Gegnerzug ab; die früher abweichenden (4...e5, 3...Sf6) enden gleich danach,
        // ihnen fehlt also AUCH nur ein Gegnerzug — gleiche Stufe, gleicher Anfang (1/1), gleiche Partien → Repertoire-Reihenfolge
        Assert.All(r.Lines.Skip(1).Take(8), l => Assert.Equal(1, l.Missing));
        Assert.Equal(new[] { "g6", "e6", "Nc6", "e5", "Bd7", "Qb6", "h6" }, r.Lines.Skip(1).Take(7).Select(l => l.Sans[9]));
        Assert.Equal(1, r.Lines[^3].Missing);
        Assert.Equal("e5", r.Lines[^3].Sans[7]);
        Assert.Equal((2, 1), (r.Lines[^2].Matched, r.Lines[^2].Missing));
        Assert.Equal((3, 2), (r.Lines[^1].Matched, r.Lines[^1].Missing));   // zwei fehlende: nach allen mit einem
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

        var r = await svc.LinesAsync(1, new TrainingLinesService.Query(null, null, TrainingLinesService.ParseOverrides(null), null), NoGames, default);
        Assert.NotNull(r);
        var reps = r!["repertoires"]!.AsArray();
        Assert.Equal(mine, Assert.Single(reps)!["id"]!.GetValue<int>());
        // 1.e4 c5 2.Sf3 d6 endet mit einem Schwarzzug → Auto-Erkennung: das Kapitel wird als Schwarz trainiert
        Assert.Equal(new[] { "b" }, reps[0]!["colors"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Null(r["repertoire"]);                                       // ohne Wahl: alle markierten
        Assert.Equal(mine, r["lines"]!.AsArray()[0]!["repertoireId"]!.GetValue<int>());
        Assert.Null(await svc.LinesAsync(1, new TrainingLinesService.Query(off, null, TrainingLinesService.ParseOverrides(null), null), NoGames, default));
        Assert.Null(await svc.LinesAsync(1, new TrainingLinesService.Query(foreign, null, TrainingLinesService.ParseOverrides(null), null), NoGames, default));
    }

    [Fact]
    public async Task Service_WithoutRepertoire_AnswersEmpty_AndDoesNotLoadGames()
    {
        using var db = new Db();
        var called = false;
        var r = await db.Service().LinesAsync(5, new TrainingLinesService.Query(null, null, TrainingLinesService.ParseOverrides(null), null), () => { called = true; return NoGames(); }, default);
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

        var r = (await svc.LinesAsync(1, new TrainingLinesService.Query(id, null, TrainingLinesService.ParseOverrides(null), null), NoGames, default))!;
        Assert.Equal("b", r["color"]!.GetValue<string>());
        Assert.Equal(new[] { "w", "b" }, r["colors"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Equal(2, r["total"]!.GetValue<int>());

        var w = (await svc.LinesAsync(1, new TrainingLinesService.Query(id, "w", TrainingLinesService.ParseOverrides(null), null), NoGames, default))!;
        Assert.Equal("w", w["color"]!.GetValue<string>());
        Assert.Equal("Weiß", Assert.Single(w["lines"]!.AsArray())!["chapter"]!.GetValue<string>());

        // eigene Festlegung: „Weiß" doch als Schwarz → dann gibt es nur noch Schwarz
        var o = (await svc.LinesAsync(1, new TrainingLinesService.Query(id, null, TrainingLinesService.ParseOverrides("{\"Weiß\":\"b\"}"), null), NoGames, default))!;
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

        var r = (await db.Service().LinesAsync(1, new TrainingLinesService.Query(id, "w", TrainingLinesService.ParseOverrides(null), null), NoGames, default))!;
        Assert.Equal(60, r["total"]!.GetValue<int>());
        Assert.Equal(50, r["lines"]!.AsArray().Count);
        Assert.Equal(10, r["more"]!.GetValue<int>());

        var all = (await db.Service().LinesAsync(1, new TrainingLinesService.Query(id, "w", TrainingLinesService.ParseOverrides(null), 5000), NoGames, default))!;
        Assert.Equal(60, all["lines"]!.AsArray().Count);
        Assert.Equal(0, all["more"]!.GetValue<int>());

        var cfg = (await db.Service(new() { [TrainingLinesService.TakeKey] = "7" }).LinesAsync(1, new TrainingLinesService.Query(id, "w", TrainingLinesService.ParseOverrides(null), null), NoGames, default))!;
        Assert.Equal(7, cfg["lines"]!.AsArray().Count);
        Assert.Equal(53, cfg["more"]!.GetValue<int>());
    }

    private static TrainingLinesService.Query Q(int? rep, string? color = null) =>
        new(rep, color, TrainingLinesService.ChapterOverrides.None, null);

    [Fact]
    public async Task CreateRepertoire_CopiesTheRankedSectionsUnchanged_InThisOrder_ReplacesTheSameName()
    {
        using var db = new Db();
        // Abschnitte mit Kommentaren und Varianten — sie müssen unverändert ankommen
        var pgn = Section("Offen", "1. e4 e5 {Hauptlinie} 2. Nf3 (2. Bc4 Nf6) 2... Nc6 3. Bb5")
                  + Section("Sizilianisch", "1. e4 c5 2. Nf3 d6 3. d4")
                  + Section("Caro", "1. e4 c6 2. d4 d5 3. e5");
        var source = await db.RepertoireAsync(1, "Weiß-Repertoire", pgn);
        db.Ctx.Repertoires.Single(r => r.Id == source).Kind = RepertoireKind.Opening;
        await db.Ctx.SaveChangesAsync();
        var games = () => Task.FromResult(new List<OpponentTrainingLines.Game>
        {
            G("e4 c5 Nf3 d6 d4 cxd4", false, 2025), G("e4 c5 Nf3 d6 d4 cxd4", false, 2024), G("e4 e5 Nf3 Nc6 Bb5 a6", false, 2023),
        });
        var svc = db.Service();

        var created = (await svc.CreateRepertoireAsync(1, "Huber, Franz", Q(source), games, default))!;
        var year = DateTime.UtcNow.Year;
        Assert.Equal($"Prep: Huber, Franz {year}", created.Name);
        Assert.Equal(3, created.Lines);
        Assert.False(created.Replaced);

        var rep = db.Ctx.Repertoires.AsNoTracking().Single(r => r.Id == created.Id);
        Assert.False(rep.UseForExtension);              // sonst wäre es selbst eine Quelle der Vorbereitung
        Assert.Equal(RepertoireKind.Opening, rep.Kind);
        Assert.Contains("Weiß-Repertoire", rep.Description);
        Assert.Contains("3 Partien", rep.Description);

        // dieselben Linien-Schlüssel wie die gereihten Quelllinien, in dieser Reihenfolge; Abschnitte unverändert
        var ranked = (await svc.LinesAsync(1, Q(source), games, default))!["lines"]!.AsArray().Select(l => l!["key"]!.GetValue<string>()).ToList();
        var file = db.Ctx.RepertoireFiles.AsNoTracking().Single(f => f.RepertoireId == created.Id).PgnContent;
        var copied = RepertoireReach.Build(TrainingLinesService.Sections(file), 'w');
        Assert.Equal(ranked, copied.Mainlines.Select(KeyOf));
        Assert.Contains("{Hauptlinie}", file);
        Assert.Contains("(2. Bc4 Nf6)", file);
        Assert.Contains("[Black \"Sizilianisch\"]", file);
        Assert.StartsWith("[Event", file);
        Assert.Equal(pgn, db.Ctx.RepertoireFiles.AsNoTracking().Single(f => f.RepertoireId == source).PgnContent);   // Quelle unberührt

        // zweites Mal: dasselbe Repertoire, Inhalt ersetzt, kein zweites
        var again = (await svc.CreateRepertoireAsync(1, "Huber, Franz", Q(source), games, default))!;
        Assert.Equal(created.Id, again.Id);
        Assert.True(again.Replaced);
        Assert.Equal(1, db.Ctx.Repertoires.Count(r => r.UserId == 1 && r.Name == created.Name));
        Assert.Equal(1, db.Ctx.RepertoireFiles.Count(f => f.RepertoireId == created.Id));
    }

    private static string KeyOf(List<RepertoireReach.Node> nodes)
    {
        var sans = new List<string>();
        for (var k = 0; k + 1 < nodes.Count; k++) sans.Add(nodes[k].Children.First(c => ReferenceEquals(c.Child, nodes[k + 1])).San);
        return ChessableTrainedLineService.LineKeyFromSans(sans);
    }

    [Fact]
    public async Task CreateRepertoire_AtMost50Lines_EvenIfTheListMayShowMore()
    {
        using var db = new Db();
        var firsts = new[] { "a6", "a5", "b6", "b5", "c6", "c5", "d6", "d5", "e6", "e5", "f6", "f5", "g6", "g5", "h6", "h5", "Na6", "Nc6", "Nf6", "Nh6" };
        var seconds = new[] { "Nf3", "d4", "Nc3" };
        var id = await db.RepertoireAsync(1, "Viel", string.Concat(firsts.SelectMany(a => seconds.Select(b => Section("Weiß", $"1. e4 {a} 2. {b}")))));

        var created = (await db.Service(new() { [TrainingLinesService.TakeKey] = "500" })
            .CreateRepertoireAsync(1, "Gegner", Q(id, "w"), NoGames, default))!;
        Assert.Equal(50, created.Lines);
        var file = db.Ctx.RepertoireFiles.AsNoTracking().Single(f => f.RepertoireId == created.Id).PgnContent;
        Assert.Equal(50, TrainingLinesService.Sections(file).Count);
    }

    [Fact]
    public async Task CreateRepertoire_SourceHasTheTargetName_IsRefusedAndStaysUntouched()
    {
        using var db = new Db();
        var name = TrainingLinesService.RepertoireName("Huber, Franz", DateTime.UtcNow.Year);
        var pgn = Section("K", "1. e4 c5 2. Nf3 d6");
        var source = await db.RepertoireAsync(1, name, pgn);   // die Quelle heißt schon wie das Ziel (und ist freigegeben)

        var e = await Assert.ThrowsAsync<TrainingLinesService.SameRepertoireException>(() =>
            db.Service().CreateRepertoireAsync(1, "Huber, Franz", Q(source), NoGames, default));
        Assert.Contains(name, e.Message);
        Assert.IsAssignableFrom<RookHub.Api.Exceptions.DomainValidationException>(e);   // ohne eigenen catch: 400
        Assert.Equal(pgn, db.Ctx.RepertoireFiles.AsNoTracking().Single(f => f.RepertoireId == source).PgnContent);
        Assert.Equal(1, db.Ctx.Repertoires.Count(r => r.UserId == 1));
    }

    [Fact]
    public async Task CreateRepertoire_ForeignRepertoireIsNull_NoLinesIsAValidationError()
    {
        using var db = new Db();
        var foreign = await db.RepertoireAsync(2, "Fremd", Section("x", "1. d4 d5"));
        var svc = db.Service();
        Assert.Null(await svc.CreateRepertoireAsync(1, "G", Q(foreign), NoGames, default));
        await Assert.ThrowsAsync<RookHub.Api.Exceptions.DomainValidationException>(() => svc.CreateRepertoireAsync(1, "G", Q(null), NoGames, default));
    }

    [Fact]
    public async Task AllMarked_OneRanking_ColorFilteredPerChapter_DuplicateLineOnce_FirstRepertoireByNameWins()
    {
        using var db = new Db();
        var a = await db.RepertoireAsync(1, "A Sizilianisch", Section("Najdorf", "1. e4 c5 2. Nf3 d6 3. d4") + Section("Offen", "1. e4 e5 2. Nf3 Nc6 3. Bb5"));
        // B: dieselbe Spanisch-Linie (Dublette), eine eigene Weiß-Linie und ein SCHWARZ-Kapitel (gehört nicht in die Weiß-Reihung)
        var b = await db.RepertoireAsync(1, "B Gemischt", Section("Spanisch", "1. e4 e5 2. Nf3 Nc6 3. Bb5")
            + Section("Französisch", "1. e4 e6 2. d4 d5 3. Nc3") + Section("Gegen d4", "1. d4 Nf6 2. c4 e6") + Section("Gegen d4", "1. d4 d5 2. c4 e6"));
        var games = () => Task.FromResult(new List<OpponentTrainingLines.Game>
        {
            G("e4 e6 d4 d5 Nc3", false), G("e4 e6 d4 d5 Nc3", false), G("e4 c5 Nf3 d6 d4", false), G("e4 e5 Nf3 Nc6 Bb5", false),
        });

        var r = (await db.Service().LinesAsync(1, Q(null, "w"), games, default))!;
        Assert.Null(r["repertoire"]);
        Assert.Equal("w", r["color"]!.GetValue<string>());
        var lines = r["lines"]!.AsArray();
        Assert.Equal(new[] { "e6", "c5", "e5" }, lines.Select(l => l!["moves"]![1]!.GetValue<string>()));   // 2/4, 1/4, 1/4 — kein d4-Kapitel
        Assert.Equal(b, lines[0]!["repertoireId"]!.GetValue<int>());
        Assert.Equal("B Gemischt", lines[0]!["repertoireName"]!.GetValue<string>());
        // die Spanisch-Linie steht in beiden: einmal, aus A (nach Name zuerst)
        Assert.Equal(a, lines[2]!["repertoireId"]!.GetValue<int>());
        Assert.Equal("Offen", lines[2]!["chapter"]!.GetValue<string>());
        var reps = r["repertoires"]!.AsArray();
        Assert.Equal(new[] { "w", "b" }, reps[1]!["colors"]!.AsArray().Select(x => x!.GetValue<string>()));

        // Schwarz: nur das Schwarz-Kapitel aus B
        var black = (await db.Service().LinesAsync(1, Q(null, "b"), games, default))!;
        Assert.Equal(2, black["total"]!.GetValue<int>());
        Assert.All(black["lines"]!.AsArray(), l => Assert.Equal(b, l!["repertoireId"]!.GetValue<int>()));
        // Filter auf EIN Repertoire wie bisher
        var onlyA = (await db.Service().LinesAsync(1, Q(a, "w"), games, default))!;
        Assert.Equal(a, onlyA["repertoire"]!.GetValue<int>());
        Assert.Equal(2, onlyA["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task CreateFromAllMarked_SectionsOfBothSources_InRankedOrder_AtMost50AcrossAll()
    {
        using var db = new Db();
        var firsts = new[] { "a6", "a5", "b6", "b5", "c6", "c5", "d6", "d5", "e6", "e5", "f6", "f5", "g6", "g5", "h6", "h5", "Na6", "Nc6", "Nf6", "Nh6" };
        string Lines(string second) => string.Concat(firsts.Select(f => Section("K", $"1. e4 {f} 2. {second}")));
        var a = await db.RepertoireAsync(1, "A", Lines("Nf3"));           // 20 Linien
        var b = await db.RepertoireAsync(1, "B", Lines("d4") + Lines("Nc3"));  // 40 Linien
        // der Gegner spielt 1...c5 und 1...e5 — deren Linien (aus beiden Quellen) zuerst
        var games = () => Task.FromResult(new List<OpponentTrainingLines.Game> { G("e4 c5 d4", false), G("e4 e5 Nc3", false) });
        var svc = db.Service();

        var created = (await svc.CreateRepertoireAsync(1, "Huber, Franz", Q(null, "w"), games, default))!;
        Assert.Equal(50, created.Lines);
        var ranked = (await svc.LinesAsync(1, new(null, "w", TrainingLinesService.ChapterOverrides.None, 50), games, default))!["lines"]!.AsArray();
        var file = db.Ctx.RepertoireFiles.AsNoTracking().Single(f => f.RepertoireId == created.Id).PgnContent;
        var copied = RepertoireReach.Build(TrainingLinesService.Sections(file), 'w');
        Assert.Equal(ranked.Select(l => l!["key"]!.GetValue<string>()), copied.Mainlines.Select(KeyOf));
        Assert.Contains(ranked, l => l!["repertoireId"]!.GetValue<int>() == a);
        Assert.Contains(ranked, l => l!["repertoireId"]!.GetValue<int>() == b);
        Assert.Contains(ranked[0]!["moves"]![1]!.GetValue<string>(), new[] { "c5", "e5" });
        var rep = db.Ctx.Repertoires.AsNoTracking().Single(r => r.Id == created.Id);
        Assert.Contains("„A“", rep.Description);
        Assert.Contains("„B“", rep.Description);
    }

    [Fact]
    public async Task CreateFromAllMarked_AMarkedRepertoireWithTheTargetName_IsLeftOut_AndReplaced()
    {
        using var db = new Db();
        var name = TrainingLinesService.RepertoireName("Huber, Franz", DateTime.UtcNow.Year);
        var source = await db.RepertoireAsync(1, "Weiß", Section("K", "1. e4 c5 2. Nf3 d6"));
        var old = await db.RepertoireAsync(1, name, Section("Alt", "1. d4 d5 2. c4"));   // früher erzeugt und angehakt
        var created = (await db.Service().CreateRepertoireAsync(1, "Huber, Franz", Q(null, "w"), NoGames, default))!;

        Assert.Equal(old, created.Id);
        Assert.True(created.Replaced);
        Assert.Equal(1, created.Lines);
        var file = db.Ctx.RepertoireFiles.AsNoTracking().Single(f => f.RepertoireId == old).PgnContent;
        Assert.Contains("1. e4 c5", file);
        Assert.DoesNotContain("1. d4 d5", file);
        Assert.DoesNotContain(name, db.Ctx.Repertoires.AsNoTracking().Single(r => r.Id == created.Id).Description!.Split(" aus ")[1]);
        Assert.NotEqual(source, created.Id);
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
        Assert.Equal('w', TrainingLinesService.ChapterColors(s, TrainingLinesService.ParseOverrides("{\"A\":\"w\"}").Flat)["A"]);
        Assert.Empty(TrainingLinesService.ParseOverrides("kaputt").Flat);
        // je Repertoire: { "7": { "A": "b" } } — gilt nur für Repertoire 7; flach nur für ein einzeln gewähltes
        var both = TrainingLinesService.ParseOverrides("{\"A\":\"w\",\"7\":{\"A\":\"b\"}}");
        Assert.Equal('b', both.For(7, single: false)["A"]);
        Assert.Equal('w', both.For(9, single: true)["A"]);
        Assert.Empty(both.For(9, single: false));
    }
}
