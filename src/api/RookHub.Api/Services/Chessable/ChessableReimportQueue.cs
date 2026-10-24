using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;

namespace RookHub.Api.Services.Chessable;

/// <summary>
/// Re-Import-Warteschlange: legt einen wartenden Import-Datensatz für einen schon importierten Kurs an (Eigentum,
/// Dedup, Lane-Klassifikation) und reiht Tickets der Download-Lane in die geteilte Background-Queue ein. Teil von
/// <see cref="ChessableImportService"/> (Zerlegung Z3), von der Fassade gebaut.
/// </summary>
internal sealed class ChessableReimportQueue
{
    private readonly AppDbContext _db;
    private readonly ChessableProxyService _proxy;
    private readonly EncryptionService _encryption;
    private readonly IBackgroundTaskQueue _taskQueue;

    public ChessableReimportQueue(AppDbContext db, ChessableProxyService proxy, EncryptionService encryption,
        IBackgroundTaskQueue taskQueue)
    {
        _db = db;
        _proxy = proxy;
        _encryption = encryption;
        _taskQueue = taskQueue;
    }

    /// <summary>Reiht ein Ticket ein, das den fair als Nächstes dran befindlichen wartenden Import
    /// verarbeitet (nicht einen festen). So bestimmt die faire Reihenfolge — nicht die FIFO-Position
    /// in der geteilten Background-Queue —, welcher Import als Nächstes läuft.</summary>
    public async Task EnqueueNextAsync()
    {
        await _taskQueue.EnqueueAsync(async (sp, ct) =>
        {
            var svc = sp.GetRequiredService<ChessableImportService>();
            await svc.RunNextAsync(ct);
        });
    }

    /// <summary>Siehe <see cref="ChessableImportService.EnqueueReimportAsync"/>.</summary>
    public async Task<int?> EnqueueReimportAsync(int ownerUserId, string bid, string target, string courseName,
        int? targetRepertoireId, bool? knownCached, bool trustOwnership, CancellationToken ct)
    {
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == ownerUserId, ct);
        // Cache-Status vorab (Batch-Wert bevorzugt) — bestimmt auch, ob ein Owner OHNE Bearer bedient
        // werden kann: ein voll-gecachter Kurs kommt aus dem piratechess-Rohdaten-Cache, ganz ohne
        // Chessable-Kontakt/Bearer.
        var fullyCached = knownCached ?? await _proxy.IsCourseCachedAsync(bid, ct);
        // Kein Bearer des Owners: nur zulässig, wenn der Kurs gecacht ist (Re-Fetch aus dem Cache
        // ohne Bearer) UND der Aufrufer vertrauenswürdig ist (Admin-Massen-Reprocess). Sonst kein
        // Weg, den Kurs zu holen → null (der Reprocess markiert das Repertoire dann nur als aktuell).
        if (cred is null && !(trustOwnership && fullyCached)) return null;
        // SICHERHEIT: nur Kurse re-fetchen, die WIRKLICH in der Chessable-Bibliothek des Owners liegen.
        // Sonst ließe sich der Eigentums-Check von StartImport umgehen — ein User kann ein eigenes
        // Repertoire mit beliebigem ChessableCourseId bzw. Dateinamen `chessable-{bid}.pgn` anlegen
        // (ImportVersion 0 = sofort „stale") und es per /reprocess re-fetchen; für gecachte Kurse liefert
        // piratechess den Inhalt ohne Eigentumsprüfung. Der Check hier schließt diese zweite Tür.
        // Ausnahme: trustOwnership (Admin-Massen-Reprocess) — Chessables getHomeData listet nur einen Teil
        // der Bibliothek, daher würde der Check eigene, längst importierte Kurse fälschlich abweisen; Admins
        // dürfen ohnehin jeden Kurs holen.
        // Dieselbe Prüfung samt Drossel wie StartImport (CourseListRefreshCooldown) — ohne sie holte „Aktualisieren"
        // über N Repertoires mit unbekannter bid die Kursliste N-mal live von Chessable (A3-014).
        if (!trustOwnership && !await ChessableImportQueueService.OwnsCourseAsync(_db, _proxy, _encryption, cred!, bid, ct)) return null;
        // Dedup: läuft/pausiert für diesen (Owner, bid) bereits ein Import, KEINEN zweiten anlegen.
        // Verhindert, dass ein erneuter „Update all"-Klick (oder ein Resume/Retry) denselben Kurs ein
        // zweites Mal komplett von Chessable holt — genau die beobachtete N-fache Flut (bid 116242 4×).
        if (await _db.ChessableImports.AnyAsync(
                x => x.UserId == ownerUserId && x.Bid == bid && (x.Status == ChessableImportStatus.Running || x.Status == ChessableImportStatus.Paused), ct))
            return null;
        var queueRound = await _db.ChessableImports.CountAsync(x => x.UserId == ownerUserId && x.Status == ChessableImportStatus.Running, ct);
        var import = new ChessableImport
        {
            UserId = ownerUserId,
            Bid = bid,
            CourseName = courseName ?? string.Empty,
            Target = target,
            TargetRepertoireId = targetRepertoireId,
            Status = ChessableImportStatus.Running,
            QueueRound = queueRound,
            CreatedAt = DateTime.UtcNow,
            // Lane-Klassifikation: voll-gecachte Kurse → schnelle, netzfreie Lane (kein Warten hinter
            // den seriellen Downloads). Cache-Check ist piratechess-DB-lokal (kein Chessable-Abruf).
            // Beim Massen-Reprocess kommt der Status vorab aus einem Batch-Abruf (knownCached) → spart
            // je Kurs einen teuren Einzel-Check, der den ganzen Cache-Blob lädt. (Oben bereits ermittelt.)
            FullyCached = fullyCached,
        };
        _db.ChessableImports.Add(import);
        await _db.SaveChangesAsync(ct);
        // Nur die Download-Lane braucht ein Queue-Ticket; die Fast-Lane treibt ihr eigener Drain-Service.
        if (import.FullyCached != true)
            await EnqueueNextAsync();
        return import.Id;
    }
}
