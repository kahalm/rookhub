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

    /// <summary>Bewertungen derselben Partie (Kurve); <c>null</c> wie <see cref="ReadAsync"/>.</summary>
    public async Task<GameEvalsDto?> EvalsAsync(string token, int id, CancellationToken ct) =>
        await ClubOfLinkedGameAsync(league, fixtures, token, id, ct) is { } c ? await club.EvalsAsync(c, id, ct) : null;
}
