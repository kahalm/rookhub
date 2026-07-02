using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Geteilte Helfer rund um die Chessable-Import-Warteschlange, die sowohl der User-Controller
/// (<see cref="Controllers.ChessableController"/>) als auch der Admin-Controller
/// (<see cref="Controllers.ChessableAdminController"/>) brauchen: Einreihen des fairen Nächst-Tickets,
/// faire globale Warteschlangen-Positionen, Kurs-Import-Status-Anreicherung, Eigentumsprüfung und die
/// DTO-Mapper. Scoped → teilt sich pro Request denselben <see cref="AppDbContext"/> wie der Controller.
/// </summary>
public class ChessableImportQueueService
{
    private readonly AppDbContext _db;
    private readonly ChessableProxyService _chessable;
    private readonly EncryptionService _encryption;
    private readonly IBackgroundTaskQueue _taskQueue;

    /// <summary>Web-JSON-Optionen für die gecachte Kursliste (Serialisieren/Deserialisieren von
    /// <c>ChessableCredential.CachedCoursesJson</c>). Von beiden Controllern mitgenutzt.</summary>
    public static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ChessableImportQueueService(
        AppDbContext db, ChessableProxyService chessable, EncryptionService encryption, IBackgroundTaskQueue taskQueue)
    {
        _db = db;
        _chessable = chessable;
        _encryption = encryption;
        _taskQueue = taskQueue;
    }

