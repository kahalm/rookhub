using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RookHub.Api.Middleware;
using Serilog.Events;

namespace RookHub.Api.Tests;

/// <summary>
/// Scope-Zaun für Personal-Access-Tokens (<c>rkh_…</c>): ein Token mit <c>scope</c>-Claim darf
/// ausschließlich <c>/api/extension/*</c> benutzen, alles andere ist 403. Vorher war der Scope NUR
/// im ExtensionController geprüft → ein „extension"-Token war faktisch ein Voll-Account-Token
/// (PUT /api/profile ändert die E-Mail → „Passwort vergessen" → Kontoübernahme).
///
/// <para>Diese Tests laufen gegen die ECHTE <see cref="PatScopeFenceMiddleware"/> aus dem
/// API-Projekt (inkl. <see cref="PatScopeFenceMiddleware.AllowedPrefixes"/>) — es gibt keine
/// Test-Kopie der Logik mehr, die von <c>Program.cs</c> abdriften könnte. Früher stand der Zaun
/// als Inline-Lambda in <c>Program.cs</c> und der Test spiegelte ihn 1:1; genau der Fehler, den der
/// Zaun verhindern soll (Präfix-Liste läuft auseinander), wäre damit unentdeckt geblieben.</para>
///
/// <para>WARUM kein <c>WebApplicationFactory</c>-Test der ganzen Pipeline: der Host kommt im
/// Testlauf nicht hoch. <c>Program.cs</c> registriert den DbContext mit
/// <c>ServerVersion.AutoDetect(connectionString)</c> und ruft direkt nach <c>builder.Build()</c>
/// <c>db.Database.Migrate()</c> — beides braucht eine ECHTE MariaDB, und ein InMemory-Ersatz kann
/// <c>Migrate()</c> gar nicht. Der Startfehler landet im globalen try/catch von <c>Program.cs</c>,
/// die Factory scheitert mit „The entry point exited without ever building an IHost" (erneut
/// empirisch geprüft, 2026-08-08). Die Verdrahtung in der echten Pipeline sichern deshalb die
/// beiden Quelltext-Tests am Ende dieser Datei ab.</para>
/// </summary>
public class PatScopeFenceTests
{
    private sealed record FenceResult(int Status, string Body, bool Passed, CapturingLogger<PatScopeFenceMiddleware> Log);

