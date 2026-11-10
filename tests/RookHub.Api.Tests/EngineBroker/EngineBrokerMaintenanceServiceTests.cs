using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Tests.EngineBroker;

/// <summary>Der Online-Punkt überlebt einen API-Neustart nur, weil die im Speicher gesehenen Polls minütlich
/// als <c>LastSeenAt</c> in die Registrierungen wandern — auch in ALLE Registrierungen desselben Selectors.</summary>
public class EngineBrokerMaintenanceServiceTests
{
    [Fact]
    public async Task FlushLastSeen_WritesTheStamp_ForEveryRegistrationOfTheSelector()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name));
        await using var sp = services.BuildServiceProvider();
        var now = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        var directory = new EngineSelectorDirectory(sp.GetRequiredService<IServiceScopeFactory>(), () => now);
        var options = new LocalBrokerOptions();
        var hub = new EngineHub(options, () => now, startSweeper: false);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AppUsers.Add(new AppUser { Id = 1, Username = "u", PasswordHash = "x" });
            foreach (var (id, sel) in new[] { ("rhe_a", "shared"), ("rhe_b", "shared"), ("rhe_c", "other") })
                db.ExternalEngineRegistrations.Add(new ExternalEngineRegistration
                {
                    Id = id, UserId = 1, Name = id, ClientSecret = "cs", ProviderSelector = sel, MaxThreads = 1, MaxHash = 16,
                });
            await db.SaveChangesAsync();
        }

        directory.MarkSeen("shared");
        var service = new EngineBrokerMaintenanceService(sp.GetRequiredService<IServiceScopeFactory>(), directory, hub, options,
            NullLogger<EngineBrokerMaintenanceService>.Instance);
        await service.FlushLastSeenAsync(CancellationToken.None);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.ExternalEngineRegistrations.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
            Assert.Equal([now, now, (DateTime?)null], rows.Select(r => r.LastSeenAt));
        }

        // Nichts Neues gesehen → nichts zu tun (kein Schreibzugriff je Minute ohne Anlass).
        Assert.Empty(directory.DrainSeen());
    }

    [Fact]
    public async Task NoteToken_IsMemoryOnly_UntilChanged_AndTheFlushWritesOnlyChangedRows()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name));
        await using var sp = services.BuildServiceProvider();
        var now = new DateTime(2026, 11, 10, 17, 0, 0, DateTimeKind.Utc);
        var directory = new EngineSelectorDirectory(sp.GetRequiredService<IServiceScopeFactory>(), () => now);
        var options = new LocalBrokerOptions();
        using var hub = new EngineHub(options, () => now, startSweeper: false);
        const int owner = 990971, stranger = 990972;

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AppUsers.AddRange(new AppUser { Id = owner, Username = "o", PasswordHash = "x" },
                new AppUser { Id = stranger, Username = "s", PasswordHash = "x" });
            db.ExternalEngineRegistrations.AddRange(
                new ExternalEngineRegistration { Id = "rhe_tokNull00001", UserId = owner, Name = "Gross", ClientSecret = "cs", ProviderSelector = "selNull", MaxThreads = 1, MaxHash = 16 },
                new ExternalEngineRegistration { Id = "rhe_tokSame00001", UserId = owner, Name = "Gross 2", ClientSecret = "cs", ProviderSelector = "selSame", MaxThreads = 1, MaxHash = 16, ApiTokenId = 990981 },
                new ExternalEngineRegistration { Id = "rhe_tokFrgn00001", UserId = owner, Name = "Gross 3", ClientSecret = "cs", ProviderSelector = "selForeign", MaxThreads = 1, MaxHash = 16 });
            await db.SaveChangesAsync();
        }

        // Viele Polls → EIN Vormerk je Selector, kein Datenbankzugriff (die Wartung ist der einzige Schreiber).
        for (var i = 0; i < 50; i++)
        {
            directory.NoteToken("selNull", owner, 990981);
            directory.NoteToken("selSame", owner, 990981);
        }
        directory.NoteToken("selForeign", stranger, 990982);             // Token eines fremden Kontos → nie zuordnen
        var service = new EngineBrokerMaintenanceService(sp.GetRequiredService<IServiceScopeFactory>(), directory, hub, options,
            NullLogger<EngineBrokerMaintenanceService>.Instance);
        await service.FlushLastSeenAsync(CancellationToken.None);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.ExternalEngineRegistrations.AsNoTracking().OrderBy(r => r.Id).ToDictionaryAsync(r => r.Id, r => r.ApiTokenId);
            Assert.Equal(990981, rows["rhe_tokNull00001"]);              // nachgetragen
            Assert.Equal(990981, rows["rhe_tokSame00001"]);              // war schon richtig
            Assert.Null(rows["rhe_tokFrgn00001"]);
        }

        // Gleiches Paar erneut: nichts vorgemerkt. Token gewechselt: einmal vorgemerkt.
        directory.NoteToken("selNull", owner, 990981);
        Assert.Empty(directory.DrainTokens());
        directory.NoteToken("selNull", owner, 990983);
        directory.NoteToken("selNull", owner, 990983);
        var pending = directory.DrainTokens();
        Assert.Equal(new EngineTokenSighting(owner, 990983), Assert.Single(pending).Value);

        // Schreiben gescheitert → wieder vorgemerkt, beim nächsten Lauf geschrieben.
        directory.RestoreTokens(pending);
        await service.FlushLastSeenAsync(CancellationToken.None);
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(990983, (await db.ExternalEngineRegistrations.AsNoTracking().SingleAsync(r => r.Id == "rhe_tokNull00001")).ApiTokenId);
        }
        Assert.Empty(directory.DrainTokens());
    }
}
