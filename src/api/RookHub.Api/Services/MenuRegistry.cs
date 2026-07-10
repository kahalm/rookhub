using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>Ein konfigurierbarer Menüeintrag mit Default-Sichtbarkeit.</summary>
public record MenuItemDef(string Key, MenuVisibilityLevel Default);

/// <summary>
/// Kanonische Liste der admin-konfigurierbaren Menüeinträge (in Anzeige-Reihenfolge).
/// Die Defaults bilden das bisherige Verhalten ab; Admins können sie überschreiben.
/// </summary>
public static class MenuRegistry
{
    public static readonly IReadOnlyList<MenuItemDef> Items = new[]
    {
        new MenuItemDef("dashboard", MenuVisibilityLevel.Registered),
        new MenuItemDef("repertoires", MenuVisibilityLevel.Registered),
        new MenuItemDef("tournaments", MenuVisibilityLevel.Registered),
        new MenuItemDef("friends", MenuVisibilityLevel.Registered),
        new MenuItemDef("puzzles", MenuVisibilityLevel.All),
        new MenuItemDef("favorites", MenuVisibilityLevel.Registered),
        new MenuItemDef("training-goals", MenuVisibilityLevel.Registered),
        new MenuItemDef("analysis", MenuVisibilityLevel.All),
        new MenuItemDef("games", MenuVisibilityLevel.Registered),
        new MenuItemDef("remembered", MenuVisibilityLevel.Registered),
        new MenuItemDef("weekly", MenuVisibilityLevel.Registered),
        new MenuItemDef("courses", MenuVisibilityLevel.Registered),
        new MenuItemDef("catalog", MenuVisibilityLevel.Registered),
        new MenuItemDef("leaderboards", MenuVisibilityLevel.Registered),
        new MenuItemDef("stats", MenuVisibilityLevel.Registered),
        new MenuItemDef("chessable", MenuVisibilityLevel.Registered),
        new MenuItemDef("install", MenuVisibilityLevel.All),
        new MenuItemDef("help", MenuVisibilityLevel.All),
    };

    public static readonly HashSet<string> Keys = Items.Select(i => i.Key).ToHashSet();
}
