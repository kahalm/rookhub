using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using RookHub.Api.Data;
using RookHub.Api.Logging;
using RookHub.Api.Middleware;
using RookHub.Api.Services;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using Elastic.Serilog.Sinks;
using RookHub.Api.Services.TournamentDirectory;
using RookHub.Api.Services.Chessable;
using RookHub.Api.Services.Courses;
using RookHub.Api.Services.Puzzles;
using RookHub.Api.Services.Repertoires;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.Scoresheet;
using RookHub.Api.Services.Library;
using RookHub.Api.Services.Kids;
using RookHub.Api.Services.Tournaments;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Social;
using RookHub.Api.Startup;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseRookHubSerilog();

    builder.Services.AddRookHubDatabase(builder.Configuration);
    // JWT oder Personal Access Token (rkh_…) + RBAC-Policies (Startup/AuthSetup.cs).
    builder.Services.AddRookHubAuthentication(builder.Configuration);
    // Leerer/fehlender Verschlüsselungs-Schlüssel und Platzhalter-Geheimnisse → Startabbruch (Startup/StartupChecks.cs).
    StartupChecks.ThrowIfCryptoKeysInvalid(builder.Configuration, builder.Environment);

    // Fachdienste je Bereich (Startup/DomainServices.*.cs).
    builder.Services.AddRookHubDomainServices(builder.Configuration);

    // Alle Hosted Services an EINER Stelle (Startup/BackgroundServicesSetup.cs: Dienst · Schalter · Grund).
    // Schalter fuer den RookHub-eigenen Chessable-Weg — hier EINMAL gelesen und hineingereicht. Vorgabe AN, damit eine
    // Installation ohne die Variable sich wie bisher verhaelt.
    var chessableEnabled = ChessableSwitch.IsEnabled(builder.Configuration);
    builder.Services.AddRookHubBackgroundServices(chessableEnabled);

    // HttpClients (Startup/HttpClientsSetup.cs).
    builder.Services.AddRookHubHttpClients(builder.Configuration);

    builder.Services.AddRookHubDataProtection(builder.Configuration);
    builder.Services.AddRookHubCors();

    // Hinter nginx (Docker) kommt sonst nur die Proxy-IP an → der globale Rate-Limiter
    // würde ALLE Nutzer in eine Partition werfen (faktische Site-weite 100/min-Drossel) und
    // die geloggte IP wäre die Proxy-IP. X-Forwarded-For nur aus den Docker-Netzen und nur für
    // die zwei Proxy-Hops (NPM + frontend-nginx) glauben — Begründung in ForwardedHeadersSetup.
    builder.Services.Configure<ForwardedHeadersOptions>(ForwardedHeadersSetup.Configure);

    // Globaler Deckel je IP + benannte Policies (Startup/RateLimitingSetup.cs).
    builder.Services.AddRookHubRateLimiting(builder.Configuration);

    builder.Services.AddMemoryCache();
    builder.Services.AddResponseCompression();
    builder.Services.AddRookHubControllers();
    builder.Services.AddRookHubSwagger();

    var app = builder.Build();

    // Die ROHE X-Forwarded-For-Kette fürs Log-Feld ForwardedFor merken, bevor UseForwardedHeaders sie verbraucht.
    app.UseForwardedChainCapture();
    // Muss VOR UseRateLimiter + dem IP-Logging laufen, damit RemoteIpAddress die echte Client-IP ist.
    app.UseForwardedHeaders();

    // Platzhalter-Geheimnisse einmal beim Start auf Error melden (Folgen je Art in SecretConfigCheck:
    // eingehend geprüfte Geheimnisse schalten ihr Feature ab, mitgeschickte Schlüssel nur gemeldet),
    // dazu eine Warnung bei leerem SchachBot:StatsSecret (Bot-Stats antworten dann 503).
    SecretConfigCheck.LogStartupFindings(app.Configuration,
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RookHub.Api.Startup"));

    // Auto-migrate on startup + seed admin
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
        await AdminSeeder.SeedAsync(db, app.Configuration);
        await RoleSeeder.SeedAsync(db);   // RBAC-Grundgerüst (System-Rollen + IsAdmin→admin-Rolle spiegeln)
        // Ligamanager-Ligen ohne Tnr-Versatz (Altbestand vor dem Versatz; erwartet: keine) nur melden, nicht umschreiben.
        await RookHub.Api.Services.League.LigamanagerSource.WarnLegacyTnrsAsync(db,
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RookHub.Api.Startup"));
        // Feste Ligapaarungen der Vereinspartien heilen (0.716.1): Schlüssel nachtragen, tote Ids über den Schlüssel bzw. die
        // Paarungssuche neu finden oder leeren. Idempotent, wirft nie.
        await RookHub.Api.Services.League.LeagueGameLinks.HealOnStartupAsync(db,
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RookHub.Api.Startup"));
    }

    // H-5: Global exception handler. Domänen-Ausnahmen kommen hier nicht an (DomainExceptionFilter);
    // was hier landet, ist ein echter Fehler → 500, die Middleware loggt ihn auf Error mit Stacktrace.
    // Rumpf { type, title, status, message, traceId }, 413/400 aus Kestrel durchgereicht (ApiErrorResponses).
    app.UseExceptionHandler(error => error.Run(ApiErrorResponses.WriteUnhandledAsync));

    if (!app.Environment.IsProduction())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseResponseCompression();
    app.UseCors();
    // Authentication MUSS vor dem Rate-Limiter laufen: die Policy „user-flag" partitioniert pro
    // User-Claim und fiel sonst IMMER auf den IP-Fallback zurück (HttpContext.User war noch anonym)
    // — die dokumentierte Pro-User-Drossel gegen Massen-Umflaggen griff also nie, und Nutzer hinter
    // demselben NAT teilten einen Bucket. Endpoint-bezogene Limiter funktionieren weiterhin, weil das
    // Routing (von WebApplication automatisch vorangestellt) davor liegt.
    app.UseAuthentication();

    // ===== Scope-Zaun für Personal-Access-Tokens (rkh_…) =====
    // Wer einen scope-Claim mitbringt (= PAT, JWTs haben keinen), darf ausschließlich die
    // Fläche seines Scopes (Extension bzw. Engine-Provider); alles andere ist 403. Muss NACH UseAuthentication() stehen, sonst ist
    // HttpContext.User noch anonym und der Zaun ein No-op. Logik + erlaubte Präfixe bewusst in
    // PatScopeFenceMiddleware statt inline hier: nur so kann PatScopeFenceTests den echten Code
    // prüfen (eine Test-Kopie der Präfix-Liste würde still auseinanderlaufen).
    app.UsePatScopeFence();

    app.UseRateLimiter();

    // Reichert JEDES Log-Event innerhalb eines Requests mit UserId/UserName/IpAddress an
    // (sofern vorhanden) — nicht nur die Request-Summary. Greift via Enrich.FromLogContext().
    // Nach UseAuthentication, damit HttpContext.User bereits gesetzt ist.
    app.Use(async (ctx, next) =>
    {
        var scopes = new List<IDisposable>(6);
        var ip = ctx.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrEmpty(ip))
            scopes.Add(LogContext.PushProperty("IpAddress", ip));
        // IpAddress oben ist die VERTRAUENSWUERDIGE, aufgeloeste Client-IP (Basis fuer
        // Rate-Limiter). Zusaetzlich die ROHE Weiterleitungs-Kette, wie sie ankam — alle Hops
        // inkl. vom Client vorangestellter Eintraege, die nicht als Client-IP gewaehlt wurden.
        // Gemerkt VOR UseForwardedHeaders (ForwardedChainCapture, auf 256 Zeichen gekappt):
        // X-Original-For traegt nur den Socket-Peer (frontend-nginx mit Port), nicht die Kette.
        // X-Real-IP wird nicht mehr geloggt — frontend-nginx ueberschreibt es mit $remote_addr,
        // dort stand also immer das Docker-Gateway (Codereview A10-008).
        var fwd = ForwardedChainCapture.Get(ctx);
        if (!string.IsNullOrEmpty(fwd))
            scopes.Add(LogContext.PushProperty("ForwardedFor", fwd));
        if (ctx.User?.Identity?.IsAuthenticated == true)
        {
            var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
                scopes.Add(LogContext.PushProperty("UserId", userId));
            if (!string.IsNullOrEmpty(ctx.User.Identity.Name))
                scopes.Add(LogContext.PushProperty("UserName", ctx.User.Identity.Name));
            // Admin-Impersonation: das Token trägt dann den `imp`-Claim (Id des impersonierenden Admins).
            // Ohne Marker sehen impersonierte Requests im Log identisch zum echten User aus → hier als
            // ImpersonatorId mitschreiben, damit jeder impersonierte Request in Kibana filterbar ist.
            var imp = ctx.User.FindFirst("imp")?.Value;
            if (!string.IsNullOrEmpty(imp))
                scopes.Add(LogContext.PushProperty("ImpersonatorId", imp));
        }
        // VisitorId fuer Kibana „Unique Visits": Username (eingeloggt) sonst validierte Anon-Session-Id
        // aus dem X-Visitor-Id-Header. So zaehlen eingeloggte UND anonyme Besucher in EINEM Feld.
        var visitorId = RookHub.Api.Logging.VisitorIdResolver.Resolve(
            ctx.User?.Identity?.IsAuthenticated == true,
            ctx.User?.Identity?.Name,
            ctx.Request.Headers[RookHub.Api.Logging.VisitorIdResolver.HeaderName].FirstOrDefault());
        if (!string.IsNullOrEmpty(visitorId))
            scopes.Add(LogContext.PushProperty("VisitorId", visitorId));
        // Systemcall-Flag: automatische, nicht nutzer-initiierte Requests (Health/Swagger, Client-
        // Heartbeat/-Diagnose, Menü-Check, Badge-/Zähler-Polls, Import-Status-Polls) als
        // RequestKind="system" markieren — echter Nutzer-Traffic bekommt "user". In Kibana filterbar,
        // um das automatische Grundrauschen vom echten Nutzerverhalten zu trennen.
        scopes.Add(LogContext.PushProperty(
            "RequestKind",
            RookHub.Api.Logging.SystemCallClassifier.Classify(ctx.Request.Path.Value)));
        // Geräteklasse (mobile/tablet/desktop/bot/unknown) aus dem User-Agent → in Kibana als
        // Mobile-vs-PC-Anteil (gesamt + je Bereich über url.path) auswertbar. Roh-UA gekürzt
        // mitschreiben (Diagnose), DeviceType immer (auch "unknown", damit die Terms-Agg vollständig ist).
        var ua = ctx.Request.Headers.UserAgent.ToString();
        if (!string.IsNullOrEmpty(ua))
            scopes.Add(LogContext.PushProperty("UserAgent", ua.Length > 512 ? ua[..512] : ua));
        scopes.Add(LogContext.PushProperty(
            "DeviceType", RookHub.Api.Logging.DeviceClassifier.Classify(ua)));
        try { await next(); }
        finally { for (var i = scopes.Count - 1; i >= 0; i--) scopes[i].Dispose(); }
    });

    app.UseSerilogRequestLogging(options =>
    {
        // UserId/UserName ans Request-Completion-Log hängen. Die LogContext-Anreicherungs-Middleware
        // oben läuft VOR UseAuthorization — ApiToken-authentifizierte Requests (z. B. die RepCheck-
        // Extension mit `rkh_`-Token) werden aber erst in der Autorisierung authentifiziert, sodass
        // dort `HttpContext.User` noch anonym war und das Request-Log KEINE UserId trug. Diese
        // Enrichment-Lambda läuft erst NACH dem Endpoint (Completion), da ist `User` gesetzt →
        // z. B. die Extension-Nutzerzahl (distinct UserId über `/api/extension/*`) wird in Kibana
        // zählbar. Für JWT-Requests ist es idempotent (gleicher Wert wie via LogContext).
        options.EnrichDiagnosticContext = (diagCtx, httpCtx) =>
        {
            if (httpCtx.User?.Identity?.IsAuthenticated != true) return;
            var uid = httpCtx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(uid)) diagCtx.Set("UserId", uid);
            if (!string.IsNullOrEmpty(httpCtx.User.Identity.Name))
                diagCtx.Set("UserName", httpCtx.User.Identity.Name);
        };
        options.GetLevel = (httpContext, elapsed, ex) =>
        {
            var path = httpContext.Request.Path.Value ?? "";
            if (path.StartsWith("/health") || path.StartsWith("/swagger"))
                return LogEventLevel.Debug;
            // Long-Poll der Engine-Provider (eigener Broker): 13 Provider = 78 Zeilen je Minute ohne Aussage.
            // Die abgeholten Auftraege protokolliert der Upload selbst (EngineBroker: Upload …).
            if (path.Equals("/api/external-engine/work", StringComparison.OrdinalIgnoreCase)
                && httpContext.Response.StatusCode < 400 && ex == null)
                return LogEventLevel.Debug;
            if (ex != null || httpContext.Response.StatusCode >= 500)
                return LogEventLevel.Error;
            return LogEventLevel.Information;
        };
    });
    app.UseAuthorization();
    app.MapControllers();

    app.MapGet("/health", async (AppDbContext db) =>
    {
        try
        {
            // FALLE: `CanConnectAsync` WIRFT bei einer toten Datenbank nicht, es liefert `false`. Der
            // verworfene Rückgabewert machte /health zu einem „immer gesund" — und daran hängen der
            // Docker-Healthcheck in Produktion und die service_healthy-Kette der e2e-Läufe.
            var ok = await db.Database.CanConnectAsync();
            return ok
                ? Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow })
                : Results.Json(new { status = "unhealthy", timestamp = DateTime.UtcNow }, statusCode: 503);
        }
        catch
        {
            return Results.Json(new { status = "unhealthy", timestamp = DateTime.UtcNow }, statusCode: 503);
        }
    });

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program { }
