using System.Text.RegularExpressions;
using RookHub.Api.Services.Accounts;
using Xunit;

namespace RookHub.Api.Tests.Accounts;

/// <summary>
/// <see cref="AdminLinks"/> spiegelt <c>ADMIN_TAB_KEYS</c> aus <c>admin-tabs.ts</c> (F5-005-Nachlauf): ein umbenannter Tab
/// liefe sonst still auf den ersten erlaubten Tab. Die Gegenseite wird als Quelltext gelesen; die Links stehen hier LITERAL.
/// </summary>
public class AdminLinksTests
{
    private static string AdminTabsSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName,
            "src", "frontend", "app", "src", "app", "features", "admin", "admin-tabs.ts"));
    }

    [Fact]
    public void LinkedTabs_ExistInFrontendAdminTabKeys()
    {
        var block = Regex.Match(AdminTabsSource(), @"ADMIN_TAB_KEYS\s*=\s*\[(?<body>[^\]]*)\]");
        Assert.True(block.Success, "ADMIN_TAB_KEYS nicht gefunden");
        var keys = Regex.Matches(block.Groups["body"].Value, @"'(?<k>\w+)'").Select(m => m.Groups["k"].Value).ToHashSet();
        Assert.Contains("users", keys);   // Gegenprobe: der Parser liest die Liste wirklich
        Assert.All(AdminLinks.LinkedTabs, t => Assert.True(keys.Contains(t), $"Tab '{t}' fehlt in ADMIN_TAB_KEYS"));
    }

    [Fact]
    public void Links_AreLiteralDeepLinks()
    {
        Assert.Equal("/admin?tab=users", AdminLinks.Users);
        Assert.Equal("/admin?tab=courseDl", AdminLinks.CourseDl);
        Assert.Equal("/admin?tab=uploads", AdminLinks.Uploads);
        Assert.Equal("/admin?tab=messages&thread=7", AdminLinks.MessagesThread(7));
        Assert.Equal(["users", "messages", "courseDl", "uploads"], AdminLinks.LinkedTabs);
    }
}
