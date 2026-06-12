using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        var plain = _encryption.Decrypt(cred.EncryptedBearer);
        return Ok(new ChessableCredentialResponse(true, Mask(plain)));
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
            var bearer = _encryption.Decrypt(cred.EncryptedBearer);
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
        return courses
            .Select(c => c with { ImportedRepertoire = rep.Contains(c.Bid), ImportedBook = book.Contains(c.Bid) })
            .ToList();
    }

    /// <summary>
    /// Startet einen asynchronen Import des Chessable-Kurses {bid} — als persönliches Repertoire
    /// ("repertoire", jeder User) oder als persönliches Buch/Kurs ("book"). Läuft im Hintergrund;
    /// das Frontend pollt GET /api/chessable/imports/{id}.
    /// </summary>
    [HttpPost("courses/{bid}/import")]
    public async Task<IActionResult> StartImport(string bid, [FromBody] StartChessableImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(bid))
            return BadRequest(new { message = "bid is required" });
        var target = (request?.Target ?? "").Trim().ToLowerInvariant();
        if (target is not ("repertoire" or "book"))
            return BadRequest(new { message = "target must be 'repertoire' or 'book'" });

        var userId = GetUserId();
        if (!await _db.ChessableCredentials.AnyAsync(c => c.UserId == userId))
            return BadRequest(new { message = "No Chessable bearer saved" });

        var import = new ChessableImport
        {
            UserId = userId,
            Bid = bid,
            CourseName = string.IsNullOrWhiteSpace(request?.Name) ? "" : request!.Name!.Trim(),
            Target = target,
            Status = "running",
            CreatedAt = DateTime.UtcNow
        };
        _db.ChessableImports.Add(import);
        await _db.SaveChangesAsync();

        await EnqueueRunAsync(import.Id);
        return Accepted(ToDto(import, await QueuedAheadAsync(import)));
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
        var list = await _db.ChessableImports
            .Where(i => i.UserId == userId)
            .OrderByDescending(i => i.CreatedAt)
            .Take(20)
            .ToListAsync();
        var runningIds = await _db.ChessableImports.Where(i => i.Status == "running").Select(i => i.Id).ToListAsync();
        return Ok(list.Select(i => ToDto(i, i.Status == "running" ? runningIds.Count(id => id < i.Id) : 0)));
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
            await EnqueueRunAsync(import.Id);
        }
        return Ok(ToDto(import, await QueuedAheadAsync(import)));
    }

    private async Task<ChessableImport?> OwnImportAsync(int id)
    {
        var userId = GetUserId();
        return await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);
    }

    private async Task EnqueueRunAsync(int importId)
    {
        await _taskQueue.EnqueueAsync(async (sp, ct) =>
        {
            var svc = sp.GetRequiredService<ChessableImportService>();
            await svc.RunAsync(importId, ct);
        });
    }

    /// <summary>Globale Warteschlangen-Position: Anzahl laufender/wartender Importe (aller User) davor.</summary>
    private async Task<int> QueuedAheadAsync(ChessableImport i)
        => i.Status == "running"
            ? await _db.ChessableImports.CountAsync(x => x.Status == "running" && x.Id < i.Id)
            : 0;

    private static ChessableImportDto ToDto(ChessableImport i, int queuedAhead) => new(
        i.Id, i.Bid, i.CourseName, i.Target, i.Status, i.Phase, i.Error, i.ResultId, i.Imported, i.Skipped, i.Invalid,
        i.ChaptersDone, i.ChaptersTotal, i.LinesDone, queuedAhead);

    private async Task<string?> LoadBearerAsync()
    {
        var userId = GetUserId();
        var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
        return cred is null ? null : _encryption.Decrypt(cred.EncryptedBearer);
    }

    private static string Mask(string value)
    {
        if (value.Length <= 8) return new string('*', value.Length);
        return value[..4] + new string('*', Math.Min(20, value.Length - 8)) + value[^4..];
    }
}
