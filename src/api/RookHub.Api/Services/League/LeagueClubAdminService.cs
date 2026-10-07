using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Tactics;

namespace RookHub.Api.Services.League;

/// <summary>
/// Vereine als Mandanten verwalten (Mandanten-Schritt 2026-10-07; nur Admins mit <c>league.manage</c>): Verein anlegen und
/// ändern, Gruppen zuordnen. Eine Gruppe gehört zu höchstens EINEM Verein; wer in ihr ist, wirkt in diesem Verein
/// (<see cref="LeagueClubResolver"/>). Der Taktik-Kurs des Vereins (<see cref="TacticHarvestService.ClubBookOf"/>) folgt den
/// Gruppen: zuordnen gibt ihn frei, lösen nimmt die Freigabe zurück.
/// </summary>
public sealed class LeagueClubAdminService(AppDbContext db, ILogger<LeagueClubAdminService> log)
{
    public const int MaxName = 120;
    public const int MaxTeamPrefix = 80;
    public const int MaxAnonName = 60;


    /// <summary>Alle Vereine für die Verwaltung (LeagueHub `/vereine`, 0.700.0) →
    /// <c>[{ id, name, anonName, teamPrefix, region, createdAt, clubGames, groups[{ id, name, members }] }]</c>.
    /// <c>clubGames</c> zählt die Vereinspartien ohne archivierte (Query-Filter), <c>members</c> die Konten der Gruppe.</summary>
    public async Task<JsonArray> ListAsync(CancellationToken ct)
    {
        var clubs = await db.LeagueClubs.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct);
        var groups = await (from m in db.LeagueClubMembers.AsNoTracking()
                            join g in db.Groups.AsNoTracking() on m.GroupId equals g.Id
                            select new { m.ClubId, g.Id, g.Name, Members = db.UserGroups.Count(u => u.GroupId == g.Id) }).ToListAsync(ct);
        var games = await db.LeagueClubGames.AsNoTracking().GroupBy(g => g.ClubId)
            .Select(g => new { ClubId = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.ClubId, g => g.Count, ct);
        return new JsonArray(clubs.Select(c =>
        {
            var o = LeagueService.ClubJson(c);
            o["createdAt"] = DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc);
            o["clubGames"] = games.GetValueOrDefault(c.Id);
            o["groups"] = new JsonArray(groups.Where(g => g.ClubId == c.Id).OrderBy(g => g.Name)
                .Select(g => (JsonNode)new JsonObject { ["id"] = g.Id, ["name"] = g.Name, ["members"] = g.Members }).ToArray());
            return (JsonNode)o;
        }).ToArray());
    }

    /// <summary>Region eines Vereins: leer = Tirol (die Vorgabe); sonst eine aus <see cref="LeagueRegions.All"/>.</summary>
    private static string? RegionOf(string? region) =>
        string.IsNullOrWhiteSpace(region) ? LeagueRegions.Tirol
        : LeagueRegions.Valid(region.Trim().ToLowerInvariant()) ? region.Trim().ToLowerInvariant() : null;

    public async Task<(LeagueClub? Club, string? Reason)> CreateAsync(string? name, string? teamPrefix, string? anonName, string? region,
        CancellationToken ct)
    {
        var n = Clean(name, MaxName);
        var p = Clean(teamPrefix, MaxTeamPrefix);
        var a = Clean(anonName, MaxAnonName);
        var reg = RegionOf(region);
        if (n is null) return (null, "invalidName");
        if (p is null) return (null, "invalidTeamPrefix");
        if (a is null) return (null, "invalidAnonName");
        if (reg is null) return (null, "invalidRegion");
        if (await db.LeagueClubs.AnyAsync(c => c.Name == n, ct)) return (null, "duplicate");
        var club = new LeagueClub { Name = n, TeamPrefix = p, AnonName = a, Region = reg, CreatedAt = DateTime.UtcNow };
        db.LeagueClubs.Add(club);
        await db.SaveChangesAsync(ct);
        log.LogInformation("LeagueHub: Verein {Id} „{Name}“ angelegt (Region {Region})", club.Id, club.Name, club.Region);
        return (club, null);
    }

    /// <summary>Fehlende Felder bleiben; <paramref name="region"/> = „" setzt Tirol.</summary>
    public async Task<(LeagueClub? Club, string? Reason)> UpdateAsync(int id, string? name, string? teamPrefix, string? anonName, string? region,
        CancellationToken ct)
    {
        var club = await db.LeagueClubs.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (club is null) return (null, "notFound");
        if (name is not null)
        {
            if (Clean(name, MaxName) is not { } n) return (null, "invalidName");
            if (n != club.Name && await db.LeagueClubs.AnyAsync(c => c.Name == n && c.Id != id, ct)) return (null, "duplicate");
            club.Name = n;
        }
        if (teamPrefix is not null)
        {
            if (Clean(teamPrefix, MaxTeamPrefix) is not { } p) return (null, "invalidTeamPrefix");
            club.TeamPrefix = p;
        }
        if (anonName is not null)
        {
            if (Clean(anonName, MaxAnonName) is not { } a) return (null, "invalidAnonName");
            club.AnonName = a;
        }
        if (region is not null)
        {
            if (RegionOf(region) is not { } reg) return (null, "invalidRegion");
            club.Region = reg;
        }
        await db.SaveChangesAsync(ct);
        // Der Name des Taktik-Kurses folgt dem Verein.
        if (await db.Books.FirstOrDefaultAsync(b => b.FileName == TacticHarvestService.ClubBookOf(club.Id), ct) is { } book)
        {
            book.DisplayName = TacticHarvestService.ClubBookName(club);
            await db.SaveChangesAsync(ct);
        }
        return (club, null);
    }

    /// <summary>→ <c>null</c> = zugeordnet; sonst <c>clubNotFound</c>/<c>groupNotFound</c>/<c>everyone</c>.</summary>
    public async Task<string?> AddGroupAsync(int clubId, int groupId, CancellationToken ct)
    {
        if (!await db.LeagueClubs.AnyAsync(c => c.Id == clubId, ct)) return "clubNotFound";
        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null) return "groupNotFound";
        // „Everyone" hat keine Mitgliedschafts-Zeilen und trägt auch keine Rollen (PermissionResolver) — sie einem Verein
        // zuzuordnen, gäbe jedem Konto den Verein.
        if (group.IsEveryone) return "everyone";
        var old = await db.LeagueClubMembers.Where(m => m.GroupId == groupId).ToListAsync(ct);
        if (old.Any(m => m.ClubId == clubId)) return null;
        foreach (var m in old) await RevokeBookAsync(m.ClubId, groupId, ct);
        db.LeagueClubMembers.RemoveRange(old);
        db.LeagueClubMembers.Add(new LeagueClubMember { ClubId = clubId, GroupId = groupId });
        await db.SaveChangesAsync(ct);
        if (await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.FileName == TacticHarvestService.ClubBookOf(clubId), ct) is { } book
            && !await db.BookGroupAccesses.AnyAsync(a => a.BookId == book.Id && a.GroupId == groupId, ct))
        {
            db.BookGroupAccesses.Add(new BookGroupAccess { BookId = book.Id, GroupId = groupId });
            await db.SaveChangesAsync(ct);
        }
        log.LogInformation("LeagueHub: Gruppe {Group} gehört jetzt zu Verein {Club}", groupId, clubId);
        return null;
    }

    public async Task<bool> RemoveGroupAsync(int clubId, int groupId, CancellationToken ct)
    {
        var m = await db.LeagueClubMembers.FirstOrDefaultAsync(x => x.ClubId == clubId && x.GroupId == groupId, ct);
        if (m is null) return false;
        db.LeagueClubMembers.Remove(m);
        await RevokeBookAsync(clubId, groupId, ct);
        await db.SaveChangesAsync(ct);
        log.LogInformation("LeagueHub: Gruppe {Group} gehört nicht mehr zu Verein {Club}", groupId, clubId);
        return true;
    }

    private async Task RevokeBookAsync(int clubId, int groupId, CancellationToken ct)
    {
        if (await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.FileName == TacticHarvestService.ClubBookOf(clubId), ct) is not { } book) return;
        db.BookGroupAccesses.RemoveRange(await db.BookGroupAccesses.Where(a => a.BookId == book.Id && a.GroupId == groupId).ToListAsync(ct));
    }

    private static string? Clean(string? s, int max)
    {
        var t = LeagueNames.Clean(s);
        return t.Length == 0 || t.Length > max ? null : t;
    }
}
