using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Engines;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.EngineBroker;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests.Engines;

/// <summary>
/// Die verlockenden Antworten auf den besten Zug (0.974.0, <see cref="MoveComparisonBestMoveTries"/>).
/// Regressionsfall: Vereinspartie 251, Zugvergleich 2615 — 20...dxc3 (bester Zug 20...e4). Der Text erzählte 21.Lf1 nach;
/// die Frage am Brett ist 21.Txe4, und das scheitert an 21...dxc3 22.bxc3 Dxa3 mit Figurengewinn. Die Zahlen sind gemessen:
/// Kandidaten aus der Prod-Analyse der Partie (Tiefe 30), flache Sicht und Zusatzlinien mit dem Stockfish des
/// Engine-Providers (Tiefe 1 auf leerem Hash bzw. Tiefe 22–24), Züge mit python-chess auf Legalität geprüft.
/// </summary>
public class MoveComparisonBestMoveTriesTests : IDisposable
{
    /// <summary>Stellung vor 20...dxc3 (Schwarz am Zug).</summary>
    private const string Fen2615 = "r4r1k/1p4bp/p1nq2p1/4pnB1/2Bp4/N1P3P1/PP3P1P/R2QR1K1 b - - 1 20";
    private const string Best = "e5e4";
    private const string Weak = "d4c3";
    /// <summary>Weiß-Sicht wie jede Broker-Zeile: nach 20...e4 ist 21.Lf1 die beste Antwort (Schwarz +3,60).</summary>
    private const string BestJson = """{"depth":30,"pvs":[{"moves":["c4f1","e4e3","f2e3","d4c3","d1d6","f5d6","b2b3","d6e4","g5f4","g6g5"],"cp":-360}]}""";
    private const string WeakJson = """{"depth":30,"pvs":[{"moves":["d1d6","f5d6","b2c3","h7h6","g5e3","e5e4","a1d1","f8d8"],"cp":-30},{"moves":["g5c1","e5e4","d1d6","f5d6","b2c3","g7c3"],"cp":-605},{"moves":["a3c2","c3b2","a1b1","h7h6","g5e3","d6d1"],"cp":-623}]}""";
    /// <summary>Die flache Sicht nach 20...e4: Tiefe 1, fünf Linien, leerer Hash — 21.Txe4 ist Zweiter, 0,40 hinter 21.Lf4.</summary>
    private const string GlanceJson = """{"time":1,"depth":1,"nodes":412,"pvs":[{"moves":["g5f4"],"cp":-194,"depth":1},{"moves":["e1e4","d4c3"],"cp":-234,"depth":1},{"moves":["c4f1"],"cp":-249,"depth":1},{"moves":["c4b3"],"cp":-250,"depth":1},{"moves":["c3d4","f5d4","e1e4"],"cp":-269,"depth":1}]}""";
    /// <summary>Dieselbe Suche auf dem WARMEN Hash einer Tiefe-20-Rechnung der Stellung: 21.Txe4 nur noch Vierter, 0,57 zurück.</summary>
    private const string WarmGlanceJson = """{"depth":1,"pvs":[{"moves":["g5f4","d6e7"],"cp":-249},{"moves":["c4f1","d4c3","d1d6","f5d6"],"cp":-284},{"moves":["c4b3"],"cp":-292},{"moves":["e1e4"],"cp":-306},{"moves":["d1b3"],"cp":-319}]}""";
    /// <summary>21.Txe4 tief: 21...dxc3 22.bxc3 Dxa3 — der Springer a3 fällt (Schwarz +5,28).</summary>
    private const string TakesJson = """{"depth":30,"pvs":[{"moves":["d4c3","b2c3","d6a3","a1b1","a8b8","c4b3","a3c5","e4c4","c5a5","c4a4"],"cp":-528}]}""";
    /// <summary>21.Lf4 tief: nur 0,37 schlechter als 21.Lf1 — verlockend, aber in Ordnung.</summary>
    private const string Bf4Json = """{"depth":30,"pvs":[{"moves":["d6e7","c3d4","g6g5","f4e5","g7e5","d4e5","a8d8","d1c2","f5d4","c2e4"],"cp":-397}]}""";
    private const string Bb3Json = """{"depth":30,"pvs":[{"moves":["a8e8","a3c4","d6c5","g5f4","d4d3","d1g4"],"cp":-398}]}""";
    /// <summary>Die Antwort-Tests: 21.Lc1 und 21.Sc2 (die zweit- und drittbeste Antwort auf den Fehlzug) nach 20...e4.</summary>
    private const string Bc1Json = """{"depth":30,"pvs":[{"moves":["d4c3","d1d6","f5d6","b2b3","c6e5","e1f1"],"cp":-555}]}""";
    private const string Nc2Json = """{"depth":30,"pvs":[{"moves":["d6c5","c4b3","e4e3","f2e3","d4c3","b2c3"],"cp":-405}]}""";

    private const int House = 5;
    private const int ClubGame = 990611;
    private const int Analysis = 990711;

    private static readonly MoveComparisonBestMoveTries.Settings Defaults = MoveComparisonBestMoveTries.Settings.Default;

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

    public MoveComparisonBestMoveTriesTests()
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

    private static string After(params string[] ucis)
    {
        var fen = Fen2615;
        foreach (var uci in ucis) fen = MoveComparisonBoard.PlayUci(fen, uci)!;
        return fen;
    }

