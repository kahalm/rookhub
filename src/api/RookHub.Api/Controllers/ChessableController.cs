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

    [HttpGet("courses")]
    public async Task<IActionResult> Courses(CancellationToken ct)
    {
        var bearer = await LoadBearerAsync();
        if (bearer is null) return BadRequest(new { message = "No Chessable bearer saved" });

        try
        {
            var courses = await _chessable.GetCoursesAsync(bearer, ct);
            return Ok(courses);
        }
        catch (ChessableProxyException ex)
        {
            _logger.LogWarning("Chessable courses failed: {Status} {Message}", ex.Status, ex.Message);
            return BadRequest(new { message = ex.Message });
        }
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

        var importId = import.Id;
        await _taskQueue.EnqueueAsync(async (sp, ct) =>
        {
            var svc = sp.GetRequiredService<ChessableImportService>();
            await svc.RunAsync(importId, ct);
        });

        return Accepted(ToDto(import));
    }

    /// <summary>Status/Fortschritt eines Imports (Polling bis status != "running").</summary>
    [HttpGet("imports/{id:int}")]
    public async Task<IActionResult> GetImport(int id)
    {
        var userId = GetUserId();
        var import = await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);
        if (import is null) return NotFound();
        return Ok(ToDto(import));
    }

    /// <summary>Die letzten Importe des Users (Verlauf).</summary>
    [HttpGet("imports")]
    public async Task<IActionResult> GetImports()
    {
        var userId = GetUserId();
        var list = await _db.ChessableImports
            .Where(i => i.UserId == userId)
            .OrderByDescending(i => i.CreatedAt)
            .Take(20)
            .ToListAsync();
        return Ok(list.Select(ToDto));
    }

    private static ChessableImportDto ToDto(ChessableImport i) => new(
        i.Id, i.Bid, i.CourseName, i.Target, i.Status, i.Phase, i.Error, i.ResultId, i.Imported, i.Skipped, i.Invalid);

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
