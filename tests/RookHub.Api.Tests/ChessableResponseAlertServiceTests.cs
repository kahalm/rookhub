using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Unerwartete Chessable-Antworten aus RepCheck: Log immer, Admin-Nachricht IMMER — Sperre 1× je Nutzer in 24 h,
/// sonst 1× je Nutzer und Kurs in 1 h.</summary>
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

    // Gewünscht 07.10.2026: JEDE unerwartete Antwort soll den Admins gemeldet werden, nicht nur eine Sperre.
    [Fact]
    public async Task Report_NotBanned_LogsWarning_AndSendsAdminMessageWithTheResponse()
    {
        var res = await _service.ReportAsync(7, Report(snippet: "{\"foo\":1}", reason: "shape"));

        Assert.False(res.Banned);
        Assert.True(res.AdminNotified);
        var msg = Assert.Single(_db.AdminMessages);
        Assert.Equal(7, msg.UserId);
        Assert.StartsWith(ChessableResponseAlertService.UnexpectedMessagePrefix, msg.Body);
        Assert.Contains("(bid 104929)", msg.Body);
        Assert.Contains("getGame oid 17672584, HTTP 200, Grund shape", msg.Body);
        Assert.Contains("Antwort (Ausschnitt): {\"foo\":1}", msg.Body);
        Assert.Contains(_db.Notifications, n => n.UserId == 1);   // Glocke beim Admin
        var entry = Assert.Single(_log.Events);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.StartsWith("ChessableUnexpectedResponse", entry.Message);
    }

    [Fact]
    public async Task Report_NotBanned_SameCourseTwiceWithinAnHour_OneMessage_OtherCourseIsANewCase()
    {
        await _service.ReportAsync(7, Report(snippet: "{}", reason: "shape"));
        await _service.ReportAsync(7, Report(snippet: "{}", reason: "shape"));
        Assert.Single(_db.AdminMessages);
        Assert.Equal(2, _log.Events.Count);

        var anderer = Report(snippet: "{}", reason: "shape");
        anderer.Bid = "27821";
        await _service.ReportAsync(7, anderer);
        Assert.Equal(2, _db.AdminMessages.Count());

        var erste = _db.AdminMessages.OrderBy(m => m.Id).First();
        erste.CreatedAt = DateTime.UtcNow - ChessableResponseAlertService.UnexpectedMessageCooldown - TimeSpan.FromMinutes(1);
        await _db.SaveChangesAsync();
        await _service.ReportAsync(7, Report(snippet: "{}", reason: "shape"));
        Assert.Equal(3, _db.AdminMessages.Count());
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

    /// <summary>Hat der Admin den Thread gelesen, seine Glocke aber nicht weggeklickt, darf die alte Glocke den
    /// Sperrhinweis nicht verschlucken (Entprellung nur innerhalb einer ungelesenen Serie, F5-001).</summary>
    [Fact]
    public async Task Report_AfterAdminReadTheThread_RingsDespiteOldUnseenBell()
    {
        var messages = new AdminMessageService(_db, new NotificationService(_db));
        await messages.SendFromUserAsync(7, "Frage zum Import");
        await messages.MarkSeenByAdminAsync(7);

        await _service.ReportAsync(7, Report("User is banned or deleted"));

        Assert.Equal(2, _db.Notifications.Count(n => n.UserId == 1 && n.Type == NotificationType.UserMessageReceived));
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

    // ----- Abbruch ohne Chessable-Anteil (RepCheck ≥ 1.73.0, gewünscht 07.10.2026) -----

    private static ChessableCrawlErrorInputDto CrawlError(string bid = "27821") => new()
    {
        Bid = bid, CourseName = "Chess Tactics from Scratch", Target = "book", Phase = "sending",
        Message = "RepCheck was updated or reloaded while this page was open", LinesFetched = 41, LinesSent = 0,
        ExtensionVersion = "1.70.0", Browser = "Firefox 143",
    };

    [Fact]
    public async Task CrawlError_LogsAndSendsAdminMessageWithTheDetails()
    {
        var res = await _service.ReportCrawlErrorAsync(7, CrawlError());

        Assert.True(res.AdminNotified);
        var msg = Assert.Single(_db.AdminMessages);
        Assert.StartsWith(ChessableResponseAlertService.CrawlErrorMessagePrefix, msg.Body);
        Assert.Contains("(bid 27821)", msg.Body);
        Assert.Contains("Ziel: book, Phase: sending, Linien: 41 geholt / 0 gesendet", msg.Body);
        Assert.Contains("„RepCheck was updated or reloaded while this page was open“", msg.Body);
        Assert.Contains("RepCheck 1.70.0, Firefox 143", msg.Body);
        Assert.Contains(_db.Notifications, n => n.UserId == 1);
        var entry = Assert.Single(_log.Events);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.StartsWith("ChessableCrawlError", entry.Message);
    }

    [Fact]
    public async Task CrawlError_SameCourseWithinAnHour_OneMessage_ButEveryOneLogged_UnexpectedDoesNotSuppressIt()
    {
        await _service.ReportAsync(7, Report(snippet: "{}", reason: "shape"));   // unerwartete Antwort, eigenes Präfix
        await _service.ReportCrawlErrorAsync(7, CrawlError("104929"));
        await _service.ReportCrawlErrorAsync(7, CrawlError("104929"));

        Assert.Equal(2, _db.AdminMessages.Count());
        Assert.Equal(3, _log.Events.Count);
    }
}
