using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Authorization;
using RookHub.Api.Controllers.EngineBroker;
using RookHub.Api.Data;
using RookHub.Api.DTOs.EngineBroker;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.EngineBroker;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Tests.EngineBroker;

/// <summary>
/// Admin-Reiter „Engines“ (0.951.0): Hintergrund-Zuordnung je Engine und Konto umschalten
/// (<c>PUT /api/admin/engines/{engineId}/background</c>). Dieselben Regeln wie die Liste im Profil (Deckel, Normalisierung,
/// Registrierung), idempotent, <c>PrunedBackgroundNames</c> konsistent, Übersichts-Cache verworfen, Vorgang protokolliert.
/// Feste Ids im 99xxxx-Bereich; jede Testmethode hat eigene InMemory-DB, eigenen Cache und eigene Singletons.
/// </summary>
public class AdminEngineBackgroundTests : IDisposable
{
    private const int Admin = 990701, Owner = 990702, Other = 990703;
    private const string RheA = "rhe_bgA000000001", RheB = "rhe_bgB000000002", RheNew = "rhe_bgN000000003",
        RheOrphan = "rhe_bgX000000009", EeiOk = "eei_bgLichess001", EeiGone = "eei_bgLichess002";

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly AppDbContext _db;
    private readonly ServiceProvider _sp;
    private readonly EncryptionService _encryption;
    private readonly LichessStub _lichessHttp = new();
    private readonly LichessEngineService _lichess;
    private readonly EngineSelectorDirectory _directory;
    private readonly EngineHub _hub;
    private readonly LocalBrokerOptions _options = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly CapturingLogger<AdminEnginesController> _log = new();

