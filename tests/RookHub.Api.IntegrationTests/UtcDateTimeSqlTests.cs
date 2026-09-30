using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// A9-002 gegen ECHTES MariaDB: <c>DATETIME</c> hat keine Zone, der Treiber liefert jeden gelesenen Wert mit
/// <see cref="DateTimeKind.Unspecified"/> — ohne den UTC-Konverter ging er ohne „Z" über die Leitung und der Browser
/// las ihn als Ortszeit. InMemory kann das nicht zeigen: es gibt den Wert zurück, wie er geschrieben wurde.
/// </summary>
public class UtcDateTimeSqlTests(UtcDateTimeSqlFixture fixture)
    : IAsyncLifetime, IClassFixture<UtcDateTimeSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [MySqlFact]
    public async Task GelesenerZeitstempel_IstUtc_InEntitaet_Projektion_UndAggregat()
    {
        var created = new DateTime(2026, 9, 29, 16, 0, 0, DateTimeKind.Utc);
        var locked = new DateTime(2026, 10, 1, 8, 30, 0, DateTimeKind.Utc);
        await using (var db = fixture.Schema.NewContext())
        {
            db.AppUsers.Add(new AppUser { Username = "utc", PasswordHash = "x", CreatedAt = created, LockedUntil = locked });
            db.AppUsers.Add(new AppUser { Username = "offen", PasswordHash = "x", CreatedAt = created.AddHours(-1) });
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.Schema.NewContext())
        {
            var user = await db.AppUsers.AsNoTracking().SingleAsync(u => u.Username == "utc");
            Assert.Equal(DateTimeKind.Utc, user.CreatedAt.Kind);
            Assert.Equal(created, user.CreatedAt);
            Assert.Equal(DateTimeKind.Utc, user.LockedUntil!.Value.Kind);
            Assert.Equal("\"2026-09-29T16:00:00Z\"", JsonSerializer.Serialize(user.CreatedAt));

            var row = await db.AppUsers.Where(u => u.Username == "offen")
                .Select(u => new { u.CreatedAt, u.LockedUntil }).SingleAsync();
            Assert.Equal(DateTimeKind.Utc, row.CreatedAt.Kind);
            Assert.Null(row.LockedUntil);

            var newest = await db.AppUsers.MaxAsync(u => u.CreatedAt);
            Assert.Equal(DateTimeKind.Utc, newest.Kind);
            Assert.Equal(created, newest);
        }
    }

    /// <summary>Werte, die die Datenbank selbst setzt (NOW() in Migrationen und rohem SQL), sind ebenfalls UTC —
    /// der Server läuft in UTC. Auch sie kommen gekennzeichnet zurück.</summary>
    [MySqlFact]
    public async Task VonSqlGeschriebenerWert_KommtAlsUtcZurueck()
    {
        await using var db = fixture.Schema.NewContext();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO AppUsers (Username, PasswordHash, CreatedAt, IsAdmin)
            VALUES ('roh', 'x', '2026-09-29 16:00:00', 0)
            """);

        var created = await db.AppUsers.Where(u => u.Username == "roh").Select(u => u.CreatedAt).SingleAsync();

        Assert.Equal(DateTimeKind.Utc, created.Kind);
        Assert.Equal(new DateTime(2026, 9, 29, 16, 0, 0), created);
    }
}

public sealed class UtcDateTimeSqlFixture() : MariaDbClassFixture("utc", withApp: false);
