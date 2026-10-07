using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// DIE EINE Stelle, die den Verein einer LeagueHub-Anfrage bestimmt (Mandanten-Schritt 2026-10-07, Wunsch: „LeagueHub für
/// mehrere Vereine — streng getrennt"). Die Controller lösen ihn auf (<see cref="ResolveAsync"/>) und reichen den
/// <see cref="LeagueClub"/> als Parameter in die Dienste; kein Dienst fragt selbst nach, zu welchem Verein ein Konto gehört.
/// <list type="bullet">
/// <item><b>Zugehörigkeit</b>: über die Gruppen des Kontos (<see cref="LeagueClubMember"/>: Gruppe → Verein). Admins gehören zu
/// ALLEN Vereinen. Die Rechte (<c>league.view/contribute/manage</c>) bleiben, wie sie sind (Rollen, auch über Gruppen) — sie
/// wirken nur in den Vereinen, zu denen das Konto gehört. Die System-Gruppe „Everyone" zählt nicht (wie im
/// <see cref="PermissionResolver"/>: dort trägt sie auch keine Rollen).</item>
/// <item><b>Der Verein der Anfrage</b>: <c>?club=&lt;id&gt;</c> (LeagueHub schickt ihn immer mit, Wert aus <c>GET /api/league/me</c>);
/// ohne Parameter der einzige Verein des Kontos, bei einem Admin der einzige Verein SEINER Gruppen; sonst 400 <c>clubRequired</c>.
/// Ein Verein, zu dem das Konto nicht gehört → 403 <c>forbidden</c> (ein unbekannter ebenso, Admins bekommen 404); ein Konto ohne
/// jeden Verein → 403 <c>noClub</c>.</item>
/// <item><b>Teilen-Links</b> (<c>/api/league/s/{token}/…</c>): der Verein kommt aus <see cref="LeagueShare.ClubId"/>
/// (<see cref="LeagueService.ShareContextAsync"/>), nie aus der Anfrage.</item>
/// </list>
/// </summary>
public sealed class LeagueClubResolver(AppDbContext db, PermissionResolver? permissions = null)
{
    /// <summary>Name des Query-Parameters.</summary>
    public const string QueryKey = "club";

    /// <summary>Ergebnis der Auflösung: der Verein, oder Status (400/403/404) + Grund.</summary>
    public sealed record Resolution(LeagueClub? Club, int Status, string? Reason)
    {
        public static Resolution Ok(LeagueClub club) => new(club, 200, null);
    }

    private List<LeagueClub>? _all;

    /// <summary>Alle Vereine (je Anfrage einmal gelesen, Id aufsteigend).</summary>
    public async Task<List<LeagueClub>> AllAsync(CancellationToken ct = default) =>
        _all ??= await db.LeagueClubs.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct);

    public async Task<LeagueClub?> ByIdAsync(int id, CancellationToken ct = default) =>
        (await AllAsync(ct)).FirstOrDefault(c => c.Id == id);

    /// <summary>Die Vereine, zu denen das Konto über seine Gruppen gehört (ohne Admin-Sonderrecht).</summary>
    public async Task<List<LeagueClub>> GroupClubsAsync(int userId, CancellationToken ct = default)
    {
        var ids = await GroupClubIdsQuery(db, userId).ToListAsync(ct);
        return (await AllAsync(ct)).Where(c => ids.Contains(c.Id)).ToList();
    }

    /// <summary>Die Vereine, in denen das Konto wirken darf: Admin alle, sonst die seiner Gruppen.</summary>
    public async Task<List<LeagueClub>> ClubsOfAsync(int userId, bool isAdmin, CancellationToken ct = default) =>
        isAdmin ? await AllAsync(ct) : await GroupClubsAsync(userId, ct);

    /// <summary>Gehört das Konto zu diesem Verein? (Admin: zu jedem.)</summary>
    public async Task<bool> IsMemberAsync(int userId, bool isAdmin, int clubId, CancellationToken ct = default) =>
        isAdmin || await GroupClubIdsQuery(db, userId).AnyAsync(id => id == clubId, ct);

    /// <summary>Verwalter DIESES Vereins: Admin, oder <c>league.manage</c> UND Mitglied. Für Wege, die nicht über
    /// <see cref="ResolveAsync"/> laufen (RookHubs Kopie einer Vereinspartie).</summary>
    public async Task<bool> CanManageAsync(int userId, bool isAdmin, int clubId, CancellationToken ct = default)
    {
        if (isAdmin) return true;
        if (permissions == null || !(await permissions.GetAsync(userId, ct)).Has(Permissions.LeagueManage)) return false;
        return await IsMemberAsync(userId, false, clubId, ct);
    }

    /// <summary>
    /// Der Verein der Anfrage. <paramref name="requested"/> = <c>?club=</c>; <paramref name="preferred"/> = wenn keiner verlangt
    /// ist und das Konto zu mehreren gehört, dieser (z. B. der Verein der Partie bei <c>GET …/club/games/{id}</c>, die RookHubs
    /// Partie-Seite ohne Vereinswahl öffnet) — nur, wenn das Konto dazugehört.
    /// </summary>
    public async Task<Resolution> ResolveAsync(int userId, bool isAdmin, int? requested, CancellationToken ct = default,
        int? preferred = null)
    {
        var mine = await ClubsOfAsync(userId, isAdmin, ct);
        if (requested is int id)
        {
            if (mine.FirstOrDefault(c => c.Id == id) is { } club) return Resolution.Ok(club);
            return isAdmin ? new(null, 404, "unknownClub") : new(null, 403, "forbidden");
        }
        if (mine.Count == 0) return new(null, 403, "noClub");
        if (mine.Count == 1) return Resolution.Ok(mine[0]);
        if (preferred is int p && mine.FirstOrDefault(c => c.Id == p) is { } pick) return Resolution.Ok(pick);
        if (isAdmin && await GroupClubsAsync(userId, ct) is { Count: 1 } own) return Resolution.Ok(own[0]);
        return new(null, 400, "clubRequired");
    }

    /// <summary>Die Vereins-Ids der Gruppen eines Kontos (ohne Admin-Sonderrecht) als Abfrage.</summary>
    private static IQueryable<int> GroupClubIdsQuery(AppDbContext db, int userId) =>
        from ug in db.UserGroups
        join m in db.LeagueClubMembers on ug.GroupId equals m.GroupId
        where ug.UserId == userId
        select m.ClubId;

    /// <summary>Die Vereine eines Kontos ohne Dienst-Instanz (RookHubs „Meine Partien": eine Kopie wird nur mit Vereinspartien
    /// DEINER Vereine verbunden) — Admin: alle.</summary>
    public static async Task<HashSet<int>> ClubIdsOfAsync(AppDbContext db, int userId, CancellationToken ct = default)
    {
        var admin = await db.AppUsers.AsNoTracking().Where(u => u.Id == userId && u.DeletedAt == null).Select(u => u.IsAdmin)
            .FirstOrDefaultAsync(ct);
        return admin ? (await db.LeagueClubs.AsNoTracking().Select(c => c.Id).ToListAsync(ct)).ToHashSet()
            : (await GroupClubIdsQuery(db, userId).Distinct().ToListAsync(ct)).ToHashSet();
    }

    /// <summary>Die Gruppen eines Vereins (Freigabe seines Taktik-Kurses).</summary>
    public static Task<List<int>> GroupIdsOfAsync(AppDbContext db, int clubId, CancellationToken ct = default) =>
        db.LeagueClubMembers.AsNoTracking().Where(m => m.ClubId == clubId).Select(m => m.GroupId).ToListAsync(ct);
}
