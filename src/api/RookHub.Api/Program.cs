using Microsoft.AspNetCore.HttpOverrides;
using RookHub.Api.Middleware;
using RookHub.Api.Services;
using RookHub.Api.Services.Chessable;
using RookHub.Api.Startup;
using Serilog;

// Startup: nur noch die REIHENFOLGE der Aufrufe. Die Registrierungen je Belang stehen in Startup/ (Logging, Datenbank,
// Auth, Fachdienste je Bereich, Hosted Services, HttpClients, CORS, Rate-Limiting, Controller, Health) — die
// Middleware-Reihenfolge unten ist Verhalten und wird von PatScopeFenceTests/ForwardedChainCaptureTests bewacht.
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
    builder.Services.AddRookHubCors(builder.Environment);

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

    // Auto-Migration beim Start + Seeds (Startup/DatabaseSetup.cs).
    await app.MigrateAndSeedAsync();

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

    // Reichert JEDES Log-Event innerhalb eines Requests an (UserId/UserName/IpAddress/VisitorId/RequestKind/DeviceType,
    // Middleware/RequestLogContextMiddleware). Nach UseAuthentication, damit HttpContext.User bereits gesetzt ist.
    app.UseRequestLogContext();

    // Request-Completion-Log je Anfrage (Startup/LoggingSetup.cs) — UserId kommt dort auch für ApiToken-Anfragen dazu,
    // die erst in der Autorisierung angemeldet werden.
    app.UseRookHubRequestLogging();
    app.UseAuthorization();
    app.MapControllers();

    app.MapRookHubHealth();

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
