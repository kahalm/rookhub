using RookHub.Api.Logging;
using Xunit;

namespace RookHub.Api.Tests;

public class SystemCallClassifierTests
{
    [Theory]
    // Infra
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/swagger/index.html")]
    // Client-Diagnose/Heartbeat + Menü
    [InlineData("/api/client-log")]
    [InlineData("/api/menu")]
    // Badge-/Zähler-Polls
    [InlineData("/api/notifications/count")]
    [InlineData("/api/messages/unread-count")]
    [InlineData("/api/challenges/incoming/count")]
    [InlineData("/api/challenges/outgoing/pending-counts")]
    [InlineData("/api/revenge/notifications/count")]
    // Import-Status-Polls
    [InlineData("/api/chessable/admin/active")]
    [InlineData("/api/chessable/admin/imports")]
    public void SystemPaths_AreSystem(string path)
    {
        Assert.True(SystemCallClassifier.IsSystemCall(path));
        Assert.Equal(SystemCallClassifier.System, SystemCallClassifier.Classify(path));
    }

    [Theory]
    // echte Nutzer-Aktionen
    [InlineData("/api/courses")]
    [InlineData("/api/courses/123/next")]
    [InlineData("/api/book-puzzles/456/attempt")]
    [InlineData("/api/repertoires")]
    [InlineData("/api/notifications")]          // Liste holen ≠ /count-Poll
    [InlineData("/api/messages")]               // Thread lesen ≠ unread-count
    [InlineData("/api/auth/login")]
    [InlineData("/api/tournaments/999/players")]
    [InlineData("")]
    [InlineData("/")]
    public void UserPaths_AreUser(string path)
    {
        Assert.False(SystemCallClassifier.IsSystemCall(path));
        Assert.Equal(SystemCallClassifier.User, SystemCallClassifier.Classify(path));
    }

    [Fact]
    public void TrailingSlash_Ignored()
    {
        Assert.True(SystemCallClassifier.IsSystemCall("/api/notifications/count/"));
        Assert.True(SystemCallClassifier.IsSystemCall("/health/"));
    }
}
