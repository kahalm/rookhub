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
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Tests.Engines;

/// <summary>Begründungen der Fehler-Vergleiche der Vereinspartien über Claude Fable (0.911.0): nur ab dem Stichtag (Spieldatum
/// der Runde, sonst Hochladen, sonst Analyse), Tagesdeckel, Rückfall aufs lokale Modell, ohne Schlüssel aus.</summary>
public class ClubFableExplanationsTests : IDisposable
{
    /// <summary>1.e4 d5 — Weiß am Zug: exd5 (bester) gegen Nc3 (Partiezug).</summary>
    private const string SCANDI = "rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 2";
    private const int House = 5;
    private const int ClubGame = 990401;
    private const int Analysis = 990501;

    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly AnalysisJobService _jobs;
    private readonly FakeLlm _local = new("fake-local") { Local = true };
    private readonly FakeLlm _fable = new(ClubFableExplanations.DefaultModel);
    private DateTime _now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

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

    private const string LocalText = """{"explanation":"Nach Nc3 nimmt dein Gegner mit dxe4 den Bauern; nach exd5 gibt es das nicht mehr."}""";
    // Fable schreibt deutsche Figurenbuchstaben (Sc3) — gespeichert wird englische SAN.
    private const string FableText = """{"explanation":"2.Sc3 lässt 2...dxe4 zu, Weiß verliert den Bauern e4.\n\n2.exd5 Dxd5 3.Sc3 gewinnt Zeit gegen die Dame.\n\nMerksatz: Erst den angegriffenen Bauern klären."}""";

