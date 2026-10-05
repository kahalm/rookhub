using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Tests;

/// <summary>
/// Partie-Analyse mit Knotenziel (lc0 & Co.): ganze Partie zusätzlich zu Stockfish von einer ausdrücklich gewählten
/// Engine rechnen lassen. Validierung, Weitergabe an die Stellungs-Aufträge, Engine des Besitzers (A4-003), Neustart.
/// </summary>
public class GameAnalysisNodesTests : IDisposable
{
    private const string Game = """
[Event "Testpartie"]
[White "Anderssen"]
[Black "Kieseritzky"]
[Result "1-0"]

1. e4 e5 2. f4 exf4 3. Bc4 Qh4+ 4. Kf1 b5 5. Bxb5 Nf6 6. Nf3 Qh6 7. d3 Nh5 1-0
""";

    private const string Lc0 = "eei_lc0";
    private const int User = 5;
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly EncryptionService _encryption;
    private ServiceProvider? _sp;

    private sealed class LichessStub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"id":"eei_lc0","name":"lc0","clientSecret":"s","maxThreads":4,"maxHash":256}]""",
                    Encoding.UTF8, "application/json"),
            });
    }

    public GameAnalysisNodesTests()
    {
        _encryption = new EncryptionService(Config());
    }

    public void Dispose() { _db.Dispose(); _sp?.Dispose(); }

    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!" }).Build();

    /// <summary>Nutzer mit Stockfish (<c>eei_sf</c>) und lc0 als Hintergrund-Engines.</summary>
    private async Task SeedAsync(int id = User, string engines = "eei_sf," + Lc0)
    {
        _db.AppUsers.Add(new AppUser { Id = id, Username = $"u{id}", PasswordHash = "x", IsAdmin = true });
        _db.LichessEngineCredentials.Add(new LichessEngineCredential
        {
            UserId = id, EncryptedToken = _encryption.Encrypt("lip_tok"), BackgroundEngineIds = engines,
        });
        await _db.SaveChangesAsync();
    }

    private GameAnalysisService Service(bool withRegistry = false, string? explicitOnly = null)
    {
        var values = new Dictionary<string, string?> { ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!" };
        if (explicitOnly is not null) values[$"{ExplicitOnlyEngines.ConfigKey}:0"] = explicitOnly;
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        EngineRegistry? registry = null;
        if (withRegistry)
        {
            var lichess = new LichessEngineService(new HttpClient(new LichessStub()), new MemoryCache(new MemoryCacheOptions()),
                cfg, NullLogger<LichessEngineService>.Instance);
            _sp ??= new ServiceCollection().BuildServiceProvider();
            registry = new EngineRegistry(_db, _encryption, lichess,
                new EngineSelectorDirectory(_sp.GetRequiredService<IServiceScopeFactory>()), new LocalBrokerOptions());
        }
        var jobs = new AnalysisJobService(_db, null, registry, cfg);
        return new GameAnalysisService(_db, jobs, new CommentSetService(_db, NullLogger<CommentSetService>.Instance),
            NullLogger<GameAnalysisService>.Instance);
    }

    private Task<List<AnalysisJob>> JobsAsync() => _db.AnalysisJobs.ToListAsync();

    [Fact]
    public async Task Create_mitKnoten_gibtEngineUndKnotenAnJedenStellungsAuftragWeiter()
    {
        await SeedAsync();
        var dto = await Service().CreateAsync(User, new CreateGameAnalysisRequest
            { Pgn = Game, EngineId = Lc0, TargetNodes = 50_000, MultiPv = 3 });

        Assert.Equal(50_000, dto.TargetNodes);
        Assert.Equal(Lc0, dto.EngineId);
        var jobs = await JobsAsync();
        Assert.NotEmpty(jobs);
        Assert.All(jobs, j => { Assert.Equal(Lc0, j.EngineId); Assert.Equal(50_000, j.TargetNodes); Assert.Equal(3, j.MultiPv); });
    }

    [Fact]
    public async Task Create_mitKnoten_haengtDieKnotenzahlAnDenTitel()
    {
        await SeedAsync();
        var dto = await Service().CreateAsync(User, new CreateGameAnalysisRequest
            { Pgn = Game, EngineId = Lc0, TargetNodes = 50_000 });

        Assert.Equal("Anderssen – Kieseritzky (50k nodes)", dto.Title);
    }

    [Theory]
    [InlineData(500L, "500")]
    [InlineData(50_000L, "50k")]
    [InlineData(1_500L, "1.5k")]
    [InlineData(1_000_000L, "1M")]
    [InlineData(2_500_000L, "2.5M")]
    public void NodesLabel_kuerztDieZahl(long nodes, string expected) =>
        Assert.Equal(expected, GameAnalysisService.NodesLabel(nodes));

    [Fact]
    public async Task Create_ohneKnoten_bleibtBeiTiefe_undTitelUnveraendert()
    {
        await SeedAsync();
        var dto = await Service().CreateAsync(User, new CreateGameAnalysisRequest { Pgn = Game });

        Assert.Null(dto.TargetNodes);
        Assert.Equal("Anderssen – Kieseritzky", dto.Title);
        Assert.All(await JobsAsync(), j => Assert.Null(j.TargetNodes));
    }

    [Theory]
    [InlineData(999L)]
    [InlineData(50_000_001L)]
    [InlineData(0L)]
    public async Task Create_knotenAusserhalbDesBereichs_wirdAbgewiesen(long nodes)
    {
        await SeedAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => Service().CreateAsync(User,
            new CreateGameAnalysisRequest { Pgn = Game, EngineId = Lc0, TargetNodes = nodes }));
        Assert.Empty(_db.GameAnalyses);
    }

    [Fact]
    public async Task Create_knotenOhneEngine_wirdAbgewiesen()
    {
        await SeedAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => Service().CreateAsync(User,
            new CreateGameAnalysisRequest { Pgn = Game, TargetNodes = 50_000 }));
        Assert.Empty(_db.GameAnalyses);
    }

    [Fact]
    public async Task Create_knotenAufNichtManuellerHerkunft_wirdAbgewiesen()
    {
        await SeedAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => Service().CreateAsync(User,
            new CreateGameAnalysisRequest { Pgn = Game, EngineId = Lc0, TargetNodes = 50_000 },
            origin: GameAnalysisOrigin.Guess));
    }

    [Fact]
    public async Task Create_engineEinesAnderen_wirdAbgewiesen_undLegtNichtsAn()
    {
        await SeedAsync();
        // Ein anderes Konto mit eigener Direkt-Engine (rhe_…): für den Nutzer fremd.
        _db.AppUsers.Add(new AppUser { Id = 77, Username = "haus", PasswordHash = "x", IsAdmin = true });
        var reg = new ExternalEngineRegistration
        {
            Id = ProviderSecrets.NewEngineId(), UserId = 77, Name = "Haus 1", ClientSecret = "cs",
            ProviderSelector = ProviderSecrets.Selector("secret-0123456789abc"), MaxThreads = 4, MaxHash = 256,
        };
        _db.ExternalEngineRegistrations.Add(reg);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Service(withRegistry: true).CreateAsync(User,
            new CreateGameAnalysisRequest { Pgn = Game, EngineId = reg.Id, TargetNodes = 50_000 }));
        Assert.Empty(_db.GameAnalyses);
        Assert.Empty(_db.AnalysisJobs);
    }

    [Fact]
    public async Task Create_ausdruecklicheEngine_funktioniertAuchWennSieNurAufAnforderungRechnet()
    {
        await SeedAsync();
        var dto = await Service(explicitOnly: Lc0).CreateAsync(User, new CreateGameAnalysisRequest
            { Pgn = Game, EngineId = Lc0, TargetNodes = 25_000 });

        Assert.All(await JobsAsync(), j => Assert.Equal(Lc0, j.EngineId));
        Assert.Equal(25_000, dto.TargetNodes);
    }

    [Fact]
    public async Task ZweiteAnalyse_ohneEngine_waehltNieDieNurAufAnforderungEngine()
    {
        await SeedAsync();
        var svc = Service(explicitOnly: Lc0);
        await svc.CreateAsync(User, new CreateGameAnalysisRequest { Pgn = Game, EngineId = Lc0, TargetNodes = 25_000 });
        var sf = await svc.CreateAsync(User, new CreateGameAnalysisRequest { Pgn = Game });

        Assert.Null(sf.TargetNodes);
        Assert.True(string.IsNullOrEmpty(sf.LastError), sf.LastError);
        var sfJobs = (await JobsAsync()).Where(j => j.TargetNodes is null).ToList();
        Assert.NotEmpty(sfJobs);
        Assert.All(sfJobs, j => Assert.Equal("eei_sf", j.EngineId));
    }

    [Fact]
    public async Task KnotenAnalyse_hinterTiefenAnalyse_wartetNicht()
    {
        await SeedAsync();
        var svc = Service(explicitOnly: Lc0);
        await svc.CreateAsync(User, new CreateGameAnalysisRequest { Pgn = Game });
        var lc0 = await svc.CreateAsync(User, new CreateGameAnalysisRequest { Pgn = Game, EngineId = Lc0, TargetNodes = 25_000 });

        Assert.Contains(await JobsAsync(), j => j.TargetNodes == 25_000);
        Assert.Equal("running", lc0.Status);
    }

    [Fact]
    public async Task Restart_behaeltDenKnotenmodus()
    {
        await SeedAsync();
        var svc = Service();
        var dto = await svc.CreateAsync(User, new CreateGameAnalysisRequest
            { Pgn = Game, EngineId = Lc0, TargetNodes = 50_000 });
        var before = (await JobsAsync()).Select(j => j.Id).ToList();

        var after = await svc.RestartAsync(User, dto.Id);

        Assert.Equal(50_000, after!.TargetNodes);
        var now = (await JobsAsync()).Where(j => !before.Contains(j.Id)).ToList();
        Assert.NotEmpty(now);   // frische Aufträge …
        Assert.All(now, j => { Assert.Equal(Lc0, j.EngineId); Assert.Equal(50_000, j.TargetNodes); });   // … weiter im Knotenmodus
    }
}
