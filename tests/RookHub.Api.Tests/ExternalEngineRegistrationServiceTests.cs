using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Tests;

/// <summary>
/// Registrierung „RookHub direkt": dieselben Regeln, die der offizielle Provider von Lichess kennt —
/// der Name ist die Identität, das providerSecret wird nur als Selector gespeichert, Grenzen wie bei
/// Lichess. Der Provider selbst bleibt unverändert; jede Abweichung hier bräche ihn auf fremden Rechnern.
/// </summary>
public class ExternalEngineRegistrationServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ServiceProvider _sp;
    private readonly ExternalEngineRegistrationService _svc;
    private readonly EngineSelectorDirectory _directory;

    public ExternalEngineRegistrationServiceTests()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name));
        _sp = services.BuildServiceProvider();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options);
        _directory = new EngineSelectorDirectory(_sp.GetRequiredService<IServiceScopeFactory>());
        _svc = new ExternalEngineRegistrationService(_db, _directory, NullLogger<ExternalEngineRegistrationService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sp.Dispose();
    }

    private async Task<int> UserAsync(string name = "kahalm")
    {
        var u = new AppUser { Username = name, PasswordHash = "x" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u.Id;
    }

    /// <summary>Genau der Rumpf, den <c>example-provider.py</c> schickt.</summary>
    private static ExternalEngineRegistrationRequest Req(string name = "Server Live", string secret = "0123456789abcdefSECRET",
        int threads = 8, int hash = 2048, params string[] variants) => new()
    {
        Name = name,
        MaxThreads = threads,
        MaxHash = hash,
        Variants = variants.Length == 0 ? ["chess"] : [.. variants],
        ProviderSecret = secret,
    };

    [Fact]
    public async Task Post_CreatesEngine_WithRheId_SelectorInsteadOfSecret()
    {
        var uid = await UserAsync();
        var r = await _svc.SaveAsync(uid, null, Req());

        Assert.Equal(200, r.Status);
        var e = Assert.IsType<ExternalEngineRegistration>(r.Engine);
        Assert.StartsWith("rhe_", e.Id);
        Assert.Equal(16, e.Id.Length);                 // so lang wie eei_… (CSV-Spalte der Hintergrund-Liste)
        Assert.Matches("^rhe_[A-Za-z0-9]{12}$", e.Id);
        Assert.Equal(ProviderSecrets.Selector("0123456789abcdefSECRET"), e.ProviderSelector);
        Assert.Equal(64, e.ProviderSelector.Length);
        // Das Secret selbst steht NIRGENDS in der Zeile.
        var row = await _db.ExternalEngineRegistrations.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("SECRET", row.ProviderSelector + row.ClientSecret + row.Variants + row.Name);
        Assert.Equal(43, row.ClientSecret.Length);
    }

    [Fact]
    public async Task Post_WithExistingName_UpdatesThatEngine_InsteadOfDuplicating()
    {
        // Der Name ist die Identität: scheiterte beim letzten Start die GET-Liste, schickt der Provider POST.
        var uid = await UserAsync();
        var first = (await _svc.SaveAsync(uid, null, Req(secret: "first-secret-0123456"))).Engine!;
        var second = (await _svc.SaveAsync(uid, null, Req(secret: "second-secret-012345", threads: 4))).Engine!;

        Assert.Equal(first.Id, second.Id);
        var row = await _db.ExternalEngineRegistrations.AsNoTracking().SingleAsync();
        Assert.Equal(4, row.MaxThreads);
        Assert.Equal(ProviderSecrets.Selector("second-secret-012345"), row.ProviderSelector);
    }

    [Fact]
    public async Task Put_ChangesSecret_KeepsIdAndClientSecret()
    {
        // Der Provider wechselt sein Secret bei jedem Start — die Kennung (Hintergrund-Liste!) bleibt.
        var uid = await UserAsync();
        var created = (await _svc.SaveAsync(uid, null, Req(secret: "start-one-0123456789"))).Engine!;
        var clientSecret = created.ClientSecret;

        var r = await _svc.SaveAsync(uid, created.Id, Req(secret: "start-two-0123456789", hash: 512));
        Assert.Equal(200, r.Status);
        var row = await _db.ExternalEngineRegistrations.AsNoTracking().SingleAsync();
        Assert.Equal(created.Id, row.Id);
        Assert.Equal(clientSecret, row.ClientSecret);
        Assert.Equal(512, row.MaxHash);
        Assert.Equal(ProviderSecrets.Selector("start-two-0123456789"), row.ProviderSelector);
    }

    [Fact]
    public async Task Put_ForeignOrUnknownId_Is404()
    {
        var alice = await UserAsync("alice");
        var bob = await UserAsync("bob");
        var aliceEngine = (await _svc.SaveAsync(alice, null, Req())).Engine!;

        Assert.Equal(404, (await _svc.SaveAsync(bob, aliceEngine.Id, Req())).Status);
        Assert.Equal(404, (await _svc.SaveAsync(alice, "rhe_doesnotexist", Req())).Status);
    }

    [Fact]
    public async Task SameNameInDifferentCase_IsAConflict_NotA500()
    {
        // utf8mb4_unicode_ci hält „server live" und „Server Live" für gleich — der Index würde werfen.
        var uid = await UserAsync();
        await _svc.SaveAsync(uid, null, Req(name: "Server Live"));
        var r = await _svc.SaveAsync(uid, null, Req(name: "server live"));
        Assert.Equal(409, r.Status);
    }

    [Fact]
    public async Task TwoUsers_MayUseTheSameName()
    {
        var alice = await UserAsync("alice");
        var bob = await UserAsync("bob");
        Assert.Equal(200, (await _svc.SaveAsync(alice, null, Req(name: "Heim-PC"))).Status);
        Assert.Equal(200, (await _svc.SaveAsync(bob, null, Req(name: "Heim-PC"))).Status);
        Assert.Equal(2, await _db.ExternalEngineRegistrations.CountAsync());
    }

    [Fact]
    public async Task AtMost32EnginesPerUser()
    {
        var uid = await UserAsync();
        for (var i = 0; i < ExternalEngineRegistrationService.MaxEnginesPerUser; i++)
            Assert.Equal(200, (await _svc.SaveAsync(uid, null, Req(name: $"E {i}"))).Status);
        var over = await _svc.SaveAsync(uid, null, Req(name: "one too many"));
        Assert.Equal(400, over.Status);
        // Ein UPDATE eines vorhandenen Namens geht auch am Deckel.
        Assert.Equal(200, (await _svc.SaveAsync(uid, null, Req(name: "E 3", threads: 2))).Status);
    }

    [Theory]
    [InlineData(0, 16, "chess", "0123456789abcdef", "maxThreads")]
    [InlineData(1025, 16, "chess", "0123456789abcdef", "maxThreads")]
    [InlineData(1, 0, "chess", "0123456789abcdef", "maxHash")]
    [InlineData(1, 1_048_577, "chess", "0123456789abcdef", "maxHash")]
    [InlineData(1, 16, "atomic", "0123456789abcdef", "chess")]
    [InlineData(1, 16, "chess,shogi", "0123456789abcdef", "unsupported variant")]
    [InlineData(1, 16, "chess", "tooshort", "providerSecret")]
    public void Validate_RejectsWhatLichessRejects(int threads, int hash, string variants, string secret, string expected)
    {
        var err = ExternalEngineRegistrationService.Validate(new ExternalEngineRegistrationRequest
        {
            Name = "x", MaxThreads = threads, MaxHash = hash, Variants = [.. variants.Split(',')], ProviderSecret = secret,
        });
        Assert.NotNull(err);
        Assert.Contains(expected, err);
    }

    [Fact]
    public void Validate_AcceptsTheLimits_AndAllProviderVariants()
    {
        Assert.Null(ExternalEngineRegistrationService.Validate(new ExternalEngineRegistrationRequest
        {
            Name = new string('n', 200), MaxThreads = 1024, MaxHash = 1_048_576,
            Variants = ["chess", "antichess", "atomic", "crazyhouse", "horde", "kingofthehill", "racingkings", "3check"],
            ProviderSecret = new string('s', 16),
        }));
        Assert.NotNull(ExternalEngineRegistrationService.Validate(new ExternalEngineRegistrationRequest
        {
            Name = new string('n', 201), MaxThreads = 1, MaxHash = 1, Variants = ["chess"], ProviderSecret = new string('s', 16),
        }));
        Assert.NotNull(ExternalEngineRegistrationService.Validate(new ExternalEngineRegistrationRequest
        {
            Name = "   ", MaxThreads = 1, MaxHash = 1, Variants = ["chess"], ProviderSecret = new string('s', 16),
        }));
    }

    [Fact]
    public async Task Delete_RemovesEngine_AndTakesItOutOfTheBackgroundList()
    {
        var uid = await UserAsync();
        var e = (await _svc.SaveAsync(uid, null, Req())).Engine!;
        var cred = new LichessEngineCredential { UserId = uid, EncryptedToken = "" };
        cred.SetBackgroundEngines([e.Id, "eei_other00000000"]);
        _db.LichessEngineCredentials.Add(cred);
        await _db.SaveChangesAsync();

        Assert.True(await _svc.DeleteAsync(uid, e.Id));
        Assert.Empty(_db.ExternalEngineRegistrations);
        var row = await _db.LichessEngineCredentials.AsNoTracking().SingleAsync();
        Assert.Equal(["eei_other00000000"], row.BackgroundEngines);
    }

    [Fact]
    public async Task Delete_ForeignEngine_ReturnsFalse_AndKeepsIt()
    {
        var alice = await UserAsync("alice");
        var bob = await UserAsync("bob");
        var e = (await _svc.SaveAsync(alice, null, Req())).Engine!;
        Assert.False(await _svc.DeleteAsync(bob, e.Id));
        Assert.Single(_db.ExternalEngineRegistrations);
    }

    [Fact]
    public async Task Registration_MakesTheSelectorKnown_ToTheBroker()
    {
        var uid = await UserAsync();
        await _svc.SaveAsync(uid, null, Req(secret: "known-secret-0123456"));
        Assert.True(await _directory.IsKnownAsync(ProviderSecrets.Selector("known-secret-0123456"), CancellationToken.None));
        Assert.False(await _directory.IsKnownAsync(ProviderSecrets.Selector("never-registered-000"), CancellationToken.None));
    }

    [Fact]
    public async Task ToDto_HasTheLichessShape_SecretOnlyForTheProvider()
    {
        var uid = await UserAsync();
        var e = (await _svc.SaveAsync(uid, null, Req(variants: ["chess", "atomic"]))).Engine!;
        var forProvider = ExternalEngineRegistrationService.ToDto(e, "kahalm", withSecret: true);
        var forBrowser = ExternalEngineRegistrationService.ToDto(e, "kahalm", withSecret: false);

        Assert.Equal(e.ClientSecret, forProvider.ClientSecret);
        Assert.Null(forBrowser.ClientSecret);
        Assert.Equal("kahalm", forProvider.UserId);
        Assert.Equal(["chess", "atomic"], forProvider.Variants);
        var json = System.Text.Json.JsonSerializer.Serialize(forBrowser,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.DoesNotContain("clientSecret", json);
        Assert.Contains("\"maxThreads\":8", json);
    }
}

public class ProviderSecretsTests
{
    [Fact]
    public void Selector_IsSha256OfPrefixedSecret_LikeLilaEngine()
    {
        // Literaler Vektor: python3 -c "import hashlib;print(hashlib.sha256(b'providerSecret:abc').hexdigest())"
        Assert.Equal("6dfb2e298ed94ec4bf26b09afffb32c575b4d82330fec7c5240abcca438c51ba", ProviderSecrets.Selector("abc"));
    }

    [Fact]
    public void Ids_HaveTheDocumentedShape()
    {
        Assert.Matches("^rhe_[A-Za-z0-9]{12}$", ProviderSecrets.NewEngineId());
        Assert.Matches("^[A-Za-z0-9]{16}$", ProviderSecrets.NewJobId());
        Assert.Matches("^[A-Za-z0-9_-]{43}$", ProviderSecrets.NewClientSecret());
        Assert.NotEqual(ProviderSecrets.NewEngineId(), ProviderSecrets.NewEngineId());
        Assert.True(ProviderSecrets.IsLocalEngineId("rhe_abc"));
        Assert.False(ProviderSecrets.IsLocalEngineId("eei_abc"));
        Assert.False(ProviderSecrets.IsLocalEngineId(null));
    }
}
