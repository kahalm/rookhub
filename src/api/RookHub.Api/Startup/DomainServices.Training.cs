using RookHub.Api.Services.Courses;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Kids;
using RookHub.Api.Services.Puzzles;
using RookHub.Api.Services.Repertoires;

namespace RookHub.Api.Startup;

public static partial class DomainServicesSetup
{
    /// <summary>Puzzles, Endless, Challenges, Bestenlisten, Wochenpost, Tipp-Generierung.</summary>
    private static void AddPuzzleServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ChallengeService>();
        services.AddScoped<FavoriteService>();
        services.AddScoped<RevengeNotificationService>();
        services.AddScoped<PuzzleTaggingService>();
        services.AddScoped<PuzzleStatsService>();
        services.AddScoped<PuzzleService>();
        services.AddScoped<EndlessProgressService>();
        services.AddScoped<DailyLeaderboardService>();
        services.AddScoped<WeeklyPostService>();
        services.AddScoped<LeaderboardService>();
        services.AddScoped<HintGenerationService>();
        // Eigene Queue fuer die Tipp-Generierung (Stockfish + LLM je Linie): ein grosser Kurs-Upload hielt den
        // einzigen allgemeinen Consumer sonst stundenlang fest; persoenliche Kurse sind dort gedeckelt (A4-002).
        services.AddSingleton<IHintTaskQueue, HintTaskQueue>();
        // Anonyme Buch-Puzzle-Solves melden hoechstens einmal je Puzzle und 30 s an den Bot (A2-003).
        services.AddSingleton<AnonymousSolveNotifyThrottle>();
        // Signierte Startmarke des Tagespuzzles: Wertung nach Serverzeit, ohne Tabelle (A2-014).
        services.AddSingleton<DailyStartTokens>();
    }

    /// <summary>Kurse, Aufgabenblätter, Kalkulation, Woodpecker, Kurs-Übersetzung, Katalog.</summary>
    private static void AddCourseServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<WorksheetService>();
        services.AddScoped<PgnImportService>();
        services.AddScoped<FlashcardMarkService>();
        services.AddScoped<BookPuzzleService>();
        services.AddScoped<CourseService>();
        services.AddScoped<WoodpeckerService>();
        services.AddScoped<CoursePgnExportService>();
        // Kurs ⇄ Repertoire (beide Richtungen). Haengt an BEIDEN Diensten — deshalb ein eigener:
        // RepertoireService darf CourseService nicht bekommen (CourseService kennt Repertoire-Code schon).
        services.AddScoped<CourseRepertoireConversionService>();
        services.AddScoped<CourseStatsService>();
        services.AddScoped<CalculationService>();
        services.AddScoped<CalcEditionService>();
        services.AddScoped<CalcSeriesAnnounceService>();
        // Kurs-Kommentare mehrsprachig (0.547.0): Übersetzen je Linie + Ausliefern über ?lang=.
        services.AddScoped<CourseTranslationService>();
        services.AddScoped<CourseCommentLocalizer>();
        // … und die Aufträge dazu (0.548.0): anfordern/zurückziehen, ein Auftrag nach dem anderen im Hintergrund,
        // Sperrzeiten der Spark (QuietHours, AddInfrastructureServices), Automatik über CourseTranslation:AutoLanguages (leer = aus).
        services.AddScoped<CourseTranslationJobService>();
        services.AddSingleton<CourseTranslationSignal>();
        services.AddScoped<CourseAuthoringService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<BookAdminService>();
    }

    /// <summary>KidHub.</summary>
    private static void AddKidsServices(IServiceCollection services, IConfiguration configuration)
    {
        // Kinderseite: Stufen-Leiter aus besonders einfachen Lichess-Puzzles + Kinderkurse.
        services.AddScoped<KidsPuzzleService>();
        services.AddScoped<KidsProgressService>();
        services.AddScoped<KidsEndlessService>();
    }

    /// <summary>Repertoires, Lochfinder (Lichess-Explorer), geteilte Linien.</summary>
    private static void AddRepertoireServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<RepertoireService>();
        services.AddScoped<RepertoireTrainingService>();
        services.AddScoped<RepertoireAnalyzeService>();
        // Eigener Cache der Positions-Sets MIT Größengrenze (Size = Stellungen; Codereview N8-005) — der allgemeine
        // IMemoryCache hat keine, und eine dort verlangte Size müsste jeder andere Eintrag im Prozess mitbringen.
        services.AddKeyedSingleton<Microsoft.Extensions.Caching.Memory.IMemoryCache>(RepertoireAnalyzeService.CacheServiceKey,
            (_, _) => new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions
            {
                SizeLimit = RepertoireAnalyzeService.CacheSizeLimit,
            }));
        services.AddScoped<RepertoireLineSource>();
        services.AddScoped<RepertoirePositionLookupService>();
        services.AddScoped<RepertoireSimilarityService>();
        // Lochfinder: EINE Explorer-Leitung für den ganzen Prozess (Lichess will Anfragen nacheinander
        // und nach 429 eine Minute Ruhe), Antworten 90 Tage in LichessExplorerCacheEntries.
        // Kontingent der Explorer-Leitung, einstellbar ohne neues Image (LichessExplorer__Burst,
        // LichessExplorer__RefillMs); Vorgaben und Messung siehe LichessExplorerGate.
        services.AddSingleton(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var refillMs = cfg.GetValue<int?>("LichessExplorer:RefillMs");
            return new LichessExplorerGate(
                burst: cfg.GetValue<int?>("LichessExplorer:Burst"),
                refillInterval: refillMs is >= 0 ? TimeSpan.FromMilliseconds(refillMs.Value) : null);
        });
        services.AddScoped<RepertoireExplorerService>();
        services.AddScoped<RepertoireCleanupService>();
        services.AddScoped<SharedLineService>();
    }
}
