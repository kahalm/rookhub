using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Engines;
using RookHub.Api.DTOs.Games;
using RookHub.Api.Models;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Services.League;

/// <summary>
/// Teilen-Link für EINE Vereinspartie (Wunsch 2026-11-09: „ich will auch /club-games/241 anonym teilen (+ summary) gleich wie meine
/// eigenen games"): Token an der Partie (<see cref="LeagueClubGame.ShareToken"/>), öffentlich unter <c>/cg/{token}</c>, gültig bis zum
/// Widerruf — wie <see cref="SavedGame.ShareToken"/> und anders als <see cref="LeagueGameShares"/>, deren versiegelte Marken nur
/// ablaufen. Eine archivierte Partie fällt über den globalen Filter heraus, der Link ist dann tot. Der Link öffnet nur das Lesen
/// dieser einen Partie, ihrer Bewertungen, Fehler-Vergleiche und der fertigen Zusammenfassung.
/// <para>Anonymisierte Partien: <see cref="LeagueClubGame.WhiteRealName"/> und Co. verlassen den Dienst nie. Das PGN wird neu gebaut (nur
/// Hauptvariante, ohne Event und ohne Elo der ersetzten Seite), siehe <see cref="SafeSummary"/> für den Text.</para>
/// </summary>
public sealed class ClubGameLinks(AppDbContext db, LeagueClubService club, ClubGameSummaries summaries, ClubGameComparisons comparisons)
{
    /// <summary>Was der Link von der Partie zeigt — bewusst nur diese Felder: keine Id, keine FIDE-Spalten außerhalb des PGN, keine
    /// Paarung (<see cref="LeagueClubGame.LeagueGameId"/>) und keine Rechte.</summary>
    public sealed record SharedClubGame(string White, string Black, string Result, string Pgn, int? WhiteElo, int? BlackElo, int? Year,
        string? Event, string? OwnSide);

    /// <summary>Token der Partie (<c>null</c> = nicht geteilt); <c>Known</c> = false: die Partie gehört nicht zu diesem Verein.</summary>
    public async Task<(bool Known, string? Token)> TokenAsync(LeagueClub c, int id, CancellationToken ct)
    {
        var row = await ClubGameReader.GamesOf(db, c).AsNoTracking().Where(g => g.Id == id).Select(g => new { g.ShareToken }).FirstOrDefaultAsync(ct);
        return row is null ? (false, null) : (true, row.ShareToken);
    }

    /// <summary>Link anlegen; gibt es schon einen, bleibt er (mehrfaches Teilen macht den alten nicht ungültig). <c>null</c> = fremd/unbekannt.</summary>
    public async Task<string?> CreateAsync(LeagueClub c, int id, CancellationToken ct)
    {
        var g = await ClubGameReader.GamesOf(db, c).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return null;
        if (g.ShareToken is not null) return g.ShareToken;
        g.ShareToken = await ShareTokens.NewUniqueAsync(t => db.LeagueClubGames.IgnoreQueryFilters().AnyAsync(x => x.ShareToken == t, ct));
        await db.SaveChangesAsync(ct);
        return g.ShareToken;
    }

