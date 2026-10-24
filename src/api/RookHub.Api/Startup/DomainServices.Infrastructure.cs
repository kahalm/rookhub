using RookHub.Api.Services;

namespace RookHub.Api.Startup;

public static partial class DomainServicesSetup
{
    /// <summary>Gemeinsame Bausteine: Pausen-Schalter, Warteschlangen, Text-Modell, Sperrzeiten, Link-Vorschau, IP-Land.</summary>
    private static void AddInfrastructureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Admin-Schalter „Hintergrundjobs pausieren" (DB SystemSettings + BackgroundJobs:Paused) — fragen PeriodicWorker und
        // die übrigen Hintergrunddienste vor jedem Takt (docs/api/hintergrundjobs-pause.md).
        services.AddSingleton<BackgroundPause>();
        // Land der Besucher-IP fuer die Startsprache von KidHub — lokale DB-IP-Liste, laedt bei Bedarf.
        services.AddSingleton<IpCountryService>();
        services.AddSingleton<IClaudeJsonClient>(sp => TextJsonClients.Create(configuration,
            sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<IHttpClientFactory>().CreateClient("text-llm")));
        // Sperrzeiten der Spark (0.546.0): TextLlm:QuietHours / TextLlm:TimeZone.
        services.AddSingleton(sp => new QuietHours(sp.GetRequiredService<IConfiguration>()));
        // Open-Graph-/Link-Vorschau (Brett-Bild + Meta-Tag-Injektion in die SPA-index.html).
        services.AddScoped<RookHub.Api.Services.Og.OgMetaService>();
        services.AddSingleton<RookHub.Api.Services.Og.OgImageService>();
        services.AddSingleton<RookHub.Api.Services.Og.OgIndexHtmlProvider>();
        services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
        // Eigene Queue NUR für schach-bot-Webhooks, damit Solver-Updates nicht
        // hinter/unter einem Chessable-Import-Schwung in der allgemeinen Queue verhungern.
        services.AddSingleton<IWebhookTaskQueue, WebhookTaskQueue>();
    }
}
