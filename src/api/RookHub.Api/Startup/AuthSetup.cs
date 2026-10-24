using Microsoft.AspNetCore.Authentication.JwtBearer;
using RookHub.Api.Services.Accounts;

namespace RookHub.Api.Startup;

/// <summary>Anmeldung (Policy-Scheme JWT oder Personal Access Token) und RBAC-Policies. Die Handler selbst stehen in
/// <c>Services/Accounts</c> (JwtTokenGate, ApiTokenAuthenticationHandler) und <c>Authorization/</c>.</summary>
public static class AuthSetup
{
    public static IServiceCollection AddRookHubAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // JWT Authentication — Schluessel, Aussteller und Pruefregeln aus JwtTokens (dieselben wie beim Ausstellen
        // in AuthService und beim Cookie-Tausch in SharedSessionService).
        if (JwtTokens.SigningKey(configuration).Key.Length < 32)
            throw new InvalidOperationException("JWT key must be at least 32 bytes for HMAC-SHA256");
        // Default-Scheme ist ein Policy-Scheme, das anhand des Bearer-Prefixes entscheidet:
        // `rkh_…` → ApiToken-Handler (Personal Access Tokens fuer Maschinen-Clients),
        // alles andere → JWT (User-Login). So koennen Endpoints transparent beides akzeptieren.
        services.AddAuthentication(options =>
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
                JwtTokens.ValidationParameters(configuration, configuration["Jwt:Audience"]);
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
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider,
            RookHub.Api.Authorization.PermissionPolicyProvider>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
            RookHub.Api.Authorization.PermissionAuthorizationHandler>();
        return services;
    }
}
