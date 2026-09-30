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

    private GameMoveExplanationService Service(GameExplanationJobs? jobs = null) => new(_db, _llm, jobs ?? new GameExplanationJobs(),
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
        var prompt = GameMoveExplanationService.UserPrompt(f, "black");
        Assert.Contains("Black played 3... Nf6 — a serious blunder.", prompt);
        Assert.Contains("Better for Black was g6. Engine line from the position before the move: g6 Qf3 Nf6", prompt);
        Assert.Contains("White's best answer to Nf6, with the engine line: Qxf7#", prompt);
        // Die Lage in Worten und was die Antwort konkret tut (0.572.0) — der Grund, nicht „senkt die Gewinnchancen".
        Assert.Contains("Situation: for the reader the position went from about equal to lost (getting mated)", prompt);
        Assert.Contains("Concretely, after Nf6 and White's best answer line: the line ends in checkmate (Qxf7#)", prompt);
        Assert.DoesNotContain("Winning chance", prompt);
        var system = GameMoveExplanationService.SystemPrompt("de");
        Assert.Contains("Write in German", system);
        Assert.Contains("Never give the evaluation or the winning chances themselves as the reason", system);
        Assert.Contains("\"you\" is always the reader", system);
        Assert.Contains("{\"explanation\": \"...\"}", system);
        // 0.585.1: keine ganze Engine-Linie aufsagen, kein „ahead" mitten im Deutschen.
        Assert.Contains("Name at most the first two moves of a line", system);
        Assert.Contains("every other word in German, no English", system);
    }

    /// <summary>Prod-Partie 35: 22.Txc4 dxc4 ist ein Tausch — der Auftrag darf keinen Materialgewinn behaupten.</summary>
    [Fact]
    public void Prompt_ARecaptureIsNoMaterialGain()
    {
        var f = new GameMistakes.Flaw(42, true, "mistake", 50, 34, ExplanationFactsTests.Game35Before22, "Rxc4", "c1c4",
            "Rfd1", ["Rfd1", "Bxd4", "exd4", "Rxc1", "Rxc1"], ExplanationFactsTests.Game35Answer, "0.0", "-1.2");
        var prompt = GameMoveExplanationService.UserPrompt(f, "black");
        Assert.DoesNotContain("ahead in material", prompt);
        Assert.Contains("the difference is positional", prompt);
    }

    /// <summary>„Your move 14. Nb5" für Weiß, obwohl der Besitzer Schwarz spielte — „du" ist der Leser, nicht der Ziehende.</summary>
    [Fact]
    public void Prompt_SaysWhoTheReaderIs_NotTheMover()
    {
        var f = GameMistakes.Find(Positions(), Moves.Length).Single(); // 3…Sf6?? von Schwarz

        var own = GameMoveExplanationService.UserPrompt(f, "black").Split('\n')[^1];
        Assert.Contains("reader's OWN move", own);
        Assert.Contains("White is \"your opponent\"", own);

        var opponents = GameMoveExplanationService.UserPrompt(f, "white").Split('\n')[^1];
        Assert.Contains("Reader: played White", opponents);
        Assert.Contains("reader's OPPONENT (Black)", opponents);
        Assert.Contains("Never call it \"your move\"", opponents);

        var neutral = GameMoveExplanationService.UserPrompt(f, "").Split('\n')[^1];
        Assert.Contains("do not use \"you\"", neutral);
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

    private async Task<(int UserId, int GameId, int AnalysisId)> SeedAsync(GameAnalysisStatus status = GameAnalysisStatus.Done,
        string? ownerSide = null)
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
        var game = new SavedGame
        {
            UserId = user.Id, Source = "lichess", Pgn = analysis.Pgn, ShareToken = "tok" + analysis.Id, GameAnalysisId = analysis.Id,
            White = "weiss", Black = "schwarz", OwnerSide = ownerSide,
        };
        _db.SavedGames.Add(game);
        await _db.SaveChangesAsync();
        return (user.Id, game.Id, analysis.Id);
    }

    [Fact]
    public async Task Generate_StoresTheGroundedText_AndRetriesOnceWhenAMoveIsInvented()
    {
        var (userId, gameId, _) = await SeedAsync(ownerSide: "black");
        _llm.Answers.Enqueue("{\"explanation\":\"Besser war Be7.\"}");  // erfunden → Nachfrage
        _llm.Answers.Enqueue("{\"explanation\":\"Nach Nf6 folgt Qxf7# — g6 hätte das verhindert.\"}");
        var service = Service();
        var game = (await service.OwnGameAsync(userId, gameId))!;

        var saved = await service.GenerateAsync(game, "de", CancellationToken.None);

        Assert.Equal(1, saved);
        var row = await _db.GameMoveExplanations.SingleAsync();
        Assert.Equal(5, row.Ply);
        Assert.Equal("de", row.Language);
        Assert.Equal("blunder", row.Class);
        Assert.Equal("fake-local", row.Model);
        Assert.Equal("black", row.Viewpoint);
        Assert.Equal(2, _llm.Prompts.Count);
        Assert.Contains("reader's OWN move", _llm.Prompts[0]);
        Assert.Contains("not in the lines above", _llm.Prompts[1]);

        // Ein zweiter Lauf erzeugt nichts doppelt.
        Assert.Equal(0, await service.GenerateAsync(game, "de", CancellationToken.None));
    }

    [Fact]
    public async Task Viewpoint_IsTheOwnersSide_FromTheProfileName_OrNeutral()
    {
        var service = Service();
        var (u1, g1, a1) = await SeedAsync();
        Assert.Equal(new GameMoveExplanationService.ExplainedGame(a1, ""), await service.OwnGameAsync(u1, g1));

        // Ohne festgelegte Seite: der lichess-Name im Profil (wie die Partienliste).
        _db.UserProfiles.Add(new UserProfile { UserId = u1, LichessUsername = "Schwarz" });
        await _db.SaveChangesAsync();
        Assert.Equal("black", (await service.OwnGameAsync(u1, g1))!.Viewpoint);
        Assert.Equal("black", (await service.SharedGameAsync("tok" + a1))!.Viewpoint);

        // Selbst festgelegt schlägt den Namen.
        var game = await _db.SavedGames.SingleAsync(g => g.Id == g1);
        game.OwnerSide = "white";
        await _db.SaveChangesAsync();
        Assert.Equal("white", (await service.OwnGameAsync(u1, g1))!.Viewpoint);
    }

    [Fact]
    public async Task OtherSide_TextIsHidden_AndRewrittenFromTheNewView()
    {
        var (userId, gameId, analysisId) = await SeedAsync(ownerSide: "white");
        var service = Service();
        _llm.Answers.Enqueue("{\"explanation\":\"Nach Nf6 setzt du mit Qxf7# matt.\"}");
        Assert.Equal(1, await service.GenerateAsync((await service.OwnGameAsync(userId, gameId))!, "de", CancellationToken.None));
        Assert.Contains("reader's OPPONENT (Black)", _llm.Prompts[0]);
        Assert.Equal("white", (await _db.GameMoveExplanations.SingleAsync()).Viewpoint);

        // Der Besitzer stellt klar, dass er Schwarz war: der alte Text passt nicht mehr.
        var game = await _db.SavedGames.SingleAsync(g => g.Id == gameId);
        game.OwnerSide = "black";
        await _db.SaveChangesAsync();
        var black = (await service.OwnGameAsync(userId, gameId))!;
        var state = await service.GetAsync(black, "de", owner: true);
        Assert.Empty(state.Items);
        Assert.True(state.CanGenerate);

        _llm.Answers.Enqueue("{\"explanation\":\"Nf6 lässt Qxf7# zu; g6 hält.\"}");
        Assert.Equal(1, await service.GenerateAsync(black, "de", CancellationToken.None));
        var row = await _db.GameMoveExplanations.AsNoTracking().SingleAsync(e => e.GameAnalysisId == analysisId);
        Assert.Equal("black", row.Viewpoint);
        Assert.Equal("Nf6 lässt Qxf7# zu; g6 hält.", row.Text);   // gespeichert in englischer Notation …
        // … gezeigt mit den Figurenbuchstaben der Sprache (0.541.1).
        Assert.Equal("Sf6 lässt Dxf7# zu; g6 hält.", Assert.Single((await service.GetAsync(black, "de", owner: true)).Items).Text);
    }

    // ── Meisterkommentar zur selben Stellung (0.542.0) ──────────────────────────────────────────────

    private async Task<LibraryGame> MasterGameAsync(string comment)
    {
        var g = new LibraryGame
        {
            OpeningLine = "e4 e5 Qh5 Nc6 Bc4 Nf6 Qxf7", White = "Anderssen", Black = "Kieseritzky", Event = "London",
            PlayedOn = new DateOnly(1851, 6, 21), Annotator = "Steinitz", Score = 80, CommentedPlies = 1, Languages = "en",
            Pgn = "[White \"Anderssen\"]\n[Black \"Kieseritzky\"]\n\n1. e4 e5 2. Qh5 Nc6 3. Bc4 Nf6 {" + comment + "} 4. Qxf7# 1-0",
        };
        _db.LibraryGames.Add(g);
        await _db.SaveChangesAsync();
        return g;
    }

    [Fact]
    public async Task MasterComment_OfTheSamePosition_GoesIntoThePrompt_AndIsShownWithItsSource()
    {
        var (userId, gameId, _) = await SeedAsync(ownerSide: "black");
        var master = await MasterGameAsync("The classic blunder: queen and bishop both hit f7, and nothing covers it.");
        _llm.Answers.Enqueue("{\"explanation\":\"Nf6 lässt Qxf7# zu — wie der Kommentator schreibt, hängt f7.\"}");
        var service = Service();
        var game = (await service.OwnGameAsync(userId, gameId))!;

        Assert.Equal(1, await service.GenerateAsync(game, "de", CancellationToken.None));

        Assert.Contains("A master game reached exactly this position: Anderssen – Kieseritzky, London 1851 (annotated by Steinitz). "
            + "Its annotator wrote here: \"3...Nf6: The classic blunder", _llm.Prompts[0]);
        Assert.Contains("never moves from the comment", _llm.Prompts[0]);
        // Die Seite der Leser-Zeile bleibt die letzte (die Systemanweisung verlässt sich darauf).
        Assert.StartsWith("Reader:", _llm.Prompts[0].Split('\n')[^1]);
        Assert.Equal(master.Id, (await _db.GameMoveExplanations.SingleAsync()).MasterLibraryGameId);

        var item = Assert.Single((await service.GetAsync(game, "de", owner: true)).Items);
        Assert.Equal(("Anderssen", "Kieseritzky", "London", (int?)1851, "Steinitz"),
            (item.Master!.White, item.Master.Black, item.Master.Event, item.Master.Year, item.Master.Annotator));
        Assert.StartsWith("3...Nf6: The classic blunder", item.Master.Text);
    }

    [Fact]
    public async Task MasterComment_TextBorrowsAMoveFromIt_AskedAgainWithoutIt_AndNoSourceIsClaimed()
    {
        var (userId, gameId, _) = await SeedAsync(ownerSide: "black");
        await MasterGameAsync("A blunder; the calm 3...Qe7 was necessary, since f7 needs a defender.");
        _llm.Answers.Enqueue("{\"explanation\":\"Besser war Qe7, wie der Meister schreibt.\"}");   // Qe7 steht in keiner Linie
        _llm.Answers.Enqueue("{\"explanation\":\"Nf6 lässt Qxf7# zu; g6 hätte gehalten.\"}");
        var service = Service();

        Assert.Equal(1, await service.GenerateAsync((await service.OwnGameAsync(userId, gameId))!, "de", CancellationToken.None));

        Assert.Contains("A master game reached", _llm.Prompts[0]);
        Assert.DoesNotContain("A master game reached", _llm.Prompts[1]);
        var row = await _db.GameMoveExplanations.SingleAsync();
        Assert.Null(row.MasterLibraryGameId);
        Assert.Null(row.MasterText);
    }

    [Fact]
    public async Task Generate_TwiceInvented_StoresNothing()
    {
        var (_, _, analysisId) = await SeedAsync();
        _llm.Answers.Enqueue("{\"explanation\":\"Be7!\"}");
        _llm.Answers.Enqueue("{\"explanation\":\"Nd4 war besser.\"}");

        Assert.Equal(0, await Service().GenerateAsync(new(analysisId, ""), "de", CancellationToken.None));
        Assert.Empty(_db.GameMoveExplanations);
    }

    /// <summary>„Von −4 auf −6 muss das nicht kommentiert werden" (2026-09-28): wer vorher schon auf Verlust stand, bekommt
    /// keine Erklärung — das Modell wird gar nicht gefragt.</summary>
    [Fact]
    public async Task Generate_AlreadyLostBeforeAndAfter_IsNotExplained()
    {
        var (userId, gameId, analysisId) = await SeedAsync(ownerSide: "black");
        var before = await _db.GameAnalysisPositions.SingleAsync(p => p.GameAnalysisId == analysisId && p.Ply == 5);
        before.CandidatesJson = """[{"uci":"g7g6","cp":-500},{"uci":"g8f6","mate":-1}]""";   // Schwarz steht schon −5
        await _db.SaveChangesAsync();
        var flaw = Assert.Single(GameMistakes.Find(await _db.GameAnalysisPositions.Where(p => p.GameAnalysisId == analysisId).ToListAsync(), Moves.Length));
        Assert.Equal(5, flaw.Ply);   // ein Fehler bleibt es …
        var service = Service();

        Assert.Equal(0, await service.GenerateAsync((await service.OwnGameAsync(userId, gameId))!, "de", CancellationToken.None));
        Assert.Empty(_llm.Prompts);   // … erklärt wird er nicht
        Assert.Empty(_db.GameMoveExplanations);
    }

    /// <summary>Texte einer älteren Fassung des Auftrags (vor 0.572.0: „senkt die Gewinnchancen") zeigt die Seite nicht mehr,
    /// der Besitzer bekommt den Knopf wieder, und das Erzeugen ersetzt sie.</summary>
    [Fact]
    public async Task OlderRevision_IsHidden_AndReplaced()
    {
        var (userId, gameId, analysisId) = await SeedAsync(ownerSide: "black");
        _db.GameMoveExplanations.Add(new GameMoveExplanation
        {
            GameAnalysisId = analysisId, Ply = 5, Language = "de", Class = "blunder", Viewpoint = "black", Revision = 0,
            Text = "Nf6 war ein Fehler, weil es deine Gewinnchancen senkt.",
        });
        await _db.SaveChangesAsync();
        var service = Service();
        var game = (await service.OwnGameAsync(userId, gameId))!;

        var state = await service.GetAsync(game, "de", owner: true);
        Assert.Empty(state.Items);
        Assert.True(state.CanGenerate);

        _llm.Answers.Enqueue("{\"explanation\":\"Nach Nf6 setzt Weiß mit Qxf7# matt; g6 hätte das verhindert.\"}");
        Assert.Equal(1, await service.GenerateAsync(game, "de", CancellationToken.None));
        var row = await _db.GameMoveExplanations.AsNoTracking().SingleAsync();
        Assert.Equal(GameMoveExplanationService.CurrentRevision, row.Revision);
        Assert.StartsWith("Nach Nf6", row.Text);
    }

    [Fact]
    public async Task OnlyOnOwnHardware_ClaudeWouldCostMoney()
    {
        var (_, _, analysisId) = await SeedAsync();
        _llm.Local = false;
        var service = Service();
        Assert.False(service.Available);
        Assert.Equal(0, await service.GenerateAsync(new(analysisId, ""), "de", CancellationToken.None));
        Assert.Empty(_llm.Prompts);
    }

    [Fact]
    public async Task Get_OwnerMayGenerate_SharedViewerOnlyReads_UnfinishedAnalysisNot()
    {
        var (userId, gameId, analysisId) = await SeedAsync();
        _db.GameMoveExplanations.Add(new GameMoveExplanation
        {
            GameAnalysisId = analysisId, Ply = 5, Language = "de", Class = "blunder", Text = "t",
            Revision = GameMoveExplanationService.CurrentRevision,
        });
        await _db.SaveChangesAsync();
        var service = Service();

        var own = await service.GetAsync(await service.OwnGameAsync(userId, gameId), "de-AT", owner: true);
        Assert.True(own.CanGenerate);
        Assert.Equal("de", own.Language);
        Assert.Equal("t", Assert.Single(own.Items).Text);

        var shared = await service.GetAsync(await service.SharedGameAsync("tok" + analysisId), "de", owner: false);
        Assert.False(shared.CanGenerate);
        Assert.Single(shared.Items);

        Assert.Null(await service.OwnGameAsync(userId + 1, gameId)); // fremde Partie

        var (u2, g2, _) = await SeedAsync(GameAnalysisStatus.Running);
        Assert.False((await service.GetAsync(await service.OwnGameAsync(u2, g2), "de", owner: true)).CanGenerate);
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

    // ── Deckel je Konto (A6-005) ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Jobs_HoechstensZweiAuftraegeJeKonto_GleichesPaarIstKeinNeuer_AndereKontenUnberuehrt()
    {
        var jobs = new GameExplanationJobs();
        Assert.Equal(GameExplanationJobs.StartResult.Started, jobs.TryStartFor(1, 10, "de"));
        Assert.Equal(GameExplanationJobs.StartResult.Started, jobs.TryStartFor(1, 11, "fr"));
        Assert.Equal(GameExplanationJobs.StartResult.UserLimit, jobs.TryStartFor(1, 12, "de"));
        Assert.False(jobs.IsRunning(12, "de"));
        // Derselbe Auftrag noch einmal: läuft schon, auch am Deckel — kein 429 für einen Doppelklick.
        Assert.Equal(GameExplanationJobs.StartResult.AlreadyRunning, jobs.TryStartFor(1, 10, "de"));
        Assert.Equal(GameExplanationJobs.StartResult.Started, jobs.TryStartFor(2, 20, "de"));

        jobs.FinishFor(1, 10, "de");
        Assert.False(jobs.IsRunning(10, "de"));
        Assert.Equal(GameExplanationJobs.StartResult.Started, jobs.TryStartFor(1, 12, "de"));
        Assert.Equal(GameExplanationJobs.StartResult.UserLimit, jobs.TryStartFor(1, 13, "de"));
        // Ein Lauf nach der Analyse (Scheduler) auf demselben Paar blockiert den Knopf, zählt aber nicht zum Deckel.
        Assert.True(jobs.TryStart(30, "de"));
        Assert.Equal(GameExplanationJobs.StartResult.AlreadyRunning, jobs.TryStartFor(2, 30, "de"));
    }

    [Fact]
    public async Task Controller_DritterParallelerAuftragDesselbenKontos_429()
    {
        var (userId, gameId, analysisId) = await SeedAsync();
        var (otherId, otherGameId, _) = await SeedAsync();
        var jobs = new GameExplanationJobs();
        GameExplanationController Controller(int uid) => new(Service(jobs))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, uid.ToString())], "t")),
                },
            },
        };
        // Zwei Aufträge desselben Kontos laufen schon (andere Partien bzw. Sprachen).
        Assert.Equal(GameExplanationJobs.StartResult.Started, jobs.TryStartFor(userId, 990001, "de"));
        Assert.Equal(GameExplanationJobs.StartResult.Started, jobs.TryStartFor(userId, analysisId, "fr"));

        var r = Assert.IsType<ObjectResult>((await Controller(userId).Generate(gameId, "de")).Result);
        Assert.Equal(429, r.StatusCode);
        Assert.False(jobs.IsRunning(analysisId, "de"));
        Assert.Empty(_llm.Prompts);

        // Ein anderes Konto ist davon nicht betroffen.
        Assert.IsType<OkObjectResult>((await Controller(otherId).Generate(otherGameId, "de")).Result);

        // Ist einer fertig, geht der nächste.
        jobs.FinishFor(userId, 990001, "de");
        Assert.IsType<OkObjectResult>((await Controller(userId).Generate(gameId, "de")).Result);
    }
}
