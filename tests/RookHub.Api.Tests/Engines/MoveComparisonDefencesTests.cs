using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers.Engines;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Engines;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests.Engines;

/// <summary>
/// Die anderen Verteidigungen in der Widerlegung eines Fehlzugs (0.971.0, <see cref="MoveComparisonDefences"/>).
/// Regressionsfall: Vereinspartie 245, Zugvergleich 2569 — 25...Lc8 (bester Zug 25...Dd6), Widerlegung 26.Txc8+. Der Text
/// zählte die Engine-Linie herunter; der Grund steht in den Nebenwegen: 26...Txc8 27.Sd7+ und 26...Kxc8 27.Tc1+ Kb8 28.Sd7+
/// gabeln König und Dame, weil der Läufer das Feld d7 verlassen hat.
/// </summary>
public class MoveComparisonDefencesTests : IDisposable
{
    /// <summary>Stellung vor 25...Lc8 (Schwarz am Zug).</summary>
    private const string Fen2569 = "1k4r1/pp1bn3/1q2pb1r/1P1pN1p1/3P2P1/P1RBQ3/4NPK1/R7 b - - 2 25";
    private const string Best = "b6d6";
    private const string Weak = "d7c8";
    /// <summary>Die Linien der Prod-Zeilen von Vergleich 2569 (gekürzt): Weiß-Sicht.</summary>
    private const string BestJson = """{"depth":30,"pvs":[{"moves":["e2g1","f6e5","d4e5","d6b6","e3b6","a7b6"],"cp":135}]}""";
    private const string WeakJson = """{"depth":30,"pvs":[{"moves":["c3c8","b8c8","a1c1","c8d8","e5f7","d8d7","f7h6","g8f8"],"cp":692}]}""";
    /// <summary>Wie die Engine die anderen Verteidigungen widerlegt (mit python-chess auf Legalität geprüft).</summary>
    private const string ForkAxb6 = """{"depth":30,"pvs":[{"moves":["e5d7","b8a8","d7b6","a7b6","e3e5"],"cp":750}]}""";
    private const string ForkNxb6 = """{"depth":30,"pvs":[{"moves":["e5d7","b8a8","d7b6","c8b6","e3e5"],"cp":770}]}""";
    private const string Nc6Json = """{"depth":30,"pvs":[{"moves":["b5c6","c8d8","c6b7"],"cp":900}]}""";

    private const int House = 5;
    private const int ClubGame = 990601;
    private const int Analysis = 990701;

    private readonly AppDbContext _db;
    private readonly AnalysisJobService _jobs;
    private readonly FakeLlm _local = new("fake-local") { Local = true };
    private readonly FakeLlm _fable = new(ClubFableExplanations.DefaultModel);
    private readonly DateTime _now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeControl : IAnalysisJobControl
    {
        public void Interrupt(int jobId) { }
        public void PreemptBackground(string engineId) { }
    }

