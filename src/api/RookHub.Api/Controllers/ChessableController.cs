using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Chessable-Integration (User-Sicht): speichert den User-Bearer verschluesselt in der
/// rookhub-DB und reicht ihn fuer Lese-Operationen (test, courses) per
/// <see cref="ChessableProxyService"/> an die piratechess-API durch. Die eigentlichen
/// Chessable-Calls (curl-impersonate) liegen vollstaendig in piratechess; RookHub haelt nur
/// den Token + UI. Die Admin-Endpoints (Import „im Namen eines Users") liegen in
/// <see cref="ChessableAdminController"/>; geteilte Queue-/Import-Helfer in
/// <see cref="ChessableImportQueueService"/>.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
// Rate-Limiting BEWUSST nicht mehr klassenweit deaktiviert: [DisableRateLimiting] sitzt nur noch auf
// den Import-Endpunkten (Einreihen im Schwung + Status-Polling erschöpften sonst das 100-Req/min-
// Fenster, 429 beim ~16. Add). POST /test und GET /courses (insb. ?refresh=true) lösen dagegen einen
// Live-Fetch zu Chessable über den geteilten VPN-Pool aus — eine Schleife darauf konnte ungebremst
// die IP verbrennen/Cloudflare-Blocks provozieren; sie unterliegen wieder dem globalen Limiter.
public class ChessableController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly ChessableProxyService _chessable;
    private readonly ChessableBearerBreaker _breaker;
    private readonly NotificationService _notifications;
    private readonly ChessableImportQueueService _queue;
    private readonly ChessableReviewLineService _reviewLines;
    private readonly ILogger<ChessableController> _logger;

    public ChessableController(
        AppDbContext db,
        EncryptionService encryption,
        ChessableProxyService chessable,
        ChessableBearerBreaker breaker,
        NotificationService notifications,
        ChessableImportQueueService queue,
        ChessableReviewLineService reviewLines,
        ILogger<ChessableController> logger)
    {
        _db = db;
        _encryption = encryption;
        _chessable = chessable;
        _breaker = breaker;
        _notifications = notifications;
        _queue = queue;
        _reviewLines = reviewLines;
        _logger = logger;
    }

    /// <summary>Hat der User den Chessable-Haftungsausschluss bestätigt?</summary>
    [HttpGet("disclaimer")]
    public async Task<IActionResult> GetDisclaimer()
    {
        var userId = GetUserId();
        var accepted = await _db.UserProfiles.AnyAsync(p => p.UserId == userId && p.ChessableDisclaimerAcceptedAt != null);
        return Ok(new ChessableDisclaimerDto(accepted));
    }

    /// <summary>Bestätigt den Chessable-Haftungsausschluss (einmalig, in der DB gespeichert).</summary>
    [HttpPost("disclaimer")]
    public async Task<IActionResult> AcceptDisclaimer()
    {
        var userId = GetUserId();
        var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile is null)
        {
            profile = new UserProfile { UserId = userId };
            _db.UserProfiles.Add(profile);
        }
        profile.ChessableDisclaimerAcceptedAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new ChessableDisclaimerDto(true));
    }

    [HttpGet("credentials")]
    public async Task<IActionResult> GetCredentials()
    {
        var userId = GetUserId();
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == userId);

        if (cred is null)
            return Ok(new ChessableCredentialResponse(false, null));

        // Robust gegen Key-Rotation/korrupte Daten: kein 500, nur keine Maske (Re-Eingabe nötig).
        var plain = _encryption.TryDecrypt(cred.EncryptedBearer);
        return Ok(new ChessableCredentialResponse(
            true, plain is null ? null : Mask(plain), cred.BlockedAt is not null, cred.BlockedReason));
    }

    [HttpPost("credentials")]
    public async Task<IActionResult> SaveCredentials([FromBody] SaveChessableBearerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Bearer))
            return BadRequest(new { message = "Bearer is required" });

        var userId = GetUserId();
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        var now = DateTime.UtcNow;
        var isNewCredential = cred is null;

        if (cred is null)
        {
            cred = new ChessableCredential
            {
                UserId = userId,
                EncryptedBearer = _encryption.Encrypt(request.Bearer.Trim()),
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.ChessableCredentials.Add(cred);
        }
        else
        {
            cred.EncryptedBearer = _encryption.Encrypt(request.Bearer.Trim());
            cred.UpdatedAt = now;
            // Bearer gewechselt → gecachte Kursliste + verknüpfte Chessable-Identität verwerfen
            // (kann zu anderem Account gehören; die uid wird erst durch einen erfolgreichen „Testen"
            // gegen Chessable BEWIESEN neu gesetzt — nie aus dem ungeprüften JWT).
            cred.CachedCoursesJson = null;
            cred.CoursesCachedAt = null;
            cred.ChessableUid = null;
            // Frischer Bearer → Circuit-Breaker schließen (ein neuer Token verdient einen Versuch).
            // Pausierte Importe werden NICHT automatisch aufgenommen — das macht erst ein erfolgreicher
            // „Testen“-Klick (ClearAndResumeAsync), damit kein gleich wieder toter Token sie hetzt.
            cred.BlockedAt = null;
            cred.BlockedReason = null;
        }

        await _db.SaveChangesAsync();
        // Der Claim anonym gesammelter getReview-Linien passiert NICHT hier: die uid stünde nur aus dem
        // ungeprüften Bearer-JWT zur Verfügung (fälschbar → fremde Anon-Daten claimbar). Er hängt am
        // „Testen"-Endpoint, der den Bearer aktiv gegen Chessable prüft und die uid BEWIESEN zurückgibt.

        // Erstmalig hinterlegter Bearer → Admins informieren (Glocke). Best-effort, blockiert das Speichern nicht.
        if (isNewCredential)
        {
            try
            {
                var username = await _db.AppUsers.Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync() ?? "?";
                var adminIds = await _db.AppUsers.Where(u => u.IsAdmin).Select(u => u.Id).ToListAsync();
                await _notifications.CreateManyAsync(adminIds, NotificationType.ChessableTokenAdded,
                    new Dictionary<string, string> { ["username"] = username }, "/admin");
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Admin-Benachrichtigung für neuen Chessable-Bearer fehlgeschlagen"); }
        }

        return Ok(new ChessableCredentialResponse(true, Mask(request.Bearer.Trim())));
    }

    [HttpDelete("credentials")]
    public async Task<IActionResult> DeleteCredentials()
    {
        var userId = GetUserId();
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        if (cred is not null)
        {
            _db.ChessableCredentials.Remove(cred);
            await _db.SaveChangesAsync();
        }
        return NoContent();
    }

    /// <summary>Prüft den Bearer aktiv gegen Chessable. Das ist zugleich der „Reset" des
    /// Circuit-Breakers: erfolgreicher Test ⇒ Breaker schließen + wegen ihm pausierte Importe wieder
    /// aufnehmen; fatale Ablehnung (Account gesperrt/gelöscht, Token tot) ⇒ Breaker (erneut) öffnen.
    /// Der Test ist die EINZIGE Anfrage, die auch bei offenem Breaker bewusst durchgelassen wird.</summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test(CancellationToken ct)
    {
        var userId = GetUserId();
        var bearer = await LoadBearerAsync();
        if (bearer is null) return BadRequest(new { message = "No Chessable bearer saved" });

        try
        {
            var result = await _chessable.TestAsync(bearer, ct);
            await _breaker.ClearAndResumeAsync(userId, ct);

            // Der Test hat den Bearer aktiv gegen Chessable geprüft → die zurückgegebene uid ist BEWIESEN
            // die Chessable-Identität dieses Users. Erst jetzt (nicht schon beim Speichern aus dem
            // ungeprüften JWT) die uid festhalten und anonym gesammelte getReview-Linien in seinen Account
            // übernehmen (best-effort; darf den Test-200 nicht kippen).
            if (!string.IsNullOrWhiteSpace(result.Uid))
            {
                try
                {
                    var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
                    if (cred is not null && cred.ChessableUid != result.Uid)
                    {
                        cred.ChessableUid = result.Uid.Length > 32 ? result.Uid[..32] : result.Uid;
                        await _db.SaveChangesAsync(ct);
                    }
                    await _reviewLines.ClaimAnonForUidAsync(userId, result.Uid, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Claim anonymer getReview-Linien (User {UserId}, uid {Uid}) fehlgeschlagen",
                        userId, result.Uid);
                }
            }
            return Ok(result);
        }
        catch (ChessableProxyException ex)
        {
            _logger.LogWarning("Chessable test failed: {Status} {Message}", ex.Status, ex.Message);
            if (ChessableBearerBreaker.IsBearerFatal(ex.Message))
                await _breaker.TripAsync(userId, ex.Message, ct);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Kursliste des Users. Standardmäßig aus dem DB-Cache (damit man nicht jedes Mal neu laden muss);
    /// <c>?refresh=true</c> holt frisch von piratechess und aktualisiert den Cache.
    /// </summary>
    [HttpGet("courses")]
    public async Task<IActionResult> Courses([FromQuery] bool refresh, CancellationToken ct)
    {
        var userId = GetUserId();
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        if (cred is null) return BadRequest(new { message = "No Chessable bearer saved" });

        if (!refresh && !string.IsNullOrEmpty(cred.CachedCoursesJson))
        {
            var cached = JsonSerializer.Deserialize<List<ChessableCourseDto>>(cred.CachedCoursesJson, ChessableImportQueueService.JsonOpts) ?? new();
            return Ok(new ChessableCoursesDto(await _queue.EnrichImportStateAsync(cached, userId, ct), cred.CoursesCachedAt));
        }

        // Frischer Abruf braucht den Bearer → bei offenem Circuit-Breaker NICHT anfragen.
        if (cred.BlockedAt is not null)
            return BadRequest(new { message = ChessableImportQueueService.BlockedMessage(cred.BlockedReason), blocked = true });

        try
        {
            var bearer = _encryption.TryDecrypt(cred.EncryptedBearer);
            if (bearer is null)
                return BadRequest(new { message = "Stored Chessable bearer could not be read — please re-enter it." });
            var courses = await _chessable.GetCoursesAsync(bearer, ct);
            cred.CachedCoursesJson = JsonSerializer.Serialize(courses, ChessableImportQueueService.JsonOpts);
            cred.CoursesCachedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return Ok(new ChessableCoursesDto(await _queue.EnrichImportStateAsync(courses, userId, ct), cred.CoursesCachedAt));
        }
        catch (ChessableProxyException ex)
        {
            _logger.LogWarning("Chessable courses failed: {Status} {Message}", ex.Status, ex.Message);
            if (ChessableBearerBreaker.IsBearerFatal(ex.Message))
                await _breaker.TripAsync(userId, ex.Message, ct);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Startet einen asynchronen Import des Chessable-Kurses {bid} — als persönliches Repertoire
    /// ("repertoire", jeder User) oder als persönliches Buch/Kurs ("book"). Läuft im Hintergrund;
    /// das Frontend pollt GET /api/chessable/imports/{id}.
    /// </summary>
    [HttpPost("courses/{bid}/import")]
    [DisableRateLimiting]   // Kurse werden oft im Schwung eingereiht (kein Chessable-Live-Fetch pro Request)
    public async Task<IActionResult> StartImport(string bid, [FromBody] StartChessableImportRequest request, CancellationToken ct = default)
    {
        // Format prüfen (Ziffern, ≤12) wie im ExtensionController: eine erfundene bid führte sonst in
        // den Eigentums-Check und dort — je nach Cache-Alter — zu einem Chessable-Live-Abruf.
        if (string.IsNullOrWhiteSpace(bid) || bid.Length > 12 || !bid.All(char.IsAsciiDigit))
            return BadRequest(new { message = "bid must be numeric (max 12 digits)" });
        var target = (request?.Target ?? "").Trim().ToLowerInvariant();
        if (target is not ("repertoire" or "book"))
            return BadRequest(new { message = "target must be 'repertoire' or 'book'" });

        var userId = GetUserId();
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        if (cred is null)
            return BadRequest(new { message = "No Chessable bearer saved" });
        // Circuit-Breaker offen → gar nicht erst einreihen (würde sofort wieder pausieren).
        if (cred.BlockedAt is not null)
            return BadRequest(new { message = ChessableImportQueueService.BlockedMessage(cred.BlockedReason), blocked = true });

        // Dedup: FALLE Doppel-Klick / zweiter Tab / Retry nach Frontend-Timeout — ohne diese Prüfung legt
        // jeder Klick einen weiteren Import an, und JEDER holt den kompletten Kurs erneut von Chessable
        // (verbrennt das 2000-Zeilen-Tageslimit und erhöht das Cloudflare-/IP-Block-Risiko). Läuft/pausiert
        // für (User, bid, Ziel) bereits einer, den BESTEHENDEN zurückgeben — das Frontend pollt dann den
        // laufenden Import weiter. Gegenstück zum Dedup in ChessableImportService.EnqueueReimportAsync.
        var existing = await _db.ChessableImports.FirstOrDefaultAsync(
            i => i.UserId == userId && i.Bid == bid && i.Target == target
                && (i.Status == ChessableImportStatus.Running || i.Status == ChessableImportStatus.Paused), ct);
        if (existing is not null)
            return Accepted(ChessableImportQueueService.ToDto(existing, await _queue.QueuedAheadAsync(existing)));

        // SICHERHEIT: Nur Kurse importieren, die WIRKLICH in der Chessable-Bibliothek dieses Users liegen.
        // Andernfalls könnte ein User einen beliebigen (öffentlich aus der chessable.com-URL bekannten)
        // Kurs-bid importieren — und für bereits GECACHTE Kurse umgeht die piratechess-Seite die
        // Chessable-Eigentumsprüfung (liefert den Cache-Inhalt direkt, ohne den Bearer gegen Chessable
        // zu validieren). Der Check hier schließt diesen Content-Bypass für ALLE Lanes (cached + fetch).
        if (!await _queue.UserOwnsCourseAsync(cred, bid, ct))
            return StatusCode(403, new { message = "Dieser Kurs ist nicht in deiner Chessable-Bibliothek." });

        // Round-Robin-Runde einfrieren: wie viele Importe dieses Users sind GERADE schon aktiv?
        // (0 = erster ⇒ Runde 0). Bestimmt die faire Position; siehe ChessableImportService.FairOrder.
        var queueRound = await _db.ChessableImports.CountAsync(x => x.UserId == userId && x.Status == ChessableImportStatus.Running);
        var import = new ChessableImport
        {
            UserId = userId,
            Bid = bid,
            CourseName = string.IsNullOrWhiteSpace(request?.Name) ? "" : request!.Name!.Trim(),
            Target = target,
            Status = ChessableImportStatus.Running,
            QueueRound = queueRound,
            CreatedAt = DateTime.UtcNow
        };
        _db.ChessableImports.Add(import);
        await _db.SaveChangesAsync();

        // Rohdaten schon gecacht → kein Chessable-Abruf nötig → sofort verarbeiten,
        // nicht hinter den (seriellen) Chessable-Fetches in der Queue warten. Phase/StartedAt sofort
        // setzen → der faire Picker (RunNextAsync, greift nur Phase "queued") überspringt diesen Job.
        // Lane-Klassifikation: voll-gecachte Kurse laufen in der schnellen, netzfreien Fast-Lane
        // (eigener Drain-Service, seriell), alles andere als Download (Queue-Ticket). Der Cache-Check
        // ist piratechess-DB-lokal (kein Chessable-Abruf).
        import.FullyCached = await _chessable.IsCourseCachedAsync(bid);
        await _db.SaveChangesAsync();
        if (import.FullyCached != true)
            await _queue.EnqueueNextAsync();
        return Accepted(ChessableImportQueueService.ToDto(import, await _queue.QueuedAheadAsync(import)));
    }

    /// <summary>Status/Fortschritt eines Imports (Polling bis status != "running").</summary>
    [HttpGet("imports/{id:int}")]
    [DisableRateLimiting]   // Status-Polling bis der Import fertig ist
    public async Task<IActionResult> GetImport(int id)
    {
        var userId = GetUserId();
        var import = await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);
        if (import is null) return NotFound();
        return Ok(ChessableImportQueueService.ToDto(import, await _queue.QueuedAheadAsync(import)));
    }

    /// <summary>Die letzten Importe des Users (Verlauf + laufende/wartende mit globaler Position).</summary>
    [HttpGet("imports")]
    [DisableRateLimiting]   // Verlaufs-/Positions-Polling der Import-Seite
    public async Task<IActionResult> GetImports()
    {
        var userId = GetUserId();
        var recent = await _db.ChessableImports
            .Where(i => i.UserId == userId)
            .OrderByDescending(i => i.CreatedAt)
            .Take(20)
            .ToListAsync();
        // Alle laufenden/pausierten Importe IMMER mitliefern — der gerade verarbeitete Job ist der
        // älteste der offenen Charge und fiel sonst aus dem 20er-Verlaufsfenster (Take). Folge: das
        // Frontend bekäme nur Warteschlangen-Plätze und nie den aktiven Import zu sehen.
        var active = await _db.ChessableImports
            .Where(i => i.UserId == userId && (i.Status == ChessableImportStatus.Running || i.Status == ChessableImportStatus.Paused))
            .ToListAsync();
        var list = recent.UnionBy(active, i => i.Id)
            .OrderByDescending(i => i.CreatedAt)
            .ToList();
        var positions = await _queue.FairQueuePositionsAsync();
        return Ok(list.Select(i => ChessableImportQueueService.ToDto(i, positions.GetValueOrDefault(i.Id, 0))));
    }

    /// <summary>Bricht einen eigenen Import ab (wartend oder laufend).</summary>
    [HttpPost("imports/{id:int}/cancel")]
    [DisableRateLimiting]
    public async Task<IActionResult> CancelImport(int id)
    {
        var import = await OwnImportAsync(id);
        if (import is null) return NotFound();
        if (import.Status is ChessableImportStatus.Running or ChessableImportStatus.Paused)
        {
            import.Status = ChessableImportStatus.Cancelled;
            import.Error = "Vom Nutzer abgebrochen";
            import.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return Ok(ChessableImportQueueService.ToDto(import, 0));
    }

    /// <summary>Pausiert einen eigenen, laufenden/wartenden Import.</summary>
    [HttpPost("imports/{id:int}/pause")]
    [DisableRateLimiting]
    public async Task<IActionResult> PauseImport(int id)
    {
        var import = await OwnImportAsync(id);
        if (import is null) return NotFound();
        if (import.Status == ChessableImportStatus.Running)
        {
            import.Status = ChessableImportStatus.Paused;
            await _db.SaveChangesAsync();
        }
        return Ok(ChessableImportQueueService.ToDto(import, 0));
    }

    /// <summary>Setzt einen pausierten Import fort (wird wieder eingereiht).</summary>
    [HttpPost("imports/{id:int}/resume")]
    [DisableRateLimiting]
    public async Task<IActionResult> ResumeImport(int id)
    {
        var import = await OwnImportAsync(id);
        if (import is null) return NotFound();
        if (import.Status == ChessableImportStatus.Paused)
        {
            import.Status = ChessableImportStatus.Running;
            import.Phase = ChessableImportPhase.Queued;
            import.Attempts = 0;
            await _db.SaveChangesAsync();
            await _queue.EnqueueNextAsync();
        }
        return Ok(ChessableImportQueueService.ToDto(import, await _queue.QueuedAheadAsync(import)));
    }

    private async Task<ChessableImport?> OwnImportAsync(int id)
    {
        var userId = GetUserId();
        return await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);
    }

    private async Task<string?> LoadBearerAsync()
    {
        var userId = GetUserId();
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        return cred is null ? null : _encryption.TryDecrypt(cred.EncryptedBearer);
    }

    private static string Mask(string value)
    {
        // Nur die letzten 4 Zeichen zur Wiedererkennung zeigen — der Anfang des Bearers wird NICHT
        // mehr offengelegt (ein Bearer ist ein Geheimnis; minimale Preisgabe genügt fürs „ist gesetzt?").
        if (value.Length <= 4) return new string('*', value.Length);
        return new string('*', Math.Min(20, value.Length - 4)) + value[^4..];
    }
}
