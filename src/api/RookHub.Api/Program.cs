using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using RookHub.Api.Data;
using RookHub.Api.Services;
using Serilog;
using Serilog.Context;
using Serilog.Events;
using Elastic.Serilog.Sinks;

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
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            // Erwartetes, harmloses Startup-Rauschen: DataProtection persistiert den Key-Ring
            // bewusst unverschlüsselt in das gemountete /keys-Volume (privat, durable). Die zwei
            // Hinweise ("no XML encryptor" / "may not be persisted") kämen bei JEDEM Neustart →
            // hier auf Error angehoben, echte DataProtection-Fehler bleiben sichtbar.
            .MinimumLevel.Override("Microsoft.AspNetCore.DataProtection", LogEventLevel.Error)
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
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
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

    // JWT Authentication
    var jwtKey = builder.Configuration["Jwt:Key"]
        ?? throw new InvalidOperationException("JWT key not configured");
    if (Encoding.UTF8.GetBytes(jwtKey).Length < 32)
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
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            // Standard-Toleranz ist 5 min — auf 1 min straffen, damit abgelaufene Tokens
            // (insb. nach Logout/Passwortwechsel) nicht unnötig lange akzeptiert werden.
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        // Gelöschte/anonymisierte Konten dürfen ihr noch gültiges JWT nicht weiterverwenden:
        // nach erfolgreicher Signatur-/Lifetime-Prüfung zusätzlich gegen DeletedAt verifizieren.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var idStr = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!int.TryParse(idStr, out var uid)) return;
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var cache = ctx.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                // Zusätzlich zum Gelöscht-Check den Security-Stamp prüfen: nach Passwort-Reset/-Änderung
                // passt der sstamp-Claim nicht mehr → Token wird abgelehnt (Alt-Token ohne Claim bleiben gültig).
                var stamp = ctx.Principal?.FindFirstValue("sstamp");
                if (!await AuthUserValidation.IsTokenValidAsync(db, cache, uid, stamp, ctx.HttpContext.RequestAborted))
                    ctx.Fail("User account is deleted or the token has been invalidated.");
            }
        };
    })
    .AddScheme<ApiTokenAuthenticationOptions, ApiTokenAuthenticationHandler>(
        ApiTokenAuthenticationHandler.SchemeName, _ => { });

    // Services
    builder.Services.AddScoped<AuthService>();
    builder.Services.AddSingleton<EncryptionService>();
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
    builder.Services.AddScoped<PasswordResetService>();
    builder.Services.AddScoped<ProfileService>();
    builder.Services.AddSingleton<DiscordLinkService>();
    builder.Services.AddScoped<NotificationService>();
    // Web-Push: VAPID-Konfig aus dem Abschnitt „WebPush" (ENV WebPush__Subject/PublicKey/PrivateKey);
    // ohne Schlüssel bleibt Push serverseitig deaktiviert (No-op).
    builder.Services.Configure<RookHub.Api.Services.WebPushOptions>(builder.Configuration.GetSection("WebPush"));
    builder.Services.AddSingleton<IWebPushSender, WebPushSender>();
    builder.Services.AddScoped<PushNotificationService>();
    builder.Services.AddScoped<AdminMessageService>();
    builder.Services.AddScoped<FriendService>();
    builder.Services.AddScoped<ChallengeService>();
    builder.Services.AddScoped<FavoriteService>();
    builder.Services.AddScoped<RevengeNotificationService>();
    builder.Services.AddScoped<RepertoireService>();
    builder.Services.AddScoped<RepertoireTrainingService>();
    builder.Services.AddScoped<RepertoireAnalyzeService>();
    builder.Services.AddScoped<PlayerSearchService>();
    builder.Services.AddScoped<PuzzleTaggingService>();
    builder.Services.AddScoped<PuzzleStatsService>();
    builder.Services.AddScoped<PuzzleService>();
    builder.Services.AddScoped<PgnImportService>();
    builder.Services.AddScoped<ChessableImportService>();
    builder.Services.AddScoped<ChessableImportQueueService>();
    builder.Services.AddScoped<ChessableBearerBreaker>();
    builder.Services.AddScoped<ChessableCourseRefreshService>();
    builder.Services.AddScoped<EndlessProgressService>();
    builder.Services.AddScoped<BookPuzzleService>();
    builder.Services.AddScoped<DailyLeaderboardService>();
    builder.Services.AddScoped<CourseService>();
    builder.Services.AddScoped<CourseStatsService>();
    builder.Services.AddScoped<ICourseReimporter>(sp => sp.GetRequiredService<ChessableImportService>());
    builder.Services.AddScoped<ImportReprocessService>();
    // Stößt Massen-Reprocess im Hintergrund an (eigener Scope) → Endpoint antwortet sofort statt in
    // den ~60-s-Request-Timeout zu laufen. Singleton, da es nur die ScopeFactory kapselt.
    builder.Services.AddSingleton<IReprocessLauncher, ReprocessLauncher>();
    builder.Services.AddScoped<TrainingGoalService>();
    builder.Services.AddScoped<RememberedPositionService>();
    builder.Services.AddScoped<SavedGameService>();
    builder.Services.AddScoped<WeeklyPostService>();
    builder.Services.AddScoped<LeaderboardService>();
    builder.Services.AddScoped<BotStatsService>();
    builder.Services.AddScoped<ApiTokenService>();
    builder.Services.AddScoped<AdminService>();
    builder.Services.AddScoped<BookAdminService>();
    // Tipp-Generierung für Buch-Puzzles (LLM + Stockfish, nur Import-/Reprocess-Pfad).
    builder.Services.AddSingleton<StockfishAnalyzer>();
    builder.Services.AddSingleton<IClaudeJsonClient, ClaudeJsonClient>();
    builder.Services.AddScoped<HintGenerationService>();
    builder.Services.AddScoped<MenuVisibilityService>();
    // Open-Graph-/Link-Vorschau (Brett-Bild + Meta-Tag-Injektion in die SPA-index.html).
    builder.Services.AddScoped<RookHub.Api.Services.Og.OgMetaService>();
    builder.Services.AddSingleton<RookHub.Api.Services.Og.OgImageService>();
    builder.Services.AddSingleton<RookHub.Api.Services.Og.OgIndexHtmlProvider>();
    builder.Services.AddHttpClient("og-frontend");
    builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
    builder.Services.AddHostedService<BackgroundTaskWorker>();
    // Eigene Queue + Consumer NUR für schach-bot-Webhooks, damit Solver-Updates nicht
    // hinter/unter einem Chessable-Import-Schwung in der allgemeinen Queue verhungern.
    builder.Services.AddSingleton<IWebhookTaskQueue, WebhookTaskQueue>();
    builder.Services.AddHostedService<WebhookTaskWorker>();
    // Beim Start unterbrochene Chessable-Importe ("running") fortsetzen.
    builder.Services.AddHostedService<ChessableImportResumeService>();
    // Download-Lane-Sicherheitsnetz: stößt wartende (nicht-gecachte) Importe an, falls der
    // Queue-Antrieb steht (bounded-DropOldest-Ticketverlust / fehlende Nachreihung nach Abschluss).
    builder.Services.AddHostedService<ChessableImportWatchdogService>();
    // Schnelle Lane: treibt voll-gecachte Importe sofort + seriell, parallel zur Download-Lane.
    builder.Services.AddHostedService<ChessableImportFastLaneService>();
    builder.Services.AddSingleton<AutoSubscriptionService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<AutoSubscriptionService>());
    builder.Services.AddHostedService<RoundMonitorService>();
    // Periodisches Lebenszeichen nach ES (Standard 60 s) → log-watcher erkennt toten Dienst.
    builder.Services.AddHostedService<HeartbeatService>();
    // Taegliche Tagespuzzle-Zuordnung um 00:00 UTC (siehe DailyPuzzles-Tabelle).
    builder.Services.AddHostedService<DailyPuzzleScheduler>();
    // Taeglicher Chessable-Kurslisten-Refresh (04:00 UTC): aktualisiert alle hinterlegten Bearer,
    // sperrt tote Tokens, benachrichtigt Admins bei neuen Kursen.
    builder.Services.AddHostedService<ChessableCourseRefreshScheduler>();

    // GitHub-Actions-Übersicht (Admin-CI-Seite). Token pro Request in GithubActionsService gesetzt.
    builder.Services.AddHttpClient<GithubActionsService>(client =>
    {
        client.BaseAddress = new Uri("https://api.github.com/");
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RookHub-CI/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    });

    // Externe Spielzeit (Lichess/chess.com) → Kategorie „Spielen" im Trainingsziele-Tracker.
    // chess.com verlangt einen aussagekraeftigen User-Agent, sonst 403.
    builder.Services.AddHttpClient<PlayTimeService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RookHub/1.0 (+https://rookhub; training-goals)");
    });
    builder.Services.AddHostedService<PlayTimeSyncService>();

    // SchachBot-Webhook (Solver-Updates fuer Daily-Puzzle live an den Bot pushen).
    // URL + Secret optional → ohne Konfig stillschweigend deaktiviert.
    builder.Services.AddHttpClient<SchachBotWebhookService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(5);
    });

    // Chessable Proxy HttpClient — leitet User-Bearer an piratechess-API durch.
    // Default-URL passt zum Container-Namen im chessable-bridge-Docker-Netz.
    var chessableBaseUrl = builder.Configuration["Chessable:ApiUrl"] ?? "http://piratechess-api:8080";
    if (!Uri.TryCreate(chessableBaseUrl, UriKind.Absolute, out var chessableUri))
        throw new InvalidOperationException($"Invalid Chessable:ApiUrl: {chessableBaseUrl}");
    var chessableServiceKey = builder.Configuration["Chessable:ServiceKey"];
    builder.Services.AddHttpClient<ChessableProxyService>(client =>
    {
        client.BaseAddress = chessableUri;
        // /direct/test + /direct/courses sind schnell; der tiefe /direct/course-Abruf
        // (ganzer Kurs, viele sequentielle curl-impersonate-Calls über VPN) kann je nach
        // Kursgröße viele Minuten dauern → großzügiger Timeout (Import läuft im Hintergrund).
        client.Timeout = TimeSpan.FromMinutes(15);
        if (!string.IsNullOrEmpty(chessableServiceKey))
            client.DefaultRequestHeaders.Add("X-Service-Key", chessableServiceKey);
    });

    // Crawler Proxy HttpClient
    var crawlerBaseUrl = builder.Configuration["Crawler:BaseUrl"] ?? "http://host.docker.internal:8080";
    if (!Uri.TryCreate(crawlerBaseUrl, UriKind.Absolute, out var crawlerUri))
        throw new InvalidOperationException($"Invalid Crawler:BaseUrl: {crawlerBaseUrl}");
    var crawlerApiKey = builder.Configuration["Crawler:ApiKey"];
    builder.Services.AddHttpClient<CrawlerProxyService>(client =>
    {
        client.BaseAddress = crawlerUri;
        client.Timeout = TimeSpan.FromSeconds(30);
        if (!string.IsNullOrEmpty(crawlerApiKey))
            client.DefaultRequestHeaders.Add("X-Api-Key", crawlerApiKey);
    });

    // FIDE search HttpClient
    builder.Services.AddHttpClient("FideSearch", client =>
    {
        client.BaseAddress = new Uri("https://api.chesstools.org");
        client.Timeout = TimeSpan.FromSeconds(15);
    });

    // Data Protection — Keys auf gemountetes Volume persistieren, damit sie Neustarts
    // ueberleben (sonst ephemere In-Memory-Keys + "No XML encryptor"-Warnings bei jedem Boot).
    // Pfad konfigurierbar (DataProtection:KeyPath, Default /keys = Prod-Volume). SetApplicationName
    // haelt die Purpose-Strings stabil, auch wenn sich der Containername aendert.
    var dataProtection = builder.Services.AddDataProtection().SetApplicationName("RookHub");
    var keyPath = builder.Configuration["DataProtection:KeyPath"] ?? "/keys";
    try
    {
        Directory.CreateDirectory(keyPath);
        dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
    }
    catch (Exception ex)
    {
        // Pfad nicht beschreibbar (z. B. lokale Dev ohne gemountetes /keys) → In-Memory-Keys
        // (ephemer; ueberleben den Prozess nicht). Kein Startup-Crash deswegen.
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
    // die geloggte IP wäre die Proxy-IP. X-Forwarded-For NUR von privaten Peers (nginx im
    // Docker-Netz) vertrauen — nicht öffentlich, sonst IP-Spoofing.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // Es gibt ZWEI vertrauenswürdige Proxy-Hops (NPM + frontend-nginx), der XFF lautet
        // z.B. "10.24.x.x, 172.26.0.1". Der Default ForwardLimit=1 rollt nur einen Hop zurück
        // → es bliebe die Docker-Gateway-IP 172.26.0.1 stehen. null = so weit zurückrollen wie
        // KnownNetworks reicht; gestoppt wird am ersten nicht-privaten Peer = echte Client-IP.
        options.ForwardLimit = null;
        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();
        options.KnownNetworks.Add(new IPNetwork(System.Net.IPAddress.Parse("10.0.0.0"), 8));
        options.KnownNetworks.Add(new IPNetwork(System.Net.IPAddress.Parse("172.16.0.0"), 12));
        options.KnownNetworks.Add(new IPNetwork(System.Net.IPAddress.Parse("192.168.0.0"), 16));
    });

    // M-11: Rate limiting for auth endpoints
    // S-6: Global rate limiting for all endpoints
    builder.Services.AddRateLimiter(options =>
    {
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
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
        options.AddPolicy("auth", ctx => PerIpFixedWindow(ctx, 10));
        options.AddPolicy("anonymous-puzzle", ctx => PerIpFixedWindow(ctx, 30));
        // Anonyme Turnier-Proxy-GETs (oeffentliche Turnierseite / Teilen-Feature):
        // bewusst ohne Login erreichbar, aber gedrosselt, damit der dahinterliegende
        // Crawler (chess-results.com) nicht ungebremst missbraucht werden kann.
        options.AddPolicy("anonymous-tournament", ctx => PerIpFixedWindow(ctx, 60));
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    });

    builder.Services.AddMemoryCache();
    builder.Services.AddResponseCompression();
    builder.Services.AddControllers()
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
        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                },
                Array.Empty<string>()
            }
        });
    });

    var app = builder.Build();

    // Muss VOR UseRateLimiter + dem IP-Logging laufen, damit RemoteIpAddress die echte Client-IP ist.
    app.UseForwardedHeaders();

    // Auto-migrate on startup + seed admin
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
        await AdminSeeder.SeedAsync(db, app.Configuration);
    }

    // H-5: Global exception handler
    app.UseExceptionHandler(error =>
    {
        error.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
                title = "An unexpected error occurred.",
                status = 500
            });
        });
    });

    if (!app.Environment.IsProduction())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseResponseCompression();
    app.UseCors();
    app.UseRateLimiter();
    app.UseAuthentication();

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
        // Rate-Limiter). Zusaetzlich die ROHE Weiterleitungs-Kette mitschreiben, damit alle
        // Hops sichtbar sind — inkl. einer evtl. oeffentlichen IP, die nicht als kanonische
        // Client-IP gewaehlt wurde. UseForwardedHeaders verschiebt das Original-XFF nach
        // X-Original-For (X-Forwarded-For enthaelt danach nur noch Unverbrauchtes), daher
        // bevorzugt X-Original-For lesen, sonst Fallback auf X-Forwarded-For.
        var fwd = ctx.Request.Headers["X-Original-For"].ToString();
        if (string.IsNullOrEmpty(fwd))
            fwd = ctx.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrEmpty(fwd))
            scopes.Add(LogContext.PushProperty("ForwardedFor", fwd));
        var realIp = ctx.Request.Headers["X-Real-IP"].ToString();
        if (!string.IsNullOrEmpty(realIp))
            scopes.Add(LogContext.PushProperty("XRealIp", realIp));
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
            await db.Database.CanConnectAsync();
            return Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
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
