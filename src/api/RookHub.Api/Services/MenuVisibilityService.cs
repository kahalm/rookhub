using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Verwaltet die admin-konfigurierbare Sichtbarkeit der Menüeinträge und löst die
/// effektive Sichtbarkeit für einen konkreten Benutzer (oder anonym) auf.
/// </summary>
public class MenuVisibilityService
{
    private readonly AppDbContext _db;
    public MenuVisibilityService(AppDbContext db) => _db = db;

    /// <summary>Vollständige Konfiguration: DB-Overrides über die Registry-Defaults gelegt.</summary>
    public async Task<List<MenuItemConfigDto>> GetConfigAsync()
    {
        var rows = await _db.MenuItemSettings.Include(s => s.Groups)
            .ToDictionaryAsync(s => s.ItemKey);

        return MenuRegistry.Items.Select(def =>
        {
            rows.TryGetValue(def.Key, out var s);
            return new MenuItemConfigDto
            {
                Key = def.Key,
                Level = s?.Level ?? def.Default,
                GroupIds = s?.Groups.Select(g => g.GroupId).OrderBy(x => x).ToList() ?? new List<int>(),
            };
        }).ToList();
    }

    /// <summary>Konfiguration speichern (nur bekannte Keys; Gruppen nur bei Level=Groups).</summary>
    public async Task SaveConfigAsync(IEnumerable<MenuItemConfigDto> items)
    {
        foreach (var item in items.Where(i => MenuRegistry.Keys.Contains(i.Key)))
        {
            var setting = await _db.MenuItemSettings.Include(s => s.Groups)
                .FirstOrDefaultAsync(s => s.ItemKey == item.Key);
            if (setting is null)
            {
                setting = new MenuItemSetting { ItemKey = item.Key };
                _db.MenuItemSettings.Add(setting);
            }
            setting.Level = item.Level;
            setting.Groups.Clear();
            if (item.Level == MenuVisibilityLevel.Groups)
            {
                foreach (var gid in item.GroupIds.Distinct())
                    setting.Groups.Add(new MenuItemGroupAccess { ItemKey = item.Key, GroupId = gid });
            }
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>Schlüssel der Einträge, die der Benutzer (oder anonym) sehen darf.</summary>
    public async Task<List<string>> GetVisibleKeysAsync(int? userId, bool isAdmin)
    {
        var config = await GetConfigAsync();

        var userGroupIds = userId is int uid
            ? (await _db.UserGroups.Where(ug => ug.UserId == uid).Select(ug => ug.GroupId).ToListAsync()).ToHashSet()
            : new HashSet<int>();
        // Jeder eingeloggte Nutzer ist implizit Mitglied der System-Gruppe „Everyone".
        if (userId != null)
        {
            var everyoneId = await _db.Groups.Where(g => g.IsEveryone).Select(g => (int?)g.Id).FirstOrDefaultAsync();
            if (everyoneId is int eid) userGroupIds.Add(eid);
        }

        var result = new List<string>();
        foreach (var c in config)
        {
            var visible = c.Level switch
            {
                MenuVisibilityLevel.All => true,
                MenuVisibilityLevel.Registered => userId != null,
                MenuVisibilityLevel.Groups => isAdmin || c.GroupIds.Any(userGroupIds.Contains),
                MenuVisibilityLevel.Admin => isAdmin,
                _ => false,
            };
            if (visible) result.Add(c.Key);
        }
        return result;
    }
}
