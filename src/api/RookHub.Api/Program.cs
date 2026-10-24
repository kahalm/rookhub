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

    builder.Host.UseSerilog((context, services, configuration) =>
    {
        configuration
            // Grundlevel + Overrides (u. a. HttpClient auf Warning) in Logging/SerilogLevels.cs.
            .ApplyRookHubLevels()
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithProperty("Application", "RookHub")
            .WriteTo.Console();

        var esUrl = context.Configuration["Elasticsearch:Url"];
        if (!string.IsNullOrEmpty(esUrl))
        {
            // ECS-Schema (Elastic.Serilog.Sinks) in einen Data-Stream. Felder werden zentral per
            // Ingest-Pipeline normalisiert (siehe log-watcher/schema/logging-schema.md).
            // Data-Stream-Basisname aus dem bisherigen Monats-IndexFormat ableiten (Teil vor "{"),
            // damit dev/prod unter ihren bestehenden "*-logs-*"-Patterns bleiben (Kibana, log-watcher).
            var indexFormat = context.Configuration["Elasticsearch:IndexFormat"] ?? "rookhub-logs-{0:yyyy.MM}";
            var streamName = indexFormat.Split('{')[0].TrimEnd('-', '.', ' ');
            configuration.WriteTo.Elasticsearch([new Uri(esUrl)], opts =>
            {
                opts.DataStream = new Elastic.Ingest.Elasticsearch.DataStreams.DataStreamName(streamName);
                opts.BootstrapMethod = Elastic.Ingest.Elasticsearch.BootstrapMethod.Silent;
            });
        }
    });

    // Database
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    builder.Services.AddDbContext<AppDbContext>(options =>
        // Feste Server-Fassung statt AutoDetect: AutoDetect oeffnete je Scope eine eigene Verbindung ausserhalb der
        // Retry-Strategie unten und warf bei einem MariaDB-Neustart schon beim Aufloesen des DbContext (A9-010).
        options.UseMySql(connectionString, DbServerVersion.Current,
            // Transiente DB-Fehler (z. B. kurzer Verbindungsverlust beim MariaDB-Neustart/Recreate)
            // automatisch wiederholen, statt Background-Tasks/Requests hart fehlschlagen zu lassen.
            mySql => mySql.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null))
        // Der Code fängt DbUpdateException an vielen Stellen bewusst ab, um idempotente Races auf
        // Unique-Indizes aufzulösen (z. B. paralleler Erstinsert von anonymem Endless-Progress,
        // CoursePuzzleResult, CourseInfoView, CourseProgress …). EF Core loggt den fehlgeschlagenen
        // SaveChanges-Versuch trotzdem auf Error → das trippt den Log-Watcher grundlos. Genuine,
        // NICHT abgefangene Fehler bleiben über das HTTP-500-Request-Log sichtbar; daher hier nur den
        // erwarteten SaveChanges-Fehl-Event auf Information herabstufen (kein Rauschen mehr).
        .ConfigureWarnings(w => w.Log(
            (Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.SaveChangesFailed, LogLevel.Information))));

    // JWT Authentication — Schluessel, Aussteller und Pruefregeln aus JwtTokens (dieselben wie beim Ausstellen
    // in AuthService und beim Cookie-Tausch in SharedSessionService).
    if (JwtTokens.SigningKey(builder.Configuration).Key.Length < 32)
        throw new InvalidOperationException("JWT key must be at least 32 bytes for HMAC-SHA256");
    // Default-Scheme ist ein Policy-Scheme, das anhand des Bearer-Prefixes entscheidet:
    // `rkh_…` → ApiToken-Handler (Personal Access Tokens fuer Maschinen-Clients),
    // alles andere → JWT (User-Login). So koennen Endpoints transparent beides akzeptieren.
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = "Bearer";
        options.DefaultChallengeScheme = "Bearer";
    })
    .AddPolicyScheme("Bearer", "JWT or ApiToken", options =>
    {
        options.ForwardDefaultSelector = ctx =>
        {
            var auth = ctx.Request.Headers.Authorization.ToString();
            if (auth.StartsWith("Bearer rkh_", StringComparison.OrdinalIgnoreCase))
                return ApiTokenAuthenticationHandler.SchemeName;
            return "Jwt";
        };
    })
    // JWT-Handler unter eigenem Namen "Jwt" — NICHT "Bearer", sonst kollidiert er mit dem
    // Policy-Scheme "Bearer" oben ("Scheme already exists: Bearer" → Startup-Crash).
    .AddJwtBearer("Jwt", options =>
    {
        // ClockSkew 1 min statt 5 (siehe JwtTokens.ClockSkew).
        options.TokenValidationParameters =
            JwtTokens.ValidationParameters(builder.Configuration, builder.Configuration["Jwt:Audience"]);
        // Gelöschte/anonymisierte Konten dürfen ihr noch gültiges JWT nicht weiterverwenden, ein
        // rotierter Security-Stamp (Passwortwechsel) entwertet es ebenfalls — und jede Ablehnung wird
        // GELOGGT (Logger RookHub.Api.JwtAuth), sonst ist ein 401 nicht von einem falschen Passwort zu
        // unterscheiden. Ein Datenbankfehler während der Prüfung lässt den Request durch statt den
        // Client auszuloggen. Details und Begründung in JwtTokenGate.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = JwtTokenGate.OnTokenValidatedAsync,
            OnAuthenticationFailed = JwtTokenGate.OnAuthenticationFailedAsync,
        };
    })
    .AddScheme<ApiTokenAuthenticationOptions, ApiTokenAuthenticationHandler>(
        ApiTokenAuthenticationHandler.SchemeName, _ => { });

    // RBAC: dynamischer Policy-Provider für [HasPermission("…")] (perm:<key>-Policies on the fly)
    // + Handler, der Admin-Rolle ODER perm-Claim akzeptiert.
    builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider,
        RookHub.Api.Authorization.PermissionPolicyProvider>();
    builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
        RookHub.Api.Authorization.PermissionAuthorizationHandler>();

    // Fail-fast gegen einen LEEREN Verschlüsselungs-Schlüssel. `EncryptionService` ist ein
    // Singleton und wird erst beim ersten Chessable-Request erzeugt — ein Konfigurationsfehler
    // fiele sonst mitten im Betrieb auf statt beim Start. Genau dieser Fall ist realistisch:
    // `${ENCRYPTION_KEY:-}` in einer Compose-Datei liefert bei ungesetzter Variable einen
    // LEERSTRING (nicht "fehlt"), und ohne diese Prüfung würde damit mit SHA256("") — einem
    // öffentlich bekannten Fixwert — „verschlüsselt". Ein komplett FEHLENDER Schlüssel ist nur mit
    // Chessable:Enabled=false erlaubt (A3-017): früher galt er pauschal als erlaubt, der Dienst warf dann
    // aber bei jeder Auflösung, und die ganze Extension-API antwortete 500. Ohne Chessable wirft er jetzt
    // erst beim Ver-/Entschlüsseln (Lichess-Engine-Zugang speichern), der Rest läuft.
    var configuredEncryptionKey = builder.Configuration["Encryption:Key"];
    if (configuredEncryptionKey != null && string.IsNullOrWhiteSpace(configuredEncryptionKey))
        throw new InvalidOperationException(
            "Encryption:Key ist gesetzt, aber leer. Entweder einen echten Schlüssel hinterlegen " +
            "(ENCRYPTION_KEY) oder die Variable ganz weglassen — ein leerer Wert würde den " +
            "gespeicherten Chessable-Bearer nur zum Schein verschlüsseln.");
    EncryptionService.ThrowIfKeyRequiredButMissing(builder.Configuration);
    // Platzhalter aus den öffentlichen Beispiel-Dateien („change_me…", „your_…") als JWT- oder
    // Verschlüsselungsschlüssel: in Production Startabbruch. Der JWT-Platzhalter hat 43 Byte und
    // bestand die Längenprüfung oben — jeder Leser des Repos hätte sich ein Admin-JWT signieren können.
    SecretConfigCheck.ThrowIfCryptoKeyIsPlaceholder(builder.Configuration, builder.Environment);

    // Fachdienste je Bereich (Startup/DomainServices.*.cs).
    builder.Services.AddRookHubDomainServices(builder.Configuration);

    // Alle Hosted Services an EINER Stelle (Startup/BackgroundServicesSetup.cs: Dienst · Schalter · Grund).
    // Schalter fuer den RookHub-eigenen Chessable-Weg — hier EINMAL gelesen und hineingereicht. Vorgabe AN, damit eine
    // Installation ohne die Variable sich wie bisher verhaelt.
    var chessableEnabled = ChessableSwitch.IsEnabled(builder.Configuration);
    builder.Services.AddRookHubBackgroundServices(chessableEnabled);

    // HttpClients (Startup/HttpClientsSetup.cs).
    builder.Services.AddRookHubHttpClients(builder.Configuration);

    // Data Protection — Keys auf gemountetes Volume persistieren, damit sie Neustarts
    // ueberleben (sonst ephemere In-Memory-Keys + "No XML encryptor"-Warnings bei jedem Boot).
    // Pfad konfigurierbar (DataProtection:KeyPath, Default /keys = Prod-Volume). SetApplicationName
    // haelt die Purpose-Strings stabil, auch wenn sich der Containername aendert.
    var dataProtection = builder.Services.AddDataProtection().SetApplicationName("RookHub");
    var keyPath = builder.Configuration["DataProtection:KeyPath"] ?? "/keys";
    try
    {
        Directory.CreateDirectory(keyPath);
        // FALLE: `CreateDirectory` wirft bei einem BESTEHENDEN Verzeichnis nicht — egal, welche
        // Rechte es hat — und `PersistKeysToFileSystem` prüft beim Registrieren gar nichts. Bei
        // einem Host-Bind-Mount (`- /data/keys:/keys`) gehört das Verzeichnis root, `appuser` darf
        // nicht schreiben (die `chown`-Rechte des Images wandern nur in ein FRISCHES benanntes
        // Volume), und der Fallback unten lief nie an: der Start meldete Erfolg, und der erste
        // „Passwort vergessen"-Request scheiterte Wochen später mit 500 statt einer Mail.
        // Deshalb hier EINMAL wirklich schreiben.
        var probe = Path.Combine(keyPath, ".write-probe");
        File.WriteAllText(probe, DateTime.UtcNow.ToString("O"));
        File.Delete(probe);
        dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
    }
    catch (Exception ex)
    {
        // Pfad nicht beschreibbar (z. B. lokale Dev ohne gemountetes /keys, oder ein root-eigener
        // Bind-Mount) → In-Memory-Keys (ephemer; ueberleben den Prozess nicht). Kein Startup-Crash,
        // aber die Warnung steht ab jetzt BEIM START im Log, nicht erst als 500 im Betrieb.
        Log.Warning(ex, "DataProtection: key path {KeyPath} not writable — falling back to in-memory keys", keyPath);
    }

    // CORS policies
    builder.Services.AddCors(options =>
    {
        // Policy for the Chrome extension (applied only to ExtensionController).
        // KEIN AllowCredentials: Auth laeuft ausschliesslich ueber Bearer-Token im
        // Authorization-Header — Cookies werden vom Userscript nicht gebraucht und
        // wuerden CSRF-Risiko schaffen, sollten weitere Origins hinzukommen.
        options.AddPolicy("ExtensionPolicy", policy =>
        {
            // Origins, auf denen das RepCheck-Userscript läuft und per fetch direkt zur API spricht
            // (die Extension-Variante geht CORS-frei über ihren Background-Worker). chessable.com kam
            // mit der Chessable-Trainingszeit-Meldung dazu; POST für analyze-game + training-activity.
            policy.WithOrigins(
                    "https://www.chess.com",
                    "https://lichess.org",
                    "https://www.chessable.com",
                    "https://chessable.com")
                .WithMethods("GET", "POST")
                .WithHeaders("Authorization", "Content-Type");
        });
        // Default policy for frontend (nur lokale Dev-Origins; Prod ist same-origin hinter nginx).
        // KEIN AllowCredentials: Auth läuft ausschließlich über den Bearer-Header (JWT aus localStorage),
        // es werden keine Cookies gebraucht. Die Kombination AllowCredentials + AllowAnyHeader wäre eine
        // gefährliche Default-Fläche, sobald jemand später eine weitere Origin hinzufügt — daher weglassen.
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins(
                    "http://localhost:4200",
                    "http://localhost:8085")
                .AllowAnyMethod()
                .AllowAnyHeader();
        });
    });

    // Hinter nginx (Docker) kommt sonst nur die Proxy-IP an → der globale Rate-Limiter
    // würde ALLE Nutzer in eine Partition werfen (faktische Site-weite 100/min-Drossel) und
    // die geloggte IP wäre die Proxy-IP. X-Forwarded-For nur aus den Docker-Netzen und nur für
    // die zwei Proxy-Hops (NPM + frontend-nginx) glauben — Begründung in ForwardedHeadersSetup.
    builder.Services.Configure<ForwardedHeadersOptions>(ForwardedHeadersSetup.Configure);

    // M-11: Rate limiting for auth endpoints
    // S-6: Global rate limiting for all endpoints
    // Faktor auf ALLE Deckel unten; setzt nur der E2E-Stack (siehe RateLimitScale).
    var permitScale = RookHub.Api.Services.RateLimitScale.FromConfig(builder.Configuration);
    builder.Services.AddSingleton<RateLimitRejectionHandler>();
    builder.Services.AddRateLimiter(options =>
    {
        // 100/min je IP — außer für die Kinderseiten-Lesezugriffe („kids-read"), deren eigene Policy die Obergrenze je
        // Adresse ist (siehe RateLimitPartitions).
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RookHub.Api.Services.RateLimitPartitions.Global(context, permitScale));
        // Die benannten Limiter MÜSSEN pro Client-IP partitionieren (wie der GlobalLimiter), sonst
        // wäre jeder ein EINZIGER globaler Bucket: ein Angreifer könnte mit 10 Logins/min das Fenster
        // site-weit für alle Nutzer ausschöpfen (Login/Register/Forgot-DoS) UND Brute-Force wäre nicht
        // pro-IP gedrosselt. Partition-Key = aufgelöste Client-IP (UseForwardedHeaders läuft davor).
        static RateLimitPartition<string> PerIpFixedWindow(HttpContext ctx, int permit) =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
        options.AddPolicy("auth", ctx => PerIpFixedWindow(ctx, 10 * permitScale));
        // Offene Endpunkte nach ZWECK getrennt (vorher teilten sich 21 davon „anonymous-puzzle", 30/min je IP — eine
        // Schulklasse hinter einer NAT-Adresse sah in KidHub ab dem 16. Kind 429). Schlüssel und Deckel in
        // RateLimitPartitions: Kinderseite je IP mit hohem Deckel und ohne den globalen Topf; anonyme Stände und
        // Lesezugriffe je Konto bzw. je IP + X-Visitor-Id; der Rest (Client-Log, Bot-Statistik, Token-Test,
        // Bestandssuche, Extension-Senke) bleibt „anonymous-puzzle", angemeldet jetzt je Konto statt je IP.
        options.AddPolicy("anonymous-puzzle", ctx => RookHub.Api.Services.RateLimitPartitions.AnonymousMisc(ctx, permitScale));
        options.AddPolicy("kids-read", ctx => RookHub.Api.Services.RateLimitPartitions.KidsRead(ctx, permitScale));
        options.AddPolicy("ebook-entries", ctx => RookHub.Api.Services.RateLimitPartitions.EbookEntries(ctx, permitScale));
        options.AddPolicy("ebook-file", ctx => RookHub.Api.Services.RateLimitPartitions.EbookFile(ctx, permitScale));
        options.AddPolicy("anonymous-read", ctx => RookHub.Api.Services.RateLimitPartitions.AnonymousRead(ctx, permitScale));
        options.AddPolicy("anonymous-write", ctx => RookHub.Api.Services.RateLimitPartitions.AnonymousWrite(ctx, permitScale));
        // Community-Review-Flags (flag-hints): jeder eingeloggte User darf setzen/aufheben — aber
        // gedrosselt PRO USER, damit niemand per Id-Iteration den Admin-Review-Bestand des ganzen
        // Katalogs umflaggt (der globale 100/min-IP-Limiter erlaubte ~6000 Flags/h).
        options.AddPolicy("user-flag", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10 * permitScale,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        // Nachricht an das Admin-Team (POST /api/messages/reply): offen für jedes Konto, und jede Nachricht legte bei
        // ALLEN Admins Glocke + Web-Push an — nur der globale 100/min-IP-Deckel bremste (~6000 Nachrichten/h). Je KONTO;
        // die Glocke entprellt zusätzlich AdminMessageService (Serie ungelesener Nachrichten = eine Glocke je Admin).
        // Derselbe Topf gilt für Meldung und Quellen-Hinweis im Turnierverzeichnis — beide schreiben in diesen Kanal.
        options.AddPolicy("user-message", ctx => RookHub.Api.Services.RateLimitPartitions.UserMessage(ctx, permitScale));
        // „Spielzeit aktualisieren" (POST /api/training-goals/sync-play): jeder Aufruf fragte von der Server-IP bei Lichess
        // und chess.com — je KONTO gedrosselt (die Sperrfrist je Plattform setzt PlayTimeService.ManualSyncCooldown).
        options.AddPolicy("sync-play", ctx => RookHub.Api.Services.RateLimitPartitions.SyncPlay(ctx, permitScale));
        // Nutzer-Suche (Autocomplete in der Freundesliste): eigene Partition PRO USER. Vorher lief sie
        // auf der „auth"-Policy — und Endpoint-Limiter partitionieren nach (Policy-Name, Key), also
        // teilten sich Suche und Login/Registrierung/Reset EIN 10/min-Fenster je IP: zehn
        // Tastenanschläge sperrten hinter einem NAT (Schule, Verein) die Anmeldung für eine Minute.
        options.AddPolicy("user-search", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30 * permitScale,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        // Rechte-Abfrage (GET /api/auth/permissions): eigene Partition PRO USER. Sie lief über das Klassen-Attribut
        // auf „auth" — jede sichtbare Oberfläche (RookHub, Turnierseite, LeagueHub) fragt beim Start, bei der Rückkehr
        // in den Tab und alle 2 min, und hinter einem NAT erschöpfte das das 10/min-Login-Fenster der ganzen IP.
        options.AddPolicy("auth-permissions", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30 * permitScale,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        // Geteilte Anmeldung (POST /api/auth/rh-session, rh-session/end): JEDER App-Start ohne Anmeldung fragt hier (auf allen
        // vier Oberflächen) — eigene IP-Partition mit höherem Deckel statt „auth": eine Schulklasse, die KidHub öffnet,
        // sind 25 Starts in einer Minute und sperrte sonst Anmeldung, Registrierung und „Passwort vergessen".
        options.AddPolicy("auth-session", ctx => PerIpFixedWindow(ctx, 60 * permitScale));
        // Chessable-Roh-Senken der Extension (problem-moves, session-moves, review-lines): eigene Partition PRO
        // USER, zusätzlich zum globalen Deckel je IP. Vorher durfte ein Konto die vollen 100 Batches/min je IP
        // schreiben (~1,3 GB/min). RepCheck flusht je Senke und Kurs höchstens alle 15 s — 60/min für alle drei
        // zusammen lässt auch mehreren offenen Kursen reichlich Luft. Die Menge je Konto deckelt ChessableSinkBytes.
        options.AddPolicy("extension-sink", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 60 * permitScale,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        // Anonyme Turnier-Proxy-GETs (oeffentliche Turnierseite / Teilen-Feature):
        // bewusst ohne Login erreichbar, aber gedrosselt, damit der dahinterliegende
        // Crawler (chess-results.com) nicht ungebremst missbraucht werden kann.
        options.AddPolicy("anonymous-tournament", ctx => PerIpFixedWindow(ctx, 60 * permitScale));
        // Nutzer-ausgeloeste Crawler-Auftraege (crawl, crawl/player-details, {id}/clubs, Monitor einschalten): je KONTO,
        // zusaetzlich zum globalen Deckel je IP — die Crawler-Warteschlange teilen sich alle (RateLimitPartitions).
        options.AddPolicy("user-crawl", ctx => RookHub.Api.Services.RateLimitPartitions.CrawlerRequest(ctx, permitScale));
        // Turnierkalender lesen — auch ohne Anmeldung (Turnierseite voll benutzbar ohne Konto): je Konto bzw. Adresse.
        options.AddPolicy("directory-read", ctx => RookHub.Api.Services.RateLimitPartitions.DirectoryRead(ctx, permitScale));
        // Lückensuche einer Rekonstruktion (gap, gap/propose): reine CPU im Request-Thread — je KONTO gedrosselt, die
        // Gleichzeitigkeit über alle Konten deckelt GapSearchGate (RateLimitPartitions, Codereview N5-001).
        options.AddPolicy("reconstruction-gap", ctx => RookHub.Api.Services.RateLimitPartitions.ReconstructionGap(ctx, permitScale));
        // Repertoire-Analyse der Erweiterung (analyze-game): je KONTO — ein Neuaufbau des Positions-Sets liest alle
        // markierten PGNs und spielt jeden Halbzug nach (RateLimitPartitions, Codereview N8-005).
        options.AddPolicy("extension-analyze", ctx => RookHub.Api.Services.RateLimitPartitions.ExtensionAnalyze(ctx, permitScale));
        // Baummodus und Ähnlichkeitssuche im Repertoire (position-tree, similar-positions): je KONTO ein Fenster plus
        // höchstens zwei gleichzeitige Scans — jede Anfrage spielt alle lesbaren Linien nach, bis zum Zeitbudget der
        // Dienste (RateLimitPartitions, WindowAndConcurrencyLimiter, Codereview N7-001).
        options.AddPolicy("repertoire-scan", ctx => RookHub.Api.Services.RateLimitPartitions.RepertoireScan(ctx, permitScale));
        // Freundschaftsanfrage und Challenge (friends/request, challenges): je KONTO, ein gemeinsames Fenster — jede legte
        // beim Empfänger Glocke + Web-Push an, Senden–Zurückziehen–Senden flutete beliebige Konten (RateLimitPartitions,
        // Codereview N9-003). Die Glocke der Freundschaftsanfrage entprellt zusätzlich FriendService.
        // Zugfolgen-Suche im lokalen Explorer (explorer/paths): je KONTO — bis zu 400 Abfragen und 20 s je Aufruf.
        options.AddPolicy("explorer-paths", ctx => RookHub.Api.Services.RateLimitPartitions.ExplorerPaths(ctx, permitScale));
        options.AddPolicy("user-social", ctx => RookHub.Api.Services.RateLimitPartitions.UserSocial(ctx, permitScale));
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        // Retry-After + Rumpf, und eine gedrosselte Warnung je (IP, Policy): vorher hinterließ eine Absage keine Spur —
        // UseRateLimiter steht vor dem Request-Log, und die Middleware meldet sie nur auf Debug (Codereview A10-010).
        options.OnRejected = RateLimitRejectionHandler.OnRejectedAsync;
    });

    builder.Services.AddMemoryCache();
    builder.Services.AddResponseCompression();
    // Domänen-Ausnahmen (NotFound/DomainValidation/Conflict/Forbidden) → { message } mit 404/400/409/403,
    // noch im Endpoint, damit das Request-Log den echten Status sieht (Begründung in DomainExceptionFilter).
    // Browser-Abbruch (RequestAborted) → 499 ohne Error-Log statt 500 (ClientAbortedExceptionFilter, 0.727.4).
    builder.Services.AddControllers(o =>
        {
            o.Filters.Add<RookHub.Api.Filters.DomainExceptionFilter>();
            o.Filters.Add<RookHub.Api.Filters.ClientAbortedExceptionFilter>();
        })
        // Validierungs-400 mit message (= erste Meldung) neben errors/traceId (Codereview A10-006).
        .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ApiErrorResponses.InvalidModelState)
        .AddJsonOptions(opts =>
            opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "RookHub API", Version = "v1" });
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Authorization header using the Bearer scheme",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey,
            Scheme = "Bearer"
        });
        // Microsoft.OpenApi 2.0 (Swashbuckle 10): AddSecurityRequirement nimmt jetzt eine Factory
        // (OpenApiDocument → Requirement); OpenApiReference/ReferenceType wurden durch dedizierte
        // Referenztypen ersetzt — OpenApiSecuritySchemeReference verweist auf das „Bearer"-Schema.
        c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecuritySchemeReference("Bearer", doc),
                new List<string>()
            }
        });
    });

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