    /// <summary>Schickt einen Request durch den echten Zaun. <paramref name="scope"/> null =
    /// JWT-User (JWTs tragen keinen scope-Claim), sonst PAT mit diesem Scope.</summary>
    private static async Task<FenceResult> RunAsync(
        string path,
        string? scope,
        bool authenticated = true,
        string method = "GET",
        string? authorizationHeader = null,
        PatScopeFenceMiddleware? reuse = null,
        CapturingLogger<PatScopeFenceMiddleware>? log = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Method = method;
        if (authorizationHeader != null) ctx.Request.Headers.Authorization = authorizationHeader;
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
        ctx.Response.Body = new MemoryStream();

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "7"), new(ClaimTypes.Name, "tester") };
        if (scope != null) claims.Add(new Claim("scope", scope));
        if (authenticated)
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, scope != null ? "ApiToken" : "Jwt"));

        var passed = false;
        log ??= new CapturingLogger<PatScopeFenceMiddleware>();
        var middleware = reuse ?? new PatScopeFenceMiddleware(_ => { passed = true; return Task.CompletedTask; }, log);
        // Bei `reuse` (Throttle-Tests) hängt das `passed`-Flag am ursprünglichen next-Delegate;
        // dort wird es nicht ausgewertet.
        await middleware.InvokeAsync(ctx);

        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body, Encoding.UTF8).ReadToEndAsync();
        return new FenceResult(ctx.Response.StatusCode, body, passed, log);
    }

    // ---------------------------------------------------------------- Zaun-Verhalten

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
        Assert.Contains(PatScopeFenceMiddleware.ErrorCode, r.Body);
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
        Assert.False(PatScopeFenceMiddleware.IsAllowedPath("/api/extensionfoo/secret"));
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

    /// <summary>Die erlaubte Fläche ist genau eine Liste — und die steht in der Middleware.
    /// Wächst sie, muss das eine bewusste Änderung sein (dieser Test schlägt dann fehl).</summary>
    [Fact]
    public void AllowedPrefixes_AreExactlyTheExtensionSurface()
    {
        Assert.Equal(new[] { "/api/extension" }, PatScopeFenceMiddleware.AllowedPrefixes);
    }

    // ---------------------------------------------------------------- Logging des Blocks

    [Fact]
    public async Task BlockedRequest_IsLogged_AsWarning_WithStructuredFields()
    {
        // Der Zaun kappt VOR Rate-Limiter, LogContext-Enricher und UseSerilogRequestLogging —
        // ohne dieses Event taucht der 403 in KEINEM Log auf.
        var r = await RunAsync("/api/profile", scope: "extension", method: "PUT");

        var e = Assert.Single(r.Log.Events);
        Assert.Equal(LogLevel.Warning, e.Level);
        Assert.Equal("extension", e.State["ApiTokenScope"]);
        Assert.Equal("PUT", e.State["RequestMethod"]);
        Assert.Equal("/api/profile", e.State["RequestPath"]);
        Assert.Equal("7", e.State["UserId"]);
        Assert.Equal("tester", e.State["UserName"]);
        Assert.Equal("203.0.113.9", e.State["IpAddress"]);
    }

    [Fact]
    public async Task AllowedRequest_IsNotLogged()
    {
        // Sonst schriebe jeder normale Extension-Aufruf ein Security-Event → Rauschen.
        var r = await RunAsync("/api/extension/repertoires", scope: "extension");
        Assert.Empty(r.Log.Events);
    }

    [Fact]
    public async Task BlockedRequest_LeaksNoTokenMaterial()
    {
        // Der Zaun sieht den rohen Token im Authorization-Header. Weder er noch sein Präfix dürfen
        // je im Log landen — ein Log-Leser darf daraus kein benutzbares Credential bauen.
        const string raw = "rkh_supersecrettokenvalue";
        var r = await RunAsync("/api/profile", scope: "extension", authorizationHeader: $"Bearer {raw}");

        var e = Assert.Single(r.Log.Events);
        var dump = e.Message + "|" + string.Join("|", e.State.Select(kv => $"{kv.Key}={kv.Value}"));
        Assert.DoesNotContain("rkh_", dump, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("supersecret", dump, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", dump, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RepeatedBlocks_WarnOnce_ThenStayQuietButStillLog()
    {
        // Ein hämmernder Client (oder ein gestohlener Token in der Schleife) darf den warn_spike
        // des log-watchers nicht dauerhaft auslösen — jeder Block wird aber weiter geloggt.
        var log = new CapturingLogger<PatScopeFenceMiddleware>();
        var mw = new PatScopeFenceMiddleware(_ => Task.CompletedTask, log);

        for (var i = 0; i < 5; i++)
            await RunAsync("/api/profile", scope: "extension", reuse: mw, log: log);

        Assert.Equal(5, log.Events.Count);
        Assert.Equal(1, log.Events.Count(e => e.Level == LogLevel.Warning));
        Assert.Equal(4, log.Events.Count(e => e.Level == LogLevel.Information));
    }

    [Fact]
    public void WarnThrottle_ReopensAfterTheWindow_AndIsPerTokenOwner()
    {
        var mw = new PatScopeFenceMiddleware(_ => Task.CompletedTask, new CapturingLogger<PatScopeFenceMiddleware>());
        var t0 = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

        Assert.True(mw.ShouldWarn("7|extension", t0));
        Assert.False(mw.ShouldWarn("7|extension", t0 + TimeSpan.FromSeconds(59)));
        Assert.True(mw.ShouldWarn("7|extension", t0 + PatScopeFenceMiddleware.WarnThrottle));
        // Ein anderer Token-Inhaber wird durch die Drossel des ersten nicht verschluckt.
        Assert.True(mw.ShouldWarn("8|extension", t0 + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void WarnThrottle_TableDoesNotGrowUnbounded()
    {
        var mw = new PatScopeFenceMiddleware(_ => Task.CompletedTask, new CapturingLogger<PatScopeFenceMiddleware>());
        var t0 = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

        // Alle innerhalb DESSELBEN Fensters: hier ist nichts abgelaufen, was weggeräumt werden
        // könnte. Genau dieser Fall zeigt, ob der Deckel echt ist — die erste Fassung ließ die
        // Tabelle hier auf das Dreifache anwachsen und der Test sicherte das auch noch zu.
        for (var i = 0; i < PatScopeFenceMiddleware.MaxThrottleEntries * 3; i++)
            mw.ShouldWarn($"user{i}|extension", t0);
        Assert.True(mw.ThrottleEntryCount <= PatScopeFenceMiddleware.MaxThrottleEntries,
            $"Throttle-Tabelle über dem Deckel: {mw.ThrottleEntryCount}");

        // Sobald ihr Fenster abgelaufen ist, fallen die alten Einträge beim nächsten Block raus.
        mw.ShouldWarn("late|extension", t0 + PatScopeFenceMiddleware.WarnThrottle + TimeSpan.FromSeconds(1));
        Assert.Equal(1, mw.ThrottleEntryCount);
    }

    /// <summary>Das Log-Event muss in Kibana unter <c>tags: auth</c> auffindbar sein
    /// (Domänen-Tag-Konvention: Serilog-Property <c>LogTags</c>, siehe
    /// log-watcher/schema/logging-schema.md). Deshalb hier gegen einen echten Serilog-Logger mit
    /// <c>Enrich.FromLogContext()</c> geprüft — ein reiner ILogger sieht die LogContext-Properties nicht.</summary>
    [Fact]
    public async Task BlockedRequest_CarriesEcsDomainTagAndStatusCode()
    {
        var sink = new CollectingSink();
        var serilog = new Serilog.LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();
        using var factory = new Serilog.Extensions.Logging.SerilogLoggerFactory(serilog, dispose: true);

        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/profile";
        ctx.Response.Body = new MemoryStream();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "7"), new Claim("scope", "extension")], "ApiToken"));

        var mw = new PatScopeFenceMiddleware(_ => Task.CompletedTask, factory.CreateLogger<PatScopeFenceMiddleware>());
        await mw.InvokeAsync(ctx);

        var e = Assert.Single(sink.Events);
        Assert.Equal(LogEventLevel.Warning, e.Level);
        Assert.Equal("auth", (string?)((ScalarValue)e.Properties["LogTags"]).Value);
        Assert.Equal(403, Convert.ToInt32(((ScalarValue)e.Properties["StatusCode"]).Value));
        Assert.Contains("Unauthorized", e.RenderMessage());   // Ingest-Pipeline taggt daran zusätzlich `auth`
    }

    private sealed class CollectingSink : Serilog.Core.ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    // ---------------------------------------------------------------- Verdrahtung in Program.cs

    /// <summary>Der Zaun muss in der echten Pipeline hängen — und zwar NACH
    /// <c>UseAuthentication()</c> (davor ist <c>HttpContext.User</c> anonym → wirkungsloses No-op).</summary>
    [Fact]
    public void Fence_IsWiredAfterAuthentication_InProgramCs()
    {
        var programCs = FindProgramCs();
        if (programCs == null) return;   // Quellbaum nicht neben der Testassembly (z. B. Paket-Lauf)

        var src = File.ReadAllText(programCs);
        var auth = src.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var fence = src.IndexOf("app.UsePatScopeFence();", StringComparison.Ordinal);
        Assert.True(auth >= 0, "app.UseAuthentication() fehlt in Program.cs");
        Assert.True(fence >= 0, "Der PAT-Scope-Zaun (app.UsePatScopeFence()) fehlt in Program.cs");
        Assert.True(auth < fence, "Der PAT-Scope-Zaun muss NACH app.UseAuthentication() stehen");

        var controllers = src.IndexOf("app.MapControllers();", StringComparison.Ordinal);
        Assert.True(controllers < 0 || fence < controllers,
            "Der PAT-Scope-Zaun muss VOR app.MapControllers() stehen");
    }

    /// <summary>Kein zweiter Zaun: die Präfix-Liste und der Fehlercode existieren nur EINMAL, in
    /// <see cref="PatScopeFenceMiddleware"/>. Kehrt jemand zur Inline-Fassung in <c>Program.cs</c>
    /// zurück, testet dieser Test wieder eine Kopie — deshalb schlägt er dann fehl.</summary>
    [Fact]
    public void ProgramCs_HasNoSecondCopyOfTheFence()
    {
        var programCs = FindProgramCs();
        if (programCs == null) return;

        var src = File.ReadAllText(programCs);
        Assert.DoesNotContain(PatScopeFenceMiddleware.ErrorCode, src, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/api/extension\"", src, StringComparison.Ordinal);
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
