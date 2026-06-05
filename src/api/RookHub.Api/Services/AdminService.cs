using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;

namespace RookHub.Api.Services;

/// <summary>
/// Admin-Backend-Operationen für Benutzer- und Puzzle-Verwaltung (vormals inline im AdminController).
/// Self-Delete/Self-Toggle → <see cref="InvalidOperationException"/> (400), nicht gefunden →
/// <see cref="KeyNotFoundException"/> (404). Ein <see cref="DbUpdateException"/> aus
/// <see cref="DeleteUserAsync"/> propagiert bewusst zum Controller (→ 409).
/// </summary>
public class AdminService
{
    private readonly AppDbContext _db;

    public AdminService(AppDbContext db) => _db = db;

    public async Task<(List<AdminUserDto> items, int totalCount, int page, int pageSize)> GetUsersAsync(string? search, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var query = _db.AppUsers.AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            if (search.Length > 100) search = search[..100];
            query = query.Where(u => u.Username.Contains(search) || (u.Email != null && u.Email.Contains(search)));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                IsAdmin = u.IsAdmin,
                CreatedAt = u.CreatedAt,
                Groups = u.Groups.Select(ug => ug.Group!.Name).OrderBy(n => n).ToList()
            })
            .ToListAsync();

        return (items, totalCount, page, pageSize);
    }

    /// <summary>Löscht einen User (samt Freundschaften wg. Restrict-FK). DbUpdateException propagiert (→ 409).</summary>
    public async Task DeleteUserAsync(int id, int currentUserId)
    {
        if (id == currentUserId)
            throw new InvalidOperationException("Cannot delete yourself.");

        var user = await _db.AppUsers.FindAsync(id)
            ?? throw new KeyNotFoundException();

        // Freundschaften zuerst entfernen (Restrict delete behavior).
        var friendships = await _db.Friendships
            .Where(f => f.RequesterId == id || f.AddresseeId == id)
            .ToListAsync();
        _db.Friendships.RemoveRange(friendships);

        _db.AppUsers.Remove(user);
        await _db.SaveChangesAsync();   // verbleibende Restrict-FKs → DbUpdateException → Controller mappt auf 409
    }

    public async Task<AdminUserDto> ToggleAdminAsync(int id, int currentUserId)
    {
        if (id == currentUserId)
            throw new InvalidOperationException("Cannot toggle your own admin status.");

        var user = await _db.AppUsers.FindAsync(id)
            ?? throw new KeyNotFoundException();

        user.IsAdmin = !user.IsAdmin;
        await _db.SaveChangesAsync();

        var groups = await _db.UserGroups
            .Where(ug => ug.UserId == user.Id)
            .Select(ug => ug.Group!.Name)
            .OrderBy(n => n)
            .ToListAsync();

        return new AdminUserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            IsAdmin = user.IsAdmin,
            CreatedAt = user.CreatedAt,
            Groups = groups
        };
    }

    public Task<int> GetPuzzleCountAsync() => _db.Puzzles.CountAsync();

    public async Task ClearPuzzlesAsync()
    {
        // InMemory-Provider unterstützt keine Transaktionen → nur mit relationalem Provider umklammern.
        if (!_db.Database.IsRelational())
        {
            await _db.PuzzleAttempts.ExecuteDeleteAsync();
            await _db.Puzzles.ExecuteDeleteAsync();
            return;
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            await _db.PuzzleAttempts.ExecuteDeleteAsync();
            await _db.Puzzles.ExecuteDeleteAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}
