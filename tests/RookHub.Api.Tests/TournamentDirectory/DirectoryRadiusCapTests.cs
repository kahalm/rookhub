using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.TournamentDirectory;
using Xunit;

namespace RookHub.Api.Tests.TournamentDirectory;

/// <summary>G1-004: der Umkreisweg kappt bei <c>MaxMaterialized</c> Kandidaten — und zwar die SPAETESTEN, nicht die,
/// die zufaellig hinten in der Lesereihenfolge der Datenbank liegen.</summary>
public class DirectoryRadiusCapTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private static readonly DateOnly Base = new(2026, 12, 1);

    private async Task SeedAsync()
    {
        // Ids steigen, die Termine fallen: die Einfuegefolge (InMemory-Lesereihenfolge) ist die SPAETESTE zuerst.
        for (var i = 0; i < 5; i++)
        {
            var start = Base.AddDays((4 - i) * 7);
            _db.TournamentDirectoryEntries.Add(new TournamentDirectoryEntry
            {
                PublicId = $"90{i}", ChessResultsId = $"90{i}", Name = $"Turnier {i}", Federation = "AUT",
                StartDate = start, EndDate = start, Lat = 47.81, Lon = 13.05, GeoSource = GeoSource.City,
                PlayerCount = 20, GroupKey = $"g{i}",
            });
        }
        await _db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Search_RadiusOverCap_KeepsTheEarliest(bool withWindow)
    {
        await SeedAsync();
        var service = new TournamentDirectoryQueryService(_db) { MaxMaterialized = 3 };

        var result = await service.SearchAsync(new DirectorySearchQuery
        {
            Lat = 47.80, Lon = 13.04, RadiusKm = 50, IncludeImplausible = true,
            From = withWindow ? Base.AddDays(-1) : null,
        }, 50);

        Assert.True(result.Truncated);
        Assert.Equal([Base, Base.AddDays(7), Base.AddDays(14)], result.Items.Select(i => i.Entry.StartDate!.Value));
    }
}
