using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Zentrale Lese-Zugriffsregel auf Repertoires (Besitzer ODER Freigabe-Empfänger) —
/// ersetzt vier gedriftete Kopien (Service/Training/Download/Positionssuche).</summary>
public class RepertoireAccessTests : IDisposable
{
    private readonly AppDbContext _db;

    public RepertoireAccessTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CanRead_OwnerAndRecipient_Yes_Stranger_No()
    {
        _db.AppUsers.AddRange(
            new AppUser { Id = 1, Username = "owner", PasswordHash = "x" },
            new AppUser { Id = 2, Username = "friend", PasswordHash = "x" },
            new AppUser { Id = 3, Username = "stranger", PasswordHash = "x" });
        var rep = new Repertoire { UserId = 1, Name = "R" };
        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();
        _db.RepertoireShares.Add(new RepertoireShare { RepertoireId = rep.Id, OwnerId = 1, RecipientId = 2, SharedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        Assert.True(await RepertoireAccess.CanReadAsync(_db, rep.Id, 1));   // Besitzer
        Assert.True(await RepertoireAccess.CanReadAsync(_db, rep.Id, 2));   // Freigabe-Empfänger
        Assert.False(await RepertoireAccess.CanReadAsync(_db, rep.Id, 3));  // Fremder

        Assert.Single(await RepertoireAccess.ReadableBy(_db, 2).ToListAsync());
        Assert.Empty(await RepertoireAccess.ReadableBy(_db, 3).ToListAsync());
    }
}
