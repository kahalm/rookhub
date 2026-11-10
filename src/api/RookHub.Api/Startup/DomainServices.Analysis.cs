using RookHub.Api.Data;
using RookHub.Api.Services;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.Library;
using RookHub.Api.Services.Scoresheet;

namespace RookHub.Api.Startup;

public static partial class DomainServicesSetup
{
    /// <summary>Analyse: Live-Tracker, Hintergrund-Analyseaufträge, Züge vergleichen, Zugfolgen-Suche, Endspiel-Datenbank.</summary>
    private static void AddEngineServices(IServiceCollection services, IConfiguration configuration)
    {
        // Zugfolgen zu einer Stellung aus dem LOKALEN Explorer (LeagueHub-Zugeditor, Modus „Stellung").
        // Eigener Speicher der Zugfolgen-Suche (24 h, gedeckelt — der allgemeine IMemoryCache hat keine Grenze).
        services.AddKeyedSingleton<Microsoft.Extensions.Caching.Memory.IMemoryCache>(ExplorerPathFinder.CacheServiceKey,
            (_, _) => new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions
            {
                SizeLimit = ExplorerPathFinder.CacheSizeLimit,
            }));
        services.AddScoped<ExplorerPathFinder>();
        // Hintergrund-Analyseaufträge: Tracker (Live-Streams je User) + Service + Worker (Singleton, damit
        // der Service laufende Aufträge unterbrechen kann — z. B. beim Löschen).
        services.AddSingleton<EngineActivityTracker>(sp =>
        {
            var tracker = new EngineActivityTracker();
            var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("EngineActivityTracker");
            tracker.OnNotifyFailed += ex => log.LogError(ex, "Benachrichtigung über Live-Analyse fehlgeschlagen");
            return tracker;
        });
        services.AddSingleton<AnalysisJobLive>();
        services.AddSingleton<AnalysisJobWorker>();
        services.AddSingleton<IAnalysisJobControl>(sp => sp.GetRequiredService<AnalysisJobWorker>());
        services.AddScoped<AnalysisJobService>();
        services.AddScoped<DeepAnalysisService>();
        // Züge vergleichen (0.602.0): Kandidatenzüge über Hintergrund-Aufträge durchrechnen, Begründung vom Modell.
        // Analyse-Verlauf (0.603.0): die letzten 20 Analysen des Analysebretts je Nutzer.
        services.AddScoped<AnalysisHistoryService>();
        services.AddScoped<LineStudyService>();   // „ohne Engine analysieren" (0.855.0)
        services.AddSingleton<MoveComparisonExplainJobs>();
        // Erklärdienst der Zugvergleiche (auch der automatischen der Vereinspartien, 0.908.0): EINE Stelle — wer die Begründungen
        // von einem anderen Modell schreiben lassen will, gibt hier einen anderen IClaudeJsonClient hinein (und prüft
        // MoveComparisonService.ExplanationsAvailable: heute nur Modelle auf eigener Hardware).
        // Seit 0.911.0 schreibt Claude Fable die Begründungen der Vereinspartien ab dem Stichtag (ClubFableExplanations, eigener
        // Anthropic-Client mit dem Konto-Schlüssel — ausdrücklich erlaubt), Rückfall aufs lokale Modell.
        services.AddSingleton(sp => new ClubFableExplanations(
            ClubFableExplanations.CreateClient(configuration, sp.GetRequiredService<ILoggerFactory>().CreateLogger<AnthropicJsonClient>()),
            configuration, sp.GetRequiredService<ILogger<ClubFableExplanations>>()));
        services.AddScoped<MoveComparisonService>(sp => new MoveComparisonService(
            sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<AnalysisJobService>(),
            sp.GetRequiredService<IClaudeJsonClient>(),
            sp.GetRequiredService<MoveComparisonExplainJobs>(), sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<MoveComparisonService>>(), sp.GetRequiredService<ClubFableExplanations>()));
        services.AddScoped<ClubMistakeComparisons>();   // Zugvergleiche zu den Fehlern der Vereinspartien (0.908.0)
        // Tipp-Generierung für Buch-Puzzles (LLM + Stockfish, nur Import-/Reprocess-Pfad).
        services.AddSingleton<StockfishAnalyzer>();
        services.AddSingleton<TablebaseGate>();
        services.AddScoped<TablebaseService>();
    }

    /// <summary>Eigener Engine-Broker und Lichess-Broker.</summary>
    private static void AddEngineBrokerServices(IServiceCollection services, IConfiguration configuration)
    {
        // Eigener Engine-Broker (Provider auf dem Rechner des Nutzers spricht direkt mit RookHub, ohne Lichess):
        // Registrierung (rhe_…), Selector-Verzeichnis, Engine-Auflösung beider Quellen.
        services.AddSingleton(RookHub.Api.Services.EngineBroker.LocalBrokerOptions.FromConfig(configuration));
        services.AddSingleton<RookHub.Api.Services.EngineBroker.EngineSelectorDirectory>();
        // Long-Polls mit UNBEKANNTEM Selector je Adresse deckeln (der Poll selbst ist vom Rate-Limiter ausgenommen).
        services.AddSingleton(new RookHub.Api.Services.EngineBroker.UnknownSelectorThrottle(
            RookHub.Api.Services.EngineBroker.UnknownSelectorThrottle.DefaultPermitPerMinute
            * RookHub.Api.Services.RateLimitScale.FromConfig(configuration)));
        services.AddScoped<RookHub.Api.Services.EngineBroker.ExternalEngineRegistrationService>();
        services.AddScoped<RookHub.Api.Services.EngineBroker.EngineClientScheduleService>();
        services.AddScoped<RookHub.Api.Services.EngineBroker.EngineRegistry>();
        services.AddSingleton<RookHub.Api.Services.EngineBroker.EngineHub>();
        // Admin-Seite „Engines" (0.946.0): Durchsatz je Engine (Worker zählt, Wartungsdienst schreibt) + Übersicht.
        services.AddSingleton<RookHub.Api.Services.EngineBroker.EngineThroughputRecorder>();
        services.AddScoped<RookHub.Api.Services.EngineBroker.EngineOverviewService>();
        services.AddSingleton<RookHub.Api.Services.EngineBroker.LocalEngineBroker>();
        services.AddTransient<RookHub.Api.Services.EngineBroker.LichessEngineBroker>();
        services.AddTransient<RookHub.Api.Services.EngineBroker.IEngineBroker, RookHub.Api.Services.EngineBroker.EngineBrokerRouter>();
    }

    /// <summary>Partien: Analyse, gespeicherte Partien, Rekonstruktion, Erklärungen/Nacherzählung über das Modell.</summary>
    private static void AddGameServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<GameAnalysisService>();
        services.AddScoped<SavedGameService>();
        services.AddScoped<ClubGameReferences>();   // Kopien von Vereinspartien → Verweise (0.924.0)
        services.AddScoped<OwnGameSummaries>();   // Zusammenfassung eigener Partien (0.923.0)
        services.AddScoped<GameMistakeProgressService>();
        services.AddScoped<MarkedPositionService>();
        services.AddScoped<GameReconstructionService>();
        // „Warum war das ein Fehler?" (0.534.0): Erklärungen über das Modell auf eigener Hardware.
        services.AddSingleton<GameExplanationJobs>();
        services.AddScoped<GameMoveExplanationService>();
        services.AddSingleton<GameRoastRolls>();   // Wurf-Zähler je Nutzer über alle Anfragen (G2-001)
        services.AddScoped<GameRoastService>();
        services.AddScoped<GameRecapService>();
        // Gescheiterte Nacherzählungen je Partie (A6-012): das Öffnen fragt dann 6 h nicht erneut beim Modell an.
        services.AddSingleton<GameRecapFailures>();
        services.AddScoped<SimilarGamesService>();
        services.AddScoped<GameReviewTexts>();
        services.AddSingleton<IGameReviewTextScheduler, GameReviewTextScheduler>();
    }

    /// <summary>Rohbestand, Punktepartie, Anmerkungen, „Frag die Kommentare".</summary>
    private static void AddLibraryServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<LibraryGameService>();
        services.AddScoped<GuessStartPly>();
        services.AddScoped<CommentSetService>();
        services.AddScoped<GuessOpeningTree>();
        services.AddScoped<CommentTranslationService>();
        services.AddScoped<GuessSessionService>();
        services.AddSingleton<ITextEmbedder>(sp => new OpenAiTextEmbedder(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("embedding"), configuration,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<OpenAiTextEmbedder>()));
        services.AddScoped<CommentSearchService>();
    }

    /// <summary>Partieformular einlesen.</summary>
    private static void AddScoresheetServices(IServiceCollection services, IConfiguration configuration)
    {
        // Partieformular einlesen (0.529.0): Foto → Claude → legale Partie; eigener DB-gestützter Worker.
        services.AddSingleton<IScoresheetVisionClient, ClaudeScoresheetVisionClient>();
        services.AddScoped<ScoresheetScanService>();
        // Engine-Prüfung der Lesung (0.646.0): Stockfish aus dem API-Image (/usr/games/stockfish), ein Prozess je Prüfung.
        services.AddSingleton<IScoresheetEngine, StockfishScoresheetEngine>();
        services.AddSingleton<ScoresheetScanSignal>();
        services.AddSingleton<ScoresheetShareInbox>();
    }
}
