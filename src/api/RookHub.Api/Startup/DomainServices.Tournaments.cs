using RookHub.Api.Services.TournamentDirectory;
using RookHub.Api.Services.Tournaments;

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
        services.AddSingleton<AutoSubscriptionService>();
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
    }
}
