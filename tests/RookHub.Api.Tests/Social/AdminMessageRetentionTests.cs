using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Tests.Social;

/// <summary>Frist der Admin-/Direktnachrichten (O-7): gelesene älter als 365 Tage gehen, ungelesene bleiben. Feste Uhr.</summary>
public class AdminMessageRetentionTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 25, 3, 0, 0, DateTimeKind.Utc);
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private void Message(string body, int ageDays, bool fromAdmin, bool seen) => _db.AdminMessages.Add(new AdminMessage
    {
        UserId = 5, SenderId = fromAdmin ? 1 : 5, FromAdmin = fromAdmin, Body = body, CreatedAt = Now.AddDays(-ageDays),
        SeenByUserAt = fromAdmin && seen ? Now.AddDays(-ageDays + 1) : null,
        SeenByAdminAt = !fromAdmin && seen ? Now.AddDays(-ageDays + 1) : null,
    });

    [Fact]
    public async Task ReadAndOld_Gone_UnreadOrYoung_Stay()
    {
        Message("admin-alt-gelesen", 400, fromAdmin: true, seen: true);
        Message("user-alt-gelesen", 400, fromAdmin: false, seen: true);
        Message("admin-alt-ungelesen", 400, fromAdmin: true, seen: false);
        Message("user-alt-ungelesen", 400, fromAdmin: false, seen: false);
        Message("admin-jung-gelesen", 300, fromAdmin: true, seen: true);
        // Falsche Seite gesehen zählt nicht: eine Admin-Nachricht ist erst gelesen, wenn der NUTZER sie sah.
        _db.AdminMessages.Add(new AdminMessage { UserId = 5, SenderId = 1, FromAdmin = true, Body = "admin-alt-nur-admin-sah",
            CreatedAt = Now.AddDays(-400), SeenByAdminAt = Now.AddDays(-399) });
        await _db.SaveChangesAsync();

        Assert.Equal(2, await AdminMessageRetention.PruneAsync(_db, Now, new RetentionOptions()));

        Assert.Equal(new[] { "admin-alt-nur-admin-sah", "admin-alt-ungelesen", "admin-jung-gelesen", "user-alt-ungelesen" },
            await _db.AdminMessages.OrderBy(m => m.Body).Select(m => m.Body).ToArrayAsync());
    }

    [Fact]
    public async Task ConfiguredDays_AreHonoured()
    {
        Message("gelesen-40", 40, fromAdmin: true, seen: true);
        await _db.SaveChangesAsync();
        Assert.Equal(0, await AdminMessageRetention.PruneAsync(_db, Now, new RetentionOptions()));
        Assert.Equal(1, await AdminMessageRetention.PruneAsync(_db, Now, new RetentionOptions { MessagesDays = 30 }));
    }
}
