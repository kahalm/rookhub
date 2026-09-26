using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>„Roast my game" (0.535.0): drei Stile, nur über eigene Hardware, nur mit Analyse, geprüfte Züge.</summary>
public class GameRoastTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FakeLlm _llm = new();

    public GameRoastTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private sealed class FakeLlm : IClaudeJsonClient
    {
        public bool IsConfigured => true;
        public bool Local { get; set; } = true;
        public bool IsLocal => Local;
        public string TranslationModel => "fake-local";
        public Queue<string?> Answers { get; } = new();
        public List<(string System, string User)> Calls { get; } = new();
        public Task<string?> GenerateHintsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> TranslateCommentsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> CompleteJsonAsync(string purpose, string system, string userPrompt, JsonNode schema, int maxTokens, CancellationToken ct = default)
        {
            Calls.Add((system, userPrompt));
            return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : null);
        }
    }

    private GameRoastService Service() => new(_db, _llm, TestServices.SavedGames(_db), NullLogger<GameRoastService>.Instance);

    // Schäfermatt aus Schwarz-Sicht (Besitzer spielt Schwarz und wird matt gesetzt).
    private static readonly (string Fen, string Uci, string San, string Cands)[] Plies =
    {
        ("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "e2e4", "e4", """[{"uci":"e2e4","cp":30}]"""),
        ("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1", "e7e5", "e5", """[{"uci":"e7e5","cp":-30}]"""),
        ("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2", "d1h5", "Qh5", """[{"uci":"g1f3","cp":40},{"uci":"d1h5","cp":20}]"""),
        ("rnbqkbnr/pppp1ppp/8/4p2Q/4P3/8/PPPP1PPP/RNB1KBNR b KQkq - 1 2", "b8c6", "Nc6", """[{"uci":"b8c6","cp":-20}]"""),
        ("r1bqkbnr/pppp1ppp/2n5/4p2Q/4P3/8/PPPP1PPP/RNB1KBNR w KQkq - 2 3", "f1c4", "Bc4", """[{"uci":"f1c4","cp":20}]"""),
        ("r1bqkbnr/pppp1ppp/2n5/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR b KQkq - 3 3", "g8f6", "Nf6", """[{"uci":"g7g6","cp":-20,"pv":["g7g6","h5f3","g8f6"]},{"uci":"g8f6","mate":-1}]"""),
        ("r1bqkb1r/pppp1ppp/2n2n2/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4", "h5f7", "Qxf7#", """[{"uci":"h5f7","mate":1,"pv":["h5f7"]}]"""),
    };

    private async Task<(int UserId, int GameId)> SeedAsync(bool withAnalysis = true)
    {
        var user = new AppUser { Username = "u" + Guid.NewGuid().ToString("N")[..8], PasswordHash = "x" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        const string pgn = "[White \"Gegner\"]\n[Black \"Ich\"]\n[Result \"1-0\"]\n\n1. e4 e5 2. Qh5 Nc6 3. Bc4 Nf6 4. Qxf7# 1-0";
        int? analysisId = null;
        if (withAnalysis)
        {
            var analysis = new GameAnalysis
            {
                UserId = user.Id, Pgn = pgn, StartFen = "startpos", Status = GameAnalysisStatus.Done, PlyCount = Plies.Length,
                TargetDepth = 20, Origin = GameAnalysisOrigin.SavedGame, AccuracyWhite = 92, AccuracyBlack = 41,
            };
            for (var i = 0; i < Plies.Length; i++)
                analysis.Positions.Add(new GameAnalysisPosition
                {
                    Ply = i, Fen = Plies[i].Fen, GameMoveUci = Plies[i].Uci, GameMoveSan = Plies[i].San, CandidatesJson = Plies[i].Cands, Depth = 20,
                });
            _db.GameAnalyses.Add(analysis);
            await _db.SaveChangesAsync();
            analysisId = analysis.Id;
        }
        var game = new SavedGame
        {
            UserId = user.Id, Source = "lichess", Pgn = pgn, White = "Gegner", Black = "Ich", Result = "1-0",
            ShareToken = Guid.NewGuid().ToString("N")[..20], GameAnalysisId = analysisId, OwnerSide = "black",
        };
        _db.SavedGames.Add(game);
        await _db.SaveChangesAsync();
        return (user.Id, game.Id);
    }

    [Fact]
    public async Task Roast_Russian_UsesTheFacts_Stores_AndRollingAgainReplaces()
    {
        var (userId, gameId) = await SeedAsync();
        _llm.Answers.Enqueue("{\"roast\":\"Nf6? Qxf7#. Dein Schachhirn ist ein verlassener Bunker.\"}");
        _llm.Answers.Enqueue("{\"roast\":\"Zweiter Versuch: g6 wäre gegangen, Genie.\"}");
        var service = Service();

        var first = await service.RoastAsync(userId, gameId, "russian", "de");
        Assert.Null(first.Reason);
        Assert.Equal("russian", first.Roast!.Style);
        var (system, facts) = _llm.Calls[0];
        Assert.Contains("merciless Soviet chess trainer", system);
        Assert.Contains("openly question the player's mental capacity", system);
        Assert.Contains("never attack ethnicity, nationality, religion, gender, sexuality or disability", system);
        Assert.Contains("Write in\nGerman", system.Replace("\r", ""));
        Assert.Contains("The player played black and lost.", facts);
        Assert.Contains("Accuracy: White 92 %, Black 41 %.", facts);
        Assert.Contains("3... Nf6 (blunder", facts);
        Assert.Contains("better was g6; punished by Qxf7#", facts);

        var second = await service.RoastAsync(userId, gameId, "russian", "de");
        Assert.Equal("Zweiter Versuch: g6 wäre gegangen, Genie.", second.Roast!.Text);
        var row = await _db.GameRoasts.SingleAsync();
        Assert.Equal(second.Roast.Text, row.Text);

        var all = await service.GetAsync(userId, gameId, "de");
        Assert.True(all!.Available);
        Assert.True(all.HasAnalysis);
        Assert.Single(all.Items);
    }

    [Fact]
    public async Task PieceLetters_StoredInEnglish_ShownInTheLanguage_WithoutConvertingTwice()
    {
        var (userId, gameId) = await SeedAsync();
        _llm.Answers.Enqueue("{\"roast\":\"Nf6? Qxf7#. Dein Schachhirn ist ein verlassener Bunker.\"}");
        _llm.Answers.Enqueue("{\"roast\":\"Nf6? Qxf7#. Bravo.\"}");
        var service = Service();

        Assert.Equal("Sf6? Dxf7#. Dein Schachhirn ist ein verlassener Bunker.",
            (await service.RoastAsync(userId, gameId, "friendly", "de")).Roast!.Text);
        Assert.Equal("Sf6? Dxf7#. Dein Schachhirn ist ein verlassener Bunker.",
            Assert.Single((await service.GetAsync(userId, gameId, "de"))!.Items).Text);
        Assert.Equal("Nf6? Qxf7#. Dein Schachhirn ist ein verlassener Bunker.", (await _db.GameRoasts.SingleAsync()).Text);

        // Französisch: der Springer heißt C, die Dame D — und jedes Lesen setzt auf dem englischen Text auf. Gespeichert
        // umgestellt, machte das nächste Lesen aus einem „R" (roi, König) einen Turm.
        Assert.Equal("Cf6? Dxf7#. Bravo.", (await service.RoastAsync(userId, gameId, "friendly", "fr")).Roast!.Text);
        Assert.Equal("Cf6? Dxf7#. Bravo.", Assert.Single((await service.GetAsync(userId, gameId, "fr"))!.Items).Text);
        Assert.Equal("Cf6? Dxf7#. Bravo.", Assert.Single((await service.GetAsync(userId, gameId, "fr"))!.Items).Text);
    }

    [Fact]
    public async Task Roast_InventedMove_IsRetried_ThenFails()
    {
        var (userId, gameId) = await SeedAsync();
        _llm.Answers.Enqueue("{\"roast\":\"Warum nicht Bb5?\"}");
        _llm.Answers.Enqueue("{\"roast\":\"Ra8 war Pflicht.\"}");

        var result = await Service().RoastAsync(userId, gameId, "cheeky", "en");

        Assert.Equal("failed", result.Reason);
        Assert.Equal(2, _llm.Calls.Count);
        Assert.Empty(_db.GameRoasts);
    }

    [Fact]
    public async Task Roast_Refusals()
    {
        var (userId, gameId) = await SeedAsync();
        var (u2, g2) = await SeedAsync(withAnalysis: false);
        var service = Service();

        Assert.Equal("invalidStyle", (await service.RoastAsync(userId, gameId, "polite", "de")).Reason);
        Assert.Equal("notFound", (await service.RoastAsync(userId + 99, gameId, "friendly", "de")).Reason);
        Assert.Equal("noAnalysis", (await service.RoastAsync(u2, g2, "friendly", "de")).Reason);
        Assert.Null(await service.GetAsync(userId + 99, gameId, "de"));

        for (var i = 0; i < GameRoastService.MaxPerDay; i++)
            _db.GameRoasts.Add(new GameRoast { SavedGameId = gameId, Language = "x" + i, Style = "friendly", Text = "t" });
        await _db.SaveChangesAsync();
        Assert.Equal("dailyLimit", (await service.RoastAsync(userId, gameId, "friendly", "de")).Reason);

        _llm.Local = false; // über Claude entstünden Kosten
        Assert.Equal("notConfigured", (await Service().RoastAsync(userId, gameId, "friendly", "de")).Reason);
        Assert.Empty(_llm.Calls);
    }

    [Fact]
    public async Task Controller_MapsReasonsToStatusCodes()
    {
        var (userId, gameId) = await SeedAsync();
        var c = new GameRoastController(Service())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "t")),
                },
            },
        };
        Assert.IsType<BadRequestObjectResult>((await c.Roast(gameId, "x", "de", default)).Result);
        Assert.IsType<NotFoundObjectResult>((await c.Roast(gameId + 99, "friendly", "de", default)).Result);
        Assert.IsType<NotFoundResult>((await c.Get(gameId + 99, "de")).Result);
        _llm.Answers.Enqueue("{\"roast\":\"Nf6 — mutig.\"}");
        Assert.IsType<OkObjectResult>((await c.Roast(gameId, "friendly", "de", default)).Result);
        var failed = Assert.IsType<ObjectResult>((await c.Roast(gameId, "friendly", "de", default)).Result);
        Assert.Equal(502, failed.StatusCode);
    }
}
