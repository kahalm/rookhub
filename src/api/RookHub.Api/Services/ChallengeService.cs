using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public class ChallengeService
{
    private readonly AppDbContext _db;
    private readonly FriendService _friendService;

    public ChallengeService(AppDbContext db, FriendService friendService)
    {
        _db = db;
        _friendService = friendService;
    }

    /// <summary>Schickt ein Puzzle als Challenge an einen Freund. Verhindert Doppel-Versand derselben
    /// offenen Challenge. Wirft, wenn die beiden nicht befreundet sind oder das Puzzle fehlt.</summary>
    public async Task<PuzzleChallenge> CreateAsync(int fromUserId, int toUserId, int puzzleId)
    {
        if (fromUserId == toUserId)
            throw new InvalidOperationException("Cannot challenge yourself.");

        if (!await _friendService.AreFriendsAsync(fromUserId, toUserId))
            throw new UnauthorizedAccessException("You can only challenge friends.");

        if (!await _db.Puzzles.AnyAsync(p => p.Id == puzzleId))
            throw new KeyNotFoundException("Puzzle not found.");

        var duplicate = await _db.PuzzleChallenges.AnyAsync(c =>
            c.FromUserId == fromUserId && c.ToUserId == toUserId &&
            c.PuzzleId == puzzleId && c.Status == ChallengeStatus.Pending);
        if (duplicate)
            throw new InvalidOperationException("You already sent this puzzle to that friend.");

        var challenge = new PuzzleChallenge
        {
            FromUserId = fromUserId,
            ToUserId = toUserId,
            PuzzleId = puzzleId
        };
        _db.PuzzleChallenges.Add(challenge);
        await _db.SaveChangesAsync();
        return challenge;
    }

    /// <summary>Offene Challenges, die an den User geschickt wurden (Posteingang).</summary>
    public async Task<List<IncomingChallengeDto>> GetIncomingAsync(int userId)
    {
        return await _db.PuzzleChallenges
            .Where(c => c.ToUserId == userId && c.Status == ChallengeStatus.Pending)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new IncomingChallengeDto
            {
                Id = c.Id,
                FromUserId = c.FromUserId,
                FromUsername = c.FromUser.Username,
                FromDisplayName = c.FromUser.Profile != null ? c.FromUser.Profile.DisplayName : null,
                PuzzleId = c.PuzzleId,
                Rating = c.Puzzle.Rating,
                Themes = c.Puzzle.Themes,
                CreatedAt = c.CreatedAt
            })
            .ToListAsync();
    }

    /// <summary>Vom User gesendete Challenges inkl. Ergebnis-Status des Empfängers.</summary>
    public async Task<List<OutgoingChallengeDto>> GetOutgoingAsync(int userId, int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);
        return await _db.PuzzleChallenges
            .Where(c => c.FromUserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(limit)
            .Select(c => new OutgoingChallengeDto
            {
                Id = c.Id,
                ToUserId = c.ToUserId,
                ToUsername = c.ToUser.Username,
                ToDisplayName = c.ToUser.Profile != null ? c.ToUser.Profile.DisplayName : null,
                PuzzleId = c.PuzzleId,
                Rating = c.Puzzle.Rating,
                Status = c.Status.ToString(),
                CreatedAt = c.CreatedAt,
                ResolvedAt = c.ResolvedAt,
                TimeSpentSeconds = c.TimeSpentSeconds
            })
            .ToListAsync();
    }

    /// <summary>Anzahl offener eingehender Challenges — für das Navbar-Badge.</summary>
    public async Task<int> GetIncomingCountAsync(int userId)
        => await _db.PuzzleChallenges.CountAsync(c => c.ToUserId == userId && c.Status == ChallengeStatus.Pending);

    /// <summary>Ergebnis einer Challenge melden (nur der Empfänger, nur solange offen).</summary>
    public async Task ResolveAsync(int challengeId, int userId, bool solved, int timeSpentSeconds)
    {
        var challenge = await _db.PuzzleChallenges.FindAsync(challengeId)
            ?? throw new KeyNotFoundException("Challenge not found.");

        if (challenge.ToUserId != userId)
            throw new UnauthorizedAccessException("Only the recipient can resolve a challenge.");

        if (challenge.Status != ChallengeStatus.Pending)
            throw new InvalidOperationException("Challenge is already resolved.");

        challenge.Status = solved ? ChallengeStatus.Solved : ChallengeStatus.Failed;
        challenge.ResolvedAt = DateTime.UtcNow;
        challenge.TimeSpentSeconds = Math.Clamp(timeSpentSeconds, 0, 3600);
        await _db.SaveChangesAsync();
    }
}
