using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>Vereinspartien bekommen automatisch eine zweite Analyse auf Lc0 mit Knotenziel (0.684.0) — die Stockfish-Analyse
/// bleibt die der Partie.</summary>
public class ClubSecondEngineSchedulerTests : IDisposable
{
    private const int Owner = 5;
    private const string Lc0 = "rhe_lc0test01";
    private const string Pgn = "[White \"Morphy\"]\n[Black \"Duke\"]\n[Result \"*\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 *";
    private readonly AppDbContext _db;

    public ClubSecondEngineSchedulerTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _db.AppUsers.Add(new AppUser { Id = Owner, Username = "kahalm", PasswordHash = "x", IsAdmin = true });
        _db.ExternalEngineRegistrations.Add(new ExternalEngineRegistration
        {
            Id = Lc0, UserId = Owner, Name = ClubSecondEngineScheduler.DefaultEngineName, ClientSecret = "s",
            ProviderSelector = "sel", MaxThreads = 1, MaxHash = 16, Variants = "chess",
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private GameAnalysisService Analyses() =>
        new(_db, new AnalysisJobService(_db), new CommentSetService(_db, NullLogger<CommentSetService>.Instance),
            NullLogger<GameAnalysisService>.Instance);

    private static ClubSecondEngineScheduler Scheduler(string? name = null) =>
        new(null!, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ClubSecondEngine:EngineName"] = name,
        }).Build(), NullLogger<ClubSecondEngineScheduler>.Instance);

    private LeagueClubGame ClubGame()
    {
        var g = new LeagueClubGame { White = "Morphy", Black = "Duke", Pgn = Pgn, Plies = 6, Year = 2025,
            MovesHash = Guid.NewGuid().ToString("N") };
        _db.LeagueClubGames.Add(g);
        _db.SaveChanges();
        return g;
    }

    [Fact]
    public async Task Tick_legtLc0AnalyseMitKnotenzielAn_neuestePartieZuerst()
    {
        var older = ClubGame();
        var newer = ClubGame();

        var id = await Scheduler().TickOnceAsync(_db, Analyses(), default);

        var a = await _db.GameAnalyses.SingleAsync(g => g.Id == id);
        Assert.Equal((GameAnalysisOrigin.Club, (int?)newer.Id, Lc0, (long?)100_000, 3),
            (a.Origin, a.LeagueClubGameId, a.EngineId, a.TargetNodes, a.MultiPv));
        Assert.EndsWith("(100k nodes)", a.Title);
        // eine offen → keine zweite
        Assert.Null(await Scheduler().TickOnceAsync(_db, Analyses(), default));
        Assert.NotEqual(older.Id, a.LeagueClubGameId);
    }

    [Fact]
    public async Task Lc0Analyse_verdecktDieStockfishAnalyseNicht()
    {
        var game = ClubGame();
        await Scheduler().TickOnceAsync(_db, Analyses(), default);

        // Die Stockfish-Analyse der Partie fehlt noch — der Meisterpartien-Takt muss sie weiter anlegen.
        var cred = new LichessEngineCredential { UserId = Owner, EncryptedToken = "enc", ShareAsHouseEngine = true };
        cred.SetBackgroundEngines(["rhe_sf1"]);
        _db.LichessEngineCredentials.Add(cred);
        await _db.SaveChangesAsync();
        var master = new MasterAnalysisScheduler(null!, new QuietHours(""), new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!" }).Build(),
            NullLogger<MasterAnalysisScheduler>.Instance);
        var sfId = await master.TickOnceAsync(_db, Analyses(), default);

        var sf = await _db.GameAnalyses.SingleAsync(g => g.Id == sfId);
        Assert.Equal(((int?)game.Id, (string?)null), (sf.LeagueClubGameId, sf.EngineId));
    }

    [Fact]
    public async Task OhneEngineNamen_oderUnbekannteEngine_passiertNichts()
    {
        ClubGame();
        Assert.Null(await Scheduler("Gibt es nicht").TickOnceAsync(_db, Analyses(), default));
        Assert.Empty(_db.GameAnalyses);
    }

    [Fact]
    public async Task Vereinsleser_findenDieLc0Analyse_andereNicht()
    {
        _db.AppUsers.Add(new AppUser { Id = 9, Username = "mitglied", PasswordHash = "x" });
        await _db.SaveChangesAsync();
        ClubGame();
        var id = (await Scheduler().TickOnceAsync(_db, Analyses(), default))!.Value;
        string[] ucis = ["e2e4", "e7e5", "g1f3", "b8c6", "f1b5", "a7a6"];

        Assert.Empty(await Analyses().SameGameAsync(9, ucis));
        var seen = await Analyses().SameGameAsync(9, ucis, clubReader: true);
        Assert.Equal(id, Assert.Single(seen).Id);
        Assert.Null(await Analyses().EvalsOfAsync(9, id));
        Assert.NotNull(await Analyses().EvalsOfAsync(9, id, clubReader: true));
    }
}
