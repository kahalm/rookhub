using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Meisterpartien im Hintergrund analysieren (2026-09-28): „zu den gleichen Zeiten wie die Übersetzung, auf allen
/// Direktengines, aber wenn ein anderer Auftrag reinkommt, hat der Vorrang".
/// </summary>
public class MasterAnalysisSchedulerTests : IDisposable
{
    private const int Owner = 5;
    private const int Viewer = 7;
    private readonly AppDbContext _db;

    // 10 bzw. 20 Halbzuege.
    private const string Short = "[White \"Morphy\"]\n[Black \"Duke\"]\n[Result \"*\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 5. O-O Be7 *";
    private const string Longer = "[White \"Kasparov\"]\n[Black \"Karpov\"]\n[Result \"*\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 5. O-O Be7 6. Re1 b5 7. Bb3 d6 8. c3 O-O 9. h3 Nb8 10. d4 Nbd7 *";

    public MasterAnalysisSchedulerTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _db.AppUsers.Add(new AppUser { Id = Owner, Username = "kahalm", PasswordHash = "x", IsAdmin = true });
        _db.AppUsers.Add(new AppUser { Id = Viewer, Username = "gast", PasswordHash = "x" });
        var cred = new LichessEngineCredential { UserId = Owner, EncryptedToken = "enc", ShareAsHouseEngine = true };
        cred.SetBackgroundEngines(Enumerable.Range(1, 16).Select(i => $"rhe_t{i}"));
        _db.LichessEngineCredentials.Add(cred);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!",
    }).Build();

    private GameAnalysisService Analyses(QuietHours? quiet = null) =>
        new(_db, new AnalysisJobService(_db, new EncryptionService(Config())),
            new CommentSetService(_db, NullLogger<CommentSetService>.Instance),
            NullLogger<GameAnalysisService>.Instance, quiet: quiet);

    private static MasterAnalysisScheduler Scheduler(QuietHours quiet) =>
        new(null!, quiet, Config(), NullLogger<MasterAnalysisScheduler>.Instance);

    private static readonly QuietHours Never = new("");

    /// <summary>Montag 28.09.2026, 10:00 in Wien — mitten in der Sperrzeit.</summary>
    private static QuietHours WorkingHours() => new(QuietHours.DefaultSpec, "Europe/Vienna",
        new QuietHoursTests.ManualTime { Now = new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero) });

    private LibraryGame Library(int id, string pgn = Short, int? commented = 3, LibraryGameStatus status = LibraryGameStatus.New)
    {
        var g = new LibraryGame { Id = id, SourceFile = "t.pgn", Pgn = pgn, White = "W", Black = "B", CommentedPlies = commented,
            Status = status, MovesHash = $"h{id}" };
        _db.LibraryGames.Add(g);
        _db.SaveChanges();
        return g;
    }

    [Fact]
    public async Task Tick_nimmtDieErsteKommentiertePartie_alsHintergrundarbeitAufDerHausEngine()
    {
        Library(1, commented: null);   // unkommentiert — kommt erst spaeter
        Library(2);
        Library(3);

        var id = await Scheduler(Never).TickOnceAsync(_db, Analyses(), default);

        var analysis = await _db.GameAnalyses.Include(g => g.Positions).SingleAsync(g => g.Id == id);
        Assert.Equal(2, analysis.LibraryGameId);
        Assert.Equal(GameAnalysisOrigin.Library, analysis.Origin);
        Assert.Equal(Owner, analysis.UserId);
        Assert.False(analysis.IsPublic);   // NICHT in den Punktepartie-Bestand
        Assert.Equal(GameAnalysisDefaults.GuessTargetDepth, analysis.TargetDepth);
        var jobs = await _db.AnalysisJobs.ToListAsync();
        Assert.NotEmpty(jobs);
        Assert.All(jobs, j => Assert.True(j.Background));   // weicht jedem anderen Auftrag
    }

    [Fact]
    public async Task Tick_legtNachwieNeueAn_bisAlleEnginesArbeitHaben()
    {
        Library(1); Library(2); Library(3);   // je 10 Halbzuege, 16 Engines
        var scheduler = Scheduler(Never);
        var svc = Analyses();

        Assert.NotNull(await scheduler.TickOnceAsync(_db, svc, default));   // 10 offen < 16
        Assert.NotNull(await scheduler.TickOnceAsync(_db, svc, default));   // 20 offen ≥ 16 danach
        Assert.Null(await scheduler.TickOnceAsync(_db, svc, default));      // genug Arbeit da
        Assert.Equal(new int?[] { 1, 2 }, await _db.GameAnalyses.OrderBy(g => g.Id).Select(g => g.LibraryGameId).ToListAsync());
    }

    [Fact]
    public async Task Tick_ueberspringtAussortierte_Dubletten_undSchonAnalysierte_danachDieUnkommentierten()
    {
        Library(1, status: LibraryGameStatus.Rejected);
        Library(2, status: LibraryGameStatus.Duplicate);
        Library(3);
        Library(4, commented: 0);
        _db.GameAnalyses.Add(new GameAnalysis { UserId = Viewer, Title = "schon da", Pgn = Short, LibraryGameId = 3,
            Status = GameAnalysisStatus.Done });
        await _db.SaveChangesAsync();

        var id = await Scheduler(Never).TickOnceAsync(_db, Analyses(), default);
        Assert.Equal(4, (await _db.GameAnalyses.SingleAsync(g => g.Id == id)).LibraryGameId);
    }

    [Fact]
    public async Task Tick_inDerSperrzeit_legtNichtsAn()
    {
        Library(1);
        Assert.Null(await Scheduler(WorkingHours()).TickOnceAsync(_db, Analyses(), default));
        Assert.Empty(_db.GameAnalyses);
    }

    [Fact]
    public async Task Sperrzeit_eineLaufendeMeisterpartieReihtNichtsEin_eineEigenePartieSchon()
    {
        var quiet = Analyses(WorkingHours());
        var master = await quiet.CreateLibraryBatchAsync(Owner, Library(1).Id, Short, "Meister");
        Assert.Empty(_db.AnalysisJobs);                        // steht still bis 17:00

        var own = await quiet.CreateAsync(Owner, new() { Pgn = Longer, TargetDepth = 20, MultiPv = 1 });
        Assert.NotEmpty(await _db.GameAnalysisPositions.Where(p => p.GameAnalysisId == own.Id && p.AnalysisJobId != null).ToListAsync());
        Assert.NotEqual(master.Id, own.Id);
    }

    [Fact]
    public async Task Tick_unspielbaresPgn_wirdUebersprungen_derStapelLaeuftWeiter()
    {
        Library(1, pgn: "kein PGN");
        Library(2);
        var scheduler = Scheduler(Never);
        var svc = Analyses();

        Assert.Null(await scheduler.TickOnceAsync(_db, svc, default));        // 1 unspielbar
        var id = await scheduler.TickOnceAsync(_db, svc, default);
        Assert.Equal(2, (await _db.GameAnalyses.SingleAsync(g => g.Id == id)).LibraryGameId);
    }

    [Fact]
    public async Task EigenePartieDesBesitzers_wartetNichtAufDieMeisterpartien()
    {
        var svc = Analyses();
        await svc.CreateLibraryBatchAsync(Owner, Library(1, Longer).Id, Longer, "Meister");   // 20 offene Stellungen

        var own = await svc.CreateAsync(Owner, new() { Pgn = Short, TargetDepth = 20, MultiPv = 1 });

        var jobs = await _db.GameAnalysisPositions.Where(p => p.GameAnalysisId == own.Id && p.AnalysisJobId != null).CountAsync();
        Assert.Equal(10, jobs);   // sofort eingereiht, nicht hinter der aelteren Meisterpartie
        Assert.All(await _db.AnalysisJobs.Where(j => _db.GameAnalysisPositions.Any(p => p.GameAnalysisId == own.Id && p.AnalysisJobId == j.Id)).ToListAsync(),
            j => Assert.False(j.Background));
    }

    [Fact]
    public async Task Meisterpartie_istFuerAlleLesbar_aberInKeinerListe()
    {
        var svc = Analyses();
        var master = await svc.CreateLibraryBatchAsync(Owner, Library(1).Id, Short, "Meister");

        Assert.NotNull(await svc.GetPlayableHeadAsync(Viewer, master.Id));      // fremder Nutzer darf oeffnen
        Assert.DoesNotContain(await svc.ListAsync(Owner, includeSavedGames: true), a => a.Id == master.Id);
        Assert.DoesNotContain(await svc.ListPublicAsync(), a => a.Id == master.Id);
    }

    [Fact]
    public async Task AngeforderteMeisterpartie_nutztDieStapelAnalyse_stattNeuZuRechnen()
    {
        var svc = Analyses();
        var game = Library(1);
        var master = await svc.CreateLibraryBatchAsync(Owner, game.Id, Short, "Meister");
        var library = new LibraryGameService(_db, svc);

        var result = await library.RequestAsync(Viewer, game.Id);

        Assert.True(result.AlreadyPlayable);
        Assert.Equal(master.Id, result.Analysis!.Id);
        Assert.Single(_db.GameAnalyses);
    }
}
