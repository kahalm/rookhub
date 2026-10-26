using System.Reflection;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RookHub.Api.Controllers.Chessable;
using RookHub.Api.Startup;

namespace RookHub.Api.Tests;

/// <summary>
/// CORS (Codereview A10-007): die <c>ExtensionPolicy</c> öffnete die Extension-Endpunkte für Skripte auf chess.com, lichess.org
/// und chessable.com — für das entfallene Userscript. Die Extension ruft RookHub nur aus ihrem Background-Worker (host_permissions,
/// CORS-frei). Bleibt nur die Dev-Vorgabe für das lokale Frontend, und die nur in Development.
/// </summary>
public class CorsSetupTests
{
    private static async Task<CorsPolicy?> PolicyAsync(string environment, string? name)
    {
        var services = new ServiceCollection().AddLogging().AddRookHubCors(new FakeWebHostEnvironment { EnvironmentName = environment });
        using var sp = services.BuildServiceProvider();
        return await sp.GetRequiredService<ICorsPolicyProvider>().GetPolicyAsync(new DefaultHttpContext(), name);
    }

    [Fact]
    public async Task Production_HasNoCorsPolicyAtAll()
    {
        Assert.Null(await PolicyAsync(Environments.Production, null));
        Assert.Null(await PolicyAsync(Environments.Production, "ExtensionPolicy"));
    }

    [Fact]
    public async Task Development_KeepsTheLocalFrontendDefault()
    {
        var policy = await PolicyAsync(Environments.Development, null);
        Assert.NotNull(policy);
        Assert.Contains("http://localhost:4200", policy!.Origins);
        Assert.Null(await PolicyAsync(Environments.Development, "ExtensionPolicy"));
    }

    [Fact]
    public void ExtensionController_DoesNotEnableCors() =>
        Assert.Null(typeof(ExtensionController).GetCustomAttribute<EnableCorsAttribute>());
}
