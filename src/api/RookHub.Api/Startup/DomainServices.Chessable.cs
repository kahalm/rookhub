using RookHub.Api.Services.Chessable;

namespace RookHub.Api.Startup;

public static partial class DomainServicesSetup
{
    /// <summary>Chessable-Import (eigener Weg + Extension-Senken) und Reprocess.</summary>
    private static void AddChessableServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ChessableImportService>();
        services.AddSingleton<ChessableIngestSessionStore>();
        services.AddScoped<ChessableImportQueueService>();
        services.AddScoped<ChessableBearerBreaker>();
        services.AddScoped<ChessableRateLimiter>();
        services.AddScoped<ChessableCourseRefreshService>();
        services.AddScoped<ChessableTrainedLineService>();
        services.AddScoped<ChessableProblemMoveService>();
        services.AddScoped<ChessableResponseAlertService>();
        services.AddScoped<ChessableReviewLineService>();
        // Byte-Stand der Chessable-Roh-Senken: EINE gecachte Summe für den ganzen Prozess (die Summe über LONGTEXT
        // liest jede Zeile — je Anfrage neu gezählt wäre der Deckel selbst die Last).
        services.AddSingleton<ChessableSinkBytes>();
        services.AddScoped<ChessableSessionMoveService>();
        services.AddScoped<ICourseReimporter>(sp => sp.GetRequiredService<ChessableImportService>());
        // Linien-Cache für den Cache-Weg von „Aktualisieren" (StaleAction.Cache). Bewusst NICHT hinter
        // `Chessable:Enabled`: der piratechess-Proxy ist immer registriert, die Extension-Wege brauchen ihn auch.
        services.AddTransient<ICachedLineSource>(sp => sp.GetRequiredService<ChessableProxyService>());
        services.AddScoped<ImportReprocessService>();
        // Stößt Massen-Reprocess im Hintergrund an (eigener Scope) → Endpoint antwortet sofort statt in
        // den ~60-s-Request-Timeout zu laufen. Singleton, da es nur die ScopeFactory kapselt.
        services.AddSingleton<IReprocessLauncher, ReprocessLauncher>();
        services.AddScoped<RememberedPositionService>();
    }
}
