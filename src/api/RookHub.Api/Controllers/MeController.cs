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
            .ToListAsync();
        // Jeder Nutzer ist implizit Mitglied der System-Gruppe „Everyone".
        var everyoneName = await _db.Groups.Where(g => g.IsEveryone).Select(g => g.Name).FirstOrDefaultAsync();
        if (everyoneName != null && !groups.Contains(everyoneName)) groups.Add(everyoneName);
        groups.Sort(StringComparer.Ordinal);
        return Ok(groups);
    }
}
