namespace RookHub.Api.Services;

/// <summary>
/// Stößt das Neu-Aufbereiten von Kursen/Repertoires (<see cref="ImportReprocessService"/>) im
/// HINTERGRUND an und kehrt sofort zurück. Nötig, weil ein Massen-Reprocess (viele Chessable-Kurse:
/// Batch-Cache-Abruf + Dedup/Enqueue je Kurs) länger als das HTTP-Request-/Proxy-Timeout (~60 s)
/// laufen kann — der synchrone Endpoint lief dann in ein „operation was canceled"-500 (Kunde/Proxy
/// bricht ab), obwohl die Arbeit selbst korrekt weiterlief. Die eigentliche Arbeit ist idempotent
/// (lokal in-place per LineId; Chessable-Re-Fetch als persistierte Import-Jobs), darf also gefahrlos
/// entkoppelt vom Request laufen.
/// <para>Eigener DI-Scope pro Lauf (frischer <see cref="Data.AppDbContext"/>), da der Request-Scope
/// nach der 202-Antwort entsorgt wird.</para>
/// </summary>
public interface IReprocessLauncher
{
    void LaunchCourses(int userId, bool isAdmin, bool localOnly);
    void LaunchRepertoires(int userId, bool isAdmin, bool localOnly);
}

public class ReprocessLauncher : IReprocessLauncher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReprocessLauncher> _logger;

    public ReprocessLauncher(IServiceScopeFactory scopeFactory, ILogger<ReprocessLauncher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void LaunchCourses(int userId, bool isAdmin, bool localOnly) =>
        Run("courses", userId, (svc, ct) => svc.ReprocessCoursesAsync(userId, isAdmin, localOnly, ct));

    public void LaunchRepertoires(int userId, bool isAdmin, bool localOnly) =>
        Run("repertoires", userId, (svc, ct) => svc.ReprocessRepertoiresAsync(userId, isAdmin, localOnly, ct));

    private void Run(string section, int userId, Func<ImportReprocessService, CancellationToken, Task> work)
    {
        // Fire-and-forget mit eigenem Scope; kein Request-Token (CancellationToken.None), damit
        // Wegnavigieren/Timeout die Aufbereitung nicht abbricht. Fehler nur loggen (unbeobachtete
        // Task-Exceptions dürfen den Prozess nicht crashen).
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<ImportReprocessService>();
                await work(svc, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hintergrund-Reprocess ({Section}) für User {UserId} fehlgeschlagen", section, userId);
            }
        });
    }
}