    private sealed class FakeLlm(string model) : IClaudeJsonClient
    {
        public bool IsConfigured { get; set; } = true;
        public bool Local { get; set; }
        public bool IsLocal => Local;
        public string TranslationModel => model;
        public Queue<string?> Answers { get; } = new();
        public List<string> Prompts { get; } = new();
        public List<string> Systems { get; } = new();
        public Task<string?> GenerateHintsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> TranslateCommentsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> CompleteJsonAsync(string purpose, string system, string userPrompt, JsonNode schema, int maxTokens, CancellationToken ct = default)
        {
            lock (Prompts)
            {
                Prompts.Add(userPrompt);
                Systems.Add(system);
                return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : null);
            }
        }
    }

    public MoveComparisonDefencesTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var encryption = new EncryptionService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!",
        }).Build());
        _jobs = new AnalysisJobService(_db, new FakeControl());
        _db.AppUsers.Add(new AppUser { Id = House, Username = "house", PasswordHash = "x" });
        _db.LichessEngineCredentials.Add(new LichessEngineCredential
        {
            UserId = House, EncryptedToken = encryption.Encrypt("lip_tok"), BackgroundEngineIds = "eei_bg",
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    // ── Auswahl (rein) ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Der Vergleich 2569 nach den Kandidaten, ohne Datenbank.</summary>
    private static MoveComparison Comparison2569(string weakJson = WeakJson)
    {
        var c = new MoveComparison { Fen = Fen2569, Depth = 30, BestUci = Best, Language = "de", Status = MoveComparisonStatus.Explaining };
        c.Lines.Add(new MoveComparisonLine { Kind = MoveComparisonLineKind.Candidate, CandidateUci = Best, Ordinal = 0,
            Fen = MoveComparisonBoard.PlayUci(Fen2569, Best)!, State = MoveComparisonLineState.Done, ResultJson = BestJson, ReachedDepth = 30 });
        c.Lines.Add(new MoveComparisonLine { Kind = MoveComparisonLineKind.Candidate, CandidateUci = Weak, Ordinal = 1,
            Fen = MoveComparisonBoard.PlayUci(Fen2569, Weak)!, State = MoveComparisonLineState.Done, ResultJson = weakJson, ReachedDepth = 30 });
        return c;
    }

    private static string After(params string[] ucis)
    {
        var fen = Fen2569;
        foreach (var uci in ucis) fen = MoveComparisonBoard.PlayUci(fen, uci)!;
        return fen;
    }

    [Fact]
    public void Plan_Fall2569_TxC8UndKb8_alsZusatzlinien()
    {
        var lines = MoveComparisonDefences.Plan(Comparison2569(), MoveComparisonDefences.DefaultMax);

        // Nach 26.Txc8+ hat Schwarz drei Züge (Kxc8 ist die Linie) → Sxc8, Txc8; nach 26...Kxc8 27.Tc1+ sechs (Kd8 ist die
        // Linie) → der Königszug Kb8 zuerst, dann der billigste Zwischenzug. Abwechselnd je Entscheidung, Deckel 4.
        Assert.Equal(["e7c8", "c8b8", "g8c8", "e7c6"], lines.Select(l => l.ReplyUci));
        Assert.Equal([1, 3, 1, 3], lines.Select(l => l.Ordinal));
        Assert.All(lines, l =>
        {
            Assert.Equal(MoveComparisonLineKind.Defence, l.Kind);
            Assert.Equal(Weak, l.CandidateUci);
            Assert.Equal(MoveComparisonLineState.Pending, l.State);
        });
        // 26...Txc8 und (nach 26...Kxc8 27.Tc1+) 27...Kb8 — die Stellung danach, Weiß am Zug.
        Assert.Equal(After(Weak, "c3c8", "g8c8"), lines.Single(l => l.ReplyUci == "g8c8").Fen);
        Assert.Equal(After(Weak, "c3c8", "b8c8", "a1c1", "c8b8"), lines.Single(l => l.ReplyUci == "c8b8").Fen);
        Assert.StartsWith("1kr5/pp2n3/1q2pb1r/1P1pN1p1/3P2P1/P2BQ3/4NPK1/R7 w", lines.Single(l => l.ReplyUci == "g8c8").Fen);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    [InlineData(8, 7)]     // 2 andere Züge nach 26.Txc8+ und 5 nach 27.Tc1+ — mehr gibt es nicht
    public void Plan_Deckel(int max, int expected)
    {
        var lines = MoveComparisonDefences.Plan(Comparison2569(), max);
        Assert.Equal(expected, lines.Count);
        if (max == 2) Assert.Equal(["e7c8", "c8b8"], lines.Select(l => l.ReplyUci));   // beide Entscheidungen kommen dran
    }

    [Fact]
    public void Plan_OhneSchach_nurZuege_dieDieGezogeneFigurSchlagen()
    {
        // 3...d6 (statt 3...Sf6): Weiß schlägt 4.Sxe5, die Linie lehnt mit 4...De7 ab — naheliegend ist, den Springer zu nehmen.
        const string fen = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 2 3";
        var c = new MoveComparison { Fen = fen, Depth = 30, BestUci = "g8f6" };
        c.Lines.Add(new MoveComparisonLine { Kind = MoveComparisonLineKind.Candidate, CandidateUci = "g8f6", Ordinal = 0,
            Fen = MoveComparisonBoard.PlayUci(fen, "g8f6")!, State = MoveComparisonLineState.Done,
            ResultJson = """{"depth":30,"pvs":[{"moves":["b1c3","f8b4"],"cp":20}]}""" });
        c.Lines.Add(new MoveComparisonLine { Kind = MoveComparisonLineKind.Candidate, CandidateUci = "d7d6", Ordinal = 1,
            Fen = MoveComparisonBoard.PlayUci(fen, "d7d6")!, State = MoveComparisonLineState.Done,
            ResultJson = """{"depth":30,"pvs":[{"moves":["f3e5","d8e7","e5c6","b7c6"],"cp":150}]}""" });

        var lines = MoveComparisonDefences.Plan(c, 4);

        // Bauer vor Springer (die billigere Figur zuerst); nach 5.Sxc6 ist bxc6 der einzige Schlagzug dort → nichts weiter.
        Assert.Equal(["d6e5", "c6e5"], lines.Select(l => l.ReplyUci));
        Assert.All(lines, l => Assert.Equal(1, l.Ordinal));
    }

    [Fact]
    public void Plan_OhneSchwaecherenKandidaten_oderOhneLinie_leer()
    {
        var c = Comparison2569();
        c.BestUci = null;
        Assert.Empty(MoveComparisonDefences.Plan(c, 4));
        Assert.Empty(MoveComparisonDefences.Plan(Comparison2569("""{"depth":30,"pvs":[{"moves":["c3c8"],"cp":692}]}"""), 4));
        Assert.Empty(MoveComparisonDefences.Plan(Comparison2569("kaputt"), 4));
    }

    // ── Fakten und Nutzer-Prompt ───────────────────────────────────────────────────────────────────

    /// <summary>2569 mit gerechneten Zusatzlinien; <paramref name="failed"/> = diese Verteidigung ist gescheitert.</summary>
    private static MoveComparison Explained2569(string? failed = null)
    {
        var c = Comparison2569();
        foreach (var line in MoveComparisonDefences.Plan(c, 4))
        {
            line.ResultJson = line.ReplyUci switch { "e7c8" => ForkNxb6, "e7c6" => Nc6Json, _ => ForkAxb6 };
            line.ReachedDepth = 30;
            line.State = MoveComparisonLineState.Done;
            if (line.ReplyUci == failed) { line.State = MoveComparisonLineState.Failed; line.ResultJson = null; }
            c.Lines.Add(line);
        }
        return c;
    }

    private static MoveComparisonPrompts.PairFacts Facts(MoveComparison c) =>
        MoveComparisonPrompts.PairFactsOf(c, c.Lines.Single(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == Weak))!;

    [Fact]
    public void UserPrompt_Fall2569_nenntDieGabelSd7_inBeidenNebenwegen()
    {
        var facts = Facts(Explained2569());
        var prompt = ClubFableExplanations.UserPrompt(facts);

        Assert.Contains("Other defences in the refutation line (the opponent's best answer line after 25...Bc8) and why they fail", prompt);
        // Erste Entscheidung: alle drei legalen Züge sind gerechnet.
        Assert.Contains("After 26.Rxc8+ Black is in check and has exactly 3 legal moves: Kxc8 (the main line above), Nxc8, Rxc8.", prompt);
        Assert.Contains("- 26...Rxc8 instead of 26...Kxc8: evaluation from the reader's view -7.50 (lost). "
            + "Engine line: 27.Nd7+ Ka8 28.Nxb6+ axb6 29.Qe5.", prompt);
        // Zweite Entscheidung: sechs Züge, nur zwei gerechnet — die anderen sind tabu.
        Assert.Contains("After 26.Rxc8+ Kxc8 27.Rc1+ Black is in check and has 6 legal moves; besides 27...Kd8 (the main line above) "
            + "only the following were calculated — do not discuss the others.", prompt);
        Assert.Contains("- 27...Kb8 instead of 27...Kd8: evaluation from the reader's view -7.50 (lost). "
            + "Engine line: 28.Nd7+ Ka8 29.Nxb6+ axb6 30.Qe5.", prompt);

        // Die Gabel als geprüfter Fakt, je Nebenweg, samt dem eigentlichen Grund (das Feld d7).
        const string fork = "Nd7+ gives check and the knight on d7 also attacks the queen on b6 and the bishop on f6 (a fork); "
            + "the knight on d7 cannot be captured";
        const string square = "d7 is the square the bishop left with 25...Bc8; "
            + "after 25...Bc8 the square d7 was covered only by the bishop on c8, which is no longer there";
        Assert.Contains($"  Concretely, after 26...Rxc8: {fork}; {square}; "
            + "White ends up ahead in material by 4 (White took bishop, queen, Black took rook, knight).", prompt);
        Assert.Contains($"  Concretely, after 27...Kb8: {fork}; {square}; White ends up ahead in material by 4", prompt);
        Assert.DoesNotContain("Nd7+ gives check;", prompt);   // das Schach steht schon in der Gabel

        // Die Hauptlinie bekommt ihre Gabel auch (28.Sf7+ auf König und Turm), und die Figuren stehen in Worten da.
        Assert.Contains("Nf7+ gives check and the knight on f7 also attacks the rook on h6 (a fork); the knight on f7 cannot be captured", prompt);
        Assert.Contains("Pieces in this position, before the move — White: king g2, queen e3, rooks a1 c3, bishop d3, knights e2 e5, "
            + "pawns a3 b5 d4 f2 g4. Black: king b8, queen b6, rooks g8 h6, bishops d7 f6, knight e7, pawns a7 b7 d5 e6 g5.", prompt);
    }

    [Fact]
    public void AllowedMoves_enthaeltDieNeuenZuege_derTextDarfSieNennen()
    {
        var facts = Facts(Explained2569());
        var allowed = facts.AllowedMoves().ToList();
        Assert.Contains("Rxc8", allowed);     // 26...Txc8 — stand vorher in keiner Linie
        Assert.Contains("Kb8", allowed);
        Assert.Contains("Nd7+", allowed);
        Assert.Contains("Nxb6+", allowed);

        const string text = "25...Lc8 gibt das Feld d7 frei. Auf 26.Txc8+ Txc8 folgt 27.Sd7+ mit Gabel, ebenso nach 26...Kxc8 27.Tc1+ Kb8 28.Sd7+.";
        Assert.True(GameMoveExplanationService.MentionsOnly(ClubFableExplanations.EnglishSan(text), allowed));
        // Ohne die Zusatzlinien fiele genau dieser Text durch die Prüfung — deshalb konnte das Modell den Grund nicht sagen.
        Assert.False(GameMoveExplanationService.MentionsOnly(ClubFableExplanations.EnglishSan(text), Facts(Comparison2569()).AllowedMoves()));
        // Nicht gerechnete Verteidigungen bleiben verboten.
        Assert.False(GameMoveExplanationService.MentionsOnly("27...Qc7 scheitert auch.", allowed));
    }

    [Fact]
    public void GescheiterteZusatzlinie_fehltImPrompt_dieAnderenBleiben()
    {
        var prompt = ClubFableExplanations.UserPrompt(Facts(Explained2569(failed: "g8c8")));

        Assert.DoesNotContain("26...Rxc8", prompt);
        Assert.Contains("- 26...Nxc8 instead of 26...Kxc8", prompt);
        Assert.Contains("- 27...Kb8 instead of 27...Kd8", prompt);
        // Nicht mehr „genau drei Züge“: einer davon ist nicht gerechnet.
        Assert.Contains("After 26.Rxc8+ Black is in check and has 3 legal moves; besides 26...Kxc8 (the main line above) only the following", prompt);
    }

    [Fact]
    public void VerteidigungSoGutWieDieHauptlinie_stehtDabei_stattZuScheitern()
    {
        var c = Explained2569();
        c.Lines.Single(l => l.ReplyUci == "c8b8").ResultJson = """{"depth":30,"pvs":[{"moves":["e2g3","b8a8"],"cp":680}]}""";

        var prompt = ClubFableExplanations.UserPrompt(Facts(c));
        Assert.Contains("- 27...Kb8 instead of 27...Kd8: evaluation from the reader's view -6.80 (lost) — about as good as the main line, "
            + "so this defence does NOT fail. Engine line: 28.Ng3 Ka8.", prompt);
        Assert.DoesNotContain("26...Rxc8 instead of 26...Kxc8: evaluation from the reader's view -7.50 (lost) — about", prompt);
    }

    [Fact]
    public void ZeileDieNichtMehrZurLiniePasst_zaehltNicht()
    {
        var c = Explained2569();
        c.Lines.Single(l => l.ReplyUci == "g8c8").Fen = After(Weak);   // eine andere Stellung als der Weg der Linie ergibt
        Assert.DoesNotContain("26...Rxc8", ClubFableExplanations.UserPrompt(Facts(c)));
    }

    [Fact]
    public void LokalesModell_bekommtDenBlockNicht_seinPromptBleibtWieBisher()
    {
        var with = MoveComparisonPrompts.UserPrompt(Facts(Explained2569()));
        var without = MoveComparisonPrompts.UserPrompt(Facts(Comparison2569()));

        Assert.Equal(without, with);
        Assert.DoesNotContain("Other defences", with);
        Assert.DoesNotContain("Pieces in this position", with);
        Assert.DoesNotContain("a fork", with);
    }

    [Fact]
    public void Auftrag_verlangtMotivGrundUndJeVerteidigungEinenHalbsatz()
    {
        var p = ClubFableExplanations.SystemPrompt;
        Assert.Contains("Der taktische Grund, nicht die Nacherzählung der Engine-Linie", p);
        Assert.Contains("was der\n   gespielte Zug aufgibt oder freigibt", p.Replace("\r", ""));
        Assert.Contains("sag zu JEDER in einem Halbsatz, woran sie scheitert", p);
        Assert.Contains("nie die ganze Engine-Linie herunterzählen", p);
        Assert.Contains("muss aus der\n  Figurenliste oder einem „Concretely“-Fakt folgen", p.Replace("\r", ""));
        Assert.Contains("zusammen höchstens 200 Wörter", p);
        // Der Schutz bleibt: nur Züge aus den Linien.
        Assert.Contains("Nenne nur Züge, die in den gegebenen Linien stehen.", p);
    }

    // ── Ablauf mit Pumpe, Aufträgen, Fable ─────────────────────────────────────────────────────────

    private ClubFableExplanations Fable(int? defenceLines = null) =>
        new(_fable, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ClubMistakeComparisons:ExplainWithAnthropic"] = "true",
            ["ClubMistakeComparisons:DefenceLines"] = defenceLines?.ToString(),
        }).Build(), NullLogger<ClubFableExplanations>.Instance, () => _now);

    private MoveComparisonService Service(ClubFableExplanations? fable) => new(_db, _jobs, _local, new MoveComparisonExplainJobs(),
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        NullLogger<MoveComparisonService>.Instance, fable);

    /// <summary>Vereinspartie samt fertiger Hauptanalyse; <paramref name="round"/> = Datum der Ligarunde (Stichtag 06.11.).</summary>
    private void SeedGame(DateOnly round)
    {
        _db.LeagueClubGames.Add(new LeagueClubGame { Id = ClubGame, ClubId = 1, Year = 2026, White = "A", Black = "B", Pgn = "", MovesHash = "h",
            LeagueTnr = 4711, LeagueRound = 1 });
        _db.LeagueRounds.Add(new LeagueRound { Tnr = 4711, Round = 1, Date = round });
        _db.GameAnalyses.Add(new GameAnalysis { Id = Analysis, UserId = House, Pgn = "", StartFen = "", Origin = GameAnalysisOrigin.Club,
            LeagueClubGameId = ClubGame, Status = GameAnalysisStatus.Done, CreatedAt = _now, MistakeComparisonsAt = _now });
        _db.SaveChanges();
    }

    private async Task FinishJobAsync(string fen, string resultJson)
    {
        var job = await _db.AnalysisJobs.SingleAsync(j => j.Fen == fen && j.Status != AnalysisJobStatus.Done);
        job.Status = AnalysisJobStatus.Done;
        job.ReachedDepth = 30;
        job.ResultJson = resultJson;
        await _db.SaveChangesAsync();
    }

    /// <summary>Der Fehler-Vergleich 2569 der Vereinspartie, Kandidaten gerechnet, ein Takt der Pumpe.</summary>
    private async Task<int> CandidatesDoneAsync(MoveComparisonService svc)
    {
        var dto = await svc.CreateAsync(House, new CreateMoveComparisonRequest { Fen = Fen2569, Moves = [Best, Weak], Depth = 30, Lang = "de" });
        var row = await _db.MoveComparisons.SingleAsync(c => c.Id == dto.Id);
        row.GameAnalysisId = Analysis;
        row.Ply = 49;
        await _db.SaveChangesAsync();
        await FinishJobAsync(After(Best), BestJson);
        await FinishJobAsync(After(Weak), WeakJson);
        await svc.PumpOneAsync(dto.Id);
        return dto.Id;
    }

    private async Task<MoveComparison> ReloadAsync(int id)
    {
        _db.ChangeTracker.Clear();
        return await _db.MoveComparisons.Include(c => c.Lines).SingleAsync(c => c.Id == id);
    }

    private static List<MoveComparisonLine> Defences(MoveComparison c) =>
        c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Defence).OrderBy(l => l.Id).ToList();

    private Task<ClubGameSummaryReadiness> ReadinessAsync() => new ClubGameSummaries(_db).ReadinessAsync(ClubGame);

    private const string FableText = """{"explanation":"25...Lc8 gibt das Feld d7 frei: Auf 26.Txc8+ Txc8 folgt 27.Sd7+ mit Gabel, ebenso nach 26...Kxc8 27.Tc1+ Kb8 28.Sd7+.\n\n25...Dd6 deckt d7."}""";

    [Fact]
    public async Task Ablauf_Zusatzlinien_werdenGerechnet_gescheiterteBlockiertNicht_ZusammenfassungBereit()
    {
        SeedGame(new DateOnly(2026, 11, 8));
        var svc = Service(Fable());
        var id = await CandidatesDoneAsync(svc);

        // Die Antwort 26.Txc8+ ist nach 25...Dd6 ein anderer Zug (kein Test) — vor der Begründung kommen die Verteidigungen.
        var c = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Replies, c.Status);
        Assert.Equal(["e7c8", "c8b8", "g8c8", "e7c6"], Defences(c).Select(l => l.ReplyUci));
        Assert.All(Defences(c), l => Assert.NotNull(l.AnalysisJobId));
        var jobs = await _db.AnalysisJobs.Where(j => j.Status != AnalysisJobStatus.Done).ToListAsync();
        Assert.Equal(4, jobs.Count);
        Assert.All(jobs, j => { Assert.Equal(30, j.TargetDepth); Assert.Equal(1, j.MultiPv); Assert.True(j.Background); });
        Assert.Empty(_fable.Prompts);
        Assert.False((await ReadinessAsync()).Ready);   // die Zusammenfassung wartet auf die Zusatzlinien

        // Drei rechnen, eine verschwindet (Auftrag weg) — noch offen heißt: noch nicht erklären.
        await FinishJobAsync(After(Weak, "c3c8", "g8c8"), ForkAxb6);
        await svc.PumpOneAsync(id);
        Assert.Equal(MoveComparisonStatus.Replies, (await ReloadAsync(id)).Status);
        await FinishJobAsync(After(Weak, "c3c8", "b8c8", "a1c1", "c8b8"), ForkAxb6);
        await FinishJobAsync(After(Weak, "c3c8", "b8c8", "a1c1", "e7c6"), Nc6Json);
        var lost = (await ReloadAsync(id)).Lines.Single(l => l.ReplyUci == "e7c8").AnalysisJobId!.Value;
        _db.AnalysisJobs.Remove(await _db.AnalysisJobs.SingleAsync(j => j.Id == lost));
        await _db.SaveChangesAsync();
        await svc.PumpOneAsync(id);

        c = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Explaining, c.Status);
        Assert.Equal(MoveComparisonLineState.Failed, c.Lines.Single(l => l.ReplyUci == "e7c8").State);
        Assert.Equal(3, Defences(c).Count(l => l.State == MoveComparisonLineState.Done));
        Assert.Empty(_db.AnalysisJobs);   // eingesammelt und gelöscht wie alle Aufträge eines Vergleichs

        // Ein weiterer Takt plant nicht noch einmal.
        await svc.PumpOneAsync(id);
        Assert.Equal(4, Defences(await ReloadAsync(id)).Count);

        _fable.Answers.Enqueue(FableText);
        await svc.ExplainAsync(id, CancellationToken.None);
        c = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Done, c.Status);
        Assert.Equal("claude-fable-5-1", c.Model);
        Assert.Contains("26.Rxc8+ Rxc8 folgt 27.Nd7+", c.Lines.Single(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == Weak).Explanation);
        var prompt = Assert.Single(_fable.Prompts);
        Assert.Contains("- 26...Rxc8 instead of 26...Kxc8", prompt);
        Assert.Contains("- 27...Kb8 instead of 27...Kd8", prompt);
        Assert.DoesNotContain("26...Nxc8", prompt);   // gescheitert: der Text entsteht ohne diese Verteidigung
        Assert.Contains("Der taktische Grund", _fable.Systems[0]);
        Assert.Empty(_local.Prompts);

        var ready = await ReadinessAsync();
        Assert.True(ready.Ready);
        Assert.Equal((1, 1), (ready.ComparisonsDone, ready.ComparisonsTotal));
    }

    [Fact]
    public async Task Ablauf_AuftragLaesstSichNichtEinreihen_ZeileGescheitert_VergleichLaeuftWeiter()
    {
        SeedGame(new DateOnly(2026, 11, 8));
        var svc = Service(Fable());
        // Die Haus-Engine fällt nach den Kandidaten weg: kein Auftrag lässt sich mehr anlegen.
        var dto = await svc.CreateAsync(House, new CreateMoveComparisonRequest { Fen = Fen2569, Moves = [Best, Weak], Depth = 30, Lang = "de" });
        var row = await _db.MoveComparisons.SingleAsync(c => c.Id == dto.Id);
        row.GameAnalysisId = Analysis;
        row.Ply = 49;
        await _db.SaveChangesAsync();
        await FinishJobAsync(After(Best), BestJson);
        await FinishJobAsync(After(Weak), WeakJson);
        _db.LichessEngineCredentials.RemoveRange(_db.LichessEngineCredentials);
        await _db.SaveChangesAsync();

        await svc.PumpOneAsync(dto.Id);

        var c = await ReloadAsync(dto.Id);
        Assert.Equal(MoveComparisonStatus.Explaining, c.Status);   // nichts offen → nicht hängen bleiben
        Assert.Equal(4, Defences(c).Count);
        Assert.All(Defences(c), l => Assert.Equal(MoveComparisonLineState.Failed, l.State));
    }

    [Fact]
    public async Task PartieVorDemStichtag_oderDeckelNull_keineZusatzlinien()
    {
        SeedGame(new DateOnly(2026, 11, 5));   // vor dem Stichtag schreibt das lokale Modell — keine Engine-Zeit dafür
        var id = await CandidatesDoneAsync(Service(Fable()));
        var c = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Explaining, c.Status);
        Assert.Empty(Defences(c));
        Assert.Empty(_db.AnalysisJobs);

        var round = await _db.LeagueRounds.SingleAsync();
        round.Date = new DateOnly(2026, 11, 8);
        await _db.SaveChangesAsync();
        await Service(Fable(defenceLines: 0)).PumpOneAsync(id);   // abgeschaltet
        Assert.Empty(Defences(await ReloadAsync(id)));
        await Service(null).PumpOneAsync(id);                      // ohne Fable
        Assert.Empty(Defences(await ReloadAsync(id)));
    }

    [Fact]
    public async Task EigenerVergleichEinesNutzers_keineZusatzlinien()
    {
        var svc = Service(Fable());
        var dto = await svc.CreateAsync(House, new CreateMoveComparisonRequest { Fen = Fen2569, Moves = [Best, Weak], Depth = 24, Lang = "de" });
        await FinishJobAsync(After(Best), BestJson);
        await FinishJobAsync(After(Weak), WeakJson);
        await svc.PumpOneAsync(dto.Id);

        var c = await ReloadAsync(dto.Id);
        Assert.Equal(MoveComparisonStatus.Explaining, c.Status);
        Assert.Empty(Defences(c));
    }

    [Fact]
    public async Task Reexplain_Altbestand_rechnetVerteidigungenNach_undErklaertNeu()
    {
        SeedGame(new DateOnly(2026, 11, 8));
        // Stand vor 0.971.0: fertig erklärt, ohne Zusatzlinien.
        var old = Service(Fable(defenceLines: 0));
        var id = await CandidatesDoneAsync(old);
        _fable.Answers.Enqueue("""{"explanation":"25...Lc8 übersieht 26.Txc8+ Kxc8 27.Tc1+ Kd8 28.Sf7+."}""");
        await old.ExplainAsync(id, CancellationToken.None);
        var before = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Done, before.Status);
        Assert.Empty(Defences(before));
        var finishedAt = before.FinishedAt;

        var svc = Service(Fable());
        Assert.True(await svc.ReexplainAsync(id));
        Assert.False(await svc.ReexplainAsync(id));   // läuft schon
        Assert.False((await ReadinessAsync()).Ready);
        await svc.PumpOneAsync(id);
        var c = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Replies, c.Status);
        Assert.Equal(4, Defences(c).Count);
        // Die alte Begründung bleibt stehen, solange gerechnet wird.
        Assert.StartsWith("25...Bc8 übersieht", c.Lines.Single(l => l.CandidateUci == Weak && l.Kind == MoveComparisonLineKind.Candidate).Explanation);

        foreach (var job in await _db.AnalysisJobs.Where(j => j.Status != AnalysisJobStatus.Done).ToListAsync())
            await FinishJobAsync(job.Fen, job.Fen.Contains("qn1pb1r") ? Nc6Json : ForkAxb6);
        await svc.PumpOneAsync(id);
        Assert.Equal(MoveComparisonStatus.Explaining, (await ReloadAsync(id)).Status);

        _fable.Answers.Enqueue(FableText);
        await svc.ExplainAsync(id, CancellationToken.None);
        c = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Done, c.Status);
        Assert.StartsWith("25...Bc8 gibt das Feld d7 frei", c.Lines.Single(l => l.CandidateUci == Weak && l.Kind == MoveComparisonLineKind.Candidate).Explanation);
        Assert.True(c.FinishedAt >= finishedAt);
        Assert.Contains("- 26...Rxc8 instead of 26...Kxc8", _fable.Prompts[^1]);
        Assert.True((await ReadinessAsync()).Ready);
    }

    [Fact]
    public async Task Reexplain_FableLiefertNichts_alteBegruendungBleibt_lokalesModellErsetztSieNicht()
    {
        SeedGame(new DateOnly(2026, 11, 8));
        var svc = Service(Fable(defenceLines: 0));
        var id = await CandidatesDoneAsync(svc);
        _fable.Answers.Enqueue("""{"explanation":"25...Lc8 übersieht 26.Txc8+."}""");
        await svc.ExplainAsync(id, CancellationToken.None);

        Assert.True(await svc.ReexplainAsync(id));
        await svc.PumpOneAsync(id);
        _fable.Answers.Enqueue(null);                                                   // Fehler/Ablehnung
        _local.Answers.Enqueue("""{"explanation":"Nach Bc8 nimmt dein Gegner mit Rxc8+."}""");
        await svc.ExplainAsync(id, CancellationToken.None);

        var c = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Done, c.Status);
        Assert.Equal("25...Bc8 übersieht 26.Rxc8+.", c.Lines.Single(l => l.CandidateUci == Weak && l.Kind == MoveComparisonLineKind.Candidate).Explanation);
        Assert.Equal("claude-fable-5-1", c.Model);
        Assert.Empty(_local.Prompts);
    }

    [Fact]
    public async Task Reexplain_Unbekannt_EigenerVergleich_NichtFertig()
    {
        SeedGame(new DateOnly(2026, 11, 8));
        var svc = Service(Fable());
        Assert.Null(await svc.ReexplainAsync(424242));
        var own = await svc.CreateAsync(House, new CreateMoveComparisonRequest { Fen = Fen2569, Moves = [Best, Weak], Depth = 24, Lang = "de" });
        Assert.Null(await svc.ReexplainAsync(own.Id));            // nur Vergleiche einer Partie-Analyse
        var row = await _db.MoveComparisons.SingleAsync(c => c.Id == own.Id);
        row.GameAnalysisId = Analysis;
        await _db.SaveChangesAsync();
        Assert.False(await svc.ReexplainAsync(own.Id));           // rechnet noch

        var controller = new AdminMoveComparisonsController(svc);
        Assert.IsType<NotFoundResult>(await controller.Reexplain(424242, CancellationToken.None));
        Assert.IsType<ConflictObjectResult>(await controller.Reexplain(own.Id, CancellationToken.None));
        row.Status = MoveComparisonStatus.Done;
        await _db.SaveChangesAsync();
        Assert.IsType<AcceptedResult>(await controller.Reexplain(own.Id, CancellationToken.None));
        Assert.Equal(MoveComparisonStatus.Explaining, (await ReloadAsync(own.Id)).Status);
    }

    [Fact]
    public async Task Vergleichsseite_zeigtDieZusatzlinienNicht_zaehltSieAberMit()
    {
        SeedGame(new DateOnly(2026, 11, 8));
        var svc = Service(Fable());
        var id = await CandidatesDoneAsync(svc);

        var dto = (await svc.GetForAnalysisAsync(Analysis, id))!;
        Assert.Equal("replies", dto.Status);
        Assert.Equal(2, dto.Candidates.Count);
        Assert.All(dto.Candidates, k => Assert.DoesNotContain(k.Tests, t => t.ReplyUci is "g8c8" or "c8b8" or "e7c8" or "e7c6"));
        Assert.Equal(4, dto.Pending);   // der Fortschritt der Seite zählt sie mit
        Assert.Equal(7, dto.Total);     // 2 Kandidaten, 1 Antwort (anderer Zug), 4 Verteidigungen
    }
}