    public AdminEngineBackgroundTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options);
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        _sp = services.BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!",
        }).Build();
        _encryption = new EncryptionService(config);
        _lichess = new LichessEngineService(new HttpClient(_lichessHttp), new MemoryCache(new MemoryCacheOptions()), config,
            NullLogger<LichessEngineService>.Instance);
        _directory = new EngineSelectorDirectory(_sp.GetRequiredService<IServiceScopeFactory>());
        _hub = new EngineHub(_options, () => DateTime.UtcNow, startSweeper: false);
    }

    public void Dispose()
    {
        _hub.Dispose();
        _db.Dispose();
        _sp.Dispose();
        _cache.Dispose();
    }

    /// <summary>Lichess-Engine-Liste: <see cref="EeiOk"/> gehört dem Konto; <see cref="Fail"/> = Netzfehler.</summary>
    private sealed class LichessStub : HttpMessageHandler
    {
        public bool Fail;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Fail) throw new HttpRequestException("lichess down");
            var json = $$"""[{"id":"{{EeiOk}}","name":"Lichess-Engine","clientSecret":"ees_x","maxThreads":2,"maxHash":64}]""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private EngineOverviewService Overview() => new(_db, _cache, _hub, _directory, _options, new EngineActivityTracker(),
        new AnalysisJobLive(), new EngineThroughputRecorder(),
        new BackgroundPause(_sp.GetRequiredService<IServiceScopeFactory>(), new ConfigurationBuilder().Build(),
            NullLogger<BackgroundPause>.Instance),
        new ConfigurationBuilder().Build());

    private AdminEnginesController Controller()
    {
        var registry = new EngineRegistry(_db, _encryption, _lichess, _directory, _options);
        return new AdminEnginesController(Overview(), _db, _encryption, registry, _log)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, Admin.ToString()), new Claim(ClaimTypes.Name, "bg-admin"),
                    ], "test")),
                },
            },
        };
    }

    private static ExternalEngineRegistration Reg(string id, int user, string name) => new()
    {
        Id = id, UserId = user, Name = name, ClientSecret = "cs", ProviderSelector = "sel" + id, MaxThreads = 2, MaxHash = 64,
        Variants = "chess", CreatedAt = DateTime.UtcNow.AddDays(-1), LastSeenAt = DateTime.UtcNow,
    };

    /// <summary>Konten + Registrierungen; Hintergrund-Liste des Besitzers wie angegeben (<c>null</c> = keine Zeile).</summary>
    private async Task SeedAsync(string? ownerList, string? otherList = null, string? ownerToken = null, string? pruned = null)
    {
        _db.AppUsers.AddRange(
            new AppUser { Id = Admin, Username = "bg-admin", PasswordHash = "x", IsAdmin = true },
            new AppUser { Id = Owner, Username = "bg-owner", PasswordHash = "x" },
            new AppUser { Id = Other, Username = "bg-other", PasswordHash = "x" });
        _db.ExternalEngineRegistrations.AddRange(Reg(RheA, Owner, "Server 1"), Reg(RheB, Owner, "Server 2"),
            Reg(RheNew, Owner, "Server 3"));
        if (ownerList is not null || ownerToken is not null || pruned is not null)
            _db.LichessEngineCredentials.Add(new LichessEngineCredential
            {
                UserId = Owner, BackgroundEngineIds = ownerList, PrunedBackgroundNames = pruned,
                EncryptedToken = ownerToken is null ? string.Empty : _encryption.Encrypt(ownerToken),
                UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
        if (otherList is not null)
            _db.LichessEngineCredentials.Add(new LichessEngineCredential { UserId = Other, BackgroundEngineIds = otherList });
        await _db.SaveChangesAsync();
    }

    private async Task<LichessEngineCredential?> CredAsync(int user)
    {
        await using var fresh = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options);
        return await fresh.LichessEngineCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == user);
    }

    private Task<IActionResult> Put(string engine, int user, bool enabled)
        => Controller().SetBackground(engine, new SetEngineBackgroundRequest(user, enabled), CancellationToken.None);

    private static SetEngineBackgroundResponse OkBody(IActionResult r)
        => Assert.IsType<SetEngineBackgroundResponse>(Assert.IsType<OkObjectResult>(r).Value);

    private static (int Status, string? Code, string Message) Error(IActionResult r)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(r);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj.Value));
        return (obj.StatusCode ?? 0,
            doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null,
            doc.RootElement.GetProperty("message").GetString()!);
    }

    // ── Berechtigung ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Endpoint_IsPut_UnderAdminOnlyController()
    {
        var type = typeof(AdminEnginesController);
        Assert.Equal("perm:" + Permissions.AdminOnly, type.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        var m = type.GetMethod(nameof(AdminEnginesController.SetBackground))!;
        Assert.Equal("{engineId}/background", m.GetCustomAttribute<HttpPutAttribute>()!.Template);
        Assert.Null(m.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(m.GetCustomAttribute<AuthorizeAttribute>());      // keine schwächere Richtlinie an der Aktion
    }

    // ── an / aus ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Enable_AppendsAtEnd_KeepsOrder_AndLogs()
    {
        await SeedAsync($"{RheB},{RheA}");

        var body = OkBody(await Put(" " + RheNew + " ", Owner, enabled: true));

        Assert.True(body.Changed);
        Assert.Equal((RheNew, Owner, true), (body.EngineId, body.UserId, body.Enabled));
        Assert.Equal([RheB, RheA, RheNew], body.BackgroundEngineIds);
        Assert.Equal([RheB, RheA, RheNew], (await CredAsync(Owner))!.BackgroundEngines);
        var entry = Assert.Single(_log.Events, e => e.Level == LogLevel.Information);
        Assert.Equal("an", entry.State["Action"]);
        Assert.Equal(RheNew, entry.State["EngineId"]);
        Assert.Equal(Owner, entry.State["UserId"]);
        Assert.Equal(Admin, entry.State["AdminId"]);
    }

    [Fact]
    public async Task Enable_WithoutCredentialRow_CreatesRowWithoutToken()
    {
        await SeedAsync(ownerList: null);

        Assert.True(OkBody(await Put(RheA, Owner, enabled: true)).Changed);

        var cred = (await CredAsync(Owner))!;
        Assert.Equal([RheA], cred.BackgroundEngines);
        Assert.Equal(string.Empty, cred.EncryptedToken);
    }

    [Fact]
    public async Task Disable_RemovesOnlyThisEngine_KeepsOrder()
    {
        await SeedAsync($"{RheB},{RheA},{RheNew}");

        var body = OkBody(await Put(RheA, Owner, enabled: false));

        Assert.True(body.Changed);
        Assert.Equal([RheB, RheNew], (await CredAsync(Owner))!.BackgroundEngines);
        Assert.Equal("aus", Assert.Single(_log.Events, e => e.Level == LogLevel.Information).State["Action"]);
    }

    [Fact]
    public async Task AlreadyInOrOut_Is200_WithoutWrite_AndWithoutLog()
    {
        await SeedAsync($"{RheA}");
        var before = (await CredAsync(Owner))!.UpdatedAt;

        var on = OkBody(await Put(RheA, Owner, enabled: true));
        var off = OkBody(await Put(RheB, Owner, enabled: false));

        Assert.False(on.Changed);
        Assert.False(off.Changed);
        Assert.Equal([RheA], on.BackgroundEngineIds);
        Assert.Equal(before, (await CredAsync(Owner))!.UpdatedAt);
        Assert.DoesNotContain(_log.Events, e => e.Level == LogLevel.Information);
    }

    // ── Deckel ───────────────────────────────────────────────────────────────────────────────────────────

    private static string Filler(int n) => string.Join(',', Enumerable.Range(0, n).Select(i => $"rhe_bgF{i:D9}"));

    [Fact]
    public async Task Enable_OnFullList_Is400_BackgroundListFull_AndListUnchanged()
    {
        await SeedAsync(Filler(EngineSettingsService.MaxBackgroundEngines));

        var (status, code, message) = Error(await Put(RheA, Owner, enabled: true));

        Assert.Equal(400, status);
        Assert.Equal(ApiErrorCodes.BackgroundListFull, code);
        Assert.Contains(EngineSettingsService.MaxBackgroundEngines.ToString(), message);
        Assert.Equal(EngineSettingsService.MaxBackgroundEngines, (await CredAsync(Owner))!.BackgroundEngines.Count);
    }

    /// <summary>Prod 10.11.2026: eine Liste mit 33 Einträgen bei Deckel 32 — Entfernen muss trotzdem gehen.</summary>
    [Fact]
    public async Task Disable_OnListOverTheCap_StillWorks()
    {
        await SeedAsync(Filler(EngineSettingsService.MaxBackgroundEngines) + "," + RheA);

        Assert.True(OkBody(await Put(RheA, Owner, enabled: false)).Changed);
        Assert.Equal(EngineSettingsService.MaxBackgroundEngines, (await CredAsync(Owner))!.BackgroundEngines.Count);
    }

    // ── fremdes Konto, unregistriert, Lichess ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ForeignAccount_CanBeRemovedFrom_ButNotAddedTo()
    {
        await SeedAsync($"{RheA}", otherList: $"{RheA},{RheOrphan}");

        Assert.True(OkBody(await Put(RheA, Other, enabled: false)).Changed);
        Assert.Equal([RheOrphan], (await CredAsync(Other))!.BackgroundEngines);
        Assert.Equal([RheA], (await CredAsync(Owner))!.BackgroundEngines);   // die Liste des Besitzers bleibt

        var (status, code, _) = Error(await Put(RheA, Other, enabled: true));  // gehört dem Konto nicht
        Assert.Equal((404, ApiErrorCodes.EngineNotFound), (status, code));
        Assert.Equal([RheOrphan], (await CredAsync(Other))!.BackgroundEngines);
    }

    [Fact]
    public async Task UnregisteredEngine_CanBeRemoved_ButNotAdded()
    {
        await SeedAsync($"{RheOrphan},{RheA},{EeiGone}");

        Assert.True(OkBody(await Put(RheOrphan, Owner, enabled: false)).Changed);
        Assert.True(OkBody(await Put(EeiGone, Owner, enabled: false)).Changed);   // Lichess-Kennung ohne Token: entfernen geht
        Assert.Equal([RheA], (await CredAsync(Owner))!.BackgroundEngines);

        var (status, code, _) = Error(await Put(RheOrphan, Owner, enabled: true));
        Assert.Equal((404, ApiErrorCodes.EngineNotFound), (status, code));
    }

    [Fact]
    public async Task LichessEngine_WithoutToken_Is400()
    {
        await SeedAsync($"{RheA}");
        var (status, _, message) = Error(await Put(EeiOk, Owner, enabled: true));
        Assert.Equal((400, "No Lichess token stored"), (status, message));
    }

    [Fact]
    public async Task LichessEngine_WithToken_IsAdded_UnknownIs404_DownIs502()
    {
        await SeedAsync($"{RheA}", ownerToken: "lip_bgtoken");

        Assert.True(OkBody(await Put(EeiOk, Owner, enabled: true)).Changed);
        Assert.Equal([RheA, EeiOk], (await CredAsync(Owner))!.BackgroundEngines);

        Assert.Equal(404, Error(await Put(EeiGone, Owner, enabled: true)).Status);

        _lichessHttp.Fail = true;
        Assert.Equal(502, Error(await Put("eei_bgLichess003", Owner, enabled: true)).Status);
        Assert.Equal([RheA, EeiOk], (await CredAsync(Owner))!.BackgroundEngines);
    }

    [Fact]
    public async Task UnknownAccount_Is404_UserNotFound_InvalidId_Is400()
    {
        await SeedAsync($"{RheA}");
        Assert.Equal((404, ApiErrorCodes.UserNotFound), (Error(await Put(RheA, 990799, true)).Status, Error(await Put(RheA, 990799, true)).Code));
        Assert.Equal(400, Error(await Put(new string('x', EngineSettingsService.MaxEngineIdLength + 1), Owner, true)).Status);
        Assert.Equal(400, Error(await Put("   ", Owner, false)).Status);
    }

    // ── PrunedBackgroundNames ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PrunedNames_ForgetTheEngine_InBothDirections()
    {
        // „Server 1“ steht in der Liste UND (Altlast) in der Merkliste; „Server 3“ nur in der Merkliste.
        await SeedAsync($"{RheA}", pruned: "Server 1\nServer 3\nAlter PC");

        await Put(RheA, Owner, enabled: false);      // von Hand entfernt → die Wartung trägt sie nicht wieder ein
        Assert.Equal(["Server 3", "Alter PC"], (await CredAsync(Owner))!.PrunedBackground);

        await Put(RheNew, Owner, enabled: true);     // von Hand aufgenommen → nicht ein zweites Mal von der Wartung
        var cred = (await CredAsync(Owner))!;
        Assert.Equal(["Alter PC"], cred.PrunedBackground);
        Assert.Equal([RheNew], cred.BackgroundEngines);
    }

    [Fact]
    public async Task PrunedNames_AfterManualRemove_ReRegistrationDoesNotReAdd()
    {
        await SeedAsync($"{RheA},{RheB}", pruned: "Server 1");
        await Put(RheA, Owner, enabled: false);

        // Der Provider meldet „Server 1“ neu an (neue Kennung): die Wartung darf ihn nicht in die Liste zurücklegen.
        var registrations = new ExternalEngineRegistrationService(_db, _directory, NullLogger<ExternalEngineRegistrationService>.Instance);
        _db.ExternalEngineRegistrations.Remove(await _db.ExternalEngineRegistrations.SingleAsync(r => r.Id == RheA));
        await _db.SaveChangesAsync();
        var result = await registrations.SaveAsync(Owner, null, new ExternalEngineRegistrationRequest
        {
            Name = "Server 1", MaxThreads = 2, MaxHash = 64, Variants = ["chess"], ProviderSecret = "bg-provider-secret-0001",
        });
        Assert.NotNull(result.Engine);
        Assert.Equal([RheB], (await CredAsync(Owner))!.BackgroundEngines);
    }

    // ── Cache ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Change_InvalidatesOverviewCache_NoChange_KeepsIt()
    {
        await SeedAsync($"{RheA}");
        var overview = Overview();
        var first = await overview.GetAsync(CancellationToken.None);
        Assert.Equal([Owner], first.Engines.Single(e => e.Id == RheA).BackgroundFor.Select(u => u.UserId));
        Assert.Same(first, await overview.GetAsync(CancellationToken.None));          // zwischengespeichert

        await Put(RheB, Owner, enabled: false);                                       // schon draußen: nichts zu verwerfen
        Assert.Same(first, await overview.GetAsync(CancellationToken.None));

        await Put(RheA, Owner, enabled: false);
        var after = await overview.GetAsync(CancellationToken.None);
        Assert.NotSame(first, after);
        Assert.Empty(after.Engines.Single(e => e.Id == RheA).BackgroundFor);
    }
}
