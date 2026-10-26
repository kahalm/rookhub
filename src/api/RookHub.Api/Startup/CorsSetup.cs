namespace RookHub.Api.Startup;

/// <summary>CORS: nur die Dev-Vorgabe für das lokale Frontend, und nur in Development (Codereview A10-007).
/// Prod ist same-origin hinter nginx; die RepCheck-Extension ruft RookHub ausschließlich aus ihrem Background-Worker
/// (host_permissions, CORS-frei). Die frühere <c>ExtensionPolicy</c> (chess.com, lichess.org, chessable.com) diente dem
/// entfallenen Userscript und ist weg.</summary>
public static class CorsSetup
{
    public static IServiceCollection AddRookHubCors(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddCors(options =>
        {
            if (!environment.IsDevelopment()) return;
            // Default policy for frontend (nur lokale Dev-Origins).
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
        return services;
    }
}
