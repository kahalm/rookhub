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
    private readonly ILogger<ChessableController> _logger;

    public ChessableController(
        AppDbContext db,
        EncryptionService encryption,
        ChessableProxyService chessable,
        ILogger<ChessableController> logger)
    {
        _db = db;
        _encryption = encryption;
        _chessable = chessable;
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
            _logger.LogInformation("Chessable test failed: {Status} {Message}", ex.Status, ex.Message);
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
            _logger.LogInformation("Chessable courses failed: {Status} {Message}", ex.Status, ex.Message);
            return BadRequest(new { message = ex.Message });
        }
    }

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
