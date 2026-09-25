using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Tests;

/// <summary>
/// Beide Engine-Quellen in EINER Auswahl: <c>rhe_</c> aus der eigenen Registrierung (ohne Lichess-Token),
/// <c>eei_</c> über den Lichess-Token. Fällt Lichess aus, bleiben die eigenen Engines benutzbar; ohne
/// eigene bleibt es beim bisherigen Wurf (→ 502 im Controller).
/// </summary>
public class EngineRegistryTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ServiceProvider _sp;
    private readonly EncryptionService _encryption;
    private readonly StubHandler _handler = new();
    private readonly LichessEngineService _lichess;
    private readonly EngineSelectorDirectory _directory;
    private DateTime _now = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

    public EngineRegistryTests()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name));
        _sp = services.BuildServiceProvider();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!" })
            .Build();
        _encryption = new EncryptionService(config);
        _lichess = new LichessEngineService(new HttpClient(_handler), new MemoryCache(new MemoryCacheOptions()), config,
            NullLogger<LichessEngineService>.Instance);
        _directory = new EngineSelectorDirectory(_sp.GetRequiredService<IServiceScopeFactory>(), () => _now);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sp.Dispose();
    }

    private EngineRegistry Registry(LocalBrokerOptions? options = null) =>
        new(_db, _encryption, _lichess, _directory, options ?? new LocalBrokerOptions(), () => _now);

    private sealed class StubHandler : HttpMessageHandler
    {
        public string Json = """[{"id":"eei_lichess0001","name":"Cloud","clientSecret":"ees_x","maxThreads":32,"maxHash":8192}]""";
        public bool Fail;
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (Fail) throw new HttpRequestException("lichess down");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private async Task<int> UserAsync()
    {
        var u = new AppUser { Username = "kahalm", PasswordHash = "x" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u.Id;
    }

    private async Task<ExternalEngineRegistration> LocalAsync(int userId, string name = "Heim-PC", string secret = "secret-0123456789abc",
        DateTime? lastSeen = null)
    {
        var r = new ExternalEngineRegistration
        {
            Id = ProviderSecrets.NewEngineId(), UserId = userId, Name = name, ClientSecret = "cs",
            ProviderSelector = ProviderSecrets.Selector(secret), MaxThreads = 4, MaxHash = 256, LastSeenAt = lastSeen,
        };
        _db.ExternalEngineRegistrations.Add(r);
        await _db.SaveChangesAsync();
        return r;
    }

    private async Task TokenAsync(int userId)
    {
        _db.LichessEngineCredentials.Add(new LichessEngineCredential { UserId = userId, EncryptedToken = _encryption.Encrypt("lip_tok") });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task List_WithoutLichessToken_ShowsLocalEngines_WithoutCallingLichess()
    {
        var uid = await UserAsync();
        var local = await LocalAsync(uid);

        var listing = await Registry().ListAsync(uid, CancellationToken.None);
        Assert.False(listing.HasCredentials);
        var e = Assert.Single(listing.Engines);
        Assert.Equal(local.Id, e.Id);
        Assert.Equal(EngineSource.Local, e.Source);
        Assert.Equal(0, _handler.Calls);
    }

    [Fact]
    public async Task List_MixesBothSources()
    {
        var uid = await UserAsync();
        await LocalAsync(uid);
        await TokenAsync(uid);

        var listing = await Registry().ListAsync(uid, CancellationToken.None);
        Assert.True(listing.HasCredentials);
        Assert.Equal([EngineSource.Local, EngineSource.Lichess], listing.Engines.Select(e => e.Source));
        Assert.Null(listing.Engines[1].Online);                 // für Lichess wissen wir es nicht
    }

    [Fact]
    public async Task List_LichessDown_KeepsLocalEngines_AndSaysSo()
    {
        var uid = await UserAsync();
        await LocalAsync(uid);
        await TokenAsync(uid);
        _handler.Fail = true;

        var listing = await Registry().ListAsync(uid, CancellationToken.None);
        Assert.True(listing.LichessUnreachable);
        Assert.Single(listing.Engines);
    }

    [Fact]
    public async Task List_LichessDown_WithoutLocalEngines_StillThrows_AsBefore()
    {
        var uid = await UserAsync();
        await TokenAsync(uid);
        _handler.Fail = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => Registry().ListAsync(uid, CancellationToken.None));
    }

    [Fact]
    public async Task Online_FollowsTheLastPoll_InMemoryFirst_ThenTheDatabaseStamp()
    {
        var uid = await UserAsync();
        var fresh = await LocalAsync(uid, "A", "secret-A-0123456789", lastSeen: _now.AddSeconds(-10));
        var stale = await LocalAsync(uid, "B", "secret-B-0123456789", lastSeen: _now.AddMinutes(-5));
        var never = await LocalAsync(uid, "C", "secret-C-0123456789");

        var reg = Registry();
        Assert.True(reg.IsOnline(fresh));
        Assert.False(reg.IsOnline(stale));
        Assert.False(reg.IsOnline(never));

        // Der Poll von eben zählt vor dem alten DB-Stempel.
        _directory.MarkSeen(stale.ProviderSelector);
        Assert.True(reg.IsOnline(stale));
        _now = _now.AddSeconds(31);
        Assert.False(reg.IsOnline(stale));
    }

    [Fact]
    public async Task Resolve_Local_NeedsNoToken_ButOnlyTheOwnersEngine()
    {
        var uid = await UserAsync();
        var other = new AppUser { Username = "other", PasswordHash = "x" };
        _db.AppUsers.Add(other);
        await _db.SaveChangesAsync();
        var local = await LocalAsync(uid);

        var found = await Registry().ResolveAsync(uid, local.Id, CancellationToken.None);
        Assert.Equal(EngineLookupFailure.None, found.Failure);
        Assert.Equal(local.ProviderSelector, found.Engine!.Local!.Selector);
        Assert.Equal("kahalm", found.Engine.Local.Engine.UserId);
        Assert.Equal("cs", found.Engine.Local.Engine.ClientSecret);

        Assert.Equal(EngineLookupFailure.NotFound, (await Registry().ResolveAsync(other.Id, local.Id, CancellationToken.None)).Failure);
        Assert.Equal(EngineLookupFailure.NotFound,
            (await Registry(new LocalBrokerOptions { Enabled = false }).ResolveAsync(uid, local.Id, CancellationToken.None)).Failure);
    }

    [Fact]
    public async Task Resolve_Lichess_WithoutToken_SaysNoToken()
    {
        var uid = await UserAsync();
        Assert.Equal(EngineLookupFailure.NoToken,
            (await Registry().ResolveAsync(uid, "eei_lichess0001", CancellationToken.None)).Failure);

        await TokenAsync(uid);
        var found = await Registry().ResolveAsync(uid, "eei_lichess0001", CancellationToken.None);
        Assert.Equal(EngineSource.Lichess, found.Engine!.Source);
        Assert.Equal("ees_x", found.Engine.Lichess!.ClientSecret);
        Assert.Equal(EngineLookupFailure.NotFound,
            (await Registry().ResolveAsync(uid, "eei_unknown00000", CancellationToken.None)).Failure);
    }

    [Fact]
    public async Task Disabled_HidesLocalEngines()
    {
        var uid = await UserAsync();
        await LocalAsync(uid);
        var listing = await Registry(new LocalBrokerOptions { Enabled = false }).ListAsync(uid, CancellationToken.None);
        Assert.Empty(listing.Engines);
    }
}
