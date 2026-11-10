using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RookHub.Api.Data;
using RookHub.Api.DTOs.EngineBroker;
using RookHub.Api.Models;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Tests.EngineBroker;

/// <summary>
/// Deckel der Hintergrund-Liste (0.952.0): EINE Regel (<see cref="LichessEngineCredential.CheckBackgroundList"/>) für alle
/// Wege, die die Liste vergrößern — Profil, Admin-Reiter, Wiedereintragen bei Neuanmeldung. Eine Liste über dem Deckel
/// darf das Bearbeiten nicht blockieren, nie wird abgeschnitten gespeichert. Feste Ids im 99xxxx-Bereich.
/// </summary>
public class BackgroundListCapTests : IDisposable
{
    private const int Owner = 990801;
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly AppDbContext _db;
    private readonly ServiceProvider _sp;

    public BackgroundListCapTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options);
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        _sp = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _db.Dispose();
        _sp.Dispose();
    }

    private static List<string> Ids(int n, string prefix = "rhe_cap") =>
        [.. Enumerable.Range(0, n).Select(i => $"{prefix}{i:D9}")];

    [Fact]
    public void Cap_Is64_ColumnHolds64RealIds_WithReserve()
    {
        Assert.Equal(64, LichessEngineCredential.MaxBackgroundEngines);
        Assert.Equal(64, EngineSettingsService.MaxBackgroundEngines);
        Assert.Equal(1200, LichessEngineCredential.BackgroundEngineIdsMaxLength);
        Assert.True(64 * 17 - 1 <= LichessEngineCredential.BackgroundEngineIdsMaxLength);
        Assert.Equal(16, Ids(1)[0].Length);                                    // wie echte rhe_/eei_-Kennungen
    }

    [Fact]
    public void CheckBackgroundList_OverCap_OnlyGrowingIsRejected()
    {
        Assert.Equal(LichessEngineCredential.BackgroundListProblem.None, LichessEngineCredential.CheckBackgroundList(Ids(64), 10));
        Assert.Equal(LichessEngineCredential.BackgroundListProblem.TooMany, LichessEngineCredential.CheckBackgroundList(Ids(65), 64));
        // Liste stand schon bei 66 (von Hand): 65 und 66 gehen, 67 nicht.
        Assert.Equal(LichessEngineCredential.BackgroundListProblem.None, LichessEngineCredential.CheckBackgroundList(Ids(65), 66));
        Assert.Equal(LichessEngineCredential.BackgroundListProblem.None, LichessEngineCredential.CheckBackgroundList(Ids(66), 66));
        Assert.Equal(LichessEngineCredential.BackgroundListProblem.TooMany, LichessEngineCredential.CheckBackgroundList(Ids(67), 66));
    }

    [Fact]
    public void CheckBackgroundList_TooLongForColumn_IsRejected_AndSetNeverTruncates()
    {
        var longIds = Enumerable.Range(0, 20).Select(i => $"x{i:D2}".PadRight(64, 'y')).ToList();   // 20 × 65 > 1 200
        Assert.Equal(LichessEngineCredential.BackgroundListProblem.TooLong, LichessEngineCredential.CheckBackgroundList(longIds, 0));

        var cred = new LichessEngineCredential { BackgroundEngineIds = "rhe_keep00000001" };
        Assert.Throws<InvalidOperationException>(() => cred.SetBackgroundEngines(longIds));
        Assert.Equal("rhe_keep00000001", cred.BackgroundEngineIds);           // unverändert, nicht abgeschnitten
    }

    [Fact]
    public void TryAppend_FullList_DoesNothing_AlreadyIn_IsNoOp()
    {
        var cred = new LichessEngineCredential();
        cred.SetBackgroundEngines(Ids(64));
        Assert.Equal(LichessEngineCredential.BackgroundListProblem.TooMany, cred.TryAppendBackgroundEngine("rhe_new000000001"));
        Assert.Equal(64, cred.BackgroundEngines.Count);
        Assert.Equal(LichessEngineCredential.BackgroundListProblem.None, cred.TryAppendBackgroundEngine(Ids(64)[3]));
        Assert.Equal(Ids(64), cred.BackgroundEngines);
    }

    /// <summary>Wiedereintragen bei Neuanmeldung (0.919.0) respektiert den Deckel: Liste voll → nicht eingetragen, Warnung.</summary>
    [Theory]
    [InlineData(63, true)]
    [InlineData(64, false)]
    [InlineData(66, false)]       // über dem Deckel (von Hand) bleibt sie, wie sie ist
    public async Task RestoreOnReRegistration_RespectsTheCap(int listed, bool expectAdded)
    {
        _db.AppUsers.Add(new AppUser { Id = Owner, Username = "cap-owner", PasswordHash = "x" });
        _db.LichessEngineCredentials.Add(new LichessEngineCredential
        {
            UserId = Owner, BackgroundEngineIds = string.Join(',', Ids(listed)), PrunedBackgroundNames = "Laptop 1",
        });
        await _db.SaveChangesAsync();
        var log = new CapturingLogger<ExternalEngineRegistrationService>();
        var svc = new ExternalEngineRegistrationService(_db,
            new EngineSelectorDirectory(_sp.GetRequiredService<IServiceScopeFactory>()), log);

        var r = await svc.SaveAsync(Owner, null, new ExternalEngineRegistrationRequest
        {
            Name = "Laptop 1", MaxThreads = 2, MaxHash = 64, Variants = ["chess"], ProviderSecret = "cap-provider-secret-01",
        });

        Assert.NotNull(r.Engine);
        var cred = await _db.LichessEngineCredentials.AsNoTracking().SingleAsync(c => c.UserId == Owner);
        Assert.Equal(expectAdded, cred.BackgroundEngines.Contains(r.Engine!.Id));
        Assert.Equal(expectAdded ? listed + 1 : listed, cred.BackgroundEngines.Count);
        Assert.Empty(cred.PrunedBackground);                                    // Merkposten in beiden Fällen verbraucht
        if (!expectAdded)
            Assert.Contains(log.Events, e => e.Level == LogLevel.Warning && e.Message.Contains("NICHT wieder eingetragen"));
    }

    /// <summary>Handgespiegelt: der Fehlertext <c>apiErrors.background_list_full</c> nennt den Deckel in ALLEN 25 Sprachen —
    /// er muss mitwandern, wenn der Deckel sich ändert.</summary>
    [Fact]
    public void ErrorText_NamesTheCap_InAllLanguages()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml"))) dir = dir.Parent;
        var i18n = Path.Combine(dir!.FullName, "src", "frontend", "app", "public", "i18n");
        var files = Directory.GetFiles(i18n, "*.json");
        Assert.Equal(25, files.Length);
        foreach (var f in files)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            var text = doc.RootElement.GetProperty("apiErrors").GetProperty("background_list_full").GetString();
            Assert.Contains(LichessEngineCredential.MaxBackgroundEngines.ToString(), text);
        }
    }
}
