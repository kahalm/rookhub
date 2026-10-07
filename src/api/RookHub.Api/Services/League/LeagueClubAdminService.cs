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

    /// <summary>Die erlaubten Liga-Quellen eines Vereins: <c>null</c> = chess-results (Tirol), <c>ligamanager</c> = Bayern.</summary>
    public static bool ValidSource(string? source) => source is null || source == LigamanagerSource.Source;

    public async Task<JsonArray> ListAsync(CancellationToken ct)
    {
        var clubs = await db.LeagueClubs.AsNoTracking().OrderBy(c => c.Id).ToListAsync(ct);
        var groups = await (from m in db.LeagueClubMembers.AsNoTracking()
                            join g in db.Groups.AsNoTracking() on m.GroupId equals g.Id
                            select new { m.ClubId, g.Id, g.Name }).ToListAsync(ct);
        return new JsonArray(clubs.Select(c =>
        {
            var o = LeagueService.ClubJson(c);
            o["groups"] = new JsonArray(groups.Where(g => g.ClubId == c.Id).OrderBy(g => g.Name)
                .Select(g => (JsonNode)new JsonObject { ["id"] = g.Id, ["name"] = g.Name }).ToArray());
            return (JsonNode)o;
        }).ToArray());
    }

    public async Task<(LeagueClub? Club, string? Reason)> CreateAsync(string? name, string? teamPrefix, string? anonName, string? source,
        CancellationToken ct)
    {
        var n = Clean(name, MaxName);
        var p = Clean(teamPrefix, MaxTeamPrefix);
        var a = Clean(anonName, MaxAnonName);
        var src = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
        if (n is null) return (null, "invalidName");
        if (p is null) return (null, "invalidTeamPrefix");
        if (a is null) return (null, "invalidAnonName");
        if (!ValidSource(src)) return (null, "invalidSource");
        if (await db.LeagueClubs.AnyAsync(c => c.Name == n, ct)) return (null, "duplicate");
        var club = new LeagueClub { Name = n, TeamPrefix = p, AnonName = a, Source = src, CreatedAt = DateTime.UtcNow };
        db.LeagueClubs.Add(club);
        await db.SaveChangesAsync(ct);
        log.LogInformation("LeagueHub: Verein {Id} „{Name}“ angelegt (Quelle {Source})", club.Id, club.Name, club.Source ?? "chess-results");
        return (club, null);
    }

    /// <summary>Fehlende Felder bleiben; <paramref name="source"/> = „" setzt chess-results.</summary>
    public async Task<(LeagueClub? Club, string? Reason)> UpdateAsync(int id, string? name, string? teamPrefix, string? anonName, string? source,
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
        if (source is not null)
        {
            var src = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
            if (!ValidSource(src)) return (null, "invalidSource");
            club.Source = src;
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
