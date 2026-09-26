using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Texte zur Partie direkt nach der Analyse (0.540.0): erst die Fehler-Erklärungen aus Sicht des Besitzers, dann die
/// drei Roasts — in der Sprache der Partie, nach der Vertiefung die Erklärungen noch einmal.
/// </summary>
public class GameReviewTextsTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FakeLlm _llm = new();
    private readonly GameExplanationJobs _jobs = new();

    public GameReviewTextsTests()
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
        public List<(string Purpose, string System, string User)> Calls { get; } = new();
        public Task<string?> GenerateHintsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> TranslateCommentsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> CompleteJsonAsync(string purpose, string system, string userPrompt, JsonNode schema, int maxTokens, CancellationToken ct = default)
        {
            Calls.Add((purpose, system, userPrompt));
            // Keine Züge im Text — so besteht jede Antwort die Prüfung auf erfundene Züge.
            return Task.FromResult<string?>(purpose == "roast"
                ? "{\"roast\":\"Roast " + Calls.Count + "\"}"
                : "{\"explanation\":\"Erklärung " + Calls.Count + "\"}");
        }
    }

    private GameMoveExplanationService Explanations() => new(_db, _llm, _jobs,
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        NullLogger<GameMoveExplanationService>.Instance);

    private GameReviewTexts Service() => new(_db, Explanations(),
        new GameRoastService(_db, _llm, TestServices.SavedGames(_db), NullLogger<GameRoastService>.Instance),
        _jobs, NullLogger<GameReviewTexts>.Instance);

    // Schäfermatt: 3…Sf6?? ist der einzige Fehler — ein Zug von SCHWARZ.
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

    private async Task<(int UserId, int GameId, int AnalysisId)> SeedAsync(string? ownerSide = "white", string? reviewLanguage = "de",
        bool link = true, int? userId = null)
    {
        if (userId == null)
        {
            var user = new AppUser { Username = "u" + Guid.NewGuid().ToString("N")[..8], PasswordHash = "x" };
            _db.AppUsers.Add(user);
            await _db.SaveChangesAsync();
            userId = user.Id;
        }
        const string pgn = "[White \"Ich\"]\n[Black \"Gegner\"]\n[Result \"1-0\"]\n\n1. e4 e5 2. Qh5 Nc6 3. Bc4 Nf6 4. Qxf7# 1-0";
        var analysis = new GameAnalysis
        {
            UserId = userId.Value, Pgn = pgn, StartFen = "startpos", Status = GameAnalysisStatus.Done, PlyCount = Plies.Length,
            TargetDepth = 20, Origin = GameAnalysisOrigin.SavedGame, AccuracyWhite = 92, AccuracyBlack = 41,
        };
        for (var i = 0; i < Plies.Length; i++)
            analysis.Positions.Add(new GameAnalysisPosition
            {
                Ply = i, Fen = Plies[i].Fen, GameMoveUci = Plies[i].Uci, GameMoveSan = Plies[i].San, CandidatesJson = Plies[i].Cands, Depth = 20,
            });
        _db.GameAnalyses.Add(analysis);
        await _db.SaveChangesAsync();
        var game = new SavedGame
        {
            UserId = userId.Value, Source = "lichess", Pgn = pgn, White = "Ich", Black = "Gegner", Result = "1-0",
            ShareToken = Guid.NewGuid().ToString("N")[..20], GameAnalysisId = link ? analysis.Id : null,
            OwnerSide = ownerSide, ReviewLanguage = reviewLanguage,
        };
        _db.SavedGames.Add(game);
        await _db.SaveChangesAsync();
        return (userId.Value, game.Id, analysis.Id);
    }

    [Fact]
    public async Task AfterTheAnalysis_FirstTheExplanationsFromTheOwnersView_ThenTheThreeRoasts()
    {
        var (_, gameId, analysisId) = await SeedAsync(ownerSide: "white", reviewLanguage: "de");

        await Service().WriteAsync(analysisId, refined: false, CancellationToken.None);

        Assert.Equal(["explanation", "roast", "roast", "roast"], _llm.Calls.Select(c => c.Purpose));
        // Der Fehler ist der des GEGNERS: der Besitzer (Weiß) liest „dein Gegner", nicht „dein Zug".
        Assert.Contains("reader's OPPONENT (Black)", _llm.Calls[0].User);
        Assert.Contains("Explain in German", _llm.Calls[0].System);
        var explanation = await _db.GameMoveExplanations.SingleAsync();
        Assert.Equal(("de", "white", 5), (explanation.Language, explanation.Viewpoint, explanation.Ply));

        var roasts = await _db.GameRoasts.OrderBy(r => r.Style).ToListAsync();
        Assert.Equal(["cheeky", "friendly", "russian"], roasts.Select(r => r.Style));
        Assert.All(roasts, r => { Assert.Equal(gameId, r.SavedGameId); Assert.Equal("de", r.Language); Assert.True(r.Automatic); });

        // Ein zweiter Anstoß schreibt nichts doppelt.
        await Service().WriteAsync(analysisId, refined: false, CancellationToken.None);
        Assert.Equal(4, _llm.Calls.Count);
    }

    [Fact]
    public async Task AfterRefining_TheExplanationsAreRewrittenInEveryLanguage_TheRoastsStay()
    {
        var (_, _, analysisId) = await SeedAsync(reviewLanguage: "de");
        await Service().WriteAsync(analysisId, refined: false, CancellationToken.None);
        // Dazu eine Erklärung, die der Besitzer per Knopf auf Englisch geholt hat.
        _db.GameMoveExplanations.Add(new GameMoveExplanation
        {
            GameAnalysisId = analysisId, Ply = 5, Language = "en", Class = "blunder", Viewpoint = "white", Text = "old en",
        });
        await _db.SaveChangesAsync();
        var roastTexts = await _db.GameRoasts.OrderBy(r => r.Style).Select(r => r.Text).ToListAsync();
        _llm.Calls.Clear();

        await Service().WriteAsync(analysisId, refined: true, CancellationToken.None);

        Assert.Equal(["explanation", "explanation"], _llm.Calls.Select(c => c.Purpose));
        var rows = await _db.GameMoveExplanations.AsNoTracking().OrderBy(e => e.Language).ToListAsync();
        Assert.Equal(["de", "en"], rows.Select(r => r.Language));
        Assert.DoesNotContain(rows, r => r.Text == "old en");
        Assert.Equal(roastTexts, await _db.GameRoasts.OrderBy(r => r.Style).Select(r => r.Text).ToListAsync());
    }

    [Fact]
    public async Task Language_OfTheGame_ElseTheUsersLastRemembered_ElseEnglish()
    {
        var (userId, _, first) = await SeedAsync(reviewLanguage: null);
        await Service().WriteAsync(first, refined: false, CancellationToken.None);
        Assert.Contains("Explain in English", _llm.Calls[0].System);

        await SeedAsync(reviewLanguage: "hr", link: false, userId: userId);   // von der Seite analysiert, in Kroatisch
        var (_, _, third) = await SeedAsync(reviewLanguage: null, userId: userId);   // über die Erweiterung
        _llm.Calls.Clear();
        await Service().WriteAsync(third, refined: false, CancellationToken.None);
        Assert.Contains("Explain in Croatian", _llm.Calls[0].System);
        Assert.All(await _db.GameRoasts.Where(r => r.SavedGame!.GameAnalysisId == third).ToListAsync(), r => Assert.Equal("hr", r.Language));
    }

    [Fact]
    public async Task ButtonAlreadyRunning_ExplanationsAreLeftToIt_RoastsStillCome()
    {
        var (_, _, analysisId) = await SeedAsync(reviewLanguage: "de");
        Assert.True(_jobs.TryStart(analysisId, "de"));

        await Service().WriteAsync(analysisId, refined: false, CancellationToken.None);

        Assert.Equal(["roast", "roast", "roast"], _llm.Calls.Select(c => c.Purpose));
        Assert.True(_jobs.IsRunning(analysisId, "de"));   // der fremde Lauf wird nicht beendet
    }

    [Fact]
    public async Task Nothing_WithoutOwnHardware_WithoutALinkedGame_OrBeforeTheAnalysisIsDone()
    {
        var (_, _, unlinked) = await SeedAsync(link: false);
        await Service().WriteAsync(unlinked, refined: false, CancellationToken.None);

        var (_, _, running) = await SeedAsync();
        (await _db.GameAnalyses.SingleAsync(a => a.Id == running)).Status = GameAnalysisStatus.Running;
        await _db.SaveChangesAsync();
        await Service().WriteAsync(running, refined: false, CancellationToken.None);

        var (_, _, done) = await SeedAsync();
        _llm.Local = false;
        await Service().WriteAsync(done, refined: false, CancellationToken.None);

        Assert.Empty(_llm.Calls);
        Assert.Empty(_db.GameMoveExplanations);
        Assert.Empty(_db.GameRoasts);
    }

    [Fact]
    public async Task AutomaticRoasts_IgnoreTheDailyCap_DoNotCountAgainstIt_AndNeverReplaceARolledText()
    {
        var (userId, gameId, _) = await SeedAsync(reviewLanguage: "de");
        var roasts = new GameRoastService(_db, _llm, TestServices.SavedGames(_db), NullLogger<GameRoastService>.Instance);
        // Der Nutzer hat heute schon alles gewürfelt, was er darf …
        for (var i = 0; i < GameRoastService.MaxPerDay; i++)
            _db.GameRoasts.Add(new GameRoast { SavedGameId = gameId, Style = "friendly", Language = "x" + i, Text = "t" });
        await _db.SaveChangesAsync();
        Assert.Equal("dailyLimit", (await roasts.RoastAsync(userId, gameId, "cheeky", "de")).Reason);

        // … die automatischen kommen trotzdem, ersetzen aber keinen gewürfelten Text.
        Assert.Null((await roasts.RoastAsync(userId, gameId, "cheeky", "de", automatic: true)).Reason);
        Assert.Equal("exists", (await roasts.RoastAsync(userId, gameId, "cheeky", "de", automatic: true)).Reason);

        // Automatische zählen nicht gegen den Deckel des Würfelns.
        _db.GameRoasts.RemoveRange(_db.GameRoasts.Where(r => !r.Automatic));
        await _db.SaveChangesAsync();
        for (var i = 0; i < GameRoastService.MaxPerDay - 1; i++)
            _db.GameRoasts.Add(new GameRoast { SavedGameId = gameId, Style = "friendly", Language = "y" + i, Text = "t", Automatic = true });
        await _db.SaveChangesAsync();
        var rolled = await roasts.RoastAsync(userId, gameId, "cheeky", "de");
        Assert.Null(rolled.Reason);
        Assert.False((await _db.GameRoasts.AsNoTracking().SingleAsync(r => r.Style == "cheeky" && r.Language == "de")).Automatic);
    }
}
