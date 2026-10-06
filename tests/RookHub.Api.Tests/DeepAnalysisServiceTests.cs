using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>„Tiefe Analyse" (0.686.0): Stockfish bis Tiefe 40 auf der Haus-Engine, Lc0 bis 500k Knoten.</summary>
public class DeepAnalysisServiceTests : IDisposable
{
    private const int House = 5;
    private const int Member = 9;
    private const string Lc0 = "rhe_lc0deep001";
    private const string Fen1 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1";
    private const string Fen2 = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2";
    private readonly AppDbContext _db;

    public DeepAnalysisServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _db.AppUsers.Add(new AppUser { Id = House, Username = "haus", PasswordHash = "x", IsAdmin = true });
        _db.AppUsers.Add(new AppUser { Id = Member, Username = "mitglied", PasswordHash = "x" });
        var cred = new LichessEngineCredential { UserId = House, EncryptedToken = "enc", ShareAsHouseEngine = true };
        cred.SetBackgroundEngines(["rhe_sf1"]);
        _db.LichessEngineCredentials.Add(cred);
        _db.ExternalEngineRegistrations.Add(new ExternalEngineRegistration
        {
            Id = Lc0, UserId = House, Name = ClubSecondEngineScheduler.DefaultEngineName, ClientSecret = "s",
            ProviderSelector = "sel", MaxThreads = 1, MaxHash = 16, Variants = "chess",
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private DeepAnalysisService Service() =>
        new(_db, new AnalysisJobService(_db), new ConfigurationBuilder().Build());

    [Fact]
    public async Task Start_legtStockfishTiefe40UndLc0MitKnotenzielAn()
    {
        var r = await Service().StartAsync(Member, Fen1);

        Assert.Equal((40, (long?)null, 3), (r.Stockfish!.TargetDepth, r.Stockfish.TargetNodes, r.Stockfish.MultiPv));
        Assert.Equal((Lc0, (long?)500_000), (r.Lc0!.EngineId, r.Lc0.TargetNodes));
        var jobs = await _db.AnalysisJobs.ToListAsync();
        Assert.All(jobs, j => Assert.Equal((Member, (int?)House), (j.UserId, j.EngineOwnerUserId)));
        Assert.Equal("rhe_sf1", jobs.Single(j => j.Title == DeepAnalysisService.StockfishTitle).EngineId);
    }

    [Fact]
    public async Task Start_dieselbeStellung_nimmtDieVorhandenenAuftraege()
    {
        var first = await Service().StartAsync(Member, Fen1);
        var again = await Service().StartAsync(Member, Fen1);
        Assert.Equal((first.Stockfish!.Id, first.Lc0!.Id), (again.Stockfish!.Id, again.Lc0!.Id));
        Assert.Equal(2, await _db.AnalysisJobs.CountAsync());
    }

    [Fact]
    public async Task Start_andereStellung_raeumtDieOffenenDerVorigenAb()
    {
        await Service().StartAsync(Member, Fen1);
        await Service().StartAsync(Member, Fen2);
        Assert.All(await _db.AnalysisJobs.ToListAsync(), j => Assert.Equal(Fen2, j.Fen));
    }

    [Fact]
    public async Task Start_ungueltigeStellung_wirft()
        => await Assert.ThrowsAnyAsync<ArgumentException>(() => Service().StartAsync(Member, "kein fen"));
}
