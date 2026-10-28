using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Holt die Fingerabdrücke (ISBN + Text-MinHash, <see cref="EbookFingerprint"/>) für Dateien nach, die keinen haben
/// (0.863.0): der Bestand vor der Dublettenprüfung, eingelesene Bibliotheksbücher und abgeleitete Dateien (PDF aus DJVU,
/// Fließtext-EPUB). Bewusst gemächlich — wenige Dateien je Takt —, damit Titelbilder und Diagramme nicht warten.
/// Formate ohne Textweg (djvu, cbz, mobi …) werden nur als erledigt markiert; deren Buch wird über die abgeleitete PDF
/// bzw. über Titel + Autor gefunden.
/// </summary>
public class EbookFingerprintWorker : PeriodicWorker
{
    /// <summary>Dateien je Takt.</summary>
    public const int BatchSize = 3;

    private readonly EbookStorage _storage;

    public EbookFingerprintWorker(IServiceScopeFactory scopeFactory, EbookStorage storage, ILogger<EbookFingerprintWorker> logger)
        : base(scopeFactory, logger)
    {
        _storage = storage;
    }

    protected override WorkerSchedule Schedule => WorkerSchedule.Every(TimeSpan.FromSeconds(20));
    protected override WorkerStart Start => WorkerStart.After(TimeSpan.FromMinutes(2));
    protected override bool Enabled => _storage.Root != null || _storage.LibraryRoot != null;
    protected override void LogDisabled() => Logger.LogInformation("E-Book-Fingerabdrücke: keine E-Book-Wurzel konfiguriert — Dienst aus");
    protected override void LogFailure(Exception ex) => Logger.LogError(ex, "E-Book-Fingerabdrücke: Durchlauf fehlgeschlagen");

    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        await RunBatchAsync(db, _storage, Logger, ct);
    }

    /// <summary>Ein Durchgang (für Tests öffentlich): bis zu <see cref="BatchSize"/> Dateien ohne Fingerabdruck → Anzahl.</summary>
    public static async Task<int> RunBatchAsync(AppDbContext db, EbookStorage storage, ILogger logger, CancellationToken ct, int batchSize = BatchSize)
    {
        var batch = await db.EbookFiles.Include(f => f.Ebook)
            .Where(f => f.FingerprintedAt == null)
            .OrderBy(f => f.Id)
            .Take(batchSize)
            .ToListAsync(ct);
        foreach (var file in batch)
        {
            ct.ThrowIfCancellationRequested();
            var fingerprint = EbookFileFingerprint.Empty;
            if (file.Format is "pdf" or "epub")
            {
                string? path = null;
                try { path = storage.ResolveFile(file.Ebook, file); }
                catch (Exception ex) when (ex is not OperationCanceledException) { /* Wurzel fehlt / Pfad ungültig */ }
                if (path != null && File.Exists(path))
                    fingerprint = await Task.Run(() => EbookFingerprint.Compute(path, file.Format, ct), ct);
                else
                    logger.LogInformation("E-Book {EbookId}: Datei {FileId} für den Fingerabdruck nicht gefunden", file.EbookId, file.Id);
            }
            file.Isbns = fingerprint.IsbnCsv;
            file.TextFingerprint = fingerprint.MinHash is { } mh ? EbookFingerprint.Encode(mh) : null;
            file.FingerprintedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return batch.Count;
    }
}
