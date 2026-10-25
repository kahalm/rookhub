using Microsoft.AspNetCore.Authorization;
using RookHub.Api.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Social;
using RookHub.Api.DTOs.Accounts;
using RookHub.Api.DTOs.Social;

namespace RookHub.Api.Controllers.Accounts;

[ApiController]
[Route("api/admin/groups")]
[HasPermission(Permissions.GroupsManage)]
public class GroupController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly TrainingGoalService _goals;
    public GroupController(AppDbContext db, TrainingGoalService goals)
    {
        _db = db;
        _goals = goals;
    }

    [HttpGet]
    public async Task<IActionResult> GetGroups()
    {
        var groups = await _db.Groups
            .OrderByDescending(g => g.IsEveryone)
            .ThenBy(g => g.Name)
            .Select(g => new GroupDto
            {
                Id = g.Id,
                Name = g.Name,
                Description = g.Description,
                CreatedAt = g.CreatedAt,
                IsEveryone = g.IsEveryone,
            })
            .ToListAsync();
        // Mitglieder zählen MIT den geerbten aus enthaltenen Gruppen (jedes Konto einmal; „Everyone" = alle Konten).
        var counts = await GroupMemberCounts.ComputeAsync(_db);
        var links = await _db.GroupInclusions.AsNoTracking().ToListAsync();
        foreach (var g in groups)
        {
            g.MemberCount = counts.GetValueOrDefault(g.Id);
            g.IncludedGroupIds = links.Where(l => l.GroupId == g.Id).Select(l => l.IncludedGroupId).OrderBy(i => i).ToList();
        }
        return Ok(groups);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateGroupDto dto)
    {
        var name = (dto.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            return BadRequest(new { message = "Name is required." });
        if (await _db.Groups.AnyAsync(g => g.Name == name))
            return BadRequest(new { message = "Group name already exists.", code = ApiErrorCodes.GroupNameTaken });

        var group = new Group { Name = name, Description = dto.Description, CreatedAt = DateTime.UtcNow };
        _db.Groups.Add(group);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Race: paralleler Create mit gleichem Namen -> Unique-Index -> sauberer 400 statt 500.
            return BadRequest(new { message = "Group name already exists.", code = ApiErrorCodes.GroupNameTaken });
        }
        return Ok(new GroupDto { Id = group.Id, Name = group.Name, Description = group.Description, MemberCount = 0, CreatedAt = group.CreatedAt });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateGroupDto dto)
    {
        var group = await _db.Groups.FindAsync(id);
        if (group == null)
            return NotFound(new { message = "Group not found." });
        if (group.IsEveryone)
            return BadRequest(new { message = "The Everyone group cannot be modified." });

        if (!string.IsNullOrWhiteSpace(dto.Name))
        {
            var name = dto.Name.Trim();
            if (await _db.Groups.AnyAsync(g => g.Name == name && g.Id != id))
                return BadRequest(new { message = "Group name already exists.", code = ApiErrorCodes.GroupNameTaken });
            group.Name = name;
        }
        if (dto.Description != null) group.Description = dto.Description;
        await _db.SaveChangesAsync();

        var count = await GroupMembership.CountAsync(_db, id);
        var included = await _db.GroupInclusions.Where(l => l.GroupId == id).Select(l => l.IncludedGroupId).OrderBy(i => i).ToListAsync();
        return Ok(new GroupDto { Id = group.Id, Name = group.Name, Description = group.Description, MemberCount = count,
            CreatedAt = group.CreatedAt, IncludedGroupIds = included });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var group = await _db.Groups.FindAsync(id);
        if (group == null)
            return NotFound(new { message = "Group not found." });
        if (group.IsEveryone)
            return BadRequest(new { message = "The Everyone group cannot be deleted." });
        // Mitgliedschaften + Buch-Freigaben + Ziel-Vorlage explizit entfernen (FK-Cascade greift bei InMemory nicht).
        _db.UserGroups.RemoveRange(_db.UserGroups.Where(ug => ug.GroupId == id));
        _db.BookGroupAccesses.RemoveRange(_db.BookGroupAccesses.Where(a => a.GroupId == id));
        _db.GroupTrainingGoals.RemoveRange(_db.GroupTrainingGoals.Where(g => g.GroupId == id));
        _db.GroupRoles.RemoveRange(_db.GroupRoles.Where(gr => gr.GroupId == id));
        _db.GroupInclusions.RemoveRange(_db.GroupInclusions.Where(l => l.GroupId == id || l.IncludedGroupId == id));
        _db.Groups.Remove(group);
        await _db.SaveChangesAsync();
        await GroupMembership.RebuildClosureAsync(_db);   // invalidiert auch die Rechte
        return NoContent();
    }

    [HttpGet("{id}/members")]
    public async Task<IActionResult> GetMembers(int id)
    {
        var group = await _db.Groups.FindAsync(id);
        if (group == null)
            return NotFound(new { message = "Group not found." });
        // „Everyone": jeder Nutzer ist implizit Mitglied → alle Nutzer zurückgeben (keine expliziten Zeilen).
        if (group.IsEveryone)
        {
            var all = await _db.AppUsers
                .Select(u => new GroupMemberDto { UserId = u.Id, Username = u.Username })
                .OrderBy(m => m.Username)
                .ToListAsync();
            return Ok(all);
        }
        var direct = await _db.UserGroups
            .Where(ug => ug.GroupId == id)
            .Select(ug => new GroupMemberDto { UserId = ug.UserId, Username = ug.User!.Username })
            .ToListAsync();
        // Geerbte: Mitglieder aller (auch mittelbar) enthaltenen Gruppen, mit den Gruppennamen, über die sie kommen.
        var inherited = await (from c in _db.GroupClosures
                               where c.AncestorId == id
                               join ug in _db.UserGroups on c.DescendantId equals ug.GroupId
                               select new { ug.UserId, ug.User!.Username, Via = ug.Group!.Name }).ToListAsync();
        var byId = direct.ToDictionary(m => m.UserId);
        foreach (var row in inherited)
        {
            if (!byId.TryGetValue(row.UserId, out var m))
                byId[row.UserId] = m = new GroupMemberDto { UserId = row.UserId, Username = row.Username, Direct = false };
            if (!m.Via.Contains(row.Via)) m.Via.Add(row.Via);
        }
        foreach (var m in byId.Values) m.Via.Sort(StringComparer.OrdinalIgnoreCase);
        return Ok(byId.Values.OrderBy(m => m.Username).ToList());
    }

    [HttpPost("{id}/members/{userId}")]
    public async Task<IActionResult> AddMember(int id, int userId)
    {
        var group = await _db.Groups.FindAsync(id);
        if (group == null)
            return NotFound(new { message = "Group not found." });
        if (group.IsEveryone)
            return BadRequest(new { message = "Everyone group membership is implicit and cannot be edited." });
        if (!await _db.AppUsers.AnyAsync(u => u.Id == userId))
            return NotFound(new { message = "User not found." });
        if (!await _db.UserGroups.AnyAsync(ug => ug.GroupId == id && ug.UserId == userId))
        {
            // N9-004: das Mitglied bekommt sofort die Rollen der Gruppe (und der umfassenden) — nur wer diese Rechte
            // selbst hat (oder Admin ist), darf es eintragen; sonst 403 permission_not_held.
            await PermissionGrantGuard.EnsureCanGrantAsync(_db, GetUserIdOrNull(), await PermissionGrantGuard.PermissionsGrantedByGroupAsync(_db, id));
            _db.UserGroups.Add(new UserGroup { GroupId = id, UserId = userId });
            await _db.SaveChangesAsync();
            PermissionResolver.InvalidateAll();   // Rollen der Gruppe gelten sofort
        }
        return NoContent();
    }

    [HttpDelete("{id}/members/{userId}")]
    public async Task<IActionResult> RemoveMember(int id, int userId)
    {
        if (await _db.Groups.AnyAsync(g => g.Id == id && g.IsEveryone))
            return BadRequest(new { message = "Everyone group membership is implicit and cannot be edited." });
        var membership = await _db.UserGroups.FirstOrDefaultAsync(ug => ug.GroupId == id && ug.UserId == userId);
        if (membership == null)
            return NotFound(new { message = "Membership not found." });
        _db.UserGroups.Remove(membership);
        await _db.SaveChangesAsync();
        PermissionResolver.InvalidateAll();       // … und fallen sofort weg
        return NoContent();
    }

    /// <summary>Gruppe <paramref name="includedId"/> in Gruppe <paramref name="id"/> aufnehmen: alle ihre Mitglieder gelten
    /// dann auch hier (Rollen, Freigaben, Vereine …). Idempotent; 400 bei „Everyone" oder einem Kreis.</summary>
    [HttpPost("{id}/includes/{includedId}")]
    public async Task<IActionResult> AddInclusion(int id, int includedId)
    {
        var both = await _db.Groups.Where(g => g.Id == id || g.Id == includedId).ToListAsync();
        if (both.All(g => g.Id != id) || both.All(g => g.Id != includedId))
            return NotFound(new { message = "Group not found." });
        if (both.Any(g => g.IsEveryone))
            return BadRequest(new { message = "The Everyone group cannot contain or be contained in other groups." });
        if (await GroupMembership.WouldCycleAsync(_db, id, includedId))
            return BadRequest(new { message = "This would make the groups contain each other.", code = ApiErrorCodes.GroupInclusionCycle });
        if (!await _db.GroupInclusions.AnyAsync(l => l.GroupId == id && l.IncludedGroupId == includedId))
        {
            // N9-004: alle Mitglieder der enthaltenen Gruppe bekommen die Rollen von „id“ (und der umfassenden).
            await PermissionGrantGuard.EnsureCanGrantAsync(_db, GetUserIdOrNull(), await PermissionGrantGuard.PermissionsGrantedByGroupAsync(_db, id));
            _db.GroupInclusions.Add(new GroupInclusion { GroupId = id, IncludedGroupId = includedId });
            await _db.SaveChangesAsync();
            await GroupMembership.RebuildClosureAsync(_db);
        }
        return NoContent();
    }

    [HttpDelete("{id}/includes/{includedId}")]
    public async Task<IActionResult> RemoveInclusion(int id, int includedId)
    {
        var link = await _db.GroupInclusions.FirstOrDefaultAsync(l => l.GroupId == id && l.IncludedGroupId == includedId);
        if (link == null)
            return NotFound(new { message = "Inclusion not found." });
        _db.GroupInclusions.Remove(link);
        await _db.SaveChangesAsync();
        await GroupMembership.RebuildClosureAsync(_db);
        return NoContent();
    }

    /// <summary>Trainingsziel-Vorlage der Gruppe (Source "none", falls keine gesetzt).</summary>
    [HttpGet("{id}/training-goal")]
    public async Task<IActionResult> GetTrainingGoal(int id)
    {
        if (!await _db.Groups.AnyAsync(g => g.Id == id))
            return NotFound(new { message = "Group not found." });
        return Ok(await _goals.GetGroupGoalAsync(id));
    }

    /// <summary>Trainingsziel-Vorlage der Gruppe setzen/aktualisieren.</summary>
    [HttpPut("{id}/training-goal")]
    public async Task<IActionResult> SetTrainingGoal(int id, [FromBody] TrainingGoalInputDto dto)
    {
        if (!await _db.Groups.AnyAsync(g => g.Id == id))
            return NotFound(new { message = "Group not found." });
        return Ok(await _goals.SetGroupGoalAsync(id, dto));
    }

    /// <summary>Trainingsziel-Vorlage der Gruppe entfernen.</summary>
    [HttpDelete("{id}/training-goal")]
    public async Task<IActionResult> DeleteTrainingGoal(int id)
    {
        if (!await _db.Groups.AnyAsync(g => g.Id == id))
            return NotFound(new { message = "Group not found." });
        await _goals.DeleteGroupGoalAsync(id);
        return NoContent();
    }
}
