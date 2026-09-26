using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>„Kurz erzählt" (0.541.0): die Partie in zwei, drei Sätzen für Link-Vorschau und Partieseite — aus Kopfdaten,
/// Verlauf nach der Kurve und Wendepunkten, nur über eigene Hardware, geprüfte Züge.</summary>
public class GameRecapTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FakeLlm _llm = new();

    public GameRecapTests()
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
        public List<(string Purpose, string System, string User)> Calls { get; } = new();
        public Task<string?> GenerateHintsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> TranslateCommentsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> CompleteJsonAsync(string purpose, string system, string userPrompt, JsonNode schema, int maxTokens, CancellationToken ct = default)
        {
            Calls.Add((purpose, system, userPrompt));
            return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : null);
        }
    }

    private sealed class RecordingScheduler : IGameReviewTextScheduler
    {
        public List<int> Recaps { get; } = new();
        public void Schedule(int analysisId, bool refined) { }
        public void ScheduleRecap(int savedGameId) => Recaps.Add(savedGameId);
    }

    private GameRecapService Service() => new(_db, _llm, TestServices.SavedGames(_db), NullLogger<GameRecapService>.Instance);

    // Schäfermatt: 3…Sf6?? ist der Wendepunkt, 4.Dxf7# das Ende.
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

    private const string Pgn = "[White \"Ich\"]\n[Black \"Gegner\"]\n[Result \"1-0\"]\n[Opening \"Scholar's Mate\"]\n\n"
        + "1. e4 e5 2. Qh5 Nc6 3. Bc4 Nf6 4. Qxf7# 1-0";

    private async Task<(int UserId, int GameId)> SeedAsync(bool withAnalysis = true, string? reviewLanguage = "de")
    {
        var user = new AppUser { Username = "u" + Guid.NewGuid().ToString("N")[..8], PasswordHash = "x" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        int? analysisId = null;
        if (withAnalysis)
        {
            var analysis = new GameAnalysis
            {
                UserId = user.Id, Pgn = Pgn, StartFen = "startpos", Status = GameAnalysisStatus.Done, PlyCount = Plies.Length,
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
            UserId = user.Id, Source = "lichess", Pgn = Pgn, White = "Ich", Black = "Gegner", Result = "1-0",
            ShareToken = Guid.NewGuid().ToString("N")[..20], GameAnalysisId = analysisId, OwnerSide = "white",
            ReviewLanguage = reviewLanguage,
        };
        _db.SavedGames.Add(game);
        await _db.SaveChangesAsync();
        return (user.Id, game.Id);
    }

    [Fact]
    public async Task Write_TellsTheStoryFromTheFacts_InTheThirdPerson_AndStores()
    {
        var (userId, gameId) = await SeedAsync();
        _llm.Answers.Enqueue("{\"recap\":\"Ich greift mit 2.Qh5 früh an; Gegner übersieht nach 3...Nf6 das Matt 4.Qxf7#.\"}");

        var result = await Service().WriteAsync(userId, gameId, "de");

        Assert.Null(result.Reason);
        var (purpose, system, facts) = _llm.Calls.Single();
        Assert.Equal("recap", purpose);
        Assert.Contains("Write in German", system);
        Assert.Contains("in the third person", system);
        Assert.Contains("no engine evaluations and no winning chances", system);
        Assert.Contains("use the words for White and Black of the language you write in", system);
        Assert.Contains("Name the opening only if it is given", system);
        Assert.Contains("never a list of opening moves", system);
        Assert.Contains("White: Ich, Black: Gegner, result 1-0, 4 moves.", facts);
        Assert.Contains("Opening: Scholar's Mate.", facts);
        Assert.Contains("First moves: 1.e4 1...e5 2.Qh5 2...Nc6 3.Bc4 3...Nf6 4.Qxf7#", facts);
        Assert.Contains("Accuracy: White 92 %, Black 41 %.", facts);
        Assert.Contains("- from the start: roughly equal\n- after 3...Nf6: White is winning", facts);
        Assert.Contains("- 3...Nf6 by Black (blunder); better was 3...g6; answered by 4.Qxf7#", facts);
        Assert.Contains("The game ended in checkmate with 4.Qxf7#.", facts);

        // Geprüft und gespeichert in englischer Notation, gezeigt mit deutschen Figurenbuchstaben (das Modell stellt sie
        // nicht um, und gespeichert umgestellt machte ein zweites Umstellen im Französischen aus dem König einen Turm).
        Assert.Equal("Ich greift mit 2.Dh5 früh an; Gegner übersieht nach 3...Sf6 das Matt 4.Dxf7#.", result.Text);
        var row = await _db.GameRecaps.SingleAsync();
        Assert.Equal((gameId, "de", "fake-local"), (row.SavedGameId, row.Language, row.Model));
        Assert.Equal("Ich greift mit 2.Qh5 früh an; Gegner übersieht nach 3...Nf6 das Matt 4.Qxf7#.", row.Text);
        Assert.Equal(result.Text, (await GameRecapService.CurrentAsync(_db, gameId, "de"))!.Text);
    }

    [Fact]
    public async Task Write_KeepsAnExistingText_UnlessReplacing()
    {
        var (userId, gameId) = await SeedAsync();
        _llm.Answers.Enqueue("{\"recap\":\"Erste Fassung.\"}");
        _llm.Answers.Enqueue("{\"recap\":\"Nach der Vertiefung.\"}");
        var service = Service();

        Assert.Null((await service.WriteAsync(userId, gameId, "de")).Reason);
        Assert.Equal("exists", (await service.WriteAsync(userId, gameId, "de")).Reason);
        Assert.Single(_llm.Calls);

        Assert.Equal("Nach der Vertiefung.", (await service.WriteAsync(userId, gameId, "de", replace: true)).Text);
        Assert.Equal("Nach der Vertiefung.", (await _db.GameRecaps.AsNoTracking().SingleAsync()).Text);
    }

    [Fact]
    public async Task Write_InventedMove_OrTooLong_IsRetried_ThenFails()
    {
        var (userId, gameId) = await SeedAsync();
        _llm.Answers.Enqueue("{\"recap\":\"Nach 3.Bb5 war alles klar.\"}");
        _llm.Answers.Enqueue("{\"recap\":\"" + new string('x', GameRecapService.MaxLength + 1) + "\"}");

        var result = await Service().WriteAsync(userId, gameId, "de");

        Assert.Equal("failed", result.Reason);
        Assert.Equal(2, _llm.Calls.Count);
        Assert.Contains("IMPORTANT: at most 50 words", _llm.Calls[1].User);
        Assert.Empty(_db.GameRecaps);
    }

    [Fact]
    public async Task Write_Refusals()
    {
        var (userId, gameId) = await SeedAsync();
        var (u2, g2) = await SeedAsync(withAnalysis: false);

        Assert.Equal("notFound", (await Service().WriteAsync(userId + 99, gameId, "de")).Reason);
        Assert.Equal("noAnalysis", (await Service().WriteAsync(u2, g2, "de")).Reason);
        _llm.Local = false; // über Claude entstünden Kosten
        Assert.Equal("notConfigured", (await Service().WriteAsync(userId, gameId, "de")).Reason);
        Assert.Empty(_llm.Calls);
    }

    // ── Verlauf nach der Kurve ─────────────────────────────────────────────────────────────────

    /// <summary>Stellungen mit vorgegebener Bewertung VOR jedem Halbzug (Weiß-Sicht, Centipawns); der Zugzähler steht
    /// in der FEN, die Kandidaten aus Sicht der Seite am Zug wie vom Broker.</summary>
    private static List<GameAnalysisPosition> Positions(params int[] whiteCpBefore)
        => whiteCpBefore.Select((cp, i) =>
        {
            var white = i % 2 == 0;
            return new GameAnalysisPosition
            {
                Ply = i, Fen = $"7k/8/8/8/8/8/8/K7 {(white ? "w" : "b")} - - 0 {i / 2 + 1}", GameMoveUci = "a1b1", GameMoveSan = "Kb" + (i % 8 + 1),
                CandidatesJson = $$"""[{"uci":"a1b1","cp":{{(white ? cp : -cp)}}}]""", Depth = 20,
            };
        }).ToList();

    [Fact]
    public void Course_OneMoveBlipBetweenTwoEqualStretches_IsSmoothedAway()
    {
        // Nach Halbzug 2 kurz +6 (der Schlag vor dem Zurückschlagen), dann wieder gleich; ab Halbzug 5 gewinnt Schwarz.
        var course = GameRecapService.Course(Positions(0, 0, 0, 600, 0, 0, -600, -600), 8);

        Assert.Equal(["from the start: roughly equal", "after 3...Kb6: Black is winning"], course);
    }

    [Fact]
    public void Course_ManySwings_KeepsTheStartAndTheLatestSegments()
    {
        // Je zwei Halbzüge ein Lagewechsel: gleich / Weiß besser / gleich / Schwarz besser … — zwölf Abschnitte.
        var cps = Enumerable.Range(0, 24).Select(i => (i / 2 % 4) switch { 1 => 200, 3 => -200, _ => 0 }).ToArray();

        var course = GameRecapService.Course(Positions(cps), cps.Length);

        Assert.Equal(GameRecapService.MaxCourseSegments, course.Count);
        Assert.Equal("from the start: roughly equal", course[0]);
        Assert.Equal("(several more swings)", course[1]);
    }

    [Theory]
    [InlineData("r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3", "3.Bb5 a6 4.Ba4")]
    [InlineData("r1bqkbnr/pppp1ppp/2n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3", "3...a6 4.Ba4 Nf6")]
    public void Numbered_WritesMoveNumbersLikeAPerson(string fen, string expected)
    {
        var sans = fen.Contains(" w ") ? new[] { "Bb5", "a6", "Ba4" } : new[] { "a6", "Ba4", "Nf6" };
        Assert.Equal(expected, GameRecapService.Numbered(fen, sans));
    }

    [Theory]
    [InlineData(80, 2)]
    [InlineData(79.9, 1)]
    [InlineData(60, 1)]
    [InlineData(50, 0)]
    [InlineData(40, -1)]
    [InlineData(20, -2)]
    public void StandingOf_Bands(double whiteWin, int expected) => Assert.Equal(expected, GameRecapService.StandingOf(whiteWin));

    [Theory]
    [InlineData("[Opening \"Sicilian Defense: Najdorf Variation\"]", "Sicilian Defense: Najdorf Variation")]
    [InlineData("[ECOUrl \"https://www.chess.com/openings/Queens-Gambit-Declined-Exchange-Variation\"]", "Queens Gambit Declined Exchange Variation")]
    [InlineData("[Opening \"?\"]", null)]
    [InlineData("[Event \"x\"]", null)]
    public void OpeningName_LichessHeader_OrChessComUrl(string header, string? expected)
        => Assert.Equal(expected, GameRecapService.OpeningName(header + "\n\n1. e4 *"));

    [Theory]
    [InlineData("[Termination \"Hikaru won on time\"]", "1-0", "How it ended: Hikaru won on time.")]
    [InlineData("[Termination \"Normal\"]", "0-1", "Black won (no checkmate on the board — not stated whether by resignation or on time).")]
    [InlineData("", "1/2-1/2", "The game was drawn.")]
    public void Ending_WithoutMate_FromTheHeaderOrTheResult(string header, string result, string expected)
    {
        var game = new SavedGameDetailDto { Pgn = header + "\n\n1. e4 e5 " + result, Result = result };
        Assert.Equal(expected, GameRecapService.Ending(game, Positions(0, 0)));
    }

    // ── Lesen ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SharedGame_CarriesTheRecapInTheGamesLanguage_ElseTheNewest()
    {
        var (_, gameId) = await SeedAsync(reviewLanguage: "de");
        _db.GameRecaps.AddRange(
            new GameRecap { SavedGameId = gameId, Language = "en", Text = "English, newer", CreatedAt = DateTime.UtcNow },
            new GameRecap { SavedGameId = gameId, Language = "de", Text = "Deutsch", CreatedAt = DateTime.UtcNow.AddHours(-1) });
        await _db.SaveChangesAsync();
        var token = await _db.SavedGames.Where(g => g.Id == gameId).Select(g => g.ShareToken).SingleAsync();

        Assert.Equal("Deutsch", (await TestServices.SavedGames(_db).GetSharedAsync(token))!.Recap);

        var game = await _db.SavedGames.SingleAsync(g => g.Id == gameId);
        game.ReviewLanguage = "hr";
        await _db.SaveChangesAsync();
        Assert.Equal("English, newer", (await TestServices.SavedGames(_db).GetSharedAsync(token))!.Recap);
    }

    [Fact]
    public async Task Controller_MissingRecapWithDoneAnalysis_IsScheduled_AndPending()
    {
        var (userId, gameId) = await SeedAsync();
        var (u2, g2) = await SeedAsync(withAnalysis: false);
        var scheduler = new RecordingScheduler();
        GameRecapController Controller(int user) => new(Service(), scheduler)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], "t")),
                },
            },
        };

        var missing = Assert.IsType<GameRecapDto>(Assert.IsType<OkObjectResult>((await Controller(userId).Get(gameId, default)).Result).Value);
        Assert.True(missing.Pending);
        Assert.Equal([gameId], scheduler.Recaps);

        // Ohne Analyse gibt es nichts zu erzählen — kein Auftrag.
        var none = Assert.IsType<GameRecapDto>(Assert.IsType<OkObjectResult>((await Controller(u2).Get(g2, default)).Result).Value);
        Assert.False(none.Pending);
        Assert.Single(scheduler.Recaps);

        _db.GameRecaps.Add(new GameRecap { SavedGameId = gameId, Language = "de", Text = "Fertig." });
        await _db.SaveChangesAsync();
        var done = Assert.IsType<GameRecapDto>(Assert.IsType<OkObjectResult>((await Controller(userId).Get(gameId, default)).Result).Value);
        Assert.Equal(("Fertig.", false), (done.Text, done.Pending));
        Assert.Single(scheduler.Recaps);

        Assert.IsType<NotFoundResult>((await Controller(u2).Get(gameId, default)).Result);
    }
}
