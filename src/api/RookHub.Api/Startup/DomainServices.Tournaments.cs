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
        // chess.com-Übertragungen (1.040.0): Kennungen „cc~…“ leitet die Quelle an ChessComLiveSource (Abfrage im Takt statt
        // Strom, ein Raum-Abruf je Event und Takt für alle Zuschauer), alles andere an Lichess.
        services.AddSingleton<IChessComEventsApi>(sp => new HttpChessComEventsApi(
            sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton(sp => new ChessComLiveSource(sp.GetRequiredService<IChessComEventsApi>(),
            sp.GetRequiredService<ILogger<ChessComLiveSource>>(), ChessComLiveSource.Options.From(configuration)));
        services.AddSingleton<ILichessLiveSource>(sp => new LiveSourceRouter(
            new HttpLichessLiveSource(sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<IConfiguration>()),
            sp.GetRequiredService<ChessComLiveSource>()));
        services.AddSingleton<LichessCloudEval>();
        services.AddSingleton<BroadcastFinder>();
        // Engine-Balken ohne (ausreichend tiefe) Cloud-Bewertung (1.027.0): die HINTERGRUND-Engines des Kontos der Analyseaufträge,
        // parallel auf allen freien, bis Tiefe 30, Zwischenstände laufend; Vorrang vor neuen Aufträgen über LiveEngineLane, ohne
        // laufende abzuwürgen (LiveBoards:Engine:*).
        services.AddSingleton(_ => LiveEngineOptions.From(configuration));
        services.AddScoped<ILiveEnginePoolSource>(sp => new RegistryLiveEnginePoolSource(sp.GetRequiredService<RookHub.Api.Data.AppDbContext>(),
            sp.GetRequiredService<RookHub.Api.Services.EngineBroker.EngineRegistry>(), sp.GetRequiredService<LiveEngineOptions>(), configuration));
        services.AddSingleton<ILiveEngineEvaluator>(sp => new BrokerLiveEngineEvaluator(sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<LiveEngineOptions>(), sp.GetRequiredService<ILogger<BrokerLiveEngineEvaluator>>(),
            sp.GetRequiredService<RookHub.Api.Services.Engines.EngineActivityTracker>()));
        services.AddSingleton(sp => new LiveEngineQueue(sp.GetRequiredService<ILiveEngineEvaluator>(), sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<LiveEngineOptions>(), sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
            sp.GetRequiredService<ILogger<LiveEngineQueue>>(), sp.GetRequiredService<RookHub.Api.Services.Engines.LiveEngineLane>(),
            sp.GetRequiredService<RookHub.Api.Services.Engines.EngineActivityTracker>()));
        services.AddSingleton(sp =>
        {
            var hub = new BroadcastLiveHub(sp.GetRequiredService<ILichessLiveSource>(),
                sp.GetRequiredService<LichessCloudEval>(), sp.GetRequiredService<ILogger<BroadcastLiveHub>>(),
                engine: sp.GetRequiredService<LiveEngineQueue>(),
                onlyMove: LiveOnlyMoveOptions.From(configuration, sp.GetRequiredService<LiveEngineOptions>()));   // „Nur ein Zug“ (1.033.0)
            // chess.com holt die Züge der ausdrücklich gewählten Bretter zuerst.
            sp.GetRequiredService<ChessComLiveSource>().WantedGames = hub.WantedGames;
            return hub;
        });
        services.AddSingleton(_ => new SseConnectionLimiter(
            Math.Clamp(configuration.GetValue<int?>("LiveBoards:MaxStreamsPerIp") ?? 6, 1, 100),
            Math.Clamp(configuration.GetValue<int?>("LiveBoards:MaxStreams") ?? 2000, 1, 100000)));
        // Zusätzliche Bretter aus fremden Übertragungen (1.025.0): nur Runden, die Lichess kennt, serverweit höchstens
        // LiveBoards:MaxExternalRounds (10) gleichzeitig — Ströme laufen über denselben Hub.
        services.AddSingleton(sp => new ExternalRoundRegistry(sp.GetRequiredService<ILichessLiveSource>(),
            sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(), sp.GetRequiredService<ILogger<ExternalRoundRegistry>>(),
            Math.Clamp(configuration.GetValue<int?>("LiveBoards:MaxExternalRounds") ?? 10, 0, 200),
            sp.GetRequiredService<ChessComLiveSource>()));
        services.AddScoped<ILiveTournamentData, CrawlerLiveTournamentData>();
        services.AddScoped<TournamentLiveService>();
        // Geteilte, lebende Ansichten (1.029.0): Änderungen wecken die offenen Ströme im selben Prozess.
        services.AddSingleton<LiveViewChanges>();
        services.AddScoped<SharedLiveViews>();
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
