using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Games;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Eine Vereinspartie über den Liga-Teilen-Link <c>/s/{token}/club-games/{id}</c> (RookHub-Seite, 0.838.3). EINE Regel für die
/// Endpunkte unter <c>api/league/s/{token}/club/games/{id}</c> und die Link-Vorschau (2026-11-22): der Link muss gelten und die Partie an
/// einem Brett SEINER Begegnung hängen (<see cref="LeagueFixtureGames.FixtureHasClubGameAsync"/>) — jede andere Vereinspartie bleibt
/// ohne Anmeldung unsichtbar. Die Vorschau liest die Partie wie <c>/cg/</c> über <see cref="ClubGameLinks.Project"/>: anonymisiert nur der
/// Anonymisierungs-Name, ohne Event und ohne Elo der ersetzten Seite.
/// </summary>
public sealed class LeagueShareClubGames(AppDbContext db, LeagueService league, LeagueFixtureGames fixtures, LeagueClubService club)
{
    /// <summary>Der Verein des Links, wenn <paramref name="id"/> an einem Brett seiner Begegnung hängt; sonst <c>null</c> (404).</summary>
    public static async Task<LeagueClub?> ClubOfLinkedGameAsync(LeagueService league, LeagueFixtureGames fixtures, string token, int id, CancellationToken ct)
    {
        if (await league.ShareFixtureAsync(token, ct) is not { } s || await league.ShareContextAsync(token, ct) is not { } link) return null;
        return await fixtures.FixtureHasClubGameAsync(link.Club, s.Tnr, s.Round, s.Team, id, ct) ? link.Club : null;
    }

    /// <summary>Die Partie für einen Leser ohne Konto; <c>null</c> = Link ungültig/abgelaufen/widerrufen oder Partie nicht an der Begegnung.</summary>
    public async Task<ClubGameLinks.SharedClubGame?> ReadAsync(string token, int id, CancellationToken ct)
    {
        if (await ClubOfLinkedGameAsync(league, fixtures, token, id, ct) is not { } c) return null;
        var g = await ClubGameReader.GamesOf(db, c).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return g is null ? null : ClubGameLinks.Project(g, c);
    }

    /// <summary>
    /// Der rote Faden der Zusammenfassung derselben Partie für die Link-Vorschau (<see cref="SummaryRedThread"/>, Wunsch 2026-11-25); <c>null</c>
    /// = Link ungültig, keine Zusammenfassung, kein roter Faden. Der Text ist derselbe, den <c>…/s/{token}/club/games/{id}/summary</c> zeigt;
    /// bei einer ANONYMISIERTEN Partie geht der Absatz zusätzlich durch <see cref="ClubGameLinks.SafeSummary"/> (interner Name oder FIDE-ID
    /// darin → <c>null</c>, „Runde N"/„Brett N" entfernt) — die Vorschau soll nicht mehr verraten als Karte und Titel.
    /// </summary>
    public async Task<string?> RedThreadAsync(string token, int id, CancellationToken ct)
    {
        if (await ClubOfLinkedGameAsync(league, fixtures, token, id, ct) is not { } c) return null;
        var g = await ClubGameReader.GamesOf(db, c).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (g is null) return null;
        var markdown = await db.ClubGameSummaries.AsNoTracking().Where(s => s.ClubGameId == id).Select(s => s.Markdown).FirstOrDefaultAsync(ct);
        var thread = SummaryRedThread.Extract(markdown);
        return thread is null || !g.Anonymized ? thread : ClubGameLinks.SafeSummary(thread, g);
    }

    /// <summary>Bewertungen derselben Partie (Kurve); <c>null</c> wie <see cref="ReadAsync"/>.</summary>
    public async Task<GameEvalsDto?> EvalsAsync(string token, int id, CancellationToken ct) =>
        await ClubOfLinkedGameAsync(league, fixtures, token, id, ct) is { } c ? await club.EvalsAsync(c, id, ct) : null;
}
