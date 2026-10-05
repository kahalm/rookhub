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