    /// <summary>Sprechende „Bearer gesperrt"-Meldung für Lese-/Import-Endpoints bei offenem Breaker.</summary>
    public static string BlockedMessage(string? reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? "Chessable-Bearer gesperrt — bitte zuerst „Testen“ (Validität bestätigen)."
            : $"Chessable-Bearer gesperrt ({reason}) — bitte zuerst „Testen“ (Validität bestätigen).";

    /// <summary>Markiert je Kurs, ob er vom User bereits als Repertoire bzw. Buch importiert wurde
    /// (Quelle: abgeschlossene ChessableImports) — Basis fürs Ausblenden der erledigten Buttons.</summary>
    public async Task<List<ChessableCourseDto>> EnrichImportStateAsync(List<ChessableCourseDto> courses, int userId, CancellationToken ct)
    {
        var done = await _db.ChessableImports
            .Where(i => i.UserId == userId && i.Status == "completed")
            .Select(i => new { i.Bid, i.Target })
            .ToListAsync(ct);
        var rep = done.Where(d => d.Target == "repertoire").Select(d => d.Bid).ToHashSet();
        var book = done.Where(d => d.Target == "book").Select(d => d.Bid).ToHashSet();
        // Bereits eingereihte/laufende Importe (Status "running") → im UI als „in Warteschlange" zeigen,
        // damit man denselben Kurs nicht doppelt einreiht.
        var queued = (await _db.ChessableImports
            .Where(i => i.UserId == userId && i.Status == "running")
            .Select(i => i.Bid)
            .ToListAsync(ct)).ToHashSet();
        // Gecachte Kurse (Rohdaten in der piratechess-DB) → sofort verfügbar. 1 Bulk-Call; Fehler → leer.
        var cached = await _chessable.GetCachedBidsAsync(ct);
        return courses
            .Select(c => c with
            {
                ImportedRepertoire = rep.Contains(c.Bid),
                ImportedBook = book.Contains(c.Bid),
                Cached = cached.Contains(c.Bid),
                Queued = queued.Contains(c.Bid),
            })
            .ToList();
    }

    /// <summary>Prüft, ob <paramref name="bid"/> in der Chessable-Bibliothek zum Bearer von
    /// <paramref name="cred"/> liegt. Erst gegen die gecachte Kursliste (schnell, kein Chessable-Call);
    /// fehlt der bid dort, wird die Liste EINMAL frisch geladen (deckt frisch gekaufte Kurse / leeren
    /// Cache ab) und der Cache aktualisiert. Nicht verifizierbar (Bearer kaputt / Chessable-Fehler) ⇒
    /// fail-closed (kein Import).</summary>
    public async Task<bool> UserOwnsCourseAsync(ChessableCredential cred, string bid, CancellationToken ct)
    {
        bool Has(string? json) =>
            !string.IsNullOrEmpty(json)
            && (JsonSerializer.Deserialize<List<ChessableCourseDto>>(json, JsonOpts) ?? new())
               .Any(c => c.Bid == bid);

        if (Has(cred.CachedCoursesJson)) return true;

        var bearer = _encryption.TryDecrypt(cred.EncryptedBearer);
        if (bearer is null) return false;
        try
        {
            var courses = await _chessable.GetCoursesAsync(bearer, ct);
            cred.CachedCoursesJson = JsonSerializer.Serialize(courses, JsonOpts);
            cred.CoursesCachedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return courses.Any(c => c.Bid == bid);
        }
        catch (ChessableProxyException)
        {
            return false;
        }
    }

    /// <summary>Reiht ein Ticket ein, das den fair als Nächstes dran befindlichen Import verarbeitet
    /// (Round-Robin über die User), nicht zwingend den gerade angelegten — siehe
    /// <see cref="ChessableImportService.RunNextAsync"/>.</summary>
    public async Task EnqueueNextAsync()
    {
        await _taskQueue.EnqueueAsync(async (sp, ct) =>
        {
            var svc = sp.GetRequiredService<ChessableImportService>();
            await svc.RunNextAsync(ct);
        });
    }

    /// <summary>Faire globale Warteschlangen-Position (aller User) je Import-Id: die gerade laufenden
    /// (Phase ≠ "queued") belegen die vorderen Plätze, danach die wartenden Importe in fairer
    /// Reihenfolge (Round-Robin über die User, siehe <see cref="ChessableImportService.FairOrder"/>).
    /// Spiegelt damit EXAKT die Reihenfolge, in der <see cref="ChessableImportService.RunNextAsync"/>
    /// sie abarbeitet — NICHT die Einreih-/Id-Reihenfolge. Nur wartende Importe stehen in der Map;
    /// laufende/pausierte fehlen (⇒ Position 0, die Anzeige zeigt für die ohnehin den Phasen-Status).</summary>
    public async Task<Dictionary<int, int>> FairQueuePositionsAsync()
    {
        var running = await _db.ChessableImports.Where(x => x.Status == "running").ToListAsync();
        var inProgress = running.Count(x => x.Phase != "queued");
        var order = ChessableImportService.FairOrder(running.Where(x => x.Phase == "queued"));
        var map = new Dictionary<int, int>();
        for (var idx = 0; idx < order.Count; idx++)
            map[order[idx].Id] = inProgress + idx;
        return map;
    }

    /// <summary>Faire globale Warteschlangen-Position eines einzelnen Imports (siehe
    /// <see cref="FairQueuePositionsAsync"/>). 0, wenn er bereits läuft oder nicht mehr wartet.</summary>
    public async Task<int> QueuedAheadAsync(ChessableImport i)
    {
        if (i.Status != "running" || i.Phase != "queued") return 0;
        return (await FairQueuePositionsAsync()).GetValueOrDefault(i.Id, 0);
    }

    public static ChessableImportDto ToDto(ChessableImport i, int queuedAhead) => new(
        i.Id, i.Bid, i.CourseName, i.Target, i.Status, i.Phase, i.Error, i.ResultId, i.Imported, i.Skipped, i.Invalid,
        i.ChaptersDone, i.ChaptersTotal, i.LinesDone, i.LinesTotal, queuedAhead, i.CreatedAt, i.StartedAt, i.CompletedAt);

    public static ChessableAdminImportDto ToAdminDto(ChessableImport i, int queuedAhead) => new(
        i.Id, i.UserId, i.User?.Username ?? "?", i.Bid, i.CourseName, i.Target, i.Status, i.Phase, i.Error,
        i.ResultId, i.Imported, i.Skipped, i.Invalid, i.ChaptersDone, i.ChaptersTotal, i.LinesDone, i.LinesTotal, queuedAhead,
        i.CreatedAt, i.StartedAt, i.CompletedAt);
}
