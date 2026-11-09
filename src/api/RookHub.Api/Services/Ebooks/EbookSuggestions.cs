using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.League;

namespace RookHub.Api.Services.Ebooks;

/// <summary>Ein Buch, das die Literatur-Liste einer Zusammenfassung vorschlagen darf — für Claude, mit den Merksätzen, die es belegt.</summary>
public sealed record EbookSuggestionCandidate(int Id, string Title, string? Author, string? Year, string? Language, int? TargetMinElo,
    IReadOnlyList<string> Maxims);

/// <summary>Anzeige eines vorgeschlagenen Buchs; <c>Accessible</c> = der Leser darf es öffnen, <c>Blocked</c> = „nicht mehr vorschlagen“.</summary>
public sealed record EbookSuggestionInfo(int Id, string Title, string? Author, string? Year, bool Accessible, bool Blocked);

/// <summary>Spielstärke einer Seite für die Buchwahl: <c>Elo</c> oder — bei der anonymisierten Seite — nur <c>Band</c> („1700–1799“).</summary>
public sealed record PlayerStrength(int? Elo, string? Band);

/// <summary>Spielstärke beider Seiten; <c>null</c> = unbekannt.</summary>
public sealed record GameStrength(PlayerStrength? White, PlayerStrength? Black);

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

    /// <summary>
    /// Spielstärke je Seite für die Literatur (Wunsch 2026-11-09), <c>null</c> = unbekannte Partie. Reihenfolge: Elo der Partie, Elo der
    /// Ligapaarung, Meldeliste über die FIDE-ID (Saison am nächsten zur Partie). Für die anonymisierte Seite zählen dabei die
    /// internen FIDE-IDs und, ohne feste Zuordnung, die einzige genaue Paarung des Finders; heraus geht von ihr nur das Band.
    /// </summary>
    public async Task<GameStrength?> StrengthAsync(string game, CancellationToken ct = default)
    {
        if (game.StartsWith('o'))
        {
            if (!int.TryParse(game[1..], out var own)) return null;
            var saved = await db.SavedGames.AsNoTracking().Where(g => g.Id == own).Select(g => new { g.WhiteElo, g.BlackElo }).FirstOrDefaultAsync(ct);
            return saved is null ? null : new GameStrength(Named(saved.WhiteElo), Named(saved.BlackElo));
        }
        if (!int.TryParse(game, out var id) || await db.LeagueClubGames.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct) is not { } c) return null;
        var club = await db.LeagueClubs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.ClubId, ct);
        var whiteAnon = c.WhiteRealName != null || c.WhiteRealFide != null || club?.IsAnon(c.White, c.WhiteFide) == true;
        var blackAnon = c.BlackRealName != null || c.BlackRealFide != null || club?.IsAnon(c.Black, c.BlackFide) == true;

        var pairing = (await LeagueGameLinks.ResolveAsync(db, [c], ct)).GetValueOrDefault(c.Id);
        var guessed = pairing is null && (whiteAnon || blackAnon) ? await OnlyExactPairingAsync(c, club, ct) : null;
        var (white, black) = BySide(pairing);
        var (guessedWhite, guessedBlack) = BySide(guessed);
        if (whiteAnon && white.Elo is null && white.Fide is null) white = guessedWhite;
        if (blackAnon && black.Elo is null && black.Fide is null) black = guessedBlack;

        var whiteFide = c.WhiteFide ?? c.WhiteRealFide ?? white.Fide;
        var blackFide = c.BlackFide ?? c.BlackRealFide ?? black.Fide;
        var roster = await RosterEloAsync(new[] { whiteFide, blackFide }, c.Year, ct);
        int? Pick(int? stored, int? paired, string? fide) => Valid(stored) ?? Valid(paired) ?? (fide is null ? null : roster.GetValueOrDefault(fide));
        var w = Pick(c.WhiteElo, white.Elo, whiteFide);
        var b = Pick(c.BlackElo, black.Elo, blackFide);
        return new GameStrength(whiteAnon ? Anon(w) : Named(w), blackAnon ? Anon(b) : Named(b));
    }

    private static int? Valid(int? elo) => elo > 0 ? elo : null;
    private static PlayerStrength? Named(int? elo) => Valid(elo) is int e ? new PlayerStrength(e, null) : null;
    private static PlayerStrength? Anon(int? elo) => Valid(elo) is int e ? new PlayerStrength(null, $"{e / 100 * 100}–{e / 100 * 100 + 99}") : null;

    /// <summary>Elo und FIDE-ID je Farbe aus einer Ligapaarung; ohne Paarung oder Farbe leer.</summary>
    private static ((int? Elo, string? Fide) White, (int? Elo, string? Fide) Black) BySide(LeagueGame? g) => g?.HomeColor switch
    {
        "w" => ((Valid(g.HomeElo), g.HomeFide), (Valid(g.AwayElo), g.AwayFide)),
        "s" => ((Valid(g.AwayElo), g.AwayFide), (Valid(g.HomeElo), g.HomeFide)),
        _ => ((null, null), (null, null)),
    };

    /// <summary>Partie ohne feste Zuordnung: die Paarung, wenn der Finder GENAU EINE genaue kennt (nur für die Stärke, nichts wird gespeichert).</summary>
    private async Task<LeagueGame?> OnlyExactPairingAsync(LeagueClubGame c, LeagueClub? club, CancellationToken ct)
    {
        var date = LeagueClubService.FullDateOf(PgnParser.SplitGames(c.Pgn).FirstOrDefault().Headers?.GetValueOrDefault("Date"));
        var exact = (await new LeaguePairingFinder(db).ForManyAsync([LeaguePairingFinder.QueryOf(c, date)], ct, club))[0]
            .Where(o => o.Exact && !o.Open).ToList();
        return exact.Count == 1 ? await db.LeagueGames.AsNoTracking().FirstOrDefaultAsync(g => g.Id == exact[0].Id, ct) : null;
    }

    /// <summary>Elo je FIDE-ID aus den Meldelisten: international vor national, die Saison am nächsten zur Partie, sonst die jüngste.</summary>
    private async Task<Dictionary<string, int>> RosterEloAsync(IEnumerable<string?> fides, int? year, CancellationToken ct)
    {
        var ids = fides.Where(f => !string.IsNullOrEmpty(f)).Select(f => f!).Distinct().ToList();
        if (ids.Count == 0) return [];
        var rows = await (from p in db.LeaguePlayers.AsNoTracking()
                          join t in db.LeagueTournaments.AsNoTracking() on p.Tnr equals t.Tnr
                          where p.FideId != null && ids.Contains(p.FideId)
                          select new { Fide = p.FideId!, p.EloI, p.EloN, t.Season }).ToListAsync(ct);
        static int Start(string season) => season.Length >= 4 && int.TryParse(season[..4], out var y) ? y : 0;
        return rows.Select(r => (r.Fide, Elo: Valid(r.EloI) ?? Valid(r.EloN), Start: Start(r.Season)))
            .Where(r => r.Elo != null)
            .GroupBy(r => r.Fide)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => year is int y ? Math.Abs(r.Start - y) : 0).ThenByDescending(r => r.Start).First().Elo!.Value);
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
