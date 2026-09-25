using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Tests;

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
}
