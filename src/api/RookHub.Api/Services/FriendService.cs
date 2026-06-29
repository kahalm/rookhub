using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public class FriendService
{
    private readonly AppDbContext _db;
    private readonly NotificationService _notifications;

    public FriendService(AppDbContext db, NotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    /// <summary>Sind die beiden User befreundet (akzeptierte Freundschaft, egal in welche Richtung angefragt)?
    /// Basis für die Sichtbarkeit von Freund-Stats/Revenge — nur Freunde dürfen die Puzzle-Historie sehen.</summary>
    public async Task<bool> AreFriendsAsync(int userId, int otherUserId)
    {
        if (userId == otherUserId) return false;
        return await _db.Friendships.AnyAsync(f =>
            f.Status == FriendshipStatus.Accepted &&
            ((f.RequesterId == userId && f.AddresseeId == otherUserId) ||
             (f.RequesterId == otherUserId && f.AddresseeId == userId)));
    }

    /// <summary>Filtert aus <paramref name="candidates"/> die mit <paramref name="userId"/> per akzeptierter
    /// Freundschaft verbundenen Ids heraus — in EINER Abfrage (Batch statt N× <see cref="AreFriendsAsync"/>,
    /// gegen N+1 z. B. beim Verschicken einer Challenge an mehrere Freunde). `userId` selbst wird nie zurückgegeben.</summary>
    public async Task<HashSet<int>> GetAcceptedFriendIdsAsync(int userId, IEnumerable<int> candidates)
    {
        var ids = candidates.Where(id => id != userId).Distinct().ToList();
        if (ids.Count == 0) return new HashSet<int>();
        var rows = await _db.Friendships
            .Where(f => f.Status == FriendshipStatus.Accepted &&
                ((f.RequesterId == userId && ids.Contains(f.AddresseeId)) ||
                 (f.AddresseeId == userId && ids.Contains(f.RequesterId))))
            .Select(f => new { f.RequesterId, f.AddresseeId })
            .ToListAsync();
        // Die jeweils ANDERE Seite ist der Freund (in-memory, um keine CASE-Projektion zu übersetzen).
        return rows.Select(r => r.RequesterId == userId ? r.AddresseeId : r.RequesterId).ToHashSet();
    }

    /// <summary>Basis-Anzeigedaten (Username + DisplayName) eines Users — für Stats-/Revenge-Header.</summary>
    public async Task<FriendDto?> GetUserBasicAsync(int userId)
    {
        return await _db.AppUsers
            .Where(u => u.Id == userId)
            .Select(u => new FriendDto
            {
                UserId = u.Id,
                Username = u.Username,
                DisplayName = u.Profile != null ? u.Profile.DisplayName : null
            })
            .FirstOrDefaultAsync();
    }

    public async Task<List<FriendDto>> GetFriendsAsync(int userId)
    {
        var friendships = await _db.Friendships
            .Include(f => f.Requester).ThenInclude(u => u.Profile)
            .Include(f => f.Addressee).ThenInclude(u => u.Profile)
            .Where(f => f.Status == FriendshipStatus.Accepted &&
                       (f.RequesterId == userId || f.AddresseeId == userId))
            .ToListAsync();

        return friendships.Select(f =>
        {
            var friend = f.RequesterId == userId ? f.Addressee : f.Requester;
            return new FriendDto
            {
                FriendshipId = f.Id,
                UserId = friend.Id,
                Username = friend.Username,
                DisplayName = friend.Profile?.DisplayName
            };
        }).ToList();
    }

    public async Task<List<FriendRequestDto>> GetPendingRequestsAsync(int userId)
    {
        return await _db.Friendships
            .Include(f => f.Requester)
            .Where(f => f.AddresseeId == userId && f.Status == FriendshipStatus.Pending)
            .Select(f => new FriendRequestDto
            {
                FriendshipId = f.Id,
                RequesterId = f.RequesterId,
                RequesterUsername = f.Requester.Username,
                CreatedAt = f.CreatedAt
            })
            .ToListAsync();
    }

    /// <summary>Von mir gesendete, noch nicht angenommene (Pending) Freundschaftsanfragen —
    /// für die Anzeige „wartet auf Bestätigung" in der Freundesliste.</summary>
    public async Task<List<SentFriendRequestDto>> GetSentPendingRequestsAsync(int userId)
    {
        return await _db.Friendships
            .Include(f => f.Addressee).ThenInclude(u => u.Profile)
            .Where(f => f.RequesterId == userId && f.Status == FriendshipStatus.Pending)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new SentFriendRequestDto
            {
                FriendshipId = f.Id,
                AddresseeId = f.AddresseeId,
                AddresseeUsername = f.Addressee.Username,
                AddresseeDisplayName = f.Addressee.Profile != null ? f.Addressee.Profile.DisplayName : null,
                CreatedAt = f.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<Friendship> SendRequestAsync(int requesterId, int addresseeId)
    {
        if (requesterId == addresseeId)
            throw new InvalidOperationException("Cannot send friend request to yourself.");

        var existing = await _db.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == requesterId && f.AddresseeId == addresseeId) ||
            (f.RequesterId == addresseeId && f.AddresseeId == requesterId));

        if (existing != null)
        {
            if (existing.Status == FriendshipStatus.Declined)
            {
                _db.Friendships.Remove(existing);
            }
            else
            {
                throw new InvalidOperationException("A friendship or request already exists.");
            }
        }

        if (!await _db.AppUsers.AnyAsync(u => u.Id == addresseeId))
            throw new KeyNotFoundException("User not found.");

        var friendship = new Friendship
        {
            RequesterId = requesterId,
            AddresseeId = addresseeId
        };

        _db.Friendships.Add(friendship);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new InvalidOperationException("A friendship or request already exists.");
        }

        // Adressat benachrichtigen: neue Freundschaftsanfrage.
        var requesterName = await UsernameAsync(requesterId);
        await _notifications.CreateAsync(addresseeId, NotificationType.FriendRequestReceived,
            new Dictionary<string, string> { ["username"] = requesterName }, "/friends");

        return friendship;
    }

    public async Task AcceptRequestAsync(int friendshipId, int userId)
    {
        var friendship = await _db.Friendships.FindAsync(friendshipId)
            ?? throw new KeyNotFoundException("Friendship request not found.");

        if (friendship.AddresseeId != userId)
            throw new UnauthorizedAccessException("Only the addressee can accept.");

        if (friendship.Status != FriendshipStatus.Pending)
            throw new InvalidOperationException("Request is not pending.");

        friendship.Status = FriendshipStatus.Accepted;
        await _db.SaveChangesAsync();

        // Ursprünglichen Anfrager benachrichtigen: Anfrage angenommen.
        var accepterName = await UsernameAsync(userId);
        await _notifications.CreateAsync(friendship.RequesterId, NotificationType.FriendRequestAccepted,
            new Dictionary<string, string> { ["username"] = accepterName }, "/friends");
    }

    private async Task<string> UsernameAsync(int userId)
        => await _db.AppUsers.Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync() ?? "?";

    public async Task DeclineRequestAsync(int friendshipId, int userId)
    {
        var friendship = await _db.Friendships.FindAsync(friendshipId)
            ?? throw new KeyNotFoundException("Friendship request not found.");

        if (friendship.AddresseeId != userId)
            throw new UnauthorizedAccessException("Only the addressee can decline.");

        if (friendship.Status != FriendshipStatus.Pending)
            throw new InvalidOperationException("Request is not pending.");

        friendship.Status = FriendshipStatus.Declined;
        await _db.SaveChangesAsync();
    }

    public async Task RemoveFriendAsync(int friendshipId, int userId)
    {
        var friendship = await _db.Friendships.FindAsync(friendshipId)
            ?? throw new KeyNotFoundException("Friendship not found.");

        if (friendship.RequesterId != userId && friendship.AddresseeId != userId)
            throw new UnauthorizedAccessException("Not part of this friendship.");

        _db.Friendships.Remove(friendship);
        await _db.SaveChangesAsync();
    }

    /// <summary>Max. Treffer einer User-Suche (harte Obergrenze, auch service-seitig erzwungen).</summary>
    private const int SearchTake = 20;
    /// <summary>Max. Länge des Suchbegriffs (zusätzlich zur Controller-Kappung als Defense-in-Depth).</summary>
    private const int MaxQueryLength = 50;

    public async Task<List<UserSearchResultDto>> SearchUsersAsync(string query, int currentUserId)
    {
        // Eingabe normalisieren + hart begrenzen (Defense-in-Depth, falls ein Aufrufer den Controller-Cap umgeht):
        // LIKE-Wildcards strippen und auf MaxQueryLength kürzen.
        var q = (query ?? string.Empty).Replace("%", "").Replace("_", "").Trim();
        if (q.Length > MaxQueryLength) q = q[..MaxQueryLength];
        if (q.Length == 0) return new List<UserSearchResultDto>();

        // Identitäts-/Konto-Felder (Username + externe Spiel-Accounts/IDs) sind PRÄFIX-Treffer
        // (`LIKE q%`) — so kann der Username-Index greifen und es bleibt ein indexfreundlicher
        // Scan; das deckt den Normalfall „ich tippe den Anfang eines Namens" ab. Nur der
        // Anzeige-/Klarname (DisplayName) bleibt Teilstring-Suche, weil dort die Mitte zählt.
        return await _db.AppUsers
            .Include(u => u.Profile)
            .Where(u => u.Id != currentUserId &&
                       (u.Username.StartsWith(q) ||
                        (u.Profile != null && (
                            (u.Profile.DisplayName != null && u.Profile.DisplayName.Contains(q)) ||
                            (u.Profile.ChessResultsId != null && u.Profile.ChessResultsId.StartsWith(q)) ||
                            (u.Profile.ChessComUsername != null && u.Profile.ChessComUsername.StartsWith(q)) ||
                            (u.Profile.LichessUsername != null && u.Profile.LichessUsername.StartsWith(q)) ||
                            (u.Profile.FideId != null && u.Profile.FideId.StartsWith(q))
                        ))))
            .OrderBy(u => u.Username)
            .Take(SearchTake)
            .Select(u => new UserSearchResultDto
            {
                UserId = u.Id,
                Username = u.Username,
                DisplayName = u.Profile != null ? u.Profile.DisplayName : null,
                ChessResultsId = u.Profile != null ? u.Profile.ChessResultsId : null,
                ChessComUsername = u.Profile != null ? u.Profile.ChessComUsername : null,
                LichessUsername = u.Profile != null ? u.Profile.LichessUsername : null,
                FideId = u.Profile != null ? u.Profile.FideId : null
            })
            .ToListAsync();
    }
}
