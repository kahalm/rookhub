using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using RookHub.Api.Data;
using RookHub.Api.Services;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Elasticsearch;

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
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithProperty("Application", "RookHub")
            .WriteTo.Console();

        var esUrl = context.Configuration["Elasticsearch:Url"];
        if (!string.IsNullOrEmpty(esUrl))
        {
            configuration.WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri(esUrl))
            {
                IndexFormat = "rookhub-logs-{0:yyyy.MM}",
                AutoRegisterTemplate = true,
                AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv7,
                BatchAction = ElasticOpType.Create,
                NumberOfReplicas = 0,
                NumberOfShards = 1
            });
        }
    });

    // Database
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

    // JWT Authentication
    var jwtKey = builder.Configuration["Jwt:Key"]
        ?? throw new InvalidOperationException("JWT key not configured");
    if (Encoding.UTF8.GetBytes(jwtKey).Length < 32)
        throw new InvalidOperationException("JWT key must be at least 32 bytes for HMAC-SHA256");
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidAudience = builder.Configuration["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
            };
        });

    // Services
    builder.Services.AddScoped<AuthService>();
    builder.Services.AddScoped<ProfileService>();
    builder.Services.AddScoped<FriendService>();
    builder.Services.AddScoped<RepertoireService>();
    builder.Services.AddScoped<PlayerSearchService>();
    builder.Services.AddScoped<PuzzleService>();
    builder.Services.AddScoped<EndlessProgressService>();
    builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
    builder.Services.AddHostedService<BackgroundTaskWorker>();
    builder.Services.AddSingleton<AutoSubscriptionService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<AutoSubscriptionService>());
    builder.Services.AddHostedService<RoundMonitorService>();

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
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo("/keys"));

    // CORS policies
    builder.Services.AddCors(options =>
    {
        // Policy for the Chrome extension (applied only to ExtensionController)
        options.AddPolicy("ExtensionPolicy", policy =>
        {
            policy.WithOrigins(
                    "https://www.chess.com")
                .WithMethods("GET", "POST")
                .WithHeaders("Authorization", "Content-Type")
                .AllowCredentials();
        });
        // Default policy for frontend
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins(
                    "http://localhost:4200",
                    "http://localhost:8085")
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        });
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
        options.AddFixedWindowLimiter("auth", limiter =>
        {
            limiter.PermitLimit = 10;
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.QueueLimit = 0;
        });
        options.AddFixedWindowLimiter("anonymous-puzzle", limiter =>
        {
            limiter.PermitLimit = 30;
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.QueueLimit = 0;
        });
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    });

    builder.Services.AddMemoryCache();
    builder.Services.AddResponseCompression();
    builder.Services.AddControllers();
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

    // H-8: Swagger only in Development
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseResponseCompression();
    app.UseCors();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("UserName", httpContext.User?.Identity?.Name ?? "anonymous");
            diagnosticContext.Set("UserId", httpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "");
            diagnosticContext.Set("IpAddress", httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
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
