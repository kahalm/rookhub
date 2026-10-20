using Microsoft.Extensions.Options;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Räumt Bücher weg, die ein Besucher OHNE Anmeldung hochgeladen und nie in ein Konto übernommen hat (0.778.0), samt
/// Dateien, Titelbild und alten anonymen Leseständen — nach <see cref="EbookOptions.AnonymousRetentionDays"/> Tagen.
/// Stündlich, höchstens 50 Bücher je Durchlauf.
/// </summary>
public class EbookAnonymousCleanupWorker : PeriodicWorker
{
    private readonly EbookStorage _storage;

    public EbookAnonymousCleanupWorker(IServiceScopeFactory scopeFactory, EbookStorage storage, ILogger<EbookAnonymousCleanupWorker> logger)
        : base(scopeFactory, logger) => _storage = storage;

    protected override WorkerSchedule Schedule => WorkerSchedule.Every(TimeSpan.FromHours(1));
    protected override WorkerStart Start => WorkerStart.After(TimeSpan.FromMinutes(5));
    protected override bool Enabled => _storage.Root != null;
    /// <summary>Nicht pausierbar: hält den Speicherplatz in Grenzen (anonyme Uploads ohne Konto) — eine lange Pause ließe
    /// Platte und Fristen auflaufen, und der Lauf ist klein (stündlich, höchstens 50 Bücher).</summary>
    protected override bool Pausable => false;
    protected override void LogDisabled() => Logger.LogInformation("E-Books ohne Anmeldung aufräumen: kein Ebooks:Root konfiguriert — Dienst aus");
    protected override void LogFailure(Exception ex) => Logger.LogError(ex, "E-Books ohne Anmeldung aufräumen: Durchlauf fehlgeschlagen");

    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        var days = Math.Max(1, services.GetRequiredService<IOptions<EbookOptions>>().Value.AnonymousRetentionDays);
        await services.GetRequiredService<EbookService>().CleanupAnonymousAsync(TimeSpan.FromDays(days), ct);
    }
}
