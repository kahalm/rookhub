using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Authorization;
using RookHub.Api.Controllers;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// S5-008: der Bot-Heartbeat kam nur über das anonyme <c>/api/client-log</c> — jeder konnte einen toten Bot für den
/// log-watcher „lebendig" halten. <c>POST /api/bot/heartbeat</c> schreibt die Heartbeat-Zeile nur mit gültiger
/// Bot-Signatur (Vertrag == schach-bot <c>puzzle/rookhub.py</c> <c>_bot_auth_headers('/api/bot/heartbeat')</c>).
/// </summary>
public class BotHeartbeatControllerTests
{
    private const string Secret = "shared_bot_stats_secret_value";
    private const string Path = "/api/bot/heartbeat";

    private static string Sign(string secret, long ts, string signedObject = Path)
        => "sha256=" + SchachBotWebhookService.ComputeHmacHex(secret, ts.ToString(CultureInfo.InvariantCulture) + "." + signedObject);

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static (BotHeartbeatController Controller, CapturingLogger<BotHeartbeatController> Log) Build(
        string? secret, string? signature, string? timestamp, string requestPath = Path)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [BotRequestSignature.SecretConfigKey] = secret })
            .Build();
        var log = new CapturingLogger<BotHeartbeatController>();
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Path = requestPath;
        if (signature != null) ctx.Request.Headers[BotRequestSignature.SignatureHeader] = signature;
        if (timestamp != null) ctx.Request.Headers[BotRequestSignature.TimestampHeader] = timestamp;
        var controller = new BotHeartbeatController(config, log)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx },
        };
        return (controller, log);
    }

    private static string Ts(long ts) => ts.ToString(CultureInfo.InvariantCulture);

    [Fact]
    public void ValidSignature_Returns204_AndWritesTheHeartbeatLine()
    {
        var ts = Now;
        var (c, log) = Build(Secret, Sign(Secret, ts), Ts(ts));

        var result = c.Post();

        Assert.IsType<NoContentResult>(result);
        var line = Assert.Single(log.Events);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, line.Level);
        // Vertrag mit dem log-watcher: gezählt wird labels.HeartbeatService == "schach-bot" (HEARTBEAT_CHECKS
        // schach-bot=rookhub-logs-*); die Altform sucht den gerenderten Satz "Heartbeat: schach-bot".
        Assert.Equal("schach-bot", line.State["HeartbeatService"]);
        Assert.Equal("healthy", line.State["HeartbeatStatus"]);
        Assert.Equal("Heartbeat: schach-bot healthy", line.Message);
        // Derselbe Kopf wie der Heartbeat der API selbst (HeartbeatService) — ein Template, nicht zwei.
        Assert.Equal("Heartbeat: {HeartbeatService} {HeartbeatStatus}", line.State["{OriginalFormat}"]);
    }

    [Fact]
    public void SignedPath_IsFixed_CaseOrTrailingSlashOfTheRequestDoNotMatter()
    {
        var ts = Now;
        var (c, log) = Build(Secret, Sign(Secret, ts), Ts(ts), requestPath: "/API/Bot/Heartbeat/");

        Assert.IsType<NoContentResult>(c.Post());
        Assert.Single(log.Events);
    }

    /// <summary>Die Header je Ablehnungsfall — erst im Test gebaut, damit der Timestamp zur Laufzeit frisch ist
    /// (bei Theorie-Daten aus der Testentdeckung rückte „301 s in der Zukunft" bis zum Lauf ins Fenster).</summary>
    private static (string? Signature, string? Timestamp) RejectedHeaders(string fall)
    {
        var ts = Now;
        return fall switch
        {
            "ohne-header" => (null, null),                                                  // anonymer Aufruf
            "ohne-timestamp" => (Sign(Secret, ts), null),
            "alt-signatur" => ("sha256=" + SchachBotWebhookService.ComputeHmacHex(Secret, Path), Ts(ts)),
            "falsches-secret" => (Sign("anderes_secret_value", ts), Ts(ts)),
            "anderer-pfad" => (Sign(Secret, ts, "/api/client-log"), Ts(ts)),
            "zu-alt" => (Sign(Secret, ts - 301), Ts(ts - 301)),
            "zu-weit-in-der-zukunft" => (Sign(Secret, ts + 301), Ts(ts + 301)),
            "timestamp-passt-nicht" => (Sign(Secret, ts - 10), Ts(ts)),
            "timestamp-keine-zahl" => (Sign(Secret, ts), "gestern"),
            _ => throw new ArgumentOutOfRangeException(nameof(fall), fall, null),
        };
    }

    [Theory]
    [InlineData("ohne-header")]
    [InlineData("ohne-timestamp")]
    [InlineData("alt-signatur")]
    [InlineData("falsches-secret")]
    [InlineData("anderer-pfad")]
    [InlineData("zu-alt")]
    [InlineData("zu-weit-in-der-zukunft")]
    [InlineData("timestamp-passt-nicht")]
    [InlineData("timestamp-keine-zahl")]
    public void InvalidOrMissingSignature_Returns401_AndWritesNoLine(string fall)
    {
        var (signature, timestamp) = RejectedHeaders(fall);
        var (c, log) = Build(Secret, signature, timestamp);

        var result = c.Post();

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(log.Events);   // keine Zeile — schon gar keine mit HeartbeatService
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("change_me_shared_with_schach_bot")]   // Platzhalter aus den Beispiel-Dateien zählt wie leer
    public void SecretNotConfigured_Returns503_AndWritesNoLine(string? secret)
    {
        const string placeholder = "change_me_shared_with_schach_bot";
        var ts = Now;
        var (c, log) = Build(secret, Sign(placeholder, ts), Ts(ts));

        var result = c.Post();

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, obj.StatusCode);
        Assert.Contains(BotStatsController.NotConfiguredReason, System.Text.Json.JsonSerializer.Serialize(obj.Value));
        Assert.Empty(log.Events);
    }
}
