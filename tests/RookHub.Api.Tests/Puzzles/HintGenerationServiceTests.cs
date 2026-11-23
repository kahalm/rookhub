using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Puzzles;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Tests.Puzzles;

public class HintGenerationServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public HintGenerationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private sealed class FakeClaude : IClaudeJsonClient
    {
        public bool IsConfigured { get; set; } = true;
        public string? Json { get; set; } =
            """{"hint1":"Achte auf die Schwäche am Rand","hint2":"Deine Dame entscheidet","hint3":"Beginne mit Dame nach h8"}""";
        public int Calls { get; private set; }
        /// <summary>Antworten der Reihe nach; leer → <see cref="Json"/>.</summary>
        public Queue<string?> Responses { get; } = new();
        public List<string> Prompts { get; } = new();
        public Task<string?> GenerateHintsJsonAsync(string system, string userPrompt, CancellationToken ct = default)
        {
            Calls++;
            Prompts.Add(userPrompt);
            return Task.FromResult(Responses.Count > 0 ? Responses.Dequeue() : Json);
        }

        public Task<string?> TranslateCommentsJsonAsync(string system, string userPrompt, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public string TranslationModel => "test";
    }

    private HintGenerationService Build(IClaudeJsonClient claude)
    {
        var sf = new StockfishAnalyzer(new ConfigurationBuilder().Build(), NullLogger<StockfishAnalyzer>.Instance);
        return new HintGenerationService(_db, claude, sf, NullLogger<HintGenerationService>.Instance);
    }

    private async Task<BookPuzzle> SeedPuzzleAsync(string moves = "d1h5 e8e7 h5f7", int startPly = -1)
    {
        var bp = new BookPuzzle
        {
            LineId = "l1",
            BookFileName = "b.pgn",
            Round = "1",
            Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            Moves = moves,
            StartPly = startPly,
        };
        _db.BookPuzzles.Add(bp);
        await _db.SaveChangesAsync();
        return bp;
    }

    // ---- Lösungslinie zerlegen ----

    [Theory]
    [InlineData("e2e4 e7e5 g1f3 b8c6", -1, "", "e2e4 e7e5 g1f3 b8c6")]
    [InlineData("e2e4 e7e5 g1f3 b8c6", 0, "e2e4", "e7e5 g1f3 b8c6")]
    [InlineData("e2e4 e7e5 g1f3 b8c6", 1, "e2e4 e7e5", "g1f3 b8c6")]
    public void SetupAndSolution_SplitAtStartPly(string moves, int startPly, string expectedSetup, string expectedSolution)
    {
        Assert.Equal(expectedSetup, HintGenerationService.SetupMovesUci(moves, startPly));
        Assert.Equal(expectedSolution, string.Join(' ', HintGenerationService.SolutionUci(moves, startPly)));
    }

    // ---- Anti-Leak ----

    [Fact]
    public void ParseAndValidate_Valid_ReturnsThreeHints()
    {
        var hints = HintGenerationService.ParseAndValidate(
            """{"hint1":"Rückständiger König","hint2":"Die Dame liefert das Matt","hint3":"Dame nach h8"}""");
        Assert.NotNull(hints);
        Assert.Equal(3, hints!.Count);
        Assert.Equal("Dame nach h8", hints[2]);
    }

    [Theory]
    [InlineData("""{"hint1":"Spiele Dh8","hint2":"egal","hint3":"Dame nach h8"}""")]   // Feld in Stufe 1
    [InlineData("""{"hint1":"Motiv","hint2":"Turm nach e4","hint3":"…"}""")]            // Feld in Stufe 2
    [InlineData("""{"hint1":"Rochiere","hint2":"O-O ist stark","hint3":"…"}""")]         // Rochade in Stufe 2
    [InlineData("""{"hint1":"Motiv","hint2":"Figur","hint3":""}""")]                     // leerer Tipp
    [InlineData("""{"hint1":"Motiv"}""")]                                                 // unvollständig
    [InlineData("kein json")]
    public void ParseAndValidate_LeakOrInvalid_ReturnsNull(string json)
    {
        Assert.Null(HintGenerationService.ParseAndValidate(json));
    }

    // ---- Generierung schreibt HintsJson ----

    [Fact]
    public async Task GenerateForPuzzle_WritesAllLanguages_AndIsIdempotent()
    {
        var bp = await SeedPuzzleAsync();
        var fake = new FakeClaude();
        var svc = Build(fake);

        var ok = await svc.GenerateForPuzzleAsync(bp.Id);
        Assert.True(ok);

        var reloaded = await _db.BookPuzzles.FindAsync(bp.Id);
        Assert.Equal(HintGenerationService.CurrentHintsVersion, reloaded!.HintsVersion);
        var map = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(reloaded.HintsJson!)!;
        Assert.Equal(3, map.Count);                       // de/en/hr
        Assert.All(map.Values, v => Assert.Equal(3, v.Count));
        Assert.Equal(3, fake.Calls);                      // ein Call je Sprache

        // Zweiter Lauf: schon aktuell → kein erneuter Call.
        var again = await svc.GenerateForPuzzleAsync(bp.Id);
        Assert.False(again);
        Assert.Equal(3, fake.Calls);
    }

    [Fact]
    public async Task GenerateForPuzzle_NoApiKey_DoesNothing()
    {
        var bp = await SeedPuzzleAsync();
        var fake = new FakeClaude { IsConfigured = false };
        var svc = Build(fake);

        Assert.False(await svc.GenerateForPuzzleAsync(bp.Id));
        var reloaded = await _db.BookPuzzles.FindAsync(bp.Id);
        Assert.Null(reloaded!.HintsJson);
    }

    [Fact]
    public async Task GenerateForPuzzle_AllLanguagesLeak_WritesNothing()
    {
        var bp = await SeedPuzzleAsync();
        // Stufe 1 enthält ein Feld → wird verworfen, für alle Sprachen.
        var fake = new FakeClaude { Json = """{"hint1":"Spiele e4","hint2":"x","hint3":"y"}""" };
        var svc = Build(fake);

        Assert.False(await svc.GenerateForPuzzleAsync(bp.Id));
        var reloaded = await _db.BookPuzzles.FindAsync(bp.Id);
        Assert.Null(reloaded!.HintsJson);
    }

    // ---- Brettprüfung mit erneutem Versuch (Muster Buch 32: Damen im Vorspiel getauscht) ----

    private const string Book32Moves =
        "e2e4 e7e5 g1f3 b8c6 d2d4 e5d4 f3d4 c6d4 d1d4 d8f6 d4f6 g8f6 b1c3 f8b4 c1d2 e8g8 e1c1 d7d6 f2f3 c8e6 "
        + "a2a3 b4c5 g2g4 f8e8 h2h4 h7h6 d2e3 c5e3 c1b1 e3f4 g4g5 h6g5";

    [Fact]
    public async Task GenerateForPuzzle_InventedQueen_RetriesWithFeedback()
    {
        var bp = await SeedPuzzleAsync(Book32Moves, 29);
        const string invented = """{"hint1":"Ein Angriff auf den König","hint2":"Deine Dame entscheidet","hint3":"Bauer nach g5"}""";
        const string clean = """{"hint1":"Ein Angriff auf den König","hint2":"Dein Bauer öffnet die Linie","hint3":"Bauer nach g5"}""";
        var fake = new FakeClaude { Json = clean };
        fake.Responses.Enqueue(invented);       // de, Versuch 1 → verworfen
        var svc = Build(fake);

        Assert.True(await svc.GenerateForPuzzleAsync(bp.Id));
        Assert.Equal(4, fake.Calls);            // de zweimal, en + hr je einmal
        Assert.Contains("r3r1k1/ppp2pp1/3pbn1p/8/4PbPP/P1N2P2/1PP5/1K1R1B1R", fake.Prompts[0]);
        Assert.Contains("Your previous answer was rejected", fake.Prompts[1]);
        Assert.Contains("Deine Dame entscheidet", fake.Prompts[1]);
        var map = JsonSerializer.Deserialize<Dictionary<string, List<string>>>((await _db.BookPuzzles.FindAsync(bp.Id))!.HintsJson!)!;
        Assert.Equal("Dein Bauer öffnet die Linie", map["de"][1]);
    }

    [Fact]
    public async Task GenerateForPuzzle_InventedQueenEveryTime_DropsLanguage()
    {
        var bp = await SeedPuzzleAsync(Book32Moves, 29);
        var fake = new FakeClaude
        {
            // Ein Text, der in jeder Prüfsprache eine Dame nennt (de/en/hr).
            Json = """{"hint1":"Angriff","hint2":"Deine Dame / your queen / tvoja dama","hint3":"Pawn to g5"}""",
        };
        var svc = Build(fake);

        var ok = await svc.GenerateForPuzzleAsync(bp.Id);
        Assert.Null((await _db.BookPuzzles.FindAsync(bp.Id))!.HintsJson);
        Assert.False(ok);
        Assert.Equal(3 * HintGenerationService.MaxAttempts, fake.Calls);
        Assert.Null((await _db.BookPuzzles.FindAsync(bp.Id))!.HintsJson);
    }
}
