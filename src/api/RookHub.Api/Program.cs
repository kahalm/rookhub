using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using RookHub.Api.Data;
using RookHub.Api.Middleware;
using RookHub.Api.Services;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddHostedService<BackgroundTaskWorker>();
builder.Services.AddSingleton<AutoSubscriptionService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AutoSubscriptionService>());
builder.Services.AddHostedService<RoundMonitorService>();
builder.Services.AddHostedService<LogRetentionService>();

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

// CORS policies
builder.Services.AddCors(options =>
{
    // Policy for the Chrome extension (applied only to ExtensionController)
    options.AddPolicy("ExtensionPolicy", policy =>
    {
        policy.WithOrigins(
                "https://www.chess.com")
            // Add specific chrome-extension://YOUR_EXTENSION_ID origins here
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
app.UseMiddleware<RequestLoggingMiddleware>();
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

public partial class Program { }
