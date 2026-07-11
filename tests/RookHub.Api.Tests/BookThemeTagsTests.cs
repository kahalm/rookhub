using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Zentrale Buch-Themen-Semantik (ersetzt die zwei gedrifteten Parser-Kopien in
/// CourseService und TrainingGoalService).</summary>
public class BookThemeTagsTests
{
    [Fact]
    public void ParseKeys_DedupesKeepsOrder_FiltersInvalid()
    {
        Assert.Equal(new[] { "endgame", "tactics" },
            BookThemeTags.ParseKeys("endgame, tactics, ENDGAME, unknown"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense, alsobad")]
    public void ParseKeys_EmptyOrInvalid_DefaultsToTactics(string? csv)
    {
        Assert.Equal(new[] { "tactics" }, BookThemeTags.ParseKeys(csv));
    }

    [Fact]
    public void IsValidKey_MatchesWhitelist()
    {
        Assert.True(BookThemeTags.IsValidKey("opening"));
        Assert.False(BookThemeTags.IsValidKey("blitz"));
    }
}
