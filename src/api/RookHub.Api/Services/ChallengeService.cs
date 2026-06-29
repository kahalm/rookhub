using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public class ChallengeService
{
    private readonly AppDbContext _db;
    private readonly FriendService _friendService;
    private readonly NotificationService _notifications;

    public ChallengeService(AppDbContext db, FriendService friendService, NotificationService notifications)
    {
        _db = db;
        _friendService = friendService;
        _notifications = notifications;
    }

    /// <summary>Schickt ein Puzzle als Challenge an genau einen Freund (Convenience-Wrapper um den Batch).</summary>
    public async Task<PuzzleChallenge> CreateAsync(int fromUserId, int toUserId, int puzzleId, PuzzleSource source = PuzzleSource.Standard)
    {
        var result = await CreateBatchAsync(fromUserId, new[] { toUserId }, puzzleId, source);
        if (result.Sent == 1)
            // Frisch angelegte Challenge zurückgeben (für Single-Caller/Tests).
            return await _db.PuzzleChallenges
                .Where(c => c.FromUserId == fromUserId && c.ToUserId == toUserId &&
                            c.PuzzleId == puzzleId && c.Source == source)
                .OrderByDescending(c => c.Id)
                .FirstAsync();

        // Einziger Empfänger wurde übersprungen → denselben Fehler wie früher werfen.
        var reason = result.Skipped.FirstOrDefault()?.Reason;
        throw reason switch
        {
            "self" => new InvalidOperationException("Cannot challenge yourself."),
            "not_friends" => new UnauthorizedAccessException("You can only challenge friends."),
            "duplicate" => new InvalidOperationException("You already sent this puzzle to that friend."),
            _ => new InvalidOperationException("Challenge could not be created.")
        };
    }

    /// <summary>Schickt ein Puzzle als Challenge an mehrere Freunde auf einmal. Tolerant: ungültige Empfänger
    /// (man selbst / kein Freund / bereits offene gleiche Challenge) werden übersprungen und gemeldet.
    /// Wirft nur, wenn das Puzzle selbst fehlt.</summary>
    public async Task<ChallengeBatchResultDto> CreateBatchAsync(int fromUserId, IEnumerable<int> toUserIds, int puzzleId, PuzzleSource source = PuzzleSource.Standard)
    {
        if (!await PuzzleExistsAsync(source, puzzleId))
            throw new KeyNotFoundException("Puzzle not found.");

        var result = new ChallengeBatchResultDto();
        var fromName = await UsernameAsync(fromUserId);
        var created = new List<int>(); // Empfänger, die benachrichtigt werden sollen.

        var candidates = toUserIds.Distinct().ToList();
        // Freundschaften + bereits offene gleiche Challenges in JE EINER Abfrage vorladen (statt 2×N
        // Einzelabfragen in der Schleife → kein N+1 mehr bei vielen Empfängern).
        var friendIds = await _friendService.GetAcceptedFriendIdsAsync(fromUserId, candidates);
        var alreadyChallenged = (await _db.PuzzleChallenges
            .Where(c => c.FromUserId == fromUserId && c.PuzzleId == puzzleId && c.Source == source &&
                        c.Status == ChallengeStatus.Pending && candidates.Contains(c.ToUserId))
            .Select(c => c.ToUserId)
            .ToListAsync()).ToHashSet();

        foreach (var toUserId in candidates)
        {
            if (toUserId == fromUserId)
            {
                result.Skipped.Add(new ChallengeSkipDto { ToUserId = toUserId, Reason = "self" });
                continue;
            }

            if (!friendIds.Contains(toUserId))
            {
                result.Skipped.Add(new ChallengeSkipDto { ToUserId = toUserId, Reason = "not_friends" });
                continue;
            }

            if (alreadyChallenged.Contains(toUserId))
            {
                result.Skipped.Add(new ChallengeSkipDto { ToUserId = toUserId, Reason = "duplicate" });
                continue;
            }

            _db.PuzzleChallenges.Add(new PuzzleChallenge
            {
                FromUserId = fromUserId,
                ToUserId = toUserId,
                PuzzleId = puzzleId,
                Source = source
            });
            created.Add(toUserId);
        }

        if (created.Count > 0)
        {
            await _db.SaveChangesAsync();
            // Empfänger in EINEM Schritt benachrichtigen (atomar, statt eines Saves je Empfänger).
            await _notifications.CreateManyAsync(created, NotificationType.ChallengeReceived,
                new Dictionary<string, string> { ["username"] = fromName }, "/friends");
        }

        result.Sent = created.Count;
        return result;
    }

    /// <summary>Offene Challenges, die an den User geschickt wurden (Posteingang).</summary>
    public async Task<List<IncomingChallengeDto>> GetIncomingAsync(int userId)
    {
        var rows = await _db.PuzzleChallenges
            .Where(c => c.ToUserId == userId && c.Status == ChallengeStatus.Pending)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new IncomingChallengeDto
            {
                Id = c.Id,
                FromUserId = c.FromUserId,
                FromUsername = c.FromUser.Username,
                FromDisplayName = c.FromUser.Profile != null ? c.FromUser.Profile.DisplayName : null,
                PuzzleId = c.PuzzleId,
                Source = c.Source.ToString(),
                CreatedAt = c.CreatedAt
            })
            .ToListAsync();

        await FillPuzzleMetadataAsync(rows, r => r.PuzzleId, r => r.Source,
            (r, rating, themes, title) => { r.Rating = rating; r.Themes = themes; r.Title = title; });
        return rows;
    }

    /// <summary>Vom User gesendete Challenges inkl. Ergebnis-Status des Empfängers.</summary>
    public async Task<List<OutgoingChallengeDto>> GetOutgoingAsync(int userId, int limit = 100)
    {
        limit = Math.Clamp(limit, 1, 500);
        var rows = await _db.PuzzleChallenges
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
                Source = c.Source.ToString(),
                Status = c.Status.ToString(),
                CreatedAt = c.CreatedAt,
                ResolvedAt = c.ResolvedAt,
                TimeSpentSeconds = c.TimeSpentSeconds
            })
            .ToListAsync();

        await FillPuzzleMetadataAsync(rows, r => r.PuzzleId, r => r.Source,
            (r, rating, themes, title) => { r.Rating = rating; r.Title = title; });
        return rows;
    }

    /// <summary>Anzahl offener eingehender Challenges — für das Navbar-Badge.</summary>
    public async Task<int> GetIncomingCountAsync(int userId)
        => await _db.PuzzleChallenges.CountAsync(c => c.ToUserId == userId && c.Status == ChallengeStatus.Pending);

    /// <summary>Pro Freund die Anzahl der von <paramref name="fromUserId"/> an ihn geschickten, noch OFFENEN
    /// (Pending) Challenges — also Puzzle, die der Freund noch nicht versucht hat. Für die Klammer-Anzeige
    /// „Freund (n)" im „An Freund schicken"-Menü. Liefert nur Freunde mit n &gt; 0 (Map ToUserId → Count).</summary>
    public async Task<Dictionary<int, int>> GetPendingOutgoingCountsAsync(int fromUserId)
        => await _db.PuzzleChallenges
            .Where(c => c.FromUserId == fromUserId && c.Status == ChallengeStatus.Pending)
            .GroupBy(c => c.ToUserId)
            .Select(g => new { ToUserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ToUserId, x => x.Count);

    /// <summary>Ergebnis einer Challenge melden (nur der Empfänger, nur solange offen).
    /// <para>
    /// <paramref name="clientSolved"/> wird NICHT blind geglaubt: ein gemeldetes „gelöst" wird serverseitig
    /// gegen die echten Versuche des Empfängers geprüft (analog Revenge) — sonst könnte der Empfänger jede
    /// Challenge als „gelöst" markieren, ohne sie wirklich zu lösen. Asymmetrisch: ein „nicht gelöst" ist
    /// harmlos und wird übernommen; ein „gelöst" zählt nur, wenn es einen bestätigten gelösten Versuch
    /// (in der zur Quelle passenden Tabelle) seit dem Erstellen der Challenge gibt.
    /// </para></summary>
    public async Task ResolveAsync(int challengeId, int userId, bool clientSolved, int timeSpentSeconds)
    {
        var challenge = await _db.PuzzleChallenges.FindAsync(challengeId)
            ?? throw new KeyNotFoundException("Challenge not found.");

        if (challenge.ToUserId != userId)
            throw new UnauthorizedAccessException("Only the recipient can resolve a challenge.");

        if (challenge.Status != ChallengeStatus.Pending)
            throw new InvalidOperationException("Challenge is already resolved.");

        // „Gelöst" serverseitig bestätigen; „nicht gelöst" unverändert übernehmen.
        var solved = clientSolved && await HasConfirmedSolveAsync(challenge, userId);

        challenge.Status = solved ? ChallengeStatus.Solved : ChallengeStatus.Failed;
        challenge.ResolvedAt = DateTime.UtcNow;
        challenge.TimeSpentSeconds = Math.Clamp(timeSpentSeconds, 0, 3600);
        await _db.SaveChangesAsync();

        // Absender benachrichtigen: Empfänger hat die Challenge gelöst/nicht gelöst.
        var byName = await UsernameAsync(userId);
        await _notifications.CreateAsync(challenge.FromUserId, NotificationType.ChallengeResolved,
            new Dictionary<string, string> { ["username"] = byName, ["solved"] = solved ? "true" : "false" }, "/friends");
    }

    /// <summary>Hat der Empfänger das Puzzle der Challenge seit deren Erstellung nachweislich gelöst?
    /// Quelle = passende Versuchstabelle (Standard → <see cref="PuzzleAttempt"/>, Book → <see cref="BookPuzzleAttempt"/>).</summary>
    private Task<bool> HasConfirmedSolveAsync(PuzzleChallenge challenge, int userId) => challenge.Source switch
    {
        PuzzleSource.Book => _db.BookPuzzleAttempts.AnyAsync(a =>
            a.UserId == userId && a.BookPuzzleId == challenge.PuzzleId && a.Solved &&
            a.AttemptedAt >= challenge.CreatedAt),
        _ => _db.PuzzleAttempts.AnyAsync(a =>
            a.UserId == userId && a.PuzzleId == challenge.PuzzleId && a.Solved &&
            a.AttemptedAt >= challenge.CreatedAt)
    };

    /// <summary>Existiert das Puzzle in der zur Quelle passenden Tabelle?</summary>
    private Task<bool> PuzzleExistsAsync(PuzzleSource source, int puzzleId) => source switch
    {
        PuzzleSource.Book => _db.BookPuzzles.AnyAsync(p => p.Id == puzzleId),
        _ => _db.Puzzles.AnyAsync(p => p.Id == puzzleId)
    };

    /// <summary>Reichert eine Liste von Challenge-DTOs mit Rating/Themes/Titel je Quelle an. Da die Quellen in
    /// zwei verschiedenen Tabellen liegen, wird pro Quelle ein Lookup gemacht und in-memory zusammengeführt.</summary>
    private async Task FillPuzzleMetadataAsync<T>(
        List<T> rows,
        Func<T, int> puzzleId,
        Func<T, string> sourceName,
        Action<T, int, string?, string?> assign)
    {
        if (rows.Count == 0) return;

        var standardIds = rows.Where(r => sourceName(r) == nameof(PuzzleSource.Standard)).Select(puzzleId).Distinct().ToList();
        var bookIds = rows.Where(r => sourceName(r) == nameof(PuzzleSource.Book)).Select(puzzleId).Distinct().ToList();

        var standard = standardIds.Count == 0
            ? new Dictionary<int, (int Rating, string? Themes)>()
            : await _db.Puzzles.Where(p => standardIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Rating, p.Themes })
                .ToDictionaryAsync(p => p.Id, p => (Rating: p.Rating, Themes: p.Themes));

        var book = bookIds.Count == 0
            ? new Dictionary<int, (int Rating, string? Themes, string? Title)>()
            : await _db.BookPuzzles.Where(p => bookIds.Contains(p.Id))
                .Select(p => new { p.Id, p.BookRating, p.Tags, p.Title })
                .ToDictionaryAsync(p => p.Id, p => (Rating: p.BookRating ?? 0, Themes: p.Tags, Title: p.Title));

        foreach (var r in rows)
        {
            if (sourceName(r) == nameof(PuzzleSource.Book))
            {
                if (book.TryGetValue(puzzleId(r), out var b)) assign(r, b.Rating, b.Themes, b.Title);
            }
            else
            {
                if (standard.TryGetValue(puzzleId(r), out var s)) assign(r, s.Rating, s.Themes, null);
            }
        }
    }

    private async Task<string> UsernameAsync(int userId)
        => await _db.AppUsers.Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync() ?? "?";
}
