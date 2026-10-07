using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Tactics;
using Xunit;

namespace RookHub.Api.Tests;

public class TacticHarvestSourceGameTests
{
    [Theory]
    [InlineData("+1.2", "Die Lösung bringt einen leichten Vorteil.")]
    [InlineData("+2.4", "Die Lösung bringt einen klaren Vorteil.")]
    [InlineData("+2.9", "Die Lösung bringt einen klaren Vorteil.")]
    [InlineData("+3.0", "Die Lösung führt zur Gewinnstellung.")]
    [InlineData("+5.3", "Die Lösung führt zur Gewinnstellung.")]
    [InlineData("#3", "Die Lösung setzt in 3 Zügen matt.")]
    [InlineData("#1", "Die Lösung setzt matt.")]
    public void Outcome_reads_naturally(string eval, string expected) =>
        Assert.Equal(expected, TacticHarvestService.Outcome(eval));

    [Fact]
    public void ParseSourceGame_reads_kind_id_and_ply()
    {
        var g = BookPuzzleService.ParseSourceGame("club:12:74");
        Assert.Equal(("club", 12, 74), (g!.Kind, g.Id, g.Ply));
        Assert.Equal("own", BookPuzzleService.ParseSourceGame("own:3:1")!.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("library:1:2")]
    [InlineData("club:x:2")]
    [InlineData("club:1")]
    public void ParseSourceGame_ignores_defects(string? value) => Assert.Null(BookPuzzleService.ParseSourceGame(value));
}

public class TacticHarvestChapterOrderTests
{
    [Fact]
    public void ClubChapterKey_parses_season_league_round()
    {
        Assert.Equal((0, 2026, "Landesliga", 1), TacticHarvestService.ClubChapterKey("2026/27 · Landesliga · Runde 1"));
        Assert.Equal((1, 0, "Andere Partien", 0), TacticHarvestService.ClubChapterKey("Andere Partien"));
        Assert.Equal((1, 0, "", 0), TacticHarvestService.ClubChapterKey(null));
    }

    [Fact]
    public async Task SortClubChapters_newest_season_first_rounds_ascending_others_last()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<RookHub.Api.Data.AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new RookHub.Api.Data.AppDbContext(options);
        string[] chapters =
        {
            "2019/20 · Landesliga · Runde 2", "Andere Partien", "2026/27 · Landesliga · Runde 1",
            "2025/26 · Landesliga · Runde 8", "2025/26 · Landesliga · Runde 7", "2019/20 · Landesliga · Runde 2",
        };
        for (var i = 0; i < chapters.Length; i++)
            db.BookPuzzles.Add(new RookHub.Api.Models.BookPuzzle
            {
                LineId = $"tactics-club.pgn:t{i}", BookFileName = TacticHarvestService.ClubBookOf(1), Round = (i + 1).ToString(),
                Fen = "x", Moves = "e2e4", Chapter = chapters[i],
            });
        await db.SaveChangesAsync();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var svc = new TacticHarvestService(db, new AnalysisJobService(db, config: config), new QuietHours("", "UTC"), config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TacticHarvestService>.Instance);
        await svc.SortClubChaptersAsync(TacticHarvestService.ClubBookOf(1), default);
        var order = db.BookPuzzles.AsEnumerable().OrderBy(p => p.Round.Length).ThenBy(p => p.Round).Select(p => p.Chapter).ToList();
        Assert.Equal(new[]
        {
            "2026/27 · Landesliga · Runde 1", "2025/26 · Landesliga · Runde 7", "2025/26 · Landesliga · Runde 8",
            "2019/20 · Landesliga · Runde 2", "2019/20 · Landesliga · Runde 2", "Andere Partien",
        }, order);
    }
}
