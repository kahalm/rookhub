using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Unerwartete Chessable-Antworten aus RepCheck: Log immer, Admin-Nachricht nur bei einer Sperre (1× je 24 h).</summary>
public class ChessableResponseAlertServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CapturingLogger<ChessableResponseAlertService> _log = new();
    private readonly ChessableResponseAlertService _service;

    public ChessableResponseAlertServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new ChessableResponseAlertService(_db, new AdminMessageService(_db, new NotificationService(_db)), _log);
        _db.AppUsers.Add(new AppUser { Id = 1, Username = "admin", PasswordHash = "x", IsAdmin = true });
        _db.AppUsers.Add(new AppUser { Id = 7, Username = "felix", PasswordHash = "x" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static ChessableUnexpectedResponseInputDto Report(string? message = null, string? snippet = null,
        int status = 200, string reason = "error") => new()
    {
        Bid = "104929",
        CourseName = "The Dynamic 1.Nc3 - A Unique Repertoire",
        Endpoint = "getGame",
        Oid = "17672584",
        Status = status,
        Reason = reason,
        Message = message,
        Snippet = snippet,
        ExtensionVersion = "1.60.0",
    };

    [Theory]
    [InlineData("User is banned or deleted", null, true)]
    [InlineData("Account suspended", null, true)]
    [InlineData("Course not found", null, false)]
    [InlineData(null, "<html><title>Attention Required! | Cloudflare</title>Sorry, you have been blocked</html>", true)]
    [InlineData(null, "{\"list\":{\"name\":\"Deleted lines\"}}", false)]   // JSON ohne Fehlermeldung: Kursinhalt
    [InlineData(null, "<p>blockedMoves</p>", false)]                         // nur ganze Wörter
    [InlineData(null, "", false)]
    [InlineData("Course not found", "Sorry, you have been blocked", false)] // mit Fehlermeldung zählt nur sie
    [InlineData("   ", "Your account was suspended", true)]
    public void LooksBanned_FollowsTheRepCheckRule(string? message, string? snippet, bool expected)
        => Assert.Equal(expected, ChessableResponseAlertService.LooksBanned(message, snippet));

    [Fact]
    public async Task Report_NotBanned_LogsWarning_WithoutAdminMessage()
    {
        var res = await _service.ReportAsync(7, Report(snippet: "{\"foo\":1}", reason: "shape"));

        Assert.False(res.Banned);
        Assert.False(res.AdminNotified);
        Assert.Empty(_db.AdminMessages);
        var entry = Assert.Single(_log.Events);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.StartsWith("ChessableUnexpectedResponse", entry.Message);
    }

    [Fact]
    public async Task Report_Banned_SendsAdminMessage_InTheUsersThread()
    {
        var res = await _service.ReportAsync(7, Report("User is banned or deleted"));

        Assert.True(res.Banned);
        Assert.True(res.AdminNotified);
        var msg = Assert.Single(_db.AdminMessages);
        Assert.Equal(7, msg.UserId);
        Assert.False(msg.FromAdmin);
        Assert.StartsWith(ChessableResponseAlertService.BanMessagePrefix, msg.Body);
        Assert.Contains("bid 104929", msg.Body);
        Assert.Contains("getGame oid 17672584, HTTP 200", msg.Body);
        Assert.Contains("„User is banned or deleted“", msg.Body);
        Assert.Contains("RepCheck 1.60.0", msg.Body);
        Assert.Contains(_db.Notifications, n => n.UserId == 1);   // Glocke beim Admin
    }

    [Fact]
    public async Task Report_BannedTwiceWithinCooldown_OnlyOneAdminMessage_ButBothLogged()
    {
        await _service.ReportAsync(7, Report("User is banned or deleted"));
        var second = await _service.ReportAsync(7, Report("User is banned or deleted"));

        Assert.True(second.AdminNotified);
        Assert.Single(_db.AdminMessages);
        Assert.Equal(2, _log.Events.Count);
    }

    [Fact]
    public async Task Report_BannedAfterCooldown_SendsAgain()
    {
        await _service.ReportAsync(7, Report("User is banned or deleted"));
        var first = _db.AdminMessages.Single();
        first.CreatedAt = DateTime.UtcNow - ChessableResponseAlertService.BanMessageCooldown - TimeSpan.FromMinutes(1);
        await _db.SaveChangesAsync();

        await _service.ReportAsync(7, Report("User is banned or deleted"));

        Assert.Equal(2, _db.AdminMessages.Count());
    }

    [Fact]
    public async Task Report_OrdinaryUserMessageInThread_DoesNotSuppressTheBanMessage()
    {
        await new AdminMessageService(_db, new NotificationService(_db)).SendFromUserAsync(7, "Frage zum Import");

        await _service.ReportAsync(7, Report("User is banned or deleted"));

        Assert.Equal(2, _db.AdminMessages.Count());
    }

    [Fact]
    public async Task Report_BlockPageWithoutMessage_QuotesTheSnippet()
    {
        var res = await _service.ReportAsync(7, Report(snippet: "Sorry, you have been blocked", status: 403, reason: "http"));

        Assert.True(res.Banned);
        var body = _db.AdminMessages.Single().Body;
        Assert.Contains("HTTP 403", body);
        Assert.Contains("Antwort (Ausschnitt): Sorry, you have been blocked", body);
    }

    [Fact]
    public void BuildBanMessage_ShortensLongSnippets()
    {
        var body = ChessableResponseAlertService.BuildBanMessage(Report(snippet: "blocked " + new string('x', 1500)));

        Assert.Contains(" …", body);
        Assert.True(body.Length < ChessableResponseAlertService.MessageSnippetChars + 500);
    }
}
