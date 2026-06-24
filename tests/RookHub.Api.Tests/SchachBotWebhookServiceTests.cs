using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class SchachBotWebhookServiceTests
{
    private static IConfiguration BuildConfig(string? url, string? secret) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SchachBot:WebhookUrl"] = url,
                ["SchachBot:WebhookSecret"] = secret,
            })
            .Build();

    private class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        public string? LastBody;
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public Exception? ThrowOnSend;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (ThrowOnSend != null) throw ThrowOnSend;
            LastRequest = request;
            if (request.Content != null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status);
        }
    }

    private static SchachBotWebhookService Build(CapturingHandler handler, IConfiguration cfg) =>
        new(new HttpClient(handler), cfg, NullLogger<SchachBotWebhookService>.Instance);

    [Fact]
    public void IsEnabled_FalseWhenUrlOrSecretMissing()
    {
        Assert.False(Build(new CapturingHandler(), BuildConfig(null, null)).IsEnabled);
        Assert.False(Build(new CapturingHandler(), BuildConfig("http://x", null)).IsEnabled);
        Assert.False(Build(new CapturingHandler(), BuildConfig(null, "s")).IsEnabled);
        Assert.True(Build(new CapturingHandler(), BuildConfig("http://x", "s")).IsEnabled);
    }

    [Fact]
    public async Task NotifyAttemptAsync_NoOp_WhenDisabled()
    {
        var handler = new CapturingHandler();
        var svc = Build(handler, BuildConfig(null, null));
        await svc.NotifyAttemptAsync(42, new BookPuzzleResultsDto());
        Assert.Null(handler.LastRequest); // gar nichts geschickt
    }

    [Fact]
    public async Task NotifyAttemptAsync_SendsPostWithSignedBody()
    {
        var handler = new CapturingHandler();
        const string url = "http://schach-bot:9000/webhook/puzzle-attempt";
        const string secret = "my-secret";
        var svc = Build(handler, BuildConfig(url, secret));

        var results = new BookPuzzleResultsDto
        {
            SolvedCount = 2,
            AnonymousSolvedCount = 1,
            AttemptCount = 5,
            Solvers = new List<BookSolverDto>
            {
                new() { Name = "Anna", DiscordId = "111", DiscordUsername = "anna#1", TimeSeconds = 42 },
                new() { Name = "Ben", DiscordId = null, DiscordUsername = null, TimeSeconds = 7 },
            },
        };
        await svc.NotifyAttemptAsync(42, results);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(url, handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("application/json", handler.LastRequest.Content!.Headers.ContentType!.MediaType);
        var sigHeader = handler.LastRequest.Headers.GetValues("X-Webhook-Signature").Single();
        Assert.StartsWith("sha256=", sigHeader);

        // Replay-Schutz: Timestamp-Header vorhanden + frisch (±300 s), Signatur über "<ts>.<body>"
        var tsHeader = handler.LastRequest.Headers.GetValues("X-Webhook-Timestamp").Single();
        var ts = long.Parse(tsHeader, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ts) <= 300);

        // Signature == HMAC-SHA256(secret, "<ts>.<body>")
        var expected = "sha256=" + SchachBotWebhookService.ComputeHmacHex(secret, tsHeader + "." + handler.LastBody!);
        Assert.Equal(expected, sigHeader);

        // Body enthält puzzleId, solvedCount, anonymousSolvedCount, attemptCount, solvers
        using var doc = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(42, doc.RootElement.GetProperty("puzzleId").GetInt32());
        var rs = doc.RootElement.GetProperty("results");
        Assert.Equal(2, rs.GetProperty("solvedCount").GetInt32());
        Assert.Equal(1, rs.GetProperty("anonymousSolvedCount").GetInt32());
        Assert.Equal(5, rs.GetProperty("attemptCount").GetInt32());
        var solvers = rs.GetProperty("solvers");
        Assert.Equal(2, solvers.GetArrayLength());
        Assert.Equal("Anna", solvers[0].GetProperty("name").GetString());
        Assert.Equal("111", solvers[0].GetProperty("discordId").GetString());
        Assert.Equal(42, solvers[0].GetProperty("timeSeconds").GetInt32());   // Lösungszeit im Payload
        Assert.Equal(7, solvers[1].GetProperty("timeSeconds").GetInt32());
    }

    [Fact]
    public async Task NotifyAttemptAsync_SwallowsHttpErrors()
    {
        var handler = new CapturingHandler { Status = HttpStatusCode.ServiceUnavailable };
        var svc = Build(handler, BuildConfig("http://x", "s"));
        // Soll nicht werfen — Webhook ist best-effort.
        await svc.NotifyAttemptAsync(1, new BookPuzzleResultsDto());
        Assert.NotNull(handler.LastRequest);
    }

    [Fact]
    public async Task NotifyAttemptAsync_SwallowsExceptions()
    {
        var handler = new CapturingHandler { ThrowOnSend = new HttpRequestException("connect failed") };
        var svc = Build(handler, BuildConfig("http://x", "s"));
        await svc.NotifyAttemptAsync(1, new BookPuzzleResultsDto());
        // Wenn wir hier sind ohne Exception, war es fire-and-forget-safe.
    }

    [Fact]
    public void ComputeHmacHex_MatchesKnownVector()
    {
        // Deterministischer Selbst-Check: gleiche Eingabe → gleiche Ausgabe; und HMAC-Standard.
        var actual = SchachBotWebhookService.ComputeHmacHex("k", "hello");
        Assert.Equal(64, actual.Length);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("k"));
        var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes("hello"))).ToLowerInvariant();
        Assert.Equal(expected, actual);
    }
}
