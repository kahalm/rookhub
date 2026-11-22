using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Services.League;
using RookHub.Api.Services.TrainingVideos;

namespace RookHub.Api.Services.Games;

/// <summary>
/// „Teilen-Link → volle Seite“ (Wunsch 2026-11-22): öffnet ein ANGEMELDETER einen Teilen-Link zu etwas, das er auch auf dem normalen
/// Weg sehen darf, schickt ihn die Teilen-Seite auf die normale Seite mit allen Funktionen. Dieser Dienst sagt, wohin — mit
/// denselben Prüfungen wie die normale Seite:
/// <list type="bullet">
/// <item><c>g</c> (<c>/g/{token}</c>) und <c>ga</c> (<c>/ga/{token}/summary</c>) → <c>/games/{id}</c>: nur der Besitzer der Partie
/// (wie <c>GET api/games/{id}</c>, auch Admins sehen fremde Partien dort nicht).</item>
/// <item><c>cg</c> (<c>/cg/{token}</c>) und <c>s</c> (<c>/s/{token}/club-games/{id}</c>) → <c>/club-games/{id}</c>:
/// <see cref="Models.Permissions.LeagueView"/> (prüft der Aufrufer) und Mitglied des Vereins der Partie (Admin: jeder Verein) — wie
/// <c>GET api/league/club/games/{id}</c> ohne <c>?club=</c>, das bei mehreren Vereinen den der Partie nimmt.</item>
/// <item><c>tl</c> (<c>/tl/{token}</c>) → <c>/training-videos/{id}</c>: Konto der Stunde oder Admin, nur solange der Link gilt.</item>
/// <item><c>r</c> (<c>/r/{token}</c>) → <c>/reconstruct/{id}</c>: nur der Besitzer.</item>
/// </list>
/// <c>null</c> heißt immer dasselbe: unbekannter, widerrufener oder abgelaufener Link, kein Zugriff oder unbekannte Art — der Aufrufer
/// erfährt dabei nichts, was die Teilen-Ansicht nicht auch zeigt (keine Id ohne Zugriff).
/// </summary>
public sealed class ShareLinkTargets(AppDbContext db, LeagueClubResolver clubs, TrainingLessonShares lessonShares)
{
    /// <summary>Der Weg der normalen Seite (ohne Unterseite und Query), z. B. <c>/club-games/241</c>; <c>null</c> = bleibt beim Teilen-Link.</summary>
    /// <param name="canViewLeague">Hat das Konto <see cref="Models.Permissions.LeagueView"/> (Live-Stand, Admin immer)?</param>
    public async Task<string?> ResolveAsync(int userId, bool isAdmin, bool canViewLeague, string? kind, string? token, int? id,
        CancellationToken ct = default)
    {
        switch (kind)
        {
            case "g":
                if (string.IsNullOrWhiteSpace(token)) return null;
                var own = await db.SavedGames.AsNoTracking().Where(g => g.ShareToken == token && g.UserId == userId)
                    .Select(g => (int?)g.Id).FirstOrDefaultAsync(ct);
                return own is int o ? $"/games/{o}" : null;
            case "ga":
                if (string.IsNullOrWhiteSpace(token)) return null;
                var anon = await db.SavedGames.AsNoTracking().Where(g => g.AnonShareToken == token && g.UserId == userId)
                    .Select(g => (int?)g.Id).FirstOrDefaultAsync(ct);
                return anon is int a ? $"/games/{a}" : null;
            case "cg":
                if (string.IsNullOrWhiteSpace(token) || !canViewLeague) return null;
                var linked = await db.LeagueClubGames.AsNoTracking().Where(g => g.ShareToken == token)
                    .Select(g => new { g.Id, g.ClubId }).FirstOrDefaultAsync(ct);
                return linked is not null && await clubs.IsMemberAsync(userId, isAdmin, linked.ClubId, ct) ? $"/club-games/{linked.Id}" : null;
            case "s":
                // Die Id steht schon in der Adresse des Liga-Links; geprüft wird nur, ob das Konto die Partie selbst sehen darf.
                if (id is not int clubGameId || !canViewLeague) return null;
                var clubId = await db.LeagueClubGames.AsNoTracking().Where(g => g.Id == clubGameId).Select(g => (int?)g.ClubId).FirstOrDefaultAsync(ct);
                return clubId is int c && await clubs.IsMemberAsync(userId, isAdmin, c, ct) ? $"/club-games/{clubGameId}" : null;
            case "tl":
                var (state, lessonId) = await lessonShares.ResolveAsync(token, ct);
                if (state != TrainingShareState.Valid) return null;
                return await db.TrainingLessons.AsNoTracking().AnyAsync(l => l.Id == lessonId && (isAdmin || l.UserId == userId), ct)
                    ? $"/training-videos/{lessonId}" : null;
            case "r":
                if (string.IsNullOrWhiteSpace(token)) return null;
                var rec = await db.GameReconstructions.AsNoTracking().Where(r => r.ShareToken == token && r.UserId == userId)
                    .Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
                return rec is int r2 ? $"/reconstruct/{r2}" : null;
            default:
                return null;
        }
    }
}
