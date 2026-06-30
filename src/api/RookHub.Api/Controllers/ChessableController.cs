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
/// Chessable-Integration: speichert den User-Bearer verschluesselt in der
/// rookhub-DB und reicht ihn fuer Lese-Operationen (test, courses) per
/// <see cref="ChessableProxyService"/> an die piratechess-API durch. Die
/// eigentlichen Chessable-Calls (curl-impersonate) liegen vollstaendig in
/// piratechess; RookHub haelt nur den Token + UI.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
// Vom globalen 100-Req/min-pro-IP-Limit ausnehmen: Das Chessable-UI pollt laufende Importe und
// reiht Kurse oft im Schwung ein — beides erschöpfte sonst das Minutenfenster (429 beim ~16. Add).
// Endpoints sind ohnehin per [Authorize] geschützt.
[DisableRateLimiting]
public class ChessableController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly ChessableProxyService _chessable;
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly ILogger<ChessableController> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ChessableController(
        AppDbContext db,
        EncryptionService encryption,
        ChessableProxyService chessable,
        IBackgroundTaskQueue taskQueue,
        ILogger<ChessableController> logger)
    {
        _db = db;
        _encryption = encryption;
        _chessable = chessable;
        _taskQueue = taskQueue;
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
        return Ok(new ChessableCredentialResponse(true, plain is null ? null : Mask(plain)));
    }

    [HttpPost("credentials")]
    public async Task<IActionResult> SaveCredentials([FromBody] SaveChessableBearerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Bearer))
            return BadRequest(new { message = "Bearer is required" });

        var userId = GetUserId();
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        var now = DateTime.UtcNow;

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
            // Bearer gewechselt → gecachte Kursliste verwerfen (kann zu anderem Account gehören).
            cred.CachedCoursesJson = null;
            cred.CoursesCachedAt = null;
        }

        await _db.SaveChangesAsync();
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

    [HttpPost("test")]
    public async Task<IActionResult> Test(CancellationToken ct)
    {
        var bearer = await LoadBearerAsync();
        if (bearer is null) return BadRequest(new { message = "No Chessable bearer saved" });

        try
        {
            var result = await _chessable.TestAsync(bearer, ct);
            return Ok(result);
        }
        catch (ChessableProxyException ex)
        {
            _logger.LogWarning("Chessable test failed: {Status} {Message}", ex.Status, ex.Message);
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
            var cached = JsonSerializer.Deserialize<List<ChessableCourseDto>>(cred.CachedCoursesJson, JsonOpts) ?? new();
            return Ok(new ChessableCoursesDto(await EnrichImportStateAsync(cached, userId, ct), cred.CoursesCachedAt));
        }

        try
        {
            var bearer = _encryption.TryDecrypt(cred.EncryptedBearer);
            if (bearer is null)
                return BadRequest(new { message = "Stored Chessable bearer could not be read — please re-enter it." });
            var courses = await _chessable.GetCoursesAsync(bearer, ct);
            cred.CachedCoursesJson = JsonSerializer.Serialize(courses, JsonOpts);
            cred.CoursesCachedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return Ok(new ChessableCoursesDto(await EnrichImportStateAsync(courses, userId, ct), cred.CoursesCachedAt));
        }
        catch (ChessableProxyException ex)
        {
            _logger.LogWarning("Chessable courses failed: {Status} {Message}", ex.Status, ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Markiert je Kurs, ob er vom User bereits als Repertoire bzw. Buch importiert wurde
    /// (Quelle: abgeschlossene ChessableImports) — Basis fürs Ausblenden der erledigten Buttons.</summary>
    private async Task<List<ChessableCourseDto>> EnrichImportStateAsync(List<ChessableCourseDto> courses, int userId, CancellationToken ct)
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

    /// <summary>
    /// Startet einen asynchronen Import des Chessable-Kurses {bid} — als persönliches Repertoire
    /// ("repertoire", jeder User) oder als persönliches Buch/Kurs ("book"). Läuft im Hintergrund;
    /// das Frontend pollt GET /api/chessable/imports/{id}.
    /// </summary>
    [HttpPost("courses/{bid}/import")]
    public async Task<IActionResult> StartImport(string bid, [FromBody] StartChessableImportRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bid))
            return BadRequest(new { message = "bid is required" });
        var target = (request?.Target ?? "").Trim().ToLowerInvariant();
        if (target is not ("repertoire" or "book"))
            return BadRequest(new { message = "target must be 'repertoire' or 'book'" });

        var userId = GetUserId();
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        if (cred is null)
            return BadRequest(new { message = "No Chessable bearer saved" });

        // SICHERHEIT: Nur Kurse importieren, die WIRKLICH in der Chessable-Bibliothek dieses Users liegen.
        // Andernfalls könnte ein User einen beliebigen (öffentlich aus der chessable.com-URL bekannten)
        // Kurs-bid importieren — und für bereits GECACHTE Kurse umgeht die piratechess-Seite die
        // Chessable-Eigentumsprüfung (liefert den Cache-Inhalt direkt, ohne den Bearer gegen Chessable
        // zu validieren). Der Check hier schließt diesen Content-Bypass für ALLE Lanes (cached + fetch).
        if (!await UserOwnsCourseAsync(cred, bid, ct))
            return StatusCode(403, new { message = "Dieser Kurs ist nicht in deiner Chessable-Bibliothek." });

        // Round-Robin-Runde einfrieren: wie viele Importe dieses Users sind GERADE schon aktiv?
        // (0 = erster ⇒ Runde 0). Bestimmt die faire Position; siehe ChessableImportService.FairOrder.
        var queueRound = await _db.ChessableImports.CountAsync(x => x.UserId == userId && x.Status == "running");
        var import = new ChessableImport
        {
            UserId = userId,
            Bid = bid,
            CourseName = string.IsNullOrWhiteSpace(request?.Name) ? "" : request!.Name!.Trim(),
            Target = target,
            Status = "running",
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
            await EnqueueNextAsync();
        return Accepted(ToDto(import, await QueuedAheadAsync(import)));
    }

    /// <summary>Prüft, ob <paramref name="bid"/> in der Chessable-Bibliothek zum Bearer von
    /// <paramref name="cred"/> liegt. Erst gegen die gecachte Kursliste (schnell, kein Chessable-Call);
    /// fehlt der bid dort, wird die Liste EINMAL frisch geladen (deckt frisch gekaufte Kurse / leeren
    /// Cache ab) und der Cache aktualisiert. Nicht verifizierbar (Bearer kaputt / Chessable-Fehler) ⇒
    /// fail-closed (kein Import).</summary>
    private async Task<bool> UserOwnsCourseAsync(ChessableCredential cred, string bid, CancellationToken ct)
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

    /// <summary>Status/Fortschritt eines Imports (Polling bis status != "running").</summary>
    [HttpGet("imports/{id:int}")]
    public async Task<IActionResult> GetImport(int id)
    {
        var userId = GetUserId();
        var import = await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);
        if (import is null) return NotFound();
        return Ok(ToDto(import, await QueuedAheadAsync(import)));
    }

    /// <summary>Die letzten Importe des Users (Verlauf + laufende/wartende mit globaler Position).</summary>
    [HttpGet("imports")]
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
            .Where(i => i.UserId == userId && (i.Status == "running" || i.Status == "paused"))
            .ToListAsync();
        var list = recent.UnionBy(active, i => i.Id)
            .OrderByDescending(i => i.CreatedAt)
            .ToList();
        var positions = await FairQueuePositionsAsync();
        return Ok(list.Select(i => ToDto(i, positions.GetValueOrDefault(i.Id, 0))));
    }

    /// <summary>ADMIN: Alle Importe ALLER User (Verlauf, neueste zuerst) inkl. Besitzer-Username.
    /// Laufende/pausierte bekommen ihre globale Warteschlangen-Position.</summary>
    [HttpGet("admin/imports")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAllImportsAdmin()
    {
        var imports = await _db.ChessableImports
            .Include(i => i.User)
            .OrderByDescending(i => i.CreatedAt)
            .Take(200)
            .ToListAsync();
        var positions = await FairQueuePositionsAsync();
        return Ok(imports.Select(i => ToAdminDto(i, positions.GetValueOrDefault(i.Id, 0))));
    }

    /// <summary>ADMIN: User, die einen Chessable-Bearer hinterlegt haben (für die „Kurse holen"-Auswahl).</summary>
    [HttpGet("admin/credentialed-users")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetCredentialedUsersAdmin()
    {
        var users = await _db.ChessableCredentials
            .Include(c => c.User)
            .OrderBy(c => c.User!.Username)
            .Select(c => new ChessableCredentialedUserDto(c.UserId, c.User!.Username, c.CoursesCachedAt))
            .ToListAsync();
        return Ok(users);
    }

    /// <summary>ADMIN: Kursliste eines beliebigen Users (mit dessen Bearer). Cache wie bei /courses;
    /// Import-Status wird gegen die EIGENEN (Admin-)Importe markiert.</summary>
    [HttpGet("admin/users/{userId:int}/courses")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetUserCoursesAdmin(int userId, [FromQuery] bool refresh, CancellationToken ct)
    {
        // Unbekannter User → 404 (statt der irreführenden „kein Bearer"-400; analog StartImportForUserAdmin).
        if (!await _db.AppUsers.AnyAsync(u => u.Id == userId, ct))
            return NotFound(new { message = "User not found" });
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (cred is null) return BadRequest(new { message = "User has no Chessable bearer saved" });

        if (!refresh && !string.IsNullOrEmpty(cred.CachedCoursesJson))
        {
            var cached = JsonSerializer.Deserialize<List<ChessableCourseDto>>(cred.CachedCoursesJson, JsonOpts) ?? new();
            return Ok(new ChessableCoursesDto(await EnrichImportStateAsync(cached, GetUserId(), ct), cred.CoursesCachedAt));
        }
        try
        {
            var bearer = _encryption.TryDecrypt(cred.EncryptedBearer);
            if (bearer is null)
                return BadRequest(new { message = "Stored Chessable bearer could not be read — please re-enter it." });
            var courses = await _chessable.GetCoursesAsync(bearer, ct);
            cred.CachedCoursesJson = JsonSerializer.Serialize(courses, JsonOpts);
            cred.CoursesCachedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return Ok(new ChessableCoursesDto(await EnrichImportStateAsync(courses, GetUserId(), ct), cred.CoursesCachedAt));
        }
        catch (ChessableProxyException ex)
        {
            _logger.LogWarning("Admin Chessable courses (user {UserId}) failed: {Status} {Message}", userId, ex.Status, ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>ADMIN: Vorab-Schätzung der Gesamt-Linienzahl eines Kurses {bid} (mit dem Bearer des
    /// Users) — für die „~N Linien · ~M min"-Anzeige in der Kursliste vor dem Import. On-demand pro
    /// Kurs (ein getCourse-Call bzw. gratis aus dem Cache).</summary>
    [HttpGet("admin/users/{userId:int}/courses/{bid}/estimate")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> EstimateCourseAdmin(int userId, string bid, CancellationToken ct)
    {
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (cred is null) return BadRequest(new { message = "User has no Chessable bearer saved" });
        var bearer = _encryption.TryDecrypt(cred.EncryptedBearer);
        if (bearer is null) return BadRequest(new { message = "Stored Chessable bearer could not be read — please re-enter it." });
        try
        {
            var info = await _chessable.GetCourseInfoAsync(bearer, bid, ct);
            if (info is null) return BadRequest(new { message = "Could not estimate the course size." });
            return Ok(info);
        }
        catch (ChessableProxyException ex)
        {
            _logger.LogWarning("Admin course estimate (user {UserId}, bid {Bid}) failed: {Status} {Message}", userId, bid, ex.Status, ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>ADMIN: Lädt den Kurs {bid} eines Users (mit dessen Bearer) in das EIGENE (Admin-)Konto
    /// herunter — als Repertoire ("repertoire", Default) oder als Buch/Kurs ("book").
    /// Besitzer/Empfänger der Benachrichtigung = der aufrufende Admin; nur der Bearer stammt vom Ziel-User.</summary>
    [HttpPost("admin/users/{userId:int}/import/{bid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> StartImportForUserAdmin(int userId, string bid, [FromBody] AdminChessableImportRequest? request)
    {
        if (string.IsNullOrWhiteSpace(bid))
            return BadRequest(new { message = "bid is required" });
        // Leeres Ziel ⇒ "repertoire" (Default + rückwärtskompatibel zu Clients ohne target).
        var target = string.IsNullOrWhiteSpace(request?.Target) ? "repertoire" : request!.Target!.Trim().ToLowerInvariant();
        if (target is not ("repertoire" or "book"))
            return BadRequest(new { message = "target must be 'repertoire' or 'book'" });
        if (!await _db.AppUsers.AnyAsync(u => u.Id == userId))
            return NotFound(new { message = "User not found" });
        if (!await _db.ChessableCredentials.AnyAsync(c => c.UserId == userId))
            return BadRequest(new { message = "User has no Chessable bearer saved" });

        var adminId = GetUserId();
        var queueRound = await _db.ChessableImports.CountAsync(x => x.UserId == adminId && x.Status == "running");
        var import = new ChessableImport
        {
            UserId = adminId,          // Ergebnis (Repertoire/Buch) + Benachrichtigung gehören dem Admin
            BearerUserId = userId,     // gefetcht wird mit dem Bearer des Ziel-Users
            Bid = bid,
            CourseName = string.IsNullOrWhiteSpace(request?.Name) ? "" : request!.Name!.Trim(),
            Target = target,
            Status = "running",
            QueueRound = queueRound,
            CreatedAt = DateTime.UtcNow
        };
        _db.ChessableImports.Add(import);
        await _db.SaveChangesAsync();

        // Lane-Klassifikation: voll-gecachte Kurse laufen in der schnellen, netzfreien Fast-Lane
        // (eigener Drain-Service, seriell), alles andere als Download (Queue-Ticket). Der Cache-Check
        // ist piratechess-DB-lokal (kein Chessable-Abruf).
        import.FullyCached = await _chessable.IsCourseCachedAsync(bid);
        await _db.SaveChangesAsync();
        if (import.FullyCached != true)
            await EnqueueNextAsync();
        return Accepted(ToDto(import, await QueuedAheadAsync(import)));
    }

    /// <summary>ADMIN: Nur die aktiven (laufenden/pausierten) Importe aller User — fürs Dashboard-Widget.</summary>
    [HttpGet("admin/active")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetActiveImportsAdmin()
    {
        var active = await _db.ChessableImports
            .Include(i => i.User)
            .Where(i => i.Status == "running" || i.Status == "paused")
            .ToListAsync();
        var positions = await FairQueuePositionsAsync();
        // Anzeige in fairer Verarbeitungsreihenfolge: gerade laufende/holende zuerst (nicht in der
        // Positions-Map), dann die wartenden in fairer Reihenfolge (Round-Robin über die User),
        // pausierte zuletzt. So liest das Widget top-down genau so, wie abgearbeitet wird.
        var ordered = active
            .OrderBy(i => i.Status == "paused" ? 2 : positions.ContainsKey(i.Id) ? 1 : 0)
            .ThenBy(i => positions.GetValueOrDefault(i.Id, 0))
            .ThenBy(i => i.CreatedAt)
            .ToList();
        return Ok(ordered.Select(i => ToAdminDto(i, positions.GetValueOrDefault(i.Id, 0))));
    }

    /// <summary>Bricht einen eigenen Import ab (wartend oder laufend).</summary>
    [HttpPost("imports/{id:int}/cancel")]
    public async Task<IActionResult> CancelImport(int id)
    {
        var import = await OwnImportAsync(id);
        if (import is null) return NotFound();
        if (import.Status is "running" or "paused")
        {
            import.Status = "cancelled";
            import.Error = "Vom Nutzer abgebrochen";
            import.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return Ok(ToDto(import, 0));
    }

    /// <summary>Pausiert einen eigenen, laufenden/wartenden Import.</summary>
    [HttpPost("imports/{id:int}/pause")]
    public async Task<IActionResult> PauseImport(int id)
    {
        var import = await OwnImportAsync(id);
        if (import is null) return NotFound();
        if (import.Status == "running")
        {
            import.Status = "paused";
            await _db.SaveChangesAsync();
        }
        return Ok(ToDto(import, 0));
    }

    /// <summary>Setzt einen pausierten Import fort (wird wieder eingereiht).</summary>
    [HttpPost("imports/{id:int}/resume")]
    public async Task<IActionResult> ResumeImport(int id)
    {
        var import = await OwnImportAsync(id);
        if (import is null) return NotFound();
        if (import.Status == "paused")
        {
            import.Status = "running";
            import.Phase = "queued";
            import.Attempts = 0;
            await _db.SaveChangesAsync();
            await EnqueueNextAsync();
        }
        return Ok(ToDto(import, await QueuedAheadAsync(import)));
    }

    private async Task<ChessableImport?> OwnImportAsync(int id)
    {
        var userId = GetUserId();
        return await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);
    }

    /// <summary>Reiht ein Ticket ein, das den fair als Nächstes dran befindlichen Import verarbeitet
    /// (Round-Robin über die User), nicht zwingend den gerade angelegten — siehe
    /// <see cref="ChessableImportService.RunNextAsync"/>.</summary>
    private async Task EnqueueNextAsync()
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
    private async Task<Dictionary<int, int>> FairQueuePositionsAsync()
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
    private async Task<int> QueuedAheadAsync(ChessableImport i)
    {
        if (i.Status != "running" || i.Phase != "queued") return 0;
        return (await FairQueuePositionsAsync()).GetValueOrDefault(i.Id, 0);
    }

    private static ChessableImportDto ToDto(ChessableImport i, int queuedAhead) => new(
        i.Id, i.Bid, i.CourseName, i.Target, i.Status, i.Phase, i.Error, i.ResultId, i.Imported, i.Skipped, i.Invalid,
        i.ChaptersDone, i.ChaptersTotal, i.LinesDone, i.LinesTotal, queuedAhead, i.CreatedAt, i.StartedAt, i.CompletedAt);

    private static ChessableAdminImportDto ToAdminDto(ChessableImport i, int queuedAhead) => new(
        i.Id, i.UserId, i.User?.Username ?? "?", i.Bid, i.CourseName, i.Target, i.Status, i.Phase, i.Error,
        i.ResultId, i.Imported, i.Skipped, i.Invalid, i.ChaptersDone, i.ChaptersTotal, i.LinesDone, i.LinesTotal, queuedAhead,
        i.CreatedAt, i.StartedAt, i.CompletedAt);

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
