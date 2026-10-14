using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Holt in kleinen Portionen die Titelbilder nach (<see cref="EbookCoverRenderer"/>), die Upload und Bibliotheks-Import
/// nur VORMERKEN (<see cref="EbookCoverState.Pending"/>) — ein 800-Bücher-Import antwortet so in Sekunden, die Bilder
/// trudeln in den Minuten danach ein. Ohne <c>Ebooks:Root</c> gibt es keinen Ort für Bilder → Dienst aus.
/// </summary>
public class EbookCoverWorker : PeriodicWorker
{
    private readonly EbookStorage _storage;

    public EbookCoverWorker(IServiceScopeFactory scopeFactory, EbookStorage storage, ILogger<EbookCoverWorker> logger)
        : base(scopeFactory, logger)
    {
        _storage = storage;
    }

    protected override WorkerSchedule Schedule => WorkerSchedule.Every(TimeSpan.FromSeconds(20));
    protected override WorkerStart Start => WorkerStart.After(TimeSpan.FromSeconds(45));
    protected override bool Enabled => _storage.Root != null;
    protected override void LogDisabled() => Logger.LogInformation("E-Book-Titelbilder: kein Ebooks:Root konfiguriert — Dienst aus");
    protected override void LogFailure(Exception ex) => Logger.LogError(ex, "E-Book-Titelbilder: Durchlauf fehlgeschlagen");

    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var renderer = services.GetRequiredService<EbookCoverRenderer>();
        var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<EbookOptions>>().Value;
        var batch = await db.Ebooks.Include(b => b.Files)
            .Where(b => b.CoverState == EbookCoverState.Pending)
            .OrderBy(b => b.Id)
            .Take(Math.Max(1, options.CoverBatchSize))
            .ToListAsync(ct);
        foreach (var ebook in batch)
        {
            ct.ThrowIfCancellationRequested();
            var result = await renderer.RenderAsync(ebook, ct);
            ebook.CoverState = result.Success ? EbookCoverState.Done : EbookCoverState.Failed;
            if (result.PageCount is > 0) ebook.PageCount = result.PageCount;
            if (!result.Success)
                Logger.LogInformation("E-Book {EbookId}: kein Titelbild ({Error})", ebook.Id, result.Error);
            await db.SaveChangesAsync(ct);
            await EbookProcessing.NotifyIfReadyAsync(services, ebook, ct);
        }
    }
}
