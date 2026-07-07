using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/admin/groups")]
[Authorize(Roles = "Admin")]
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
                MemberCount = g.Members.Count,
                CreatedAt = g.CreatedAt,
                IsEveryone = g.IsEveryone,
            })
            .ToListAsync();
        // „Everyone" hat keine expliziten Mitgliedszeilen — jeder ist implizit Mitglied.
        if (groups.Any(g => g.IsEveryone))
        {
            var totalUsers = await _db.AppUsers.CountAsync();
            foreach (var g in groups.Where(g => g.IsEveryone)) g.MemberCount = totalUsers;
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
            return BadRequest(new { message = "Group name already exists." });

        var group = new Group { Name = name, Description = dto.Description, CreatedAt = DateTime.UtcNow };
        _db.Groups.Add(group);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Race: paralleler Create mit gleichem Namen -> Unique-Index -> sauberer 400 statt 500.
            return BadRequest(new { message = "Group name already exists." });
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
                return BadRequest(new { message = "Group name already exists." });
            group.Name = name;
        }
        if (dto.Description != null) group.Description = dto.Description;
        await _db.SaveChangesAsync();

        var count = await _db.UserGroups.CountAsync(ug => ug.GroupId == id);
        return Ok(new GroupDto { Id = group.Id, Name = group.Name, Description = group.Description, MemberCount = count, CreatedAt = group.CreatedAt });
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
        _db.Groups.Remove(group);
        await _db.SaveChangesAsync();
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
        var members = await _db.UserGroups
            .Where(ug => ug.GroupId == id)
            .Select(ug => new GroupMemberDto { UserId = ug.UserId, Username = ug.User!.Username })
            .OrderBy(m => m.Username)
            .ToListAsync();
        return Ok(members);
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
            _db.UserGroups.Add(new UserGroup { GroupId = id, UserId = userId });
            await _db.SaveChangesAsync();
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