    /// <summary>Der Vergleich 2615 nach den Kandidaten, ohne Datenbank; <paramref name="glance"/> = die fertige flache Sicht.</summary>
    private static MoveComparison Comparison2615(string? glance = GlanceJson, string bestJson = BestJson)
    {
        var c = new MoveComparison { Fen = Fen2615, Depth = 30, BestUci = Best, Language = "de", Status = MoveComparisonStatus.Explaining };
        c.Lines.Add(new MoveComparisonLine { Kind = MoveComparisonLineKind.Candidate, CandidateUci = Best, Ordinal = 0,
            Fen = After(Best), State = MoveComparisonLineState.Done, ResultJson = bestJson, ReachedDepth = 30 });
        c.Lines.Add(new MoveComparisonLine { Kind = MoveComparisonLineKind.Candidate, CandidateUci = Weak, Ordinal = 1,
            Fen = After(Weak), State = MoveComparisonLineState.Done, ResultJson = WeakJson, ReachedDepth = 30 });
        if (glance is not null)
            c.Lines.Add(new MoveComparisonLine { Kind = MoveComparisonLineKind.BestMoveGlance, CandidateUci = Best, Ordinal = 0,
                Fen = After(Best), State = MoveComparisonLineState.Done, ResultJson = glance, ReachedDepth = 1 });
        return c;
    }

    [Fact]
    public void PlanGlance_StellungNachDemBestenZug_eineZeileOhneAntwort()
    {
        var line = MoveComparisonBestMoveTries.PlanGlance(Comparison2615(glance: null))!;

        Assert.Equal(MoveComparisonLineKind.BestMoveGlance, line.Kind);
        Assert.Equal(Best, line.CandidateUci);
        Assert.Null(line.ReplyUci);
        Assert.Equal(After(Best), line.Fen);
        Assert.Equal(MoveComparisonLineState.Pending, line.State);
        Assert.StartsWith("r4r1k/1p4bp/p1nq2p1/5nB1/2Bpp3/N1P3P1/PP3P1P/R2QR1K1 w", line.Fen);
    }

    [Fact]
    public void PlanGlance_OhneBestenZug_oderOhneSchwaecherenKandidaten_nichts()
    {
        var c = Comparison2615(glance: null);
        c.BestUci = null;
        Assert.Null(MoveComparisonBestMoveTries.PlanGlance(c));

        var only = Comparison2615(glance: null);
        only.Lines.RemoveAll(l => l.CandidateUci == Weak);
        Assert.Null(MoveComparisonBestMoveTries.PlanGlance(only));

        // Der beste Zug setzt matt: nach ihm gibt es nichts anzusehen.
        const string mateIn1 = "6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1";
        var mate = new MoveComparison { Fen = mateIn1, Depth = 30, BestUci = "a1a8" };
        mate.Lines.Add(new MoveComparisonLine { Kind = MoveComparisonLineKind.Candidate, CandidateUci = "a1a8", Ordinal = 0,
            Fen = MoveComparisonBoard.PlayUci(mateIn1, "a1a8")!, State = MoveComparisonLineState.Done });
        mate.Lines.Add(new MoveComparisonLine { Kind = MoveComparisonLineKind.Candidate, CandidateUci = "h2h3", Ordinal = 1,
            Fen = MoveComparisonBoard.PlayUci(mateIn1, "h2h3")!, State = MoveComparisonLineState.Done,
            ResultJson = """{"depth":30,"pvs":[{"moves":["h7h6"],"cp":500}]}""" });
        Assert.Null(MoveComparisonBestMoveTries.PlanGlance(mate));
    }

