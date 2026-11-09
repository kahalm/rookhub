using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Services.Ebooks;

/// <summary>Ein Buch, das die Literatur-Liste einer Zusammenfassung vorschlagen darf — für Claude, mit den Merksätzen, die es belegt.</summary>
public sealed record EbookSuggestionCandidate(int Id, string Title, string? Author, string? Year, string? Language, int? TargetMinElo,
    IReadOnlyList<string> Maxims);

/// <summary>Anzeige eines vorgeschlagenen Buchs; <c>Accessible</c> = der Leser darf es öffnen, <c>Blocked</c> = „nicht mehr vorschlagen“.</summary>
public sealed record EbookSuggestionInfo(int Id, string Title, string? Author, string? Year, bool Accessible, bool Blocked);

/// <summary>
/// „Empfohlene Literatur“ unter einer Zusammenfassung (Wunsch 2026-11-09). Vorschlagen darf Claude nur Bücher aus den Belegen der
/// kuratierten Merksätze (<see cref="ChessMaxims"/>): deren Inhalt ist geprüft, und <see cref="InfoAsync"/> verrät nichts über
/// andere, womöglich private Bücher. „Nicht mehr vorschlagen“ gilt je Konto.
/// </summary>
public sealed class EbookSuggestions(AppDbContext db)
{
    private static IReadOnlySet<int> Allowed => ChessMaxims.All.SelectMany(m => m.Sources).Select(s => s.EbookId).ToHashSet();

    /// <summary>Alle vorschlagbaren Bücher ohne die vom Nutzer gesperrten; <paramref name="userId"/> <c>null</c> = ohne Sperren.</summary>
    public async Task<List<EbookSuggestionCandidate>> CandidatesAsync(int? userId, CancellationToken ct = default)
    {
        var ids = Allowed.ToList();
        var blocked = userId is int u
            ? (await db.EbookSuggestionBlocks.AsNoTracking().Where(b => b.UserId == u).Select(b => b.EbookId).ToListAsync(ct)).ToHashSet()
            : new HashSet<int>();
        var books = await db.Ebooks.AsNoTracking().Where(b => ids.Contains(b.Id))
            .Select(b => new { b.Id, b.Title, b.Author, b.Year, b.Language, b.TargetMinElo }).ToListAsync(ct);
        return books.Where(b => !blocked.Contains(b.Id)).OrderBy(b => b.Id).Select(b => new EbookSuggestionCandidate(b.Id, b.Title, b.Author,
                b.Year?.ToString(), b.Language, b.TargetMinElo,
                ChessMaxims.All.Where(m => m.Sources.Any(s => s.EbookId == b.Id)).Select(m => m.Id).ToList()))
            .ToList();
    }

    /// <summary>Anzeige für die Leser der Zusammenfassung; Kennungen außerhalb der Vorschlagsliste werden ignoriert.</summary>
    public async Task<List<EbookSuggestionInfo>> InfoAsync(IEnumerable<int> requested, EbookCaller caller, bool manageAll, CancellationToken ct = default)
    {
        var allowed = Allowed;
        var ids = requested.Where(allowed.Contains).Distinct().Take(20).ToList();
        var visible = (await EbookAccess.Visible(db, caller, manageAll).Where(b => ids.Contains(b.Id)).Select(b => b.Id).ToListAsync(ct)).ToHashSet();
        var blocked = caller.UserId is int u
            ? (await db.EbookSuggestionBlocks.AsNoTracking().Where(b => b.UserId == u && ids.Contains(b.EbookId)).Select(b => b.EbookId).ToListAsync(ct)).ToHashSet()
            : new HashSet<int>();
        var books = await db.Ebooks.AsNoTracking().Where(b => ids.Contains(b.Id)).Select(b => new { b.Id, b.Title, b.Author, b.Year }).ToListAsync(ct);
        return books.Select(b => new EbookSuggestionInfo(b.Id, b.Title, b.Author, b.Year?.ToString(), visible.Contains(b.Id), blocked.Contains(b.Id))).ToList();
    }

    /// <summary>„Nicht mehr vorschlagen“ setzen bzw. zurücknehmen; <c>false</c> = kein vorschlagbares Buch.</summary>
    public async Task<bool> SetBlockedAsync(int userId, int ebookId, bool blocked, DateTime now, CancellationToken ct = default)
    {
        if (!Allowed.Contains(ebookId) || !await db.Ebooks.AnyAsync(b => b.Id == ebookId, ct)) return false;
        var row = await db.EbookSuggestionBlocks.FirstOrDefaultAsync(b => b.UserId == userId && b.EbookId == ebookId, ct);
        if (blocked && row is null) db.EbookSuggestionBlocks.Add(new EbookSuggestionBlock { UserId = userId, EbookId = ebookId, CreatedAt = now });
        else if (!blocked && row is not null) db.EbookSuggestionBlocks.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Wessen Sperren beim Schreiben gelten: <c>o&lt;id&gt;</c> = Besitzer der eigenen Partie, sonst wer die Vereins-Zusammenfassung anforderte.</summary>
    public async Task<int?> RequesterAsync(string game, CancellationToken ct = default)
    {
        if (game.StartsWith('o') && int.TryParse(game[1..], out var own))
            return await db.SavedGames.AsNoTracking().Where(g => g.Id == own).Select(g => (int?)g.UserId).FirstOrDefaultAsync(ct);
        return int.TryParse(game, out var club)
            ? await db.ClubGameSummaries.AsNoTracking().Where(s => s.ClubGameId == club).Select(s => s.RequestedByUserId).FirstOrDefaultAsync(ct)
            : null;
    }
}
