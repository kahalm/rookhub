using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Tests.Games;

/// <summary>
/// A4-016: <c>AnalysisJobService.ListAsync</c> blendet Stapel-Aufträge per Semijoin
/// <c>g.Positions.Any(p => p.AnalysisJobId == j.Id)</c> aus. Ohne Index auf <c>AnalysisJobId</c> las MariaDB dafür die
/// Stellungen des Haus-Engine-Kontos linear (Prod 2026-10-24: 1,2 Mio. Zeilen). Der Index führt <c>GameAnalysisId</c>
/// mit, damit er die Verknüpfung zur Analyse ohne Zeilenzugriff liefert.
/// </summary>
public class GameAnalysisPositionIndexTests
{
    [Fact]
    public void GameAnalysisPositions_HaveIndexLeadingWithAnalysisJobId()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var entity = db.Model.FindEntityType(typeof(GameAnalysisPosition))!;
        Assert.Contains(entity.GetIndexes(), i => !i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(
                new[] { nameof(GameAnalysisPosition.AnalysisJobId), nameof(GameAnalysisPosition.GameAnalysisId) }));
    }
}
