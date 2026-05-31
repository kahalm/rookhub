using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class MeController : BaseApiController
{
    private readonly AppDbContext _db;
    public MeController(AppDbContext db) => _db = db;

    /// <summary>Gruppen-Namen des aktuell eingeloggten Users — Basis für gruppenabhängige Anzeige.</summary>
    [HttpGet("my-groups")]
    public async Task<IActionResult> MyGroups()
    {
        var userId = GetUserId();
        var groups = await _db.UserGroups
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.Group!.Name)
            .OrderBy(n => n)
            .ToListAsync();
        return Ok(groups);
    }
}
