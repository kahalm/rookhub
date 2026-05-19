using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public class FriendService
{
    private readonly AppDbContext _db;

    public FriendService(AppDbContext db) => _db = db;

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

    public async Task<Friendship> SendRequestAsync(int requesterId, int addresseeId)
    {
        if (requesterId == addresseeId)
            throw new InvalidOperationException("Cannot send friend request to yourself.");

        var existing = await _db.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == requesterId && f.AddresseeId == addresseeId) ||
            (f.RequesterId == addresseeId && f.AddresseeId == requesterId));

        if (existing != null)
        {
            // M-12: Allow re-request after decline by removing the old record
            if (existing.Status == FriendshipStatus.Declined)
            {
                _db.Friendships.Remove(existing);
                await _db.SaveChangesAsync();
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
        await _db.SaveChangesAsync();
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
    }

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

    public async Task<List<UserSearchResultDto>> SearchUsersAsync(string query, int currentUserId)
    {
        return await _db.AppUsers
            .Include(u => u.Profile)
            .Where(u => u.Id != currentUserId &&
                       (u.Username.Contains(query) ||
                        (u.Profile != null && (
                            (u.Profile.DisplayName != null && u.Profile.DisplayName.Contains(query)) ||
                            (u.Profile.ChessResultsId != null && u.Profile.ChessResultsId.Contains(query)) ||
                            (u.Profile.ChessComUsername != null && u.Profile.ChessComUsername.Contains(query)) ||
                            (u.Profile.LichessUsername != null && u.Profile.LichessUsername.Contains(query)) ||
                            (u.Profile.FideId != null && u.Profile.FideId.Contains(query))
                        ))))
            .Take(20)
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
