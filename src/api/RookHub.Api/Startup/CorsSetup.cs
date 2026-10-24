namespace RookHub.Api.Startup;

/// <summary>CORS: Policy der Extension/des Userscripts (nur ExtensionController) und die Dev-Vorgabe.</summary>
public static class CorsSetup
{
    public static IServiceCollection AddRookHubCors(this IServiceCollection services)
    {
        // CORS policies
        services.AddCors(options =>
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
        return services;
    }
}
