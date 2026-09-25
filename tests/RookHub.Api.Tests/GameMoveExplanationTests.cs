using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace RookHub.Api.Tests;

/// <summary>„Warum war das ein Fehler?" (0.534.0): Server-Spiegel der Zug-Klassen, Fakten, Prüfung, Ablauf.</summary>
public class GameMoveExplanationTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FakeLlm _llm = new();

    public GameMoveExplanationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private sealed class FakeLlm : IClaudeJsonClient
    {
        public bool IsConfigured { get; set; } = true;
        public bool Local { get; set; } = true;
        public bool IsLocal => Local;
        public string TranslationModel => "fake-local";
        public Queue<string?> Answers { get; } = new();
        public List<string> Prompts { get; } = new();
        public Task<string?> GenerateHintsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> TranslateCommentsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> CompleteJsonAsync(string purpose, string system, string userPrompt, JsonNode schema, int maxTokens, CancellationToken ct = default)
        {
            lock (Prompts) { Prompts.Add(userPrompt); return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : null); }
        }
    }

    private GameMoveExplanationService Service() => new(_db, _llm, new GameExplanationJobs(),
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        NullLogger<GameMoveExplanationService>.Instance);

    // ── Zug-Klassen: LITERALE Grenzwerte wie game-review.util.spec.ts ──────────────────────────────

    [Theory]
    [InlineData(60, 60, false, "best")]
    [InlineData(60, 58, false, "excellent")]     // 2 Punkte: Grenze gehört zur besseren Klasse
    [InlineData(60, 57.9, false, "good")]
    [InlineData(60, 55, false, "good")]
    [InlineData(60, 50, false, "inaccuracy")]
    [InlineData(60, 40, false, "mistake")]
    [InlineData(60, 39.9, false, "blunder")]
    [InlineData(60, 10, true, "best")]          // der Engine-Bestzug ist immer „best"
    public void Classify_ChessComBands(double before, double after, bool isBest, string expected)
        => Assert.Equal(expected, GameMistakes.Classify(before, after, isBest));

    [Fact]
    public void LineSans_TranslatesEngineLines_IncludingCastlingAsKingTakesRook()
    {
        Assert.Equal(["g6", "Qf3", "Nf6"], GameMistakes.LineSans(
            "r1bqkbnr/pppp1ppp/2n5/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR b KQkq - 3 3", ["g7g6", "h5f3", "g8f6"], 8));
        // Der Broker schreibt Rochade als König-schlägt-Turm.
        Assert.Equal(["O-O"], GameMistakes.LineSans("r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4", ["e1h1"], 8));
        // Ein Zug, der nicht geht, beendet die Linie.
        Assert.Equal(["e4"], GameMistakes.LineSans("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", ["e2e4", "e2e4"], 8));
    }

    [Theory]
    [InlineData(135, null, true, "+1.35")]
    [InlineData(135, null, false, "-1.35")]
    [InlineData(null, 3, true, "mate in 3")]
    [InlineData(null, 1, false, "gets mated in 1")]
    public void EvalText_FromTheMoversView(int? cp, int? mate, bool white, string expected)
        => Assert.Equal(expected, GameMistakes.EvalText(cp, mate, white));

    // ── Schäfermatt: 3…Sf6?? ist der grobe Fehler ─────────────────────────────────────────────────

    private static readonly string[] Fens =
    {
        "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
        "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1",
        "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2",
        "rnbqkbnr/pppp1ppp/8/4p2Q/4P3/8/PPPP1PPP/RNB1KBNR b KQkq - 1 2",
        "r1bqkbnr/pppp1ppp/2n5/4p2Q/4P3/8/PPPP1PPP/RNB1KBNR w KQkq - 2 3",
        "r1bqkbnr/pppp1ppp/2n5/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR b KQkq - 3 3",
        "r1bqkb1r/pppp1ppp/2n2n2/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4",
    };
    private static readonly (string Uci, string San, string Cands)[] Moves =
    {
        ("e2e4", "e4", """[{"uci":"e2e4","cp":30}]"""),
        ("e7e5", "e5", """[{"uci":"e7e5","cp":-30}]"""),
        ("d1h5", "Qh5", """[{"uci":"g1f3","cp":40},{"uci":"d1h5","cp":20}]"""),
        ("b8c6", "Nc6", """[{"uci":"b8c6","cp":-20}]"""),
        ("f1c4", "Bc4", """[{"uci":"f1c4","cp":20}]"""),
        ("g8f6", "Nf6", """[{"uci":"g7g6","cp":-20,"pv":["g7g6","h5f3","g8f6"]},{"uci":"g8f6","mate":-1}]"""),
        ("h5f7", "Qxf7#", """[{"uci":"h5f7","mate":1,"pv":["h5f7"]}]"""),
    };

    private static List<GameAnalysisPosition> Positions()
        => Enumerable.Range(0, Moves.Length).Select(i => new GameAnalysisPosition
        {
            Ply = i, Fen = Fens[i], GameMoveUci = Moves[i].Uci, GameMoveSan = Moves[i].San, CandidatesJson = Moves[i].Cands, Depth = 20,
        }).ToList();

    [Fact]
    public void Find_TheBlunder_WithBestLineAndRefutationAsSan()
    {
        var flaws = GameMistakes.Find(Positions(), Moves.Length);

        var f = Assert.Single(flaws);
        Assert.Equal(5, f.Ply);
        Assert.False(f.White);
        Assert.Equal("blunder", f.Class);
        Assert.Equal("Nf6", f.PlayedSan);
        Assert.Equal("g6", f.BestSan);
        Assert.Equal(["g6", "Qf3", "Nf6"], f.BestLine);
        Assert.Equal(["Qxf7#"], f.Refutation);
        Assert.Equal("gets mated in 1", f.EvalAfter);
        Assert.True(f.WinBefore > 45 && f.WinAfter == 0);
    }

    [Fact]
    public void Prompt_CarriesOnlyTheCheckedFacts()
    {
        var f = GameMistakes.Find(Positions(), Moves.Length).Single();
        var prompt = GameMoveExplanationService.UserPrompt(f);
        Assert.Contains("The player is Black. Played move: 3... Nf6 — a serious blunder.", prompt);
        Assert.Contains("Better was g6. Engine line from the position before the move: g6 Qf3 Nf6", prompt);
        Assert.Contains("Opponent's best answer to Nf6, with the engine line: Qxf7#", prompt);
        var system = GameMoveExplanationService.SystemPrompt("de");
        Assert.Contains("Explain in German", system);
        Assert.Contains("{\"explanation\": \"...\"}", system);
    }

    [Theory]
    [InlineData("Nach Nf6 setzt Weiß mit Qxf7# sofort matt; g6 hätte die Dame von f7 abgeschnitten.", true)]
    [InlineData("Der Springer auf f6 deckt h7 nicht, und f7 ist nur vom König gedeckt.", true)] // bloße Felder zählen nicht
    [InlineData("Besser war Be7, dann hält f7.", false)]                                         // Be7 steht in keiner Linie
    [InlineData("Nach 0-0 wäre alles gut.", false)]
    public void IsGrounded_OnlyMovesFromTheGivenLines(string text, bool expected)
    {
        var f = GameMistakes.Find(Positions(), Moves.Length).Single();
        Assert.Equal(expected, GameMoveExplanationService.IsGrounded(text, f));
    }

    // ── Ablauf ─────────────────────────────────────────────────────────────────────────────────────

    private async Task<(int UserId, int GameId, int AnalysisId)> SeedAsync(GameAnalysisStatus status = GameAnalysisStatus.Done)
    {
        var user = new AppUser { Username = "u" + Guid.NewGuid().ToString("N")[..8], PasswordHash = "x" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        var analysis = new GameAnalysis
        {
            UserId = user.Id, Pgn = "1. e4 e5 2. Qh5 Nc6 3. Bc4 Nf6 4. Qxf7# 1-0", StartFen = "startpos", Status = status,
            PlyCount = Moves.Length, TargetDepth = 20, Origin = GameAnalysisOrigin.SavedGame,
        };
        foreach (var p in Positions()) analysis.Positions.Add(p);
        _db.GameAnalyses.Add(analysis);
        await _db.SaveChangesAsync();
        var game = new SavedGame { UserId = user.Id, Source = "lichess", Pgn = analysis.Pgn, ShareToken = "tok" + analysis.Id, GameAnalysisId = analysis.Id };
        _db.SavedGames.Add(game);
        await _db.SaveChangesAsync();
        return (user.Id, game.Id, analysis.Id);
    }

    [Fact]
    public async Task Generate_StoresTheGroundedText_AndRetriesOnceWhenAMoveIsInvented()
    {
        var (_, _, analysisId) = await SeedAsync();
        _llm.Answers.Enqueue("{\"explanation\":\"Besser war Be7.\"}");  // erfunden → Nachfrage
        _llm.Answers.Enqueue("{\"explanation\":\"Nach Nf6 folgt Qxf7# — g6 hätte das verhindert.\"}");
        var service = Service();

        var saved = await service.GenerateAsync(analysisId, "de", CancellationToken.None);

        Assert.Equal(1, saved);
        var row = await _db.GameMoveExplanations.SingleAsync();
        Assert.Equal(5, row.Ply);
        Assert.Equal("de", row.Language);
        Assert.Equal("blunder", row.Class);
        Assert.Equal("fake-local", row.Model);
        Assert.Equal(2, _llm.Prompts.Count);
        Assert.Contains("not in the lines above", _llm.Prompts[1]);

        // Ein zweiter Lauf erzeugt nichts doppelt.
        Assert.Equal(0, await service.GenerateAsync(analysisId, "de", CancellationToken.None));
    }

    [Fact]
    public async Task Generate_TwiceInvented_StoresNothing()
    {
        var (_, _, analysisId) = await SeedAsync();
        _llm.Answers.Enqueue("{\"explanation\":\"Be7!\"}");
        _llm.Answers.Enqueue("{\"explanation\":\"Nd4 war besser.\"}");

        Assert.Equal(0, await Service().GenerateAsync(analysisId, "de", CancellationToken.None));
        Assert.Empty(_db.GameMoveExplanations);
    }

    [Fact]
    public async Task OnlyOnOwnHardware_ClaudeWouldCostMoney()
    {
        var (_, _, analysisId) = await SeedAsync();
        _llm.Local = false;
        var service = Service();
        Assert.False(service.Available);
        Assert.Equal(0, await service.GenerateAsync(analysisId, "de", CancellationToken.None));
        Assert.Empty(_llm.Prompts);
    }

    [Fact]
    public async Task Get_OwnerMayGenerate_SharedViewerOnlyReads_UnfinishedAnalysisNot()
    {
        var (userId, gameId, analysisId) = await SeedAsync();
        _db.GameMoveExplanations.Add(new GameMoveExplanation { GameAnalysisId = analysisId, Ply = 5, Language = "de", Class = "blunder", Text = "t" });
        await _db.SaveChangesAsync();
        var service = Service();

        var own = await service.GetAsync(await service.AnalysisOfOwnGameAsync(userId, gameId), "de-AT", owner: true);
        Assert.True(own.CanGenerate);
        Assert.Equal("de", own.Language);
        Assert.Equal("t", Assert.Single(own.Items).Text);

        var shared = await service.GetAsync(await service.AnalysisOfSharedGameAsync("tok" + analysisId), "de", owner: false);
        Assert.False(shared.CanGenerate);
        Assert.Single(shared.Items);

        Assert.Null(await service.AnalysisOfOwnGameAsync(userId + 1, gameId)); // fremde Partie

        var (u2, g2, _) = await SeedAsync(GameAnalysisStatus.Running);
        Assert.False((await service.GetAsync(await service.AnalysisOfOwnGameAsync(u2, g2), "de", owner: true)).CanGenerate);
    }

    [Fact]
    public async Task Controller_WithoutOwnHardware_503_ForeignGame_404()
    {
        var (userId, gameId, _) = await SeedAsync();
        GameExplanationController Controller(int uid) => new(Service())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, uid.ToString())], "t")),
                },
            },
        };

        Assert.IsType<NotFoundResult>((await Controller(userId + 99).Generate(gameId, "de")).Result);
        _llm.Local = false;
        var r = Assert.IsType<ObjectResult>((await Controller(userId).Generate(gameId, "de")).Result);
        Assert.Equal(503, r.StatusCode);
    }
}
