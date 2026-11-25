using RookHub.Api.Services.TournamentDirectory;
using RookHub.Api.Services.Tournaments;
using RookHub.Api.Services.Tournaments.Live;

namespace RookHub.Api.Startup;

public static partial class DomainServicesSetup
{
    /// <summary>Turnier-Proxy-Folgedienste: Rundenpläne, FIDE-Details, Turnierverlauf, Spielersuche, Abos.</summary>
    private static void AddTournamentServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<TournamentRoundPlanService>();
        services.AddScoped<FideEventDetailService>();
        services.AddScoped<TournamentHistoryService>();
        services.AddScoped<PlayerSearchService>();
        services.AddScoped<AutoSubscriptionService>();
        // Live-Bretter (1.014.0): EIN Lichess-Strom je Runde mit Zuschauern (Hub), Cloud-Eval, Suche der Übertragung —
        // Singletons mit Zustand im Speicher; je Anfrage nur die Verknüpfung (DB) und die Crawler-Daten.
        services.AddSingleton<ILichessLiveSource>(sp => new HttpLichessLiveSource(
            sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton<LichessCloudEval>();
        services.AddSingleton<BroadcastFinder>();
        services.AddSingleton(sp => new BroadcastLiveHub(sp.GetRequiredService<ILichessLiveSource>(),
            sp.GetRequiredService<LichessCloudEval>(), sp.GetRequiredService<ILogger<BroadcastLiveHub>>()));
        services.AddSingleton(_ => new SseConnectionLimiter(
            Math.Clamp(configuration.GetValue<int?>("LiveBoards:MaxStreamsPerIp") ?? 6, 1, 100),
            Math.Clamp(configuration.GetValue<int?>("LiveBoards:MaxStreams") ?? 2000, 1, 100000)));
        services.AddScoped<ILiveTournamentData, CrawlerLiveTournamentData>();
        services.AddScoped<TournamentLiveService>();
    }

    /// <summary>Turnierverzeichnis: Sweeps je Quelle, Umkreis-Auflösung, Abfrage.</summary>
    private static void AddTournamentDirectoryServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<VenueDisambiguationService>();
        services.AddScoped<TournamentCalendarSweepService>();
        services.AddScoped<FsiDirectorySweepService>();
        services.AddScoped<SzsDirectorySweepService>();
        services.AddScoped<ChessSkDirectorySweepService>();
        services.AddScoped<ChessHuDirectorySweepService>();
        services.AddScoped<ChessCzDirectorySweepService>();
        services.AddScoped<ChessArbiterDirectorySweepService>();
        services.AddScoped<SchachbundDirectorySweepService>();
        services.AddScoped<EcfDirectorySweepService>();
        services.AddScoped<IcuDirectorySweepService>();
        services.AddScoped<FfeDirectorySweepService>();
        services.AddScoped<SjakkDirectorySweepService>();
        services.AddScoped<ChessScotlandDirectorySweepService>();
        services.AddScoped<FrsahDirectorySweepService>();
        services.AddScoped<WcuDirectorySweepService>();
        services.AddScoped<CfcDirectorySweepService>();
        services.AddScoped<KnsbDirectorySweepService>();
        services.AddScoped<FideDirectorySweepService>();
        // Turnierverzeichnis: Sweep, Umkreis-Aufloesung und der (nur per Admin ausgeloeste) Import
        // des GeoNames-Ortslexikons.
        services.AddScoped<GeocodingService>();
        services.AddScoped<TournamentDirectoryService>();
        services.AddScoped<TournamentDirectoryQueryService>();
        // Die Pflege-Aktionen hinter dem Admin-Controller (Status, Umschrift, Nachverortung …).
        services.AddScoped<TournamentDirectoryMaintenanceService>();
    }
}
