using Microsoft.Extensions.Options;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Wann ist ein Buch „fertig verarbeitet“ — und die Glocken-Meldung an den Hochladenden (0.773.0). Fertig heißt: kein
/// Hintergrundschritt steht mehr aus (Titelbild, DJVU→PDF, Diagramm-Erkennung, Stockfish-Prüfung — letztere nur, wenn
/// Diagramme gefunden wurden und die Prüfung eingeschaltet ist). Jeder Worker ruft nach seinem Schritt
/// <see cref="NotifyIfReadyAsync"/>; gemeldet wird genau einmal (<see cref="Ebook.ReadyNotifiedAt"/>).
/// </summary>
public static class EbookProcessing
{
    public const string NotificationType = "ebook_ready";

    public static bool IsProcessing(Ebook b, bool engineCheck) =>
        b.CoverState == EbookCoverState.Pending
        || b.ConversionState == EbookConversionState.Pending
        || b.DiagramState == EbookDiagramState.Pending
        || (engineCheck && b.DiagramState == EbookDiagramState.Done && b.DiagramCount > 0 && b.SolutionCheckState == EbookDiagramState.Pending);

    public static async Task NotifyIfReadyAsync(IServiceProvider services, Ebook b, CancellationToken ct)
    {
        if (b.Source != EbookSource.Upload || b.OwnerUserId is not int owner || b.ReadyNotifiedAt != null) return;
        var engineCheck = services.GetService<IOptions<EbookOptions>>()?.Value.EngineCheck ?? true;
        if (IsProcessing(b, engineCheck)) return;
        var db = services.GetRequiredService<AppDbContext>();
        b.ReadyNotifiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        var notifications = services.GetService<NotificationService>();
        if (notifications == null) return;
        var data = new Dictionary<string, string>
        {
            ["title"] = b.Title,
            ["diagrams"] = (b.DiagramState == EbookDiagramState.Done ? b.DiagramCount : 0).ToString(),
        };
        await notifications.CreateAsync(owner, NotificationType, data, $"/ebooks/{b.Id}");
    }
}