    [Fact]
    public void Plan_Fall2615_TxE4_istVerlockend_undWirdTiefGerechnet()
    {
        var lines = MoveComparisonBestMoveTries.Plan(Comparison2615(), Defaults);

        // Flache Sicht: 1. Lf4 (−1,94), 2. Txe4 (−2,34, 0,40 zurück), 3. Lf1 (−2,49 — die beste Antwort der tiefen Rechnung,
        // zählt nicht, und läge mit 0,55 ohnehin über der Schwelle). Lb3 und cxd4 sind nicht unter den besten drei.
        Assert.Equal(["g5f4", "e1e4"], lines.Select(l => l.ReplyUci));
        Assert.Equal([0, 1], lines.Select(l => l.Ordinal));
        Assert.All(lines, l =>
        {
            Assert.Equal(MoveComparisonLineKind.BestMoveTry, l.Kind);
            Assert.Equal(Best, l.CandidateUci);
            Assert.Equal(MoveComparisonLineState.Pending, l.State);
        });
        // Die Stellung nach 20...e4 21.Txe4, Schwarz am Zug.
        Assert.Equal(After(Best, "e1e4"), lines[1].Fen);
        Assert.StartsWith("r4r1k/1p4bp/p1nq2p1/5nB1/2BpR3/N1P3P1/PP3P1P/R2Q2K1 b", lines[1].Fen);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 2)]     // mehr als zwei verlockende Züge gibt es hier nicht
    public void Plan_Deckel(int max, int expected)
    {
        var lines = MoveComparisonBestMoveTries.Plan(Comparison2615(), Defaults with { Max = max });
        Assert.Equal(expected, lines.Count);
    }

    [Fact]
    public void Plan_OhneFlacheSicht_oderUnlesbar_nichts()
    {
        Assert.Empty(MoveComparisonBestMoveTries.Plan(Comparison2615(glance: null), Defaults));
        Assert.Empty(MoveComparisonBestMoveTries.Plan(Comparison2615(glance: "kaputt"), Defaults));
        var failed = Comparison2615();
        failed.Lines.Single(l => l.Kind == MoveComparisonLineKind.BestMoveGlance).State = MoveComparisonLineState.Failed;
        Assert.Empty(MoveComparisonBestMoveTries.Plan(failed, Defaults));
    }

    [Fact]
    public void Plan_SchlagSchonBeiTiefe1KlarSchlecht_wirdNichtAufgenommen()
    {
        // Txe4 steht zwar unter den besten drei, aber 1,36 hinter dem Ersten — das sieht schon der erste Blick.
        const string clearlyBad = """{"depth":1,"pvs":[{"moves":["g5f4"],"cp":-194},{"moves":["c4f1"],"cp":-249},{"moves":["e1e4","d4c3"],"cp":-330},{"moves":["c4b3"],"cp":-340}]}""";
        Assert.Equal(["g5f4"], MoveComparisonBestMoveTries.Plan(Comparison2615(clearlyBad), Defaults).Select(l => l.ReplyUci));

        // Auf dem warmen Hash der tiefen Rechnung ist Txe4 nur Vierter: nicht unter den besten drei → nicht verlockend.
        // (Deshalb rechnet die flache Sicht in einer eigenen Sitzung, also auf leerem Hash.)
        Assert.Equal(["g5f4", "c4b3"], MoveComparisonBestMoveTries.Plan(Comparison2615(WarmGlanceJson), Defaults).Select(l => l.ReplyUci));
    }

    [Fact]
    public void Plan_BesteAntwortDerTiefenRechnung_zaehltNicht_keineDoppelung()
    {
        // Wäre 21.Txe4 die beste Antwort, stünde es schon in der Kandidatenlinie (Lf1 liegt mit 0,55 über der Schwelle).
        const string takesIsBest = """{"depth":30,"pvs":[{"moves":["e1e4","d4c3","b2c3","d6a3"],"cp":-360}]}""";
        Assert.Equal(["g5f4"], MoveComparisonBestMoveTries.Plan(Comparison2615(bestJson: takesIsBest), Defaults).Select(l => l.ReplyUci));

        // Die beste Antwort (Lf1) als Zweite der flachen Sicht, 0,10 zurück: verlockend wäre sie — gerechnet wird sie nicht noch einmal.
        const string bestSecond = """{"depth":1,"pvs":[{"moves":["g5f4"],"cp":-194},{"moves":["c4f1"],"cp":-204},{"moves":["e1e4","d4c3"],"cp":-234}]}""";
        Assert.Equal(["g5f4", "e1e4"], MoveComparisonBestMoveTries.Plan(Comparison2615(bestSecond), Defaults).Select(l => l.ReplyUci));
    }

    [Fact]
    public void Plan_Schwellen_TopUndAbstand()
    {
        Assert.Equal(["g5f4"], MoveComparisonBestMoveTries.Plan(Comparison2615(), Defaults with { Top = 1 }).Select(l => l.ReplyUci));
        Assert.Equal(["g5f4"], MoveComparisonBestMoveTries.Plan(Comparison2615(), Defaults with { WithinCp = 39 }).Select(l => l.ReplyUci));
        Assert.Equal(["g5f4", "e1e4"], MoveComparisonBestMoveTries.Plan(Comparison2615(), Defaults with { WithinCp = 40 }).Select(l => l.ReplyUci));
        // Top 5 und 0,8 Bauern: Lb3 (0,56) und cxd4 (0,75) kämen dazu — der Deckel hält es bei zwei tiefen Rechnungen.
        var wide = Defaults with { Top = 5, WithinCp = 80 };
        Assert.Equal(["g5f4", "e1e4"], MoveComparisonBestMoveTries.Plan(Comparison2615(), wide).Select(l => l.ReplyUci));
        Assert.Equal(["g5f4", "e1e4", "c4b3", "c3d4"], MoveComparisonBestMoveTries.Plan(Comparison2615(), wide with { Max = 4 }).Select(l => l.ReplyUci));
    }

    [Fact]
    public void Plan_ZugSchonGerechnet_brauchtKeineEigeneRechnung()
    {
        // Als Antwort-Test: 21.Txe4 wäre (angenommen) auch eine der besten Antworten auf den Fehlzug gewesen.
        var c = Comparison2615();
        c.Lines.Add(new MoveComparisonLine { Kind = MoveComparisonLineKind.Reply, CandidateUci = Weak, ReplyUci = "e1e4", Ordinal = 1,
            Fen = After(Best, "e1e4"), State = MoveComparisonLineState.Done, ResultJson = TakesJson, ReachedDepth = 30 });
        Assert.Equal(["g5f4"], MoveComparisonBestMoveTries.Plan(c, Defaults).Select(l => l.ReplyUci));
        // … und gelesen wird sie von dort.
        var tries = MoveComparisonBestMoveTries.Of(c, c.Lines.Single(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == Weak), Defaults);
        Assert.Equal("Rxe4", Assert.Single(tries).San);

        // Als weitere Linie des besten Kandidaten: 21.Lf4 ist dort nur 0,30 schlechter — tief in Ordnung, keine Rechnung.
        const string twoLines = """{"depth":30,"pvs":[{"moves":["c4f1","e4e3","f2e3","d4c3"],"cp":-360},{"moves":["g5f4","d6e7","c3d4"],"cp":-390}]}""";
        Assert.Equal(["e1e4"], MoveComparisonBestMoveTries.Plan(Comparison2615(bestJson: twoLines), Defaults).Select(l => l.ReplyUci));
        // … wäre er dort klar schlechter, wird er wie jeder andere gerechnet (die Zeile braucht ihre eigene Linie).
        const string twoLinesBad = """{"depth":30,"pvs":[{"moves":["c4f1","e4e3","f2e3","d4c3"],"cp":-360},{"moves":["g5f4","d6e7","c3d4"],"cp":-520}]}""";
        Assert.Equal(["g5f4", "e1e4"], MoveComparisonBestMoveTries.Plan(Comparison2615(bestJson: twoLinesBad), Defaults).Select(l => l.ReplyUci));
    }

    [Fact]
    public void Settings_AusDerKonfiguration_mitVorgabenUndGrenzen()
    {
        Assert.Equal(new MoveComparisonBestMoveTries.Settings(2, 1, 3, 50, 100), MoveComparisonBestMoveTries.Settings.From(null));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ClubMistakeComparisons:BestMoveTries"] = "1",
            ["ClubMistakeComparisons:BestMoveTryShallowDepth"] = "3",
            ["ClubMistakeComparisons:BestMoveTryTop"] = "9",            // das Protokoll liefert höchstens fünf Linien
            ["ClubMistakeComparisons:BestMoveTryWithinPawns"] = "0.3",
            ["ClubMistakeComparisons:BestMoveTryLossPawns"] = "1.5",
        }).Build();
        Assert.Equal(new MoveComparisonBestMoveTries.Settings(1, 3, 5, 30, 150), MoveComparisonBestMoveTries.Settings.From(config));
        // Tiefer als die Grenze der eigenen Sitzung wäre keine flache Sicht mehr (und liefe auf warmem Hash).
        var deep = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ClubMistakeComparisons:BestMoveTryShallowDepth"] = "12",
        }).Build();
        Assert.Equal(AnalysisJobEngineResolver.FreshSessionMaxDepth, MoveComparisonBestMoveTries.Settings.From(deep).ShallowDepth);
    }

    [Fact]
    public void FlacheSuche_rechnetInEigenerSitzung_alsoAufLeeremHash()
    {
        var engine = new EngineRef("eei_bg", "Stockfish", 8, 1024, EngineSource.Lichess);
        var shallow = new AnalysisJob { Id = 77, UserId = House, Fen = After(Best), TargetDepth = 1, MultiPv = 5 };
        var deep = new AnalysisJob { Id = 78, UserId = House, Fen = After(Best), TargetDepth = 30, MultiPv = 1 };
        var nodes = new AnalysisJob { Id = 79, UserId = House, Fen = After(Best), TargetDepth = 1, TargetNodes = 50_000, MultiPv = 1 };

        Assert.Equal("rh-bg-5-fresh-77", AnalysisJobEngineResolver.WorkOf(shallow, engine).SessionId);
        Assert.Equal(1, AnalysisJobEngineResolver.WorkOf(shallow, engine).Depth);
        Assert.Equal(5, AnalysisJobEngineResolver.WorkOf(shallow, engine).MultiPv);
        Assert.Equal("rh-bg-5", AnalysisJobEngineResolver.WorkOf(deep, engine).SessionId);
        Assert.Equal("rh-bg-5", AnalysisJobEngineResolver.WorkOf(nodes, engine).SessionId);   // Knotenauftrag: die Tiefe zählt dort nicht
        Assert.Equal("rh-bg-5", AnalysisJobEngineResolver.SessionOf(new AnalysisJob { Id = 80, UserId = House, TargetDepth = AnalysisJobEngineResolver.FreshSessionMaxDepth + 1 }));
    }

    // ── Fakten und Nutzer-Prompt ───────────────────────────────────────────────────────────────────

    /// <summary>2615 mit flacher Sicht und gerechneten Zusatzlinien; <paramref name="failed"/> = diese ist gescheitert.</summary>
    private static MoveComparison Explained2615(string? failed = null, string takesJson = TakesJson)
    {
        var c = Comparison2615();
        foreach (var line in MoveComparisonBestMoveTries.Plan(c, Defaults))
        {
            line.ResultJson = line.ReplyUci == "e1e4" ? takesJson : Bf4Json;
            line.ReachedDepth = 30;
            line.State = MoveComparisonLineState.Done;
            if (line.ReplyUci == failed) { line.State = MoveComparisonLineState.Failed; line.ResultJson = null; }
            c.Lines.Add(line);
        }
        return c;
    }

    private static MoveComparisonPrompts.PairFacts Facts(MoveComparison c, MoveComparisonBestMoveTries.Settings? settings = null) =>
        MoveComparisonPrompts.PairFactsOf(c, c.Lines.Single(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == Weak), settings)!;

    private const string Block2615 =
        "Obvious replies to the better move 20...e4 and why they fail — moves that look good at first sight but lose when calculated "
        + "to depth 30, the first thing the reader asks at the board; evaluation from the reader's view, the reader's reply with the "
        + "engine line, checked facts:\n"
        + "- 21.Rxe4 (takes the pawn on e4; looks good at first sight: the shallow search (depth 1) ranks it 2nd of 51 legal moves, "
        + "0.40 pawns behind its first choice): evaluation from the reader's view +5.28 (winning), against +3.60 after the opponent's "
        + "best answer 21.Bf1 — this reply fails. The reader's reply, with the engine line: 21...dxc3 22.bxc3 Qxa3 23.Rb1 Rab8 24.Bb3 "
        + "Qc5 25.Rc4 Qa5 26.Ra4.\n"
        + "  Concretely, after 20...e4 21.Rxe4: dxc3 opens the d-file for the queen on d6, which now attacks the queen on d1 (a "
        + "discovered attack); Qxa3 takes the knight on a3 and the queen on a3 cannot be recaptured (at the start of this line the "
        + "knight was defended only by the pawn on b2, which is no longer there); Black ends up ahead in material by 2 (White took 2 "
        + "pawns, Black took pawn, knight).\n";

    [Fact]
    public void UserPrompt_Fall2615_nenntTxE4_undDenFigurengewinn()
    {
        var prompt = ClubFableExplanations.UserPrompt(Facts(Explained2615()));

        Assert.Contains(Block2615, prompt.Replace("\r", ""));
        // Der Block steht hinter der besten Antwort auf den besten Zug und vor dem Leser-Hinweis.
        Assert.True(prompt.IndexOf("The opponent's best answer to 20...e4 itself: 21.Bf1", StringComparison.Ordinal)
                    < prompt.IndexOf("Obvious replies to the better move", StringComparison.Ordinal));
        Assert.True(prompt.IndexOf("Obvious replies to the better move", StringComparison.Ordinal)
                    < prompt.IndexOf("Reader: plays Black", StringComparison.Ordinal));
        // 21.Lf4 war auf den ersten Blick sogar Erster, ist aber tief nur 0,37 schlechter als 21.Lf1: fällt still weg.
        Assert.DoesNotContain("21.Bf4", prompt);
        Assert.DoesNotContain("as good as", prompt);
    }

    [Fact]
    public void VerlockendAberAuchTiefInOrdnung_faelltStillWeg()
    {
        // 21.Txe4 wäre tief nur 0,60 schlechter als die beste Antwort (Schwelle 1,0): kein Block, kein erlaubter Zug.
        const string fine = """{"depth":30,"pvs":[{"moves":["d4c3","b2c3","d6a3","a1b1"],"cp":-420}]}""";
        var facts = Facts(Explained2615(takesJson: fine));

        Assert.Empty(facts.Tries!);
        Assert.DoesNotContain("Obvious replies", ClubFableExplanations.UserPrompt(facts));
        Assert.DoesNotContain("Rxe4", facts.AllowedMoves());

        // Genau an der Schwelle zählt er, mit einer strengeren Schwelle nicht mehr.
        const string exactly = """{"depth":30,"pvs":[{"moves":["d4c3","b2c3","d6a3","a1b1"],"cp":-460}]}""";
        Assert.Single(Facts(Explained2615(takesJson: exactly)).Tries!);
        Assert.Empty(Facts(Explained2615(takesJson: exactly), Defaults with { LossCp = 150 }).Tries!);
        // Abgeschaltet: auch gerechnete Zeilen stehen nicht im Prompt.
        Assert.Empty(Facts(Explained2615(), Defaults with { Max = 0 }).Tries!);
    }

    [Fact]
    public void AllowedMoves_enthaeltDieNeuenZuege_derTextDarfSieNennen()
    {
        var allowed = Facts(Explained2615()).AllowedMoves().ToList();
        Assert.Contains("Rxe4", allowed);     // 21.Txe4 — stand vorher in keiner Linie
        Assert.Contains("Qxa3", allowed);
        Assert.Contains("bxc3", allowed);

        const string text = "20...e4 ist stark, weil 21.Txe4 an 21...dxc3 22.bxc3 Dxa3 mit Figurengewinn scheitert. Auf 21.Lf1 folgt 21...e3.";
        Assert.True(GameMoveExplanationService.MentionsOnly(ClubFableExplanations.EnglishSan(text), allowed));
        // Ohne die Zusatzlinie fiele genau dieser Text durch die Prüfung — deshalb konnte das Modell den Grund nicht sagen.
        Assert.False(GameMoveExplanationService.MentionsOnly(ClubFableExplanations.EnglishSan(text), Facts(Comparison2615()).AllowedMoves()));
        // Die Linie des verlockenden, aber tief in Ordnung befundenen 21.Lf4 (21...De7) bleibt verboten.
        Assert.DoesNotContain("Qe7", allowed);
        Assert.False(GameMoveExplanationService.MentionsOnly("21.Bf4 Qe7 hält auch.", allowed));
    }

    [Fact]
    public void GescheiterteZusatzlinie_fehltImPrompt_dieAndereBleibt()
    {
        Assert.Contains("- 21.Rxe4 (takes the pawn on e4;", ClubFableExplanations.UserPrompt(Facts(Explained2615(failed: "g5f4"))));
        Assert.DoesNotContain("Obvious replies", ClubFableExplanations.UserPrompt(Facts(Explained2615(failed: "e1e4"))));
    }

    [Fact]
    public void ZeileDieNichtMehrZurStellungPasst_zaehltNicht()
    {
        var c = Explained2615();
        c.Lines.Single(l => l.ReplyUci == "e1e4").Fen = After(Best);   // eine andere Stellung als bester Zug + Antwort
        Assert.DoesNotContain("21.Rxe4", ClubFableExplanations.UserPrompt(Facts(c)));
    }

    [Fact]
    public void LokalesModell_bekommtDenBlockNicht_seinPromptBleibtWieBisher()
    {
        var with = MoveComparisonPrompts.UserPrompt(Facts(Explained2615()));
        var without = MoveComparisonPrompts.UserPrompt(Facts(Comparison2615(glance: null)));

        Assert.Equal(without, with);
        Assert.DoesNotContain("Obvious replies", with);
        Assert.DoesNotContain("Rxe4", with);
    }

    [Fact]
    public void Auftrag_beantwortetZuerstDieNaheliegendeAntwort_dannDieHauptlinie()
    {
        var p = ClubFableExplanations.SystemPrompt.Replace("\r", "");
        Assert.Contains("Beantworte ZUERST die Frage, die der Leser am Brett hat", p);
        Assert.Contains("„Obvious replies to the better move“ eine Antwort des Gegners", p);
        Assert.Contains("„21.Txe4 scheitert\n   an 21...dxc3 22.bxc3 Dxa3 mit Figurengewinn“", p);
        Assert.Contains("DANN erst die ruhige Hauptlinie", p);
        Assert.True(p.IndexOf("Beantworte ZUERST", StringComparison.Ordinal) < p.IndexOf("DANN erst die ruhige Hauptlinie", StringComparison.Ordinal));
        // Absatz 1 (0.971.0) und der Schutz bleiben; die Wortgrenze ist nicht angehoben.
        Assert.Contains("Der taktische Grund, nicht die Nacherzählung der Engine-Linie", p);
        Assert.Contains("Nenne nur Züge, die in den gegebenen Linien stehen.", p);
        Assert.Contains("zusammen höchstens 200 Wörter", p);
        // Der Beispielsatz selbst besteht die Zugprüfung der Daten des Falls.
        Assert.True(GameMoveExplanationService.MentionsOnly(
            ClubFableExplanations.EnglishSan("21.Txe4 scheitert an 21...dxc3 22.bxc3 Dxa3 mit Figurengewinn"), Facts(Explained2615()).AllowedMoves()));
    }

    // ── Ablauf mit Pumpe, Aufträgen, Fable ─────────────────────────────────────────────────────────

    private ClubFableExplanations Fable(int? tries = null, int defenceLines = 0) =>
        new(_fable, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ClubMistakeComparisons:ExplainWithAnthropic"] = "true",
            ["ClubMistakeComparisons:BestMoveTries"] = tries?.ToString(),
            ["ClubMistakeComparisons:DefenceLines"] = defenceLines.ToString(),
        }).Build(), NullLogger<ClubFableExplanations>.Instance, () => _now);

    /// <summary>Die Lauf-Marken aller Dienste dieses Tests (<see cref="BackgroundRunsEndedAsync"/>).</summary>
    private readonly List<MoveComparisonExplainJobs> _running = [];

    private MoveComparisonService Service(ClubFableExplanations? fable)
    {
        var running = new MoveComparisonExplainJobs();
        _running.Add(running);
        return new(_db, _jobs, _local, running,
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MoveComparisonService>.Instance, fable);
    }

    /// <summary>Wartet, bis kein Hintergrundlauf der Begründung mehr markiert ist: die Pumpe stößt ihn per <c>Task.Run</c> an,
    /// sobald ein Vergleich „wird erklärt“ erreicht, und solange die Marke steht, lehnt <c>ReexplainAsync</c> ab und die
    /// Pumpe plant keine Zusatzlinien (derselbe Wettlauf machte <c>MoveComparisonDefencesTests</c> in der vollen Suite rot).</summary>
    private async Task BackgroundRunsEndedAsync(int id)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (_running.Any(r => r.IsRunning(id)) && DateTime.UtcNow < until) await Task.Delay(5);
        Assert.DoesNotContain(_running, r => r.IsRunning(id));
    }

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

    private async Task FinishJobAsync(string fen, string resultJson, int depth = 30)
    {
        var job = await _db.AnalysisJobs.SingleAsync(j => j.Fen == fen && j.Status != AnalysisJobStatus.Done);
        job.Status = AnalysisJobStatus.Done;
        job.ReachedDepth = depth;
        job.ResultJson = resultJson;
        await _db.SaveChangesAsync();
    }

    /// <summary>Der Fehler-Vergleich 2615 der Vereinspartie: Kandidaten und Antwort-Tests gerechnet, je ein Takt der Pumpe.
    /// Danach ist alles da, was der Vergleich vor 0.974.0 brauchte.</summary>
    private async Task<int> RepliesDoneAsync(MoveComparisonService svc)
    {
        var dto = await svc.CreateAsync(House, new CreateMoveComparisonRequest { Fen = Fen2615, Moves = [Best, Weak], Depth = 30, Lang = "de" });
        var row = await _db.MoveComparisons.SingleAsync(c => c.Id == dto.Id);
        row.GameAnalysisId = Analysis;
        row.Ply = 39;
        await _db.SaveChangesAsync();
        await FinishJobAsync(After(Best), BestJson);
        await FinishJobAsync(After(Weak), WeakJson);
        await svc.PumpOneAsync(dto.Id);
        // 21.Dxd6 geht nach 20...e4 nicht (der Bauer d4 steht noch) — 21.Lc1 und 21.Sc2 werden gerechnet.
        await FinishJobAsync(After(Best, "g5c1"), Bc1Json);
        await FinishJobAsync(After(Best, "a3c2"), Nc2Json);
        await svc.PumpOneAsync(dto.Id);
        await BackgroundRunsEndedAsync(dto.Id);   // ohne flache Sicht ist der Vergleich hier schon „wird erklärt“
        return dto.Id;
    }

    private async Task<MoveComparison> ReloadAsync(int id)
    {
        _db.ChangeTracker.Clear();
        return await _db.MoveComparisons.Include(c => c.Lines).SingleAsync(c => c.Id == id);
    }

    private static List<MoveComparisonLine> Of(MoveComparison c, MoveComparisonLineKind kind) =>
        c.Lines.Where(l => l.Kind == kind).OrderBy(l => l.Id).ToList();

    private static string? Explanation(MoveComparison c) =>
        c.Lines.Single(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == Weak).Explanation;

    private Task<ClubGameSummaryReadiness> ReadinessAsync() => new ClubGameSummaries(_db).ReadinessAsync(ClubGame);

    private const string FableText = """{"explanation":"20...dxc3 erlaubt 21.Dxd6 mit Damentausch.\n\n20...e4 ist stark, weil 21.Txe4 an 21...dxc3 22.bxc3 Dxa3 mit Figurengewinn scheitert; auf 21.Lf1 folgt 21...e3."}""";

    [Fact]
    public async Task Ablauf_ErstFlacheSicht_dannNurDieVerlockendenTief_gescheiterteBlockiertNicht_ZusammenfassungBereit()
    {
        SeedGame(new DateOnly(2026, 11, 8));
        var svc = Service(Fable());
        var id = await RepliesDoneAsync(svc);

        // Erster Durchgang: nur die flache Sicht — Tiefe 1, fünf Linien, eigene Sitzung; noch keine tiefe Rechnung.
        var c = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Replies, c.Status);
        var glance = Assert.Single(Of(c, MoveComparisonLineKind.BestMoveGlance));
        Assert.Empty(Of(c, MoveComparisonLineKind.BestMoveTry));
        var look = await _db.AnalysisJobs.SingleAsync(j => j.Status != AnalysisJobStatus.Done);
        Assert.Equal(glance.AnalysisJobId, look.Id);
        Assert.Equal((After(Best), 1, 5), (look.Fen, look.TargetDepth, look.MultiPv));
        Assert.True(look.Background);
        Assert.Equal($"rh-bg-{House}-fresh-{look.Id}", AnalysisJobEngineResolver.SessionOf(look));
        Assert.Empty(_fable.Prompts);
        Assert.False((await ReadinessAsync()).Ready);   // die Zusammenfassung wartet

        // Zweiter Durchgang: die zwei verlockenden Züge in der Tiefe des Vergleichs, je eine Linie.
        await FinishJobAsync(After(Best), GlanceJson, depth: 1);
        await svc.PumpOneAsync(id);
        c = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Replies, c.Status);
        Assert.Equal(1, Of(c, MoveComparisonLineKind.BestMoveGlance).Single().ReachedDepth);
        Assert.Equal(["g5f4", "e1e4"], Of(c, MoveComparisonLineKind.BestMoveTry).Select(l => l.ReplyUci));
        var jobs = await _db.AnalysisJobs.Where(j => j.Status != AnalysisJobStatus.Done).ToListAsync();
        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, j =>
        {
            Assert.Equal((30, 1), (j.TargetDepth, j.MultiPv));
            Assert.True(j.Background);
            Assert.Equal($"rh-bg-{House}", AnalysisJobEngineResolver.SessionOf(j));
        });
        Assert.False((await ReadinessAsync()).Ready);

        // 21.Txe4 wird fertig, der Auftrag für 21.Lf4 verschwindet — offen hält auf, gescheitert nicht.
        await FinishJobAsync(After(Best, "e1e4"), TakesJson);
        await svc.PumpOneAsync(id);
        Assert.Equal(MoveComparisonStatus.Replies, (await ReloadAsync(id)).Status);
        var lost = (await ReloadAsync(id)).Lines.Single(l => l.ReplyUci == "g5f4").AnalysisJobId!.Value;
        _db.AnalysisJobs.Remove(await _db.AnalysisJobs.SingleAsync(j => j.Id == lost));
        await _db.SaveChangesAsync();
        await svc.PumpOneAsync(id);

        c = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Explaining, c.Status);
        Assert.Equal(MoveComparisonLineState.Failed, c.Lines.Single(l => l.ReplyUci == "g5f4").State);
        Assert.Empty(_db.AnalysisJobs);   // eingesammelt und gelöscht wie alle Aufträge eines Vergleichs

        // Ein weiterer Takt plant nichts noch einmal.
        await svc.PumpOneAsync(id);
        c = await ReloadAsync(id);
        Assert.Single(Of(c, MoveComparisonLineKind.BestMoveGlance));
        Assert.Equal(2, Of(c, MoveComparisonLineKind.BestMoveTry).Count);
        Assert.Empty(_db.AnalysisJobs);

        _fable.Answers.Enqueue(FableText);
        await svc.ExplainAsync(id, CancellationToken.None);
        c = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Done, c.Status);
        Assert.Equal("claude-fable-5-1", c.Model);
        Assert.Contains("weil 21.Rxe4 an 21...dxc3 22.bxc3 Qxa3 mit Figurengewinn scheitert", Explanation(c));
        var prompt = Assert.Single(_fable.Prompts);
        Assert.Contains(Block2615, prompt.Replace("\r", ""));
        // Der alte Befund steht weiter da: die beste Antwort auf den Fehlzug geht nach dem besten Zug nicht.
        Assert.Contains("- 21.Qxd6: not possible after 20...e4.", prompt);
        Assert.Contains("Beantworte ZUERST die Frage, die der Leser am Brett hat", _fable.Systems[0]);
        Assert.Empty(_local.Prompts);

        var ready = await ReadinessAsync();
        Assert.True(ready.Ready);
        Assert.Equal((1, 1), (ready.ComparisonsDone, ready.ComparisonsTotal));
    }

    [Fact]
    public async Task Ablauf_MitVerteidigungen_flacheSichtImSelbenDurchgang()
    {
        SeedGame(new DateOnly(2026, 11, 8));
        var svc = Service(Fable(defenceLines: 4));
        var id = await RepliesDoneAsync(svc);

        var c = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Replies, c.Status);
        Assert.Single(Of(c, MoveComparisonLineKind.BestMoveGlance));
        // Die Widerlegung 21.Dxd6 Sxd6 22.bxc3 hat keine andere naheliegende Verteidigung — sonst stünde sie hier daneben.
        Assert.Empty(Of(c, MoveComparisonLineKind.Defence));
        Assert.Single(_db.AnalysisJobs.Where(j => j.Status != AnalysisJobStatus.Done));
    }

    [Fact]
    public async Task Ablauf_FlacheSichtLaesstSichNichtEinreihen_keineVerlockendenAntworten_VergleichLaeuftWeiter()
    {
        SeedGame(new DateOnly(2026, 11, 8));
        var noTries = Service(Fable(tries: 0));
        var id = await RepliesDoneAsync(noTries);
        Assert.Equal(MoveComparisonStatus.Explaining, (await ReloadAsync(id)).Status);
        // Die Haus-Engine fällt weg: kein Auftrag lässt sich mehr anlegen.
        _db.LichessEngineCredentials.RemoveRange(_db.LichessEngineCredentials);
        await _db.SaveChangesAsync();

        var svc = Service(Fable());
        await svc.PumpOneAsync(id);

        var c = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Explaining, c.Status);   // nichts offen → nicht hängen bleiben
        Assert.Equal(MoveComparisonLineState.Failed, Of(c, MoveComparisonLineKind.BestMoveGlance).Single().State);
        await svc.PumpOneAsync(id);
        Assert.Empty(Of(await ReloadAsync(id), MoveComparisonLineKind.BestMoveTry));

        _fable.Answers.Enqueue("""{"explanation":"20...dxc3 erlaubt 21.Dxd6."}""");
        await svc.ExplainAsync(id, CancellationToken.None);
        c = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Done, c.Status);
        Assert.DoesNotContain("Obvious replies", Assert.Single(_fable.Prompts));
        Assert.True((await ReadinessAsync()).Ready);
    }

    [Fact]
    public async Task PartieVorDemStichtag_oderDeckelNull_oderOhneFable_keineFlacheSicht()
    {
        SeedGame(new DateOnly(2026, 11, 5));   // vor dem Stichtag schreibt das lokale Modell — keine Engine-Zeit dafür
        var id = await RepliesDoneAsync(Service(Fable()));
        var c = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Explaining, c.Status);
        Assert.Empty(Of(c, MoveComparisonLineKind.BestMoveGlance));
        Assert.Empty(_db.AnalysisJobs);

        var round = await _db.LeagueRounds.SingleAsync();
        round.Date = new DateOnly(2026, 11, 8);
        await _db.SaveChangesAsync();
        await Service(Fable(tries: 0)).PumpOneAsync(id);   // abgeschaltet
        Assert.Empty(Of(await ReloadAsync(id), MoveComparisonLineKind.BestMoveGlance));
        await Service(null).PumpOneAsync(id);              // ohne Fable
        Assert.Empty(Of(await ReloadAsync(id), MoveComparisonLineKind.BestMoveGlance));
        Assert.Empty(_db.AnalysisJobs);
    }

    [Fact]
    public async Task EigenerVergleichEinesNutzers_keineFlacheSicht()
    {
        var svc = Service(Fable());
        var dto = await svc.CreateAsync(House, new CreateMoveComparisonRequest { Fen = Fen2615, Moves = [Best, Weak], Depth = 24, Lang = "de" });
        await FinishJobAsync(After(Best), BestJson);
        await FinishJobAsync(After(Weak), WeakJson);
        await svc.PumpOneAsync(dto.Id);
        await FinishJobAsync(After(Best, "g5c1"), Bc1Json);
        await FinishJobAsync(After(Best, "a3c2"), Nc2Json);
        await svc.PumpOneAsync(dto.Id);

        var c = await ReloadAsync(dto.Id);
        Assert.Equal(MoveComparisonStatus.Explaining, c.Status);
        Assert.All(c.Lines, l => Assert.True(l.Kind is MoveComparisonLineKind.Candidate or MoveComparisonLineKind.Reply));
    }

    [Fact]
    public async Task Reexplain_Altbestand_holtFlacheSichtUndTxE4Nach_undErklaertNeu()
    {
        SeedGame(new DateOnly(2026, 11, 8));
        // Stand vor 0.974.0: fertig erklärt, ohne flache Sicht — der Text erzählt 21.Lf1 nach.
        var old = Service(Fable(tries: 0));
        var id = await RepliesDoneAsync(old);
        _fable.Answers.Enqueue("""{"explanation":"Nach 20...e4 kommt Weiß mit 21.Lf1 e3 22.fxe3 dxc3 23.Dxd6 doch noch zum Damentausch."}""");
        await old.ExplainAsync(id, CancellationToken.None);
        var before = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Done, before.Status);
        Assert.Empty(Of(before, MoveComparisonLineKind.BestMoveGlance));
        Assert.DoesNotContain("Obvious replies", _fable.Prompts[^1]);
        Assert.True((await ReadinessAsync()).Ready);

        var svc = Service(Fable());
        Assert.True(await svc.ReexplainAsync(id));
        Assert.False((await ReadinessAsync()).Ready);
        await svc.PumpOneAsync(id);
        var c = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Replies, c.Status);
        Assert.Single(Of(c, MoveComparisonLineKind.BestMoveGlance));
        // Die alte Begründung bleibt stehen, solange gerechnet wird.
        Assert.StartsWith("Nach 20...e4 kommt Weiß mit 21.Bf1", Explanation(c));

        await FinishJobAsync(After(Best), GlanceJson, depth: 1);
        await svc.PumpOneAsync(id);
        Assert.Equal(["g5f4", "e1e4"], Of(await ReloadAsync(id), MoveComparisonLineKind.BestMoveTry).Select(l => l.ReplyUci));
        await FinishJobAsync(After(Best, "g5f4"), Bf4Json);
        await FinishJobAsync(After(Best, "e1e4"), TakesJson);
        await svc.PumpOneAsync(id);
        Assert.Equal(MoveComparisonStatus.Explaining, (await ReloadAsync(id)).Status);
        Assert.False((await ReadinessAsync()).Ready);

        _fable.Answers.Enqueue(FableText);
        await svc.ExplainAsync(id, CancellationToken.None);
        c = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Done, c.Status);
        Assert.Contains("weil 21.Rxe4 an 21...dxc3 22.bxc3 Qxa3 mit Figurengewinn scheitert", Explanation(c));
        Assert.Contains(Block2615, _fable.Prompts[^1].Replace("\r", ""));
        Assert.DoesNotContain("21.Bf4", _fable.Prompts[^1]);
        Assert.True((await ReadinessAsync()).Ready);
    }

    [Fact]
    public async Task Reexplain_GescheiterteFlacheSicht_bekommtEinenNeuenAnlauf()
    {
        SeedGame(new DateOnly(2026, 11, 8));
        var svc = Service(Fable());
        var id = await RepliesDoneAsync(svc);
        // Der Auftrag der flachen Sicht verschwindet: gescheitert, der Vergleich wird ohne sie erklärt.
        _db.AnalysisJobs.RemoveRange(_db.AnalysisJobs);
        await _db.SaveChangesAsync();
        await svc.PumpOneAsync(id);
        _fable.Answers.Enqueue("""{"explanation":"20...dxc3 erlaubt 21.Dxd6."}""");
        await svc.ExplainAsync(id, CancellationToken.None);
        var c = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Done, c.Status);
        Assert.Equal(MoveComparisonLineState.Failed, Of(c, MoveComparisonLineKind.BestMoveGlance).Single().State);

        var again = Service(Fable());
        Assert.True(await again.ReexplainAsync(id));
        await again.PumpOneAsync(id);

        c = await ReloadAsync(id);
        Assert.Equal(MoveComparisonStatus.Replies, c.Status);
        Assert.Equal(MoveComparisonLineState.Pending, Of(c, MoveComparisonLineKind.BestMoveGlance).Single().State);
        Assert.Equal(1, (await _db.AnalysisJobs.SingleAsync()).TargetDepth);
    }

    [Fact]
    public async Task Vergleichsseite_zeigtDieZusatzlinienNicht_zaehltSieAberMit()
    {
        SeedGame(new DateOnly(2026, 11, 8));
        var svc = Service(Fable());
        var id = await RepliesDoneAsync(svc);
        await FinishJobAsync(After(Best), GlanceJson, depth: 1);
        await svc.PumpOneAsync(id);

        var dto = (await svc.GetForAnalysisAsync(Analysis, id))!;
        Assert.Equal("replies", dto.Status);
        Assert.Equal(2, dto.Candidates.Count);
        Assert.All(dto.Candidates, k => Assert.DoesNotContain(k.Tests, t => t.ReplyUci is "e1e4" or "g5f4" or ""));
        Assert.Equal(2, dto.Pending);   // der Fortschritt der Seite zählt sie mit
        Assert.Equal(8, dto.Total);     // 2 Kandidaten, 3 Antworten, flache Sicht, 2 verlockende Antworten
    }
}