    /// <summary>Link widerrufen: das Token ist weg, ein neues Teilen vergibt ein neues. <c>false</c> = fremd/unbekannt.</summary>
    public async Task<bool> RevokeAsync(LeagueClub c, int id, CancellationToken ct)
    {
        var g = await ClubGameReader.GamesOf(db, c).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return false;
        g.ShareToken = null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Partie und Verein des Links; <c>null</c> = kein gültiger Link.</summary>
    private async Task<(LeagueClubGame Game, LeagueClub Club)?> OpenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var g = await db.LeagueClubGames.AsNoTracking().FirstOrDefaultAsync(x => x.ShareToken == token, ct);
        if (g is null) return null;
        var c = await db.LeagueClubs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == g.ClubId, ct);
        return c is null ? null : (g, c);
    }

    /// <summary>Die Partie des Links; <c>null</c> = ungültig oder widerrufen.</summary>
    public async Task<SharedClubGame?> ReadAsync(string token, CancellationToken ct)
    {
        if (await OpenAsync(token, ct) is not var (g, c)) return null;
        var own = ClubGameReader.OwnSideOf(c, g);
        if (!g.Anonymized) return new SharedClubGame(g.White, g.Black, g.Result, g.Pgn, g.WhiteElo, g.BlackElo, g.Year, g.Event, own);
        int? whiteElo = c.IsAnon(g.White, g.WhiteFide) ? null : g.WhiteElo, blackElo = c.IsAnon(g.Black, g.BlackFide) ? null : g.BlackElo;
        var pgn = ClubPgnText.PgnOf(new LeagueClubGame { White = g.White, Black = g.Black, Result = g.Result, Year = g.Year, WhiteElo = whiteElo,
            BlackElo = blackElo, WhiteFide = g.WhiteFide, BlackFide = g.BlackFide }, ClubPgnText.SansOf(g.Pgn));
        return new SharedClubGame(g.White, g.Black, g.Result, pgn, whiteElo, blackElo, g.Year, null, own);
    }

    /// <summary>Bewertungen derselben Partie; <c>null</c> = ungültiger Link.</summary>
    public async Task<GameEvalsDto?> EvalsAsync(string token, CancellationToken ct) =>
        await OpenAsync(token, ct) is var (g, c) ? await club.EvalsAsync(c, g.Id, ct) : null;

    /// <summary>Fehler-Vergleiche derselben Partie, nur lesend; ohne <c>Title</c> (trägt Spielernamen) und <c>Error</c> (Technik) wie beim
    /// Liga-Teilen-Link. <c>null</c> = ungültiger Link.</summary>
    public async Task<List<ClubMistakeComparisonDto>?> ComparisonsAsync(string token, CancellationToken ct)
    {
        if (await OpenAsync(token, ct) is not var (g, c)) return null;
        return (await comparisons.ForGameAsync(c, g.Id, ct))?.Select(x => x with { Comparison = x.Comparison with { Title = null, Error = null } }).ToList();
    }

    /// <summary>Fertige Zusammenfassung, nur lesend: ohne Text <c>none</c>; Warte-Stand, Modell und Fehler bleiben Vereinsmitgliedern
    /// vorbehalten. <c>null</c> = ungültiger Link.</summary>
    public async Task<ClubGameSummaryDto?> SummaryAsync(string token, CancellationToken ct)
    {
        if (await OpenAsync(token, ct) is not var (g, c)) return null;
        var s = await summaries.GetAsync(c, g.Id, ct);
        if (s?.Markdown is null) return new ClubGameSummaryDto("none", null, null, null, false, null, null);
        var text = g.Anonymized ? SafeSummary(s.Markdown, g) : s.Markdown;
        return text is null
            ? new ClubGameSummaryDto("none", null, null, null, false, null, null)
            : new ClubGameSummaryDto("done", text, s.DoneAt, null, s.Stale, null, null);
    }

    private static readonly Regex PairingMention = new(@"[\s,·–-]*\b(?:Runde|Round|Rd\.?|Brett|Board)\s*\d+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Der Text einer anonymisierten Partie für Leser ohne Konto. Geschrieben wird er aus der Admin-Sicht, also mit
    /// <c>leagueGameLabel</c> (Runde und Brett) und ohne Klarnamen — Fund 2026-11-09: mindestens ein Text nennt „Runde 3, Brett 5", und
    /// Runde + Brett + Gegner ergäben über die öffentlichen Paarungen den Spieler (wie <see cref="LeagueGameShares.SharedGame"/>);
    /// das wird hier entfernt. Steht trotzdem ein interner Name oder eine FIDE-ID darin (<see cref="LeagueClubGame.WhiteRealName"/> usw.),
    /// gibt es keinen Text: lieber keine Zusammenfassung als ein Klarname. Prüft alle Namensteile ab drei Zeichen — ein Spieler „Bauer"
    /// sperrt also auch Texte, die den Schach-Bauern meinen; gewollt so vorsichtig.
    /// </summary>
    internal static string? SafeSummary(string markdown, LeagueClubGame g)
    {
        var parts = new[] { g.WhiteRealName, g.BlackRealName }.Where(n => n is not null)
            .SelectMany(n => Regex.Split(n!, @"[\s,.\-]+")).Where(t => t.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parts)
            if (Regex.IsMatch(markdown, $@"\b{Regex.Escape(part)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return null;
        foreach (var fide in new[] { g.WhiteRealFide, g.BlackRealFide })
            if (!string.IsNullOrWhiteSpace(fide) && markdown.Contains(fide, StringComparison.Ordinal)) return null;
        return PairingMention.Replace(markdown, "");
    }
}
