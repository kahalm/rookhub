using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Startup;

/// <summary><c>GET /health</c> — Docker-Healthcheck in Produktion und service_healthy-Kette der e2e-Läufe.</summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapRookHubHealth(this IEndpointRouteBuilder app)
    {
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
        return app;
    }
}
