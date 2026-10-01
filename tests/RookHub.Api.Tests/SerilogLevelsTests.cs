using Microsoft.Extensions.Logging;
using RookHub.Api.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace RookHub.Api.Tests;

/// <summary>
/// Overrides des Serilog-Bootstraps am echten Logger (Codereview 2026-09-29, A10-014): die Logging-Handler von
/// Microsoft.Extensions.Http schrieben je ausgehendem Request 4 INF-Zeilen samt Pfad (chess.com-/Lichess-Namen
/// aus PlayTimeService) ins ES — piratechess daempfte sie schon, rookhub nicht.
/// </summary>
public class SerilogLevelsTests
{
    private sealed class Sammler : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static (Microsoft.Extensions.Logging.ILoggerFactory Factory, Sammler Sink) Factory()
    {
        var sink = new Sammler();
        var logger = new LoggerConfiguration().ApplyRookHubLevels().WriteTo.Sink(sink).CreateLogger();
        return (new SerilogLoggerFactory(logger, dispose: true), sink);
    }

    [Theory]
    // Kategorien, unter denen IHttpClientFactory loggt (benannter bzw. typisierter Client).
    [InlineData("System.Net.Http.HttpClient.crawler.LogicalHandler")]
    [InlineData("System.Net.Http.HttpClient.PlayTimeService.ClientHandler")]
    public void HttpClientHandler_InformationFaelltWeg_WarnungBleibt(string category)
    {
        var (factory, sink) = Factory();
        using (factory)
        {
            var log = factory.CreateLogger(category);
            log.LogInformation("Sending HTTP request GET https://api.chess.com/pub/player/someone/games/2026/09");
            log.LogWarning("upstream kaputt");
        }
        var e = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Warning, e.Level);
    }

    [Fact]
    public void EigeneDienste_BleibenAufInformation_DataProtectionErstAbError()
    {
        var (factory, sink) = Factory();
        using (factory)
        {
            factory.CreateLogger("RookHub.Api.Services.PlayTimeService").LogInformation("eigene Zeile");
            factory.CreateLogger("Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager").LogWarning("no XML encryptor");
            factory.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics").LogInformation("Request starting");
        }
        var e = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Information, e.Level);
    }

    [Fact]
    public void HttpClientOverride_WieInPiratechess()
    {
        // Spiegel von piratechess_docker/src/api/PirateChess.Api/Program.cs:
        // .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
        Assert.Equal(LogEventLevel.Warning, SerilogLevels.Overrides["System.Net.Http.HttpClient"]);
    }
}
