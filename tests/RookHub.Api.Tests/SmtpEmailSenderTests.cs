using Microsoft.Extensions.Configuration;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.Tests;

public class SmtpEmailSenderTests
{
    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

    private static SmtpEmailSender Create(CapturingLogger<SmtpEmailSender> logger, string env) =>
        new(EmptyConfig(), logger, new FakeWebHostEnvironment { EnvironmentName = env });

    [Fact]
    public async Task SendAsync_SmtpDisabledInDevelopment_LogsBodyWithLink()
    {
        var logger = new CapturingLogger<SmtpEmailSender>();
        var sut = Create(logger, "Development");

        await sut.SendAsync("a@b.c", "Reset", "<a>x</a>", "Open https://app/reset?token=SECRET-RAW-TOKEN");

        // In Development darf der Body (inkl. Reset-Link) zur lokalen Nachvollziehbarkeit geloggt werden.
        Assert.Contains(logger.Events, e => e.Message.Contains("SECRET-RAW-TOKEN"));
    }

    [Fact]
    public async Task SendAsync_SmtpDisabledInProduction_DoesNotLogBodyOrLink()
    {
        var logger = new CapturingLogger<SmtpEmailSender>();
        var sut = Create(logger, "Production");

        await sut.SendAsync("a@b.c", "Reset", "<a>x</a>", "Open https://app/reset?token=SECRET-RAW-TOKEN");

        // Ausserhalb Development darf der Klartext-Body NIE nach ES wandern.
        Assert.DoesNotContain(logger.Events, e => e.Message.Contains("SECRET-RAW-TOKEN"));
        Assert.DoesNotContain(logger.Events, e => e.Message.Contains("token="));
        // Aber die Tatsache (Empfänger + Subject) wird als Fehlkonfiguration geloggt.
        Assert.Contains(logger.Events, e => e.Message.Contains("a@b.c"));
    }
}
