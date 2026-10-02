using System.Text.RegularExpressions;
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

    /// <summary>
    /// Jeder Menüeintrag hat einen Abschnitt auf der Hilfeseite (Codereview UX-011): Punktepartie, Partien,
    /// Rekonstruieren und Aufgabenblätter standen lange im ☰-Menü, ohne dass /help ein Wort dazu sagte. Die
    /// Gegenseite ist <c>MENU_HELP</c> in <c>src/frontend/app/src/app/features/help/help.component.ts</c> —
    /// gelesen als Quelltext, weil Menü-Schlüssel HIER entstehen und die Hilfe sonst niemand nachzieht.
    /// </summary>
    [Fact]
    public void EveryMenuItem_HasHelpSection()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName,
            "src", "frontend", "app", "src", "app", "features", "help", "help.component.ts"));

        var block = Regex.Match(source, @"MENU_HELP\b[^=]*=\s*\{(?<body>[^}]*)\}");
        Assert.True(block.Success, "MENU_HELP nicht gefunden");
        var map = Regex.Matches(block.Groups["body"].Value, @"'(?<key>[\w-]+)'\s*:\s*'(?<section>\w+)'")
            .ToDictionary(m => m.Groups["key"].Value, m => m.Groups["section"].Value);
        var sections = Regex.Matches(source, @"\{\s*id:\s*'(?<id>\w+)'")
            .Select(m => m.Groups["id"].Value).ToHashSet();

        Assert.All(MenuRegistry.Items, i =>
        {
            Assert.True(map.TryGetValue(i.Key, out var section), $"MENU_HELP fehlt der Menü-Schlüssel '{i.Key}'");
            Assert.Contains(section, sections);
        });
    }
}
