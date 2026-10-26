using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Models;
using RookHub.Api.Services.TournamentDirectory;

namespace RookHub.Api.Tests.TournamentDirectory;

/// <summary>
/// Die reinen Teile des Turniersuche-Abgleichs (<see cref="DirectorySweepMerge"/>, <see cref="DirectoryArtPasses"/>),
/// ohne Datenbank. Den ganzen Durchgang decken die Sweep-Tests in <c>TournamentDirectoryServiceTests</c> ab.
/// </summary>
public class DirectorySweepMergeTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 3, 0, 0, DateTimeKind.Utc);

    private static DirectorySearchRows.CrawlerDirectoryRow Row(string id, string name = "Open Graz",
        DateOnly? start = null, string? location = "Graz", string? timeControl = "90 min + 30 sec") =>
        new(id, name, "AUT", "STMK", start ?? new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 8), location,
            timeControl, "Dir", "Org", "Arb", 7, 40, null);

    [Fact]
    public void Map_WritesFieldsKindAndGrouping()
    {
        var entry = new TournamentDirectoryEntry { PublicId = "1", ChessResultsId = "1" };
        var artMap = new Dictionary<string, (TournamentKind Kind, TournamentSystem System)>
        {
            ["1"] = (TournamentKind.Team, TournamentSystem.RoundRobin),
        };

        DirectorySweepMerge.Map(Row("1"), entry, Now, artMap);

        Assert.Equal("Open Graz", entry.Name);
        Assert.Equal("AUT", entry.Federation);
        Assert.True(entry.StartsOnWeekend);   // 2026-11-07 ist ein Samstag
        Assert.Equal(TournamentSpeed.Standard, entry.Speed);
        Assert.Equal(TournamentKind.Team, entry.Kind);
        Assert.Equal(TournamentSystem.RoundRobin, entry.System);
        Assert.Equal(DirectoryKeys.ComputeChangeHash(entry.StartDate, entry.EndDate, "Graz"), entry.ChangeHash);
        Assert.Equal(Now, entry.LastSeenAt);
        Assert.Equal(Now, entry.UpdatedAt);
        Assert.False(string.IsNullOrEmpty(entry.GroupKey));
    }

    [Fact]
    public void Map_WithoutArtMap_KeepsKnownKindAndSystem()
    {
        var entry = new TournamentDirectoryEntry
        {
            PublicId = "1", ChessResultsId = "1", Kind = TournamentKind.Team, System = TournamentSystem.Swiss,
        };

        DirectorySweepMerge.Map(Row("1"), entry, Now, artMap: null);

        Assert.Equal(TournamentKind.Team, entry.Kind);
        Assert.Equal(TournamentSystem.Swiss, entry.System);
    }

    [Fact]
    public void MarkVanished_CountsMissThenRemoves_OnlyEntriesWithNumber()
    {
        var gone = new TournamentDirectoryEntry { PublicId = "2", ChessResultsId = "2", MissedSweeps = 1 };
        var fresh = new TournamentDirectoryEntry { PublicId = "3", ChessResultsId = "3" };
        var external = new TournamentDirectoryEntry { PublicId = "x-1", ChessResultsId = null };
        var seen = new TournamentDirectoryEntry { PublicId = "1", ChessResultsId = "1", MissedSweeps = 1 };

        var removed = DirectorySweepMerge.MarkVanished([Row("1")], [gone, fresh, external, seen], Now,
            truncated: false, missedSweepsUntilRemoved: 2);

        Assert.Equal([gone], removed);
        Assert.Equal(Now, gone.RemovedAt);
        Assert.Equal(1, fresh.MissedSweeps);
        Assert.Null(fresh.RemovedAt);
        Assert.Equal(0, external.MissedSweeps);
        Assert.Equal(1, seen.MissedSweeps);   // geliefert: der Zaehler setzt der Abgleich zurueck, nicht MarkVanished
    }

    [Fact]
    public void MarkVanished_TruncatedOrWithinCooldown_CountsNothing()
    {
        var a = new TournamentDirectoryEntry { PublicId = "2", ChessResultsId = "2" };
        Assert.Empty(DirectorySweepMerge.MarkVanished([], [a], Now, truncated: true, missedSweepsUntilRemoved: 1));
        Assert.Equal(0, a.MissedSweeps);

        var b = new TournamentDirectoryEntry { PublicId = "3", ChessResultsId = "3", LastMissAt = Now.AddHours(-1) };
        Assert.Empty(DirectorySweepMerge.MarkVanished([], [b], Now, truncated: false, missedSweepsUntilRemoved: 1));
        Assert.Equal(0, b.MissedSweeps);
    }

    private static JsonElement Rows(params string[] ids) => JsonDocument.Parse(JsonSerializer.Serialize(
        ids.Select(id => new { chessResultsId = id, name = "T" + id }))).RootElement;

    [Fact]
    public async Task ArtPasses_AllFour_MapsEachIdToItsArt()
    {
        var paths = new List<string>();
        var map = await DirectoryArtPasses.FetchMapAsync((path, _) =>
        {
            paths.Add(path);
            return Task.FromResult(path.EndsWith("&art=2") ? Rows("10") : path.EndsWith("&art=0") ? Rows("11") : Rows());
        }, NullLogger.Instance, "AUT", new DateOnly(2026, 9, 26), new DateOnly(2028, 4, 26), 2000, default);

        Assert.NotNull(map);
        Assert.Equal((TournamentKind.Team, TournamentSystem.RoundRobin), map!["10"]);
        Assert.Equal((TournamentKind.Individual, TournamentSystem.Swiss), map["11"]);
        Assert.Equal(4, paths.Count);
        Assert.Equal("/api/tournament-search?fed=AUT&from=2026-09-26&to=2028-04-26&maxRows=2000&art=0", paths[0]);
    }

    [Fact]
    public async Task ArtPasses_OnePassFailsOrTruncated_ReturnsNull()
    {
        Assert.Null(await DirectoryArtPasses.FetchMapAsync((path, _) => path.EndsWith("&art=3")
                ? throw new HttpRequestException("weg") : Task.FromResult(Rows()),
            NullLogger.Instance, "AUT", default, default, 2000, default));
        Assert.Null(await DirectoryArtPasses.FetchMapAsync((_, _) => Task.FromResult(Rows("1", "2")),
            NullLogger.Instance, "AUT", default, default, 2, default));
    }
}
