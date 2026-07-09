using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// „Katalog": ein Besitzer (aktuell nur Admins) gibt einzelnen Usern und/oder Gruppen die LISTE
/// seiner Kurse + Repertoires frei; berechtigte Viewer sehen die Liste und fordern einzelne Items an.
/// Der Besitzer genehmigt/lehnt ab; bei Genehmigung wird das Item über die bestehende Kurs-/Repertoire-
/// Teilen-Logik freigegeben (Admin teilt an jeden — keine Freundschaft nötig).
/// </summary>
public class CatalogService
{
    private readonly AppDbContext _db;
    private readonly CourseService _courses;
    private readonly RepertoireService _repertoires;
    private readonly NotificationService _notifications;

    public CatalogService(AppDbContext db, CourseService courses, RepertoireService repertoires, NotificationService notifications)
    {
        _db = db;
        _courses = courses;
        _repertoires = repertoires;
        _notifications = notifications;
    }

    // ---- Freigaben (Besitzer-Sicht) ----

    public async Task<CatalogGrantsDto> GetGrantsAsync(int ownerId)
    {
        var grants = await _db.CatalogGrants.Where(g => g.OwnerUserId == ownerId).ToListAsync();
        return new CatalogGrantsDto
        {
            UserIds = grants.Where(g => g.SubjectUserId != null).Select(g => g.SubjectUserId!.Value).ToList(),
            GroupIds = grants.Where(g => g.SubjectGroupId != null).Select(g => g.SubjectGroupId!.Value).ToList(),
        };
    }

