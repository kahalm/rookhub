using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace RookHub.Api.Tests;

/// <summary>
/// Scope-Zaun für Personal-Access-Tokens (<c>rkh_…</c>) aus <c>Program.cs</c>: ein Token mit
/// <c>scope</c>-Claim darf ausschließlich <c>/api/extension/*</c> benutzen, alles andere ist 403.
/// Vorher war der Scope NUR im ExtensionController geprüft → ein „extension"-Token war faktisch ein
/// Voll-Account-Token (PUT /api/profile ändert die E-Mail → „Passwort vergessen" → Kontoübernahme).
///
/// <para>WARUM Middleware-Test statt WebApplicationFactory: der Host lässt sich im Testlauf nicht
/// hochfahren. <c>Program.cs</c> ruft direkt nach <c>builder.Build()</c> <c>db.Database.Migrate()</c>
/// auf — das braucht eine ECHTE MariaDB (der Provider macht schon beim Auflösen des DbContexts ein
/// <c>ServerVersion.AutoDetect</c>, ein InMemory-Ersatz kann `Migrate()` gar nicht). Der Startfehler
/// landet im globalen try/catch von <c>Program.cs</c>, der Host stirbt vor der Pipeline
/// („The server has not been started or no web application was configured") — empirisch geprüft.
/// Der Zaun wird deshalb als Delegate-Kette gegen <see cref="DefaultHttpContext"/> geprüft; damit die
/// Kopie nicht vom Original abdriftet, hält <see cref="Fence_IsWiredAfterAuthentication_InProgramCs"/>
/// zusätzlich den echten Quelltext fest (Zaun vorhanden UND nach <c>UseAuthentication</c>).</para>
/// </summary>
public class PatScopeFenceTests
{
    // --- 1:1-Spiegel der Middleware aus Program.cs („Scope-Zaun für Personal-Access-Tokens") ---
    private static readonly string[] PatAllowedPrefixes = ["/api/extension"];

    private static async Task FenceAsync(HttpContext ctx, RequestDelegate next)
    {
        var scope = ctx.User?.FindFirst("scope")?.Value;
        if (scope != null &&
            !PatAllowedPrefixes.Any(p => ctx.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new
            {
                error = "api_token_scope",
                detail = $"API tokens (scope '{scope}') may only be used on {string.Join(", ", PatAllowedPrefixes)}.",
            });
            return;
        }
        await next(ctx);
    }

    /// <summary>Schickt einen Request durch den Zaun. <paramref name="scope"/> null = JWT-User
    /// (JWTs tragen keinen scope-Claim), sonst PAT mit diesem Scope.</summary>
    private static async Task<(int Status, string Body, bool Passed)> RunAsync(string path, string? scope, bool authenticated = true)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "7"), new(ClaimTypes.Name, "tester") };
        if (scope != null) claims.Add(new Claim("scope", scope));
        if (authenticated)
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, scope != null ? "ApiToken" : "Jwt"));

        var passed = false;
        await FenceAsync(ctx, _ => { passed = true; return Task.CompletedTask; });

        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body, Encoding.UTF8).ReadToEndAsync();
        return (ctx.Response.StatusCode, body, passed);
    }

    [Fact]
    public async Task ExtensionToken_MayUseExtensionApi()
    {
        var r = await RunAsync("/api/extension/repertoires", scope: "extension");
        Assert.True(r.Passed);
        Assert.Equal(StatusCodes.Status200OK, r.Status);
    }

    [Fact]
    public async Task ExtensionToken_IsRejectedOnProfile()
    {
        var r = await RunAsync("/api/profile", scope: "extension");
        Assert.False(r.Passed);                       // Request erreicht den Controller gar nicht
        Assert.Equal(StatusCodes.Status403Forbidden, r.Status);
        Assert.Contains("api_token_scope", r.Body);
    }

    [Theory]
    [InlineData("/api/profile/tokens")]
    [InlineData("/api/repertoires")]
    [InlineData("/api/admin/book-puzzles/import")]
    [InlineData("/api/auth/login")]
    public async Task Token_IsRejectedOnEverythingOutsideExtension(string path)
    {
        var r = await RunAsync(path, scope: "extension");
        Assert.False(r.Passed);
        Assert.Equal(StatusCodes.Status403Forbidden, r.Status);
    }

    [Fact]
    public async Task AnyOtherScope_IsAlsoFencedIn()
    {
        // Der Zaun hängt am VORHANDENSEIN des scope-Claims, nicht an seinem Wert: ein künftiger
        // Scope öffnet nicht versehentlich die ganze API.
        var r = await RunAsync("/api/profile", scope: "readonly");
        Assert.Equal(StatusCodes.Status403Forbidden, r.Status);
        Assert.Contains("readonly", r.Body);
    }

    [Fact]
    public async Task PrefixMatch_IsSegmentBased_NotStringBased()
    {
        // FALLE: StartsWithSegments („/api/extensionfoo" ist KEIN Treffer) — ein String-StartsWith
        // hätte eine neue Route mit diesem Präfix still für PATs geöffnet.
        var r = await RunAsync("/api/extensionfoo/secret", scope: "extension");
        Assert.Equal(StatusCodes.Status403Forbidden, r.Status);
    }

    [Fact]
    public async Task PrefixMatch_IgnoresCase()
    {
        var r = await RunAsync("/API/Extension/games", scope: "extension");
        Assert.True(r.Passed);
    }

    [Fact]
    public async Task JwtUser_IsUntouched()
    {
        // JWTs tragen keinen scope-Claim → normaler User-Login bleibt überall durchgelassen.
        Assert.True((await RunAsync("/api/profile", scope: null)).Passed);
        Assert.True((await RunAsync("/api/extension/repertoires", scope: null)).Passed);
    }

    [Fact]
    public async Task AnonymousRequest_IsUntouched()
    {
        // Anonyme Requests laufen weiter (die Autorisierung dahinter entscheidet), sonst wären
        // AllowAnonymous-Endpoints wie /api/menu tot.
        Assert.True((await RunAsync("/api/menu", scope: null, authenticated: false)).Passed);
    }

    // ---- Der Zaun muss im echten Program.cs stehen (und NACH UseAuthentication) ----
    [Fact]
    public void Fence_IsWiredAfterAuthentication_InProgramCs()
    {
        var programCs = FindProgramCs();
        if (programCs == null) return;   // Quellbaum nicht neben der Testassembly (z. B. Paket-Lauf)

        var src = File.ReadAllText(programCs);
        var auth = src.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var fence = src.IndexOf("patAllowedPrefixes", StringComparison.Ordinal);
        Assert.True(auth >= 0, "app.UseAuthentication() fehlt in Program.cs");
        Assert.True(fence >= 0, "Der PAT-Scope-Zaun (patAllowedPrefixes) fehlt in Program.cs");
        // FALLE: vor UseAuthentication ist HttpContext.User noch anonym → der Zaun fände nie einen
        // scope-Claim und wäre ein wirkungsloses No-op.
        Assert.True(auth < fence, "Der PAT-Scope-Zaun muss NACH app.UseAuthentication() stehen");
        Assert.Contains("\"/api/extension\"", src, StringComparison.Ordinal);
        Assert.Contains("api_token_scope", src, StringComparison.Ordinal);
    }

    private static string? FindProgramCs([CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile);
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "src", "api", "RookHub.Api", "Program.cs");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
