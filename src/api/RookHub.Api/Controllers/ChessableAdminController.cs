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
/// Chessable-Integration (Admin-Sicht): Kurse „im Namen eines Users" ansehen/schätzen/importieren
/// sowie der globale Import-Verlauf/-Status aller User. Routen bleiben unter <c>api/chessable/admin/*</c>
/// (aus <see cref="ChessableController"/> ausgegliedert); geteilte Queue-/Import-Helfer liegen in
/// <see cref="ChessableImportQueueService"/>.
/// </summary>
[ApiController]
[Route("api/chessable")]
[Authorize(Roles = "Admin")]
// Wie der User-Controller vom globalen Minutenlimit ausnehmen (Kurs-Schwung/Polling).
[DisableRateLimiting]
public class ChessableAdminController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly ChessableProxyService _chessable;
    private readonly ChessableBearerBreaker _breaker;
    private readonly ChessableImportQueueService _queue;
    private readonly ILogger<ChessableAdminController> _logger;

    public ChessableAdminController(
        AppDbContext db,
        EncryptionService encryption,
        ChessableProxyService chessable,
        ChessableBearerBreaker breaker,
        ChessableImportQueueService queue,
        ILogger<ChessableAdminController> logger)
    {
        _db = db;
        _encryption = encryption;
        _chessable = chessable;
        _breaker = breaker;
        _queue = queue;
        _logger = logger;
    }

    /// <summary>ADMIN: Alle Importe ALLER User (Verlauf, neueste zuerst) inkl. Besitzer-Username.
    /// Laufende/pausierte bekommen ihre globale Warteschlangen-Position.</summary>
    [HttpGet("admin/imports")]
    public async Task<IActionResult> GetAllImportsAdmin()
    {
        var imports = await _db.ChessableImports
            .Include(i => i.User)
            .OrderByDescending(i => i.CreatedAt)
            .Take(200)
            .ToListAsync();
        var positions = await _queue.FairQueuePositionsAsync();
        return Ok(imports.Select(i => ChessableImportQueueService.ToAdminDto(i, positions.GetValueOrDefault(i.Id, 0))));
    }

    /// <summary>ADMIN: User, die einen Chessable-Bearer hinterlegt haben (für die „Kurse holen"-Auswahl).</summary>
    [HttpGet("admin/credentialed-users")]
    public async Task<IActionResult> GetCredentialedUsersAdmin()
    {
        var users = await _db.ChessableCredentials
            .Include(c => c.User)
            .OrderBy(c => c.User!.Username)
            .Select(c => new ChessableCredentialedUserDto(
                c.UserId, c.User!.Username, c.CoursesCachedAt, c.BlockedAt != null, c.BlockedReason))
            .ToListAsync();
        return Ok(users);
    }

    /// <summary>ADMIN: Kursliste eines beliebigen Users (mit dessen Bearer). Cache wie bei /courses;
    /// Import-Status wird gegen die EIGENEN (Admin-)Importe markiert.</summary>
    [HttpGet("admin/users/{userId:int}/courses")]
    public async Task<IActionResult> GetUserCoursesAdmin(int userId, [FromQuery] bool refresh, CancellationToken ct)
    {
        // Unbekannter User → 404 (statt der irreführenden „kein Bearer"-400; analog StartImportForUserAdmin).
        if (!await _db.AppUsers.AnyAsync(u => u.Id == userId, ct))
            return NotFound(new { message = "User not found" });
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (cred is null) return BadRequest(new { message = "User has no Chessable bearer saved" });

        if (!refresh && !string.IsNullOrEmpty(cred.CachedCoursesJson))
        {
            var cached = JsonSerializer.Deserialize<List<ChessableCourseDto>>(cred.CachedCoursesJson, ChessableImportQueueService.JsonOpts) ?? new();
            return Ok(new ChessableCoursesDto(await _queue.EnrichImportStateAsync(cached, GetUserId(), ct), cred.CoursesCachedAt));
        }
        // Frischer Abruf mit dem Bearer des Ziel-Users → bei offenem Breaker nicht anfragen.
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
            return Ok(new ChessableCoursesDto(await _queue.EnrichImportStateAsync(courses, GetUserId(), ct), cred.CoursesCachedAt));
        }
        catch (ChessableProxyException ex)
        {
            _logger.LogWarning("Admin Chessable courses (user {UserId}) failed: {Status} {Message}", userId, ex.Status, ex.Message);
            if (ChessableBearerBreaker.IsBearerFatal(ex.Message))
                await _breaker.TripAsync(userId, ex.Message, ct);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>ADMIN: Vorab-Schätzung der Gesamt-Linienzahl eines Kurses {bid} (mit dem Bearer des
    /// Users) — für die „~N Linien · ~M min"-Anzeige in der Kursliste vor dem Import. On-demand pro
    /// Kurs (ein getCourse-Call bzw. gratis aus dem Cache).</summary>
    [HttpGet("admin/users/{userId:int}/courses/{bid}/estimate")]
    public async Task<IActionResult> EstimateCourseAdmin(int userId, string bid, CancellationToken ct)
    {
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (cred is null) return BadRequest(new { message = "User has no Chessable bearer saved" });
        if (cred.BlockedAt is not null)
            return BadRequest(new { message = ChessableImportQueueService.BlockedMessage(cred.BlockedReason), blocked = true });
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
            if (ChessableBearerBreaker.IsBearerFatal(ex.Message))
                await _breaker.TripAsync(userId, ex.Message, ct);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>ADMIN: Testet den Bearer eines Users aktiv gegen Chessable — zugleich der „Reset" des
    /// Circuit-Breakers dieses Users (Erfolg ⇒ Breaker schließen + pausierte Importe aufnehmen; fatale
    /// Ablehnung ⇒ Breaker öffnen). Damit kann der Admin einen gesperrten Fremd-Bearer freigeben,
    /// ohne dass der betroffene User selbst aktiv werden muss.</summary>
    [HttpPost("admin/users/{userId:int}/test")]
    public async Task<IActionResult> TestUserBearerAdmin(int userId, CancellationToken ct)
    {
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (cred is null) return BadRequest(new { message = "User has no Chessable bearer saved" });
        var bearer = _encryption.TryDecrypt(cred.EncryptedBearer);
        if (bearer is null) return BadRequest(new { message = "Stored Chessable bearer could not be read — please re-enter it." });
        try
        {
            var result = await _chessable.TestAsync(bearer, ct);
            await _breaker.ClearAndResumeAsync(userId, ct);
            return Ok(result);
        }
        catch (ChessableProxyException ex)
        {
            _logger.LogWarning("Admin Chessable test (user {UserId}) failed: {Status} {Message}", userId, ex.Status, ex.Message);
            if (ChessableBearerBreaker.IsBearerFatal(ex.Message))
                await _breaker.TripAsync(userId, ex.Message, ct);
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>ADMIN: Lädt den Kurs {bid} eines Users (mit dessen Bearer) in das EIGENE (Admin-)Konto
    /// herunter — als Repertoire ("repertoire", Default) oder als Buch/Kurs ("book").
    /// Besitzer/Empfänger der Benachrichtigung = der aufrufende Admin; nur der Bearer stammt vom Ziel-User.</summary>
    [HttpPost("admin/users/{userId:int}/import/{bid}")]
    public async Task<IActionResult> StartImportForUserAdmin(int userId, string bid, [FromBody] AdminChessableImportRequest? request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bid))
            return BadRequest(new { message = "bid is required" });
        // Leeres Ziel ⇒ "repertoire" (Default + rückwärtskompatibel zu Clients ohne target).
        var target = string.IsNullOrWhiteSpace(request?.Target) ? "repertoire" : request!.Target!.Trim().ToLowerInvariant();
        if (target is not ("repertoire" or "book"))
            return BadRequest(new { message = "target must be 'repertoire' or 'book'" });
        if (!await _db.AppUsers.AnyAsync(u => u.Id == userId))
            return NotFound(new { message = "User not found" });
        var targetCred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        if (targetCred is null)
            return BadRequest(new { message = "User has no Chessable bearer saved" });
        // Bearer des Ziel-Users gesperrt → nicht einreihen (würde sofort pausieren).
        // Vor der (ggf. fetchenden) Eigentumsprüfung, damit ein toter Bearer keinen Request auslöst.
        if (targetCred.BlockedAt is not null)
            return BadRequest(new { message = ChessableImportQueueService.BlockedMessage(targetCred.BlockedReason), blocked = true });
        // Auch hier: nur Kurse, die in der Bibliothek des ZIEL-Users liegen (dessen Bearer fetcht/cached).
        if (!await _queue.UserOwnsCourseAsync(targetCred, bid, ct))
            return StatusCode(403, new { message = "Dieser Kurs ist nicht in der Chessable-Bibliothek des Users." });

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
            await _queue.EnqueueNextAsync();
        return Accepted(ChessableImportQueueService.ToDto(import, await _queue.QueuedAheadAsync(import)));
    }

    /// <summary>ADMIN: Nur die aktiven (laufenden/pausierten) Importe aller User — fürs Dashboard-Widget.</summary>
    [HttpGet("admin/active")]
    public async Task<IActionResult> GetActiveImportsAdmin()
    {
        var active = await _db.ChessableImports
            .Include(i => i.User)
            .Where(i => i.Status == "running" || i.Status == "paused")
            .ToListAsync();
        var positions = await _queue.FairQueuePositionsAsync();
        // Anzeige in fairer Verarbeitungsreihenfolge: gerade laufende/holende zuerst (nicht in der
        // Positions-Map), dann die wartenden in fairer Reihenfolge (Round-Robin über die User),
        // pausierte zuletzt. So liest das Widget top-down genau so, wie abgearbeitet wird.
        var ordered = active
            .OrderBy(i => i.Status == "paused" ? 2 : positions.ContainsKey(i.Id) ? 1 : 0)
            .ThenBy(i => positions.GetValueOrDefault(i.Id, 0))
            .ThenBy(i => i.CreatedAt)
            .ToList();
        return Ok(ordered.Select(i => ChessableImportQueueService.ToAdminDto(i, positions.GetValueOrDefault(i.Id, 0))));
    }
}
