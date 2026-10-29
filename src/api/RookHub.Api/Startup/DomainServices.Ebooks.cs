
namespace RookHub.Api.Startup;

public static partial class DomainServicesSetup
{
    /// <summary>E-Book-Bibliothek und Diagramm-Erkennung.</summary>
    private static void AddEbookServices(IServiceCollection services, IConfiguration configuration)
    {
        // E-Book-Bibliothek (0.764.0): Wurzeln aus dem Abschnitt „Ebooks" (ENV Ebooks__Root / Ebooks__LibraryRoot);
        // ohne Root bleibt Hochladen aus, ohne LibraryRoot das Einlesen — Übersicht und Leser laufen trotzdem.
        services.Configure<RookHub.Api.Services.Ebooks.EbookOptions>(configuration.GetSection(RookHub.Api.Services.Ebooks.EbookOptions.Section));
        services.AddSingleton<RookHub.Api.Services.Ebooks.EbookStorage>();
        services.AddSingleton<RookHub.Api.Services.Ebooks.EbookAccessTokens>();
        services.AddScoped<RookHub.Api.Services.Ebooks.EbookService>();
        services.AddScoped<RookHub.Api.Services.Ebooks.EbookCourseBuilder>();
        services.AddScoped<RookHub.Api.Services.Ebooks.EbookCollectionService>();
        services.AddScoped<RookHub.Api.Services.Ebooks.EbookGameService>();
        services.AddScoped<RookHub.Api.Services.Ebooks.EbookLibraryImporter>();
        services.AddScoped<RookHub.Api.Services.Ebooks.EbookCoverRenderer>();
        // Diagramme aus Schach-Font-PDFs (0.766.0): Text-Diagramme → FEN je Diagramm, im Hintergrund je Buch.
        services.AddScoped(sp => new RookHub.Api.Services.Ebooks.EbookDiagramExtractor(
            sp.GetRequiredService<ILogger<RookHub.Api.Services.Ebooks.EbookDiagramExtractor>>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RookHub.Api.Services.Ebooks.EbookOptions>>().Value.ScanDiagrams
                ? sp.GetRequiredService<RookHub.Api.Services.Ebooks.EbookScanDiagramExtractor>() : null));
        services.AddScoped<RookHub.Api.Services.Ebooks.EbookEpubDiagramExtractor>();
        // Fließtext-EPUB aus Text-PDFs (0.802.0, Prototyp): POST /api/admin/ebooks/{id}/reflow; nach der Erkennung nur mit Ebooks__ReflowAfterDiagrams.
        services.AddScoped<RookHub.Api.Services.Ebooks.EbookReflowService>();
        services.AddScoped<RookHub.Api.Services.Ebooks.EbookScanDiagramExtractor>();
        services.AddScoped<RookHub.Api.Services.Ebooks.CourseEbookLinker>();
        // Externe Diagramm-Worker (0.870.0, /api/diagram-worker): Leihe, Dateien, Ergebnis über denselben Speicherweg wie der Worker.
        services.AddScoped<RookHub.Api.Services.Ebooks.EbookDiagramWorkerService>();
        services.AddScoped<RookHub.Api.Services.Ebooks.BoardCutJobService>();
    }
}