    /// <summary>Ersetzt die komplette Freigabe-Liste eines Besitzers (nur existierende User/Gruppen).</summary>
    public async Task<CatalogGrantsDto> SetGrantsAsync(int ownerId, List<int> userIds, List<int> groupIds)
    {
        var reqUsers = (userIds ?? new()).Distinct().Where(id => id != ownerId).ToList();
        var reqGroups = (groupIds ?? new()).Distinct().ToList();
        var validUsers = await _db.AppUsers.Where(u => reqUsers.Contains(u.Id)).Select(u => u.Id).ToListAsync();
        var validGroups = await _db.Groups.Where(g => reqGroups.Contains(g.Id)).Select(g => g.Id).ToListAsync();

        _db.CatalogGrants.RemoveRange(_db.CatalogGrants.Where(g => g.OwnerUserId == ownerId));
        foreach (var uid in validUsers)
            _db.CatalogGrants.Add(new CatalogGrant { OwnerUserId = ownerId, SubjectUserId = uid, CreatedAt = DateTime.UtcNow });
        foreach (var gid in validGroups)
            _db.CatalogGrants.Add(new CatalogGrant { OwnerUserId = ownerId, SubjectGroupId = gid, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        return new CatalogGrantsDto { UserIds = validUsers, GroupIds = validGroups };
    }

    // ---- Viewer-Sicht ----

    /// <summary>Besitzer, die dem Viewer (direkt oder über eine seiner Gruppen) Katalog-Zugriff geben.</summary>
    private async Task<List<int>> GrantingOwnerIdsAsync(int viewerId)
    {
        var myGroups = await _db.UserGroups.Where(ug => ug.UserId == viewerId).Select(ug => ug.GroupId).ToListAsync();
        return await _db.CatalogGrants
            .Where(g => g.SubjectUserId == viewerId || (g.SubjectGroupId != null && myGroups.Contains(g.SubjectGroupId.Value)))
            .Select(g => g.OwnerUserId)
            .Distinct()
            .ToListAsync();
    }

    public async Task<bool> HasAccessAsync(int viewerId, bool isAdmin)
        => isAdmin || (await GrantingOwnerIdsAsync(viewerId)).Count > 0;

    public async Task<List<CatalogItemDto>> GetCatalogAsync(int viewerId)
    {
        var owners = await GrantingOwnerIdsAsync(viewerId);
        if (owners.Count == 0) return new();

        var ownerNames = await _db.AppUsers.Where(u => owners.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username);

        var courses = await _db.Books
            .Where(b => b.OwnerUserId != null && owners.Contains(b.OwnerUserId.Value))
            .Select(b => new { b.Id, b.DisplayName, Owner = b.OwnerUserId!.Value })
            .ToListAsync();
        var reps = await _db.Repertoires
            .Where(r => owners.Contains(r.UserId))
            .Select(r => new { r.Id, r.Name, Owner = r.UserId })
            .ToListAsync();

        var sharedCourseIds = (await _db.CourseShares.Where(s => s.RecipientId == viewerId).Select(s => s.BookId).ToListAsync()).ToHashSet();
        var sharedRepIds = (await _db.RepertoireShares.Where(s => s.RecipientId == viewerId).Select(s => s.RepertoireId).ToListAsync()).ToHashSet();
        var pending = await _db.CatalogRequests
            .Where(r => r.RequesterUserId == viewerId && r.Status == "pending")
            .Select(r => new { r.ItemType, r.ItemId })
            .ToListAsync();
        var pendingCourses = pending.Where(p => p.ItemType == CatalogItemType.Course).Select(p => p.ItemId).ToHashSet();
        var pendingReps = pending.Where(p => p.ItemType == CatalogItemType.Repertoire).Select(p => p.ItemId).ToHashSet();

        string OwnerName(int id) => ownerNames.TryGetValue(id, out var n) ? n : "?";
        var items = new List<CatalogItemDto>();
        foreach (var c in courses)
            items.Add(new CatalogItemDto
            {
                OwnerUserId = c.Owner, OwnerName = OwnerName(c.Owner), ItemType = "course", ItemId = c.Id, Name = c.DisplayName,
                Status = sharedCourseIds.Contains(c.Id) ? "shared" : pendingCourses.Contains(c.Id) ? "pending" : "none",
            });
        foreach (var r in reps)
            items.Add(new CatalogItemDto
            {
                OwnerUserId = r.Owner, OwnerName = OwnerName(r.Owner), ItemType = "repertoire", ItemId = r.Id, Name = r.Name,
                Status = sharedRepIds.Contains(r.Id) ? "shared" : pendingReps.Contains(r.Id) ? "pending" : "none",
            });

        return items.OrderBy(i => i.OwnerName).ThenBy(i => i.ItemType).ThenBy(i => i.Name).ToList();
    }

    /// <summary>Fordert ein einzelnes Item an. Liefert den resultierenden Status ("pending"/"shared").
    /// <see cref="KeyNotFoundException"/>, wenn das Item nicht existiert oder der Viewer keinen Katalog-
    /// Zugriff auf dessen Besitzer hat (aus Sicht des Aufrufers ununterscheidbar → kein Info-Leak).</summary>
    public async Task<string> RequestAsync(int viewerId, string itemType, int itemId)
    {
        var type = itemType?.Trim().ToLowerInvariant() == "repertoire" ? CatalogItemType.Repertoire : CatalogItemType.Course;
        var owners = (await GrantingOwnerIdsAsync(viewerId)).ToHashSet();

        int ownerId;
        string itemName;
        if (type == CatalogItemType.Course)
        {
            var b = await _db.Books.Where(x => x.Id == itemId).Select(x => new { x.OwnerUserId, x.DisplayName }).FirstOrDefaultAsync();
            if (b?.OwnerUserId == null || !owners.Contains(b.OwnerUserId.Value)) throw new KeyNotFoundException("Item not available.");
            ownerId = b.OwnerUserId.Value; itemName = b.DisplayName;
            if (await _db.CourseShares.AnyAsync(s => s.BookId == itemId && s.RecipientId == viewerId)) return "shared";
        }
        else
        {
            var r = await _db.Repertoires.Where(x => x.Id == itemId).Select(x => new { x.UserId, x.Name }).FirstOrDefaultAsync();
            if (r == null || !owners.Contains(r.UserId)) throw new KeyNotFoundException("Item not available.");
            ownerId = r.UserId; itemName = r.Name;
            if (await _db.RepertoireShares.AnyAsync(s => s.RepertoireId == itemId && s.RecipientId == viewerId)) return "shared";
        }

        var already = await _db.CatalogRequests.AnyAsync(r =>
            r.RequesterUserId == viewerId && r.ItemType == type && r.ItemId == itemId && r.Status == "pending");
        if (already) return "pending";

        _db.CatalogRequests.Add(new CatalogRequest
        {
            RequesterUserId = viewerId, OwnerUserId = ownerId, ItemType = type, ItemId = itemId,
            Status = "pending", CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var requesterName = await _db.AppUsers.Where(u => u.Id == viewerId).Select(u => u.Username).FirstOrDefaultAsync() ?? "?";
        await _notifications.CreateManyAsync(new List<int> { ownerId }, NotificationType.CatalogRequestReceived,
            new Dictionary<string, string> { ["username"] = requesterName, ["itemName"] = itemName }, "/catalog");
        return "pending";
    }

    // ---- Anforderungen (Besitzer-Sicht) ----

    public async Task<List<CatalogRequestDto>> GetPendingRequestsAsync(int ownerId)
    {
        var reqs = await _db.CatalogRequests
            .Where(r => r.OwnerUserId == ownerId && r.Status == "pending")
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();
        if (reqs.Count == 0) return new();

        var userIds = reqs.Select(r => r.RequesterUserId).Distinct().ToList();
        var names = await _db.AppUsers.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username);
        var courseIds = reqs.Where(r => r.ItemType == CatalogItemType.Course).Select(r => r.ItemId).ToList();
        var repIds = reqs.Where(r => r.ItemType == CatalogItemType.Repertoire).Select(r => r.ItemId).ToList();
        var courseNames = await _db.Books.Where(b => courseIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.DisplayName);
        var repNames = await _db.Repertoires.Where(r => repIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name);

        return reqs.Select(r => new CatalogRequestDto
        {
            Id = r.Id,
            RequesterUserId = r.RequesterUserId,
            RequesterName = names.TryGetValue(r.RequesterUserId, out var n) ? n : "?",
            ItemType = r.ItemType == CatalogItemType.Repertoire ? "repertoire" : "course",
            ItemId = r.ItemId,
            ItemName = r.ItemType == CatalogItemType.Course
                ? (courseNames.TryGetValue(r.ItemId, out var cn) ? cn : "?")
                : (repNames.TryGetValue(r.ItemId, out var rn) ? rn : "?"),
            Status = r.Status,
            CreatedAt = r.CreatedAt,
        }).ToList();
    }

    /// <summary>Genehmigt eine Anforderung: teilt das Item mit dem Anfragenden (über die bestehende
    /// Teilen-Logik, die den Empfänger benachrichtigt) und markiert die Anforderung als approved.</summary>
    public async Task ApproveAsync(int ownerId, int requestId, bool isAdmin)
    {
        var req = await _db.CatalogRequests.FirstOrDefaultAsync(r => r.Id == requestId)
            ?? throw new KeyNotFoundException("Request not found.");
        if (req.OwnerUserId != ownerId) throw new KeyNotFoundException("Request not found.");
        if (req.Status != "pending") return;

        if (req.ItemType == CatalogItemType.Course)
            await _courses.ShareCourseAsync(ownerId, req.ItemId, new List<int> { req.RequesterUserId }, isAdmin);
        else
            await _repertoires.ShareAsync(ownerId, req.ItemId, new List<int> { req.RequesterUserId }, isAdmin);

        req.Status = "approved";
        req.ResolvedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task DeclineAsync(int ownerId, int requestId)
    {
        var req = await _db.CatalogRequests.FirstOrDefaultAsync(r => r.Id == requestId)
            ?? throw new KeyNotFoundException("Request not found.");
        if (req.OwnerUserId != ownerId) throw new KeyNotFoundException("Request not found.");
        if (req.Status != "pending") return;

        req.Status = "declined";
        req.ResolvedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var itemName = req.ItemType == CatalogItemType.Course
            ? await _db.Books.Where(b => b.Id == req.ItemId).Select(b => b.DisplayName).FirstOrDefaultAsync()
            : await _db.Repertoires.Where(r => r.Id == req.ItemId).Select(r => r.Name).FirstOrDefaultAsync();
        await _notifications.CreateManyAsync(new List<int> { req.RequesterUserId }, NotificationType.CatalogRequestDeclined,
            new Dictionary<string, string> { ["itemName"] = itemName ?? "?" }, "/catalog");
    }
}