    public ClubFableExplanationsTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _encryption = new EncryptionService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!",
        }).Build());
        _jobs = new AnalysisJobService(_db, new FakeControl());
        _db.AppUsers.Add(new AppUser { Id = House, Username = "house", PasswordHash = "x" });
        _db.LichessEngineCredentials.Add(new LichessEngineCredential
        {
            UserId = House, EncryptedToken = _encryption.Encrypt("lip_tok"), BackgroundEngineIds = "eei_bg",
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private ClubFableExplanations Fable(int cap = 200, bool enabled = true, IClaudeJsonClient? client = null) =>
        new(client ?? _fable, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ClubMistakeComparisons:ExplainWithAnthropic"] = enabled ? "true" : "false",
            ["ClubMistakeComparisons:AnthropicDailyCap"] = cap.ToString(),
        }).Build(), NullLogger<ClubFableExplanations>.Instance, () => _now);

    private MoveComparisonService Service(ClubFableExplanations? fable) => new(_db, _jobs, _local, new MoveComparisonExplainJobs(),
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        NullLogger<MoveComparisonService>.Instance, fable);

    /// <summary>Vereinspartie samt Hauptanalyse; <paramref name="roundDate"/> = zugeordnete Ligarunde.</summary>
    private void SeedGame(DateOnly? roundDate, DateTime? uploaded, DateTime analysisCreated, int game = ClubGame, int analysis = Analysis)
    {
        _db.LeagueClubGames.Add(new LeagueClubGame { Id = game, ClubId = 1, Year = 2026, White = "A", Black = "B", Pgn = "", MovesHash = $"h{game}",
            CreatedAt = uploaded, LeagueTnr = roundDate is null ? null : 4711, LeagueRound = roundDate is null ? null : game });
        if (roundDate is { } d) _db.LeagueRounds.Add(new LeagueRound { Tnr = 4711, Round = game, Date = d });
        _db.GameAnalyses.Add(new GameAnalysis { Id = analysis, UserId = House, Pgn = "", StartFen = "", Origin = GameAnalysisOrigin.Club,
            LeagueClubGameId = game, Status = GameAnalysisStatus.Done, CreatedAt = analysisCreated });
        _db.SaveChanges();
    }

    private async Task FinishJobAsync(string fen, string resultJson)
    {
        var job = await _db.AnalysisJobs.SingleAsync(j => j.Fen == fen && j.Status != AnalysisJobStatus.Done);
        job.Status = AnalysisJobStatus.Done;
        job.ReachedDepth = 22;
        job.ResultJson = resultJson;
        await _db.SaveChangesAsync();
    }

    private static string Pvs(params (int cp, string moves)[] pvs)
        => "{\"depth\":22,\"time\":1000,\"nodes\":5000000,\"pvs\":["
           + string.Join(',', pvs.Select(p => $"{{\"depth\":22,\"cp\":{p.cp},\"moves\":[{string.Join(',', p.moves.Split(' ').Select(m => $"\"{m}\""))}]}}"))
           + "]}";

    /// <summary>Ein Zugvergleich der Vereinspartie bis „wird erklärt", dann erklären. → der gespeicherte Vergleich.</summary>
    private async Task<MoveComparison> ExplainClubComparisonAsync(MoveComparisonService svc, int analysis = Analysis)
    {
        var dto = await svc.CreateAsync(House, new CreateMoveComparisonRequest { Fen = SCANDI, Moves = ["e4d5", "b1c3"], Depth = 22, Lang = "de" });
        var row = await _db.MoveComparisons.SingleAsync(c => c.Id == dto.Id);
        row.GameAnalysisId = analysis;
        await _db.SaveChangesAsync();
        await FinishJobAsync(MoveComparisonBoard.PlayUci(SCANDI, "e4d5")!, Pvs((45, "d8d5 b1c3 d5a5")));
        await FinishJobAsync(MoveComparisonBoard.PlayUci(SCANDI, "b1c3")!, Pvs((20, "d5e4 c3e4"), (35, "g8f6 e4e5")));
        await svc.PumpOneAsync(dto.Id);
        foreach (var job in await _db.AnalysisJobs.Where(j => j.Status != AnalysisJobStatus.Done).ToListAsync())
            await FinishJobAsync(job.Fen, Pvs((70, "c2c4 c7c6")));
        await svc.PumpOneAsync(dto.Id);
        Assert.Equal(MoveComparisonStatus.Explaining, (await _db.MoveComparisons.AsNoTracking().SingleAsync(c => c.Id == dto.Id)).Status);
        await svc.ExplainAsync(dto.Id, CancellationToken.None);
        _db.ChangeTracker.Clear();
        return await _db.MoveComparisons.Include(c => c.Lines).SingleAsync(c => c.Id == dto.Id);
    }

    private static string? WeakExplanation(MoveComparison c) =>
        c.Lines.Single(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == "b1c3").Explanation;

    [Fact]
    public async Task PartieVorDemStichtag_lokalesModell()
    {
        SeedGame(new DateOnly(2026, 11, 5), null, new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc));
        _local.Answers.Enqueue(LocalText);

        var c = await ExplainClubComparisonAsync(Service(Fable()));

        Assert.Empty(_fable.Prompts);
        Assert.Equal("fake-local", c.Model);
        Assert.StartsWith("Nach Nc3", WeakExplanation(c));
    }

    [Fact]
    public async Task PartieAbDemStichtag_Fable_deutscherPrompt_englischGespeichert()
    {
        SeedGame(new DateOnly(2026, 11, 7), null, new DateTime(2026, 11, 7, 22, 0, 0, DateTimeKind.Utc));
        _fable.Answers.Enqueue(FableText);

        var c = await ExplainClubComparisonAsync(Service(Fable()));

        Assert.Empty(_local.Prompts);
        Assert.Equal("claude-fable-5-1", c.Model);
        var text = WeakExplanation(c)!;
        Assert.StartsWith("2.Nc3 lässt 2...dxe4 zu", text);
        Assert.Contains("2.exd5 Qxd5 3.Nc3", text);
        // 0.917.0: ein frei erfundener Merksatz fällt weg — nur die kuratierte Liste zählt.
        Assert.DoesNotContain("Merksatz:", text);
        Assert.Contains("Merksätze (nur diese, höchstens einer, per Kennung)", _fable.Prompts[0]);
        Assert.Contains("- raum-und-tausch: ", _fable.Prompts[0]);
        Assert.Contains("Klubspielern (Elo 1500–2000)", _fable.Systems[0]);
        Assert.Contains("L (Läufer), S (Springer)", _fable.Systems[0]);
        Assert.True(_fable.Prompts[0].Contains("Weaker move: 2.Nc3"), _fable.Prompts[0]);
    }

    [Fact]
    public async Task Merksatz_perKennung_wirdZumFestenWortlaut()
    {
        SeedGame(new DateOnly(2026, 11, 7), null, new DateTime(2026, 11, 7, 22, 0, 0, DateTimeKind.Utc));
        _fable.Answers.Enqueue(FableText.Replace("Merksatz: Erst den angegriffenen Bauern klären.", "Merksatz: raum-und-tausch"));

        var c = await ExplainClubComparisonAsync(Service(Fable()));

        var text = WeakExplanation(c)!;
        Assert.EndsWith("\n\nMerksatz: " + ChessMaxims.ById("raum-und-tausch")!.Text, text);
    }

    [Fact]
    public async Task FableOhneLokalesModell_reichtFuerDieErklaerung()
    {
        SeedGame(new DateOnly(2026, 11, 8), null, _now);
        _local.IsConfigured = false;
        _fable.Answers.Enqueue(FableText);

        var c = await ExplainClubComparisonAsync(Service(Fable()));
        Assert.Equal(MoveComparisonStatus.Done, c.Status);
        Assert.Equal("claude-fable-5-1", c.Model);
    }

    [Fact]
    public async Task FableFehler_oderFremderZug_Rueckfall()
    {
        SeedGame(new DateOnly(2026, 11, 8), null, _now);
        _fable.Answers.Enqueue(null);                                             // Fehler/Ablehnung: kein zweiter bezahlter Versuch
        _local.Answers.Enqueue(LocalText);

        var c = await ExplainClubComparisonAsync(Service(Fable()));
        Assert.Single(_fable.Prompts);
        Assert.Equal("fake-local", c.Model);
        Assert.StartsWith("Nach Nc3", WeakExplanation(c));
    }

    [Fact]
    public async Task FableNenntZweimalFremdenZug_Rueckfall()
    {
        SeedGame(new DateOnly(2026, 11, 8), null, _now);
        _fable.Answers.Enqueue("""{"explanation":"2.Sc3 Lb4 fesselt."}""");       // Lb4 steht in keiner Linie
        _fable.Answers.Enqueue("""{"explanation":"2.Sc3 Lb4 fesselt immer noch."}""");
        _local.Answers.Enqueue(LocalText);

        var c = await ExplainClubComparisonAsync(Service(Fable()));
        Assert.Equal(2, _fable.Prompts.Count);
        Assert.Equal("fake-local", c.Model);
    }

    [Fact]
    public async Task Tagesdeckel_erreicht_Rueckfall_neuerTag_wiederFable()
    {
        SeedGame(new DateOnly(2026, 11, 8), null, _now);
        SeedGame(new DateOnly(2026, 11, 8), null, _now, ClubGame + 1, Analysis + 1);
        var fable = Fable(cap: 1);
        _fable.Answers.Enqueue(FableText);
        Assert.Equal("claude-fable-5-1", (await ExplainClubComparisonAsync(Service(fable))).Model);

        _local.Answers.Enqueue(LocalText);
        var second = await ExplainClubComparisonAsync(Service(fable), Analysis + 1);
        Assert.Single(_fable.Prompts);
        Assert.Equal("fake-local", second.Model);

        // Nach einem Neustart (neuer Zähler) zählt der heute schon mit Fable fertige Vergleich mit.
        Assert.False(Fable(cap: 1).TryTakeBudget(doneToday: 1));
        _now = _now.AddDays(1);
        Assert.True(fable.TryTakeBudget(doneToday: 0));
    }

    [Fact]
    public void OhneSchluessel_oderPlatzhalter_oderAus_istFableAus()
    {
        IConfiguration Config(string? key, string? enabled = "true") => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Anthropic:ApiKey"] = key, ["ClubMistakeComparisons:ExplainWithAnthropic"] = enabled,
        }).Build();
        ClubFableExplanations Of(IConfiguration cfg) =>
            new(ClubFableExplanations.CreateClient(cfg, NullLogger.Instance), cfg, NullLogger<ClubFableExplanations>.Instance);

        Assert.False(Of(Config(null)).Available);
        Assert.False(Of(Config("change_me")).Available);
        Assert.False(Of(Config("your_anthropic_key")).Available);
        Assert.False(Of(Config("sk-ant-echt", enabled: null)).Available);       // Vorgabe aus (Dev trägt denselben Schlüssel)
        var on = Of(Config("sk-ant-echt"));
        Assert.True(on.Available);
        Assert.Equal("claude-fable-5-1", on.Model);
        Assert.Equal(new DateOnly(2026, 11, 6), on.From);
        Assert.Equal(200, on.DailyCap);
        // Eigener Schlüssel geht vor dem Konto-Schlüssel, ein Platzhalter dort fällt auf den Konto-Schlüssel zurück.
        var own = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ClubMistakeComparisons:AnthropicApiKey"] = "change_me", ["Anthropic:ApiKey"] = null,
        }).Build();
        Assert.False(ClubFableExplanations.CreateClient(own, NullLogger.Instance).IsConfigured);
    }

    [Fact]
    public async Task KeinSchluessel_lokalesModell_auchAbDemStichtag()
    {
        SeedGame(new DateOnly(2026, 11, 8), null, _now);
        _fable.IsConfigured = false;
        _local.Answers.Enqueue(LocalText);

        var c = await ExplainClubComparisonAsync(Service(Fable()));
        Assert.Empty(_fable.Prompts);
        Assert.Equal("fake-local", c.Model);
    }

    [Fact]
    public async Task Spieldatum_Runde_sonstHochladen_sonstAnalyse()
    {
        SeedGame(new DateOnly(2026, 11, 7), new DateTime(2026, 10, 1), new DateTime(2026, 10, 2), 990411, 990511);
        SeedGame(null, new DateTime(2026, 11, 6, 9, 0, 0), new DateTime(2026, 10, 2), 990412, 990512);
        SeedGame(null, null, new DateTime(2026, 11, 5, 23, 0, 0), 990413, 990513);
        Assert.Equal(new DateOnly(2026, 11, 7), await ClubFableExplanations.GameDateAsync(_db, 990511));
        Assert.Equal(new DateOnly(2026, 11, 6), await ClubFableExplanations.GameDateAsync(_db, 990512));
        Assert.Equal(new DateOnly(2026, 11, 5), await ClubFableExplanations.GameDateAsync(_db, 990513));
        Assert.Null(await ClubFableExplanations.GameDateAsync(_db, 424242));

        var fable = Fable();
        Assert.True(await fable.AppliesAsync(_db, new MoveComparison { GameAnalysisId = 990512 }));
        Assert.False(await fable.AppliesAsync(_db, new MoveComparison { GameAnalysisId = 990513 }));
        Assert.False(await fable.AppliesAsync(_db, new MoveComparison()));        // eigener Vergleich eines Nutzers
    }

    [Theory]
    [InlineData("2.Sc3 Lb4 3.Dxd5 Txe1+ 0-0 e8=D", "2.Nc3 Bb4 3.Qxd5 Rxe1+ 0-0 e8=Q")]
    [InlineData("Der Läufer auf Sd5? Stellung", "Der Läufer auf Nd5? Stellung")]
    [InlineData("Kein Zug: Dame, Turm, Lehrer, S-Bahn", "Kein Zug: Dame, Turm, Lehrer, S-Bahn")]
    public void EnglishSan_ersetztNurFigurenzuege(string german, string english)
        => Assert.Equal(english, ClubFableExplanations.EnglishSan(german));
}
