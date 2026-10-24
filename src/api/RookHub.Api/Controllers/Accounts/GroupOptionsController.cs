using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Authorization;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Accounts;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;

namespace RookHub.Api.Controllers.Accounts;

/// <summary>
/// Gruppen als AUSWAHLLISTE für alle, die Gruppen nur zuordnen (F5-005-Nachlauf): Katalog-Freigaben (catalog.manage),
/// Menü-Sichtbarkeit (menu.manage), Gruppenrollen (roles.manage) und Bücher-Freigaben (books.manage). <see cref="GroupController"/>
/// hängt als Ganzes an groups.manage (Mitglieder, Anlegen, Löschen) — eine Rolle mit nur <c>menu.manage</c> bekam dort 403
/// und eine leere Auswahl. Eigener Controller, weil sich ein Klassen-<c>[HasPermission]</c> nicht je Aktion lockern lässt;
/// „eines von" erledigt <see cref="HasPermissionAttribute"/> mit mehreren Rechten.
/// </summary>
[ApiController]
[Route("api/admin/groups/options")]
[HasPermission(Permissions.GroupsManage, Permissions.CatalogManage, Permissions.MenuManage, Permissions.RolesManage, Permissions.BooksManage)]
public class GroupOptionsController(AppDbContext db) : BaseApiController
{
    /// <summary>Alle Gruppen, „Everyone" zuerst, dann nach Name — Id, Name, IsEveryone und die Mitgliederzahl (der
    /// Rollen-Tab zeigt sie an der Gruppe), aber keine Mitglieder selbst.</summary>
    [HttpGet]
    public async Task<ActionResult<List<GroupOptionDto>>> Get(CancellationToken ct)
    {
        var groups = await db.Groups.AsNoTracking()
            .OrderByDescending(g => g.IsEveryone).ThenBy(g => g.Name)
            .Select(g => new { g.Id, g.Name, g.IsEveryone })
            .ToListAsync(ct);
        var counts = await GroupMemberCounts.ComputeAsync(db, ct);
        return Ok(groups.Select(g => new GroupOptionDto(g.Id, g.Name, g.IsEveryone, counts.GetValueOrDefault(g.Id))).ToList());
    }
}
