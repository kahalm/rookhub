using RookHub.Api.Models;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.Tests;

public class MenuRegistryTests
{
    [Fact]
    public void Items_HaveUniqueKeys()
    {
        var keys = MenuRegistry.Items.Select(i => i.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Keys_MatchesItemKeys()
    {
        Assert.Equal(MenuRegistry.Items.Select(i => i.Key).ToHashSet(), MenuRegistry.Keys);
    }

    [Fact]
    public void PublicEntries_DefaultToAll()
    {
        // „puzzles", „analysis", „install", „help" sollen auch anonym sichtbar sein.
        foreach (var key in new[] { "puzzles", "analysis", "install", "help" })
        {
            var item = MenuRegistry.Items.Single(i => i.Key == key);
            Assert.Equal(MenuVisibilityLevel.All, item.Default);
        }
    }

    [Fact]
    public void AccountEntries_DefaultToRegistered()
    {
        // „dashboard"/„courses"/„leaderboards" erfordern per Default ein Login.
        foreach (var key in new[] { "dashboard", "courses", "leaderboards", "stats" })
        {
            var item = MenuRegistry.Items.Single(i => i.Key == key);
            Assert.Equal(MenuVisibilityLevel.Registered, item.Default);
        }
    }

    [Fact]
    public void EveryDefault_IsAllOrRegistered()
    {
        // Aktuell gibt es keine Admin-/Groups-Defaults — Overrides liegen in der DB.
        Assert.All(MenuRegistry.Items, i =>
            Assert.True(i.Default is MenuVisibilityLevel.All or MenuVisibilityLevel.Registered));
    }
}
