using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>Wer auf einen Entwurf zugreift: ein Konto (Verwalter dürfen alle), oder ohne Konto der Schlüssel des Browsers.</summary>
/// <summary>Wer auf Entwürfe zugreift — und in welchem Verein (Mandanten-Schritt 2026-10-07: ein Entwurf gehört dem Verein, für
/// den er eingereicht wurde; die anderen Vereine sehen ihn nicht, auch ihre Verwalter nicht).</summary>
public sealed record DraftActor(int? UserId, string? Key, bool Manager, int ClubId)
{
    public static DraftActor User(int userId, bool manager, int clubId) => new(userId, null, manager, clubId);
    public static DraftActor Anonymous(string? key, int clubId) => new(null, key, false, clubId);
}

/// <summary>
/// Entwürfe von PGN-Importen (<see cref="LeagueClubDraft"/>, 0.595.0) — analog zu den offenen Partieformularen: jede
/// eingereichte Partieliste liegt sofort online, mit dem Stand der Übersicht und den schon importierten Partien. Wer
/// abbricht, macht später weiter; ein Verwalter (<c>league.manage</c>) sieht alle und kann den Import fertigstellen.
/// Abgeschlossen oder verworfen wird die Zeile gelöscht (der Rohtext nennt die Spieler des Vereins noch mit Namen).
/// </summary>
public class LeagueClubDraftService(AppDbContext db, Func<DateTime>? now = null)
{
    /// <summary>Ohne Bewegung so lange aufgehoben, danach gelöscht (beim nächsten Anlegen oder Auflisten).</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    public const int MaxOpenPerUser = 20;
    /// <summary>Ohne Konto je Adresse — ein Teilen-Link soll kein Lager für fremde Texte werden.</summary>
    public const int MaxOpenPerIp = 5;
    public const int MaxStateChars = 4_000_000;

    private DateTime Now => (now ?? (() => DateTime.UtcNow))();

    public async Task<(LeagueClubDraftDto? Draft, string? Reason)> CreateAsync(int clubId, int? userId, string? ipHash, string pgn,
        string? source, string? label, CancellationToken ct = default)
    {
        await PurgeAsync(ct);
        var open = userId is int u
            ? await db.LeagueClubDrafts.CountAsync(d => d.UserId == u, ct)
            : await db.LeagueClubDrafts.CountAsync(d => d.UserId == null && d.AnonIpHash == ipHash, ct);
        if (open >= (userId is null ? MaxOpenPerIp : MaxOpenPerUser)) return (null, "tooManyDrafts");
        var d = new LeagueClubDraft
        {
            ClubId = clubId,
            UserId = userId,
            AccessKey = userId is null ? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant() : null,
            AnonIpHash = userId is null ? ipHash : null,
            Source = Clip(source, 16), Label = Clip(label, 300), Pgn = pgn,
            GameCount = Math.Min(LeagueClubService.MaxImportGames,
                PgnParser.SplitGames(pgn).Count(g => !string.IsNullOrWhiteSpace(g.MoveText))),
            CreatedAt = Now, UpdatedAt = Now,
        };
        db.LeagueClubDrafts.Add(d);
        await db.SaveChangesAsync(ct);
        var dto = ToDto(d, null);
        dto.Key = d.AccessKey;
        return (dto, null);
    }

    /// <summary>Die eigenen offenen Entwürfe (Konto) bzw. die zu den Schlüsseln dieses Browsers (ohne Konto).</summary>
    public async Task<List<LeagueClubDraftDto>> ListAsync(DraftActor a, IReadOnlyCollection<string>? keys = null, CancellationToken ct = default)
    {
        await PurgeAsync(ct);
        var q = db.LeagueClubDrafts.AsNoTracking().Where(d => d.ClubId == a.ClubId);
        if (a.UserId is int u) q = q.Where(d => d.UserId == u);
        else
        {
            var k = (keys ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Take(50).ToList();
            if (k.Count == 0) return [];
            q = q.Where(d => d.UserId == null && d.AccessKey != null && k.Contains(d.AccessKey));
        }
        var rows = await q.OrderByDescending(d => d.UpdatedAt).Select(Light).ToListAsync(ct);
        return rows.Select(d => { var dto = ToDto(d, null); if (a.UserId is null) dto.Key = d.AccessKey; return dto; }).ToList();
    }

    /// <summary>Alle offenen Entwürfe des Vereins — für Verwalter, damit nichts liegen bleibt (auch über Teilen-Links).</summary>
    public async Task<List<LeagueClubDraftDto>> ListAllAsync(int clubId, int managerId, CancellationToken ct = default)
    {
        await PurgeAsync(ct);
        var rows = await db.LeagueClubDrafts.AsNoTracking().Where(d => d.ClubId == clubId).OrderByDescending(d => d.UpdatedAt).Take(100)
            .Select(Light).ToListAsync(ct);
        var ids = rows.Where(d => d.UserId != null).Select(d => d.UserId!.Value).Distinct().ToList();
        var names = await db.AppUsers.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        return rows.Select(d =>
        {
            var dto = ToDto(d, d.UserId is int u ? names.GetValueOrDefault(u) : null);
            dto.Mine = d.UserId == managerId;
            return dto;
        }).ToList();
    }

    public async Task<LeagueClubDraftDetailDto?> GetAsync(DraftActor a, int? id, CancellationToken ct = default)
    {
        var d = await Find(a, id).AsNoTracking().FirstOrDefaultAsync(ct);
        if (d == null) return null;
        var light = ToDto(d, null);
        return new LeagueClubDraftDetailDto
        {
            Id = d.Id, Key = a.UserId is null ? d.AccessKey : null, Source = d.Source, Label = d.Label, GameCount = d.GameCount,
            ImportedCount = light.ImportedCount, CreatedAt = d.CreatedAt, UpdatedAt = d.UpdatedAt, ViaShareLink = d.UserId == null,
            Mine = a.UserId != null && d.UserId == a.UserId,
            Pgn = d.Pgn, State = d.StateJson, Imported = ParseImported(d.Imported),
        };
    }

    /// <summary>Stand der Übersicht und/oder die importierten Partien speichern; <c>false</c> = nicht gefunden.</summary>
    public async Task<(bool Found, string? Reason)> SaveAsync(DraftActor a, int? id, LeagueClubDraftSaveRequest req, CancellationToken ct = default)
    {
        if (req.State is { Length: > MaxStateChars }) return (true, "tooLarge");
        var d = await Find(a, id).FirstOrDefaultAsync(ct);
        if (d == null) return (false, null);
        if (req.State != null) d.StateJson = req.State;
        if (req.Imported != null)
            d.Imported = string.Join(',', req.Imported.Where(i => i > 0 && i <= LeagueClubService.MaxImportGames).Distinct().OrderBy(i => i));
        d.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>Abgeschlossen oder verworfen — die Zeile samt Rohtext geht.</summary>
    public async Task<bool> DeleteAsync(DraftActor a, int? id, CancellationToken ct = default)
    {
        var d = await Find(a, id).FirstOrDefaultAsync(ct);
        if (d == null) return false;
        db.LeagueClubDrafts.Remove(d);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Für wen Übersicht und Import rechnen, wenn sie zu einem Entwurf gehören: der, der ihn eingereicht hat — ein
    /// Verwalter, der fertigstellt, bekommt so die Vorgaben des Einreichers (seine Seite „du selbst" wird ersetzt), und die
    /// Partien tragen ihn als Hochladenden (ohne Konto: niemanden). <c>null</c> = kein Entwurf mit Zugriff → wie ohne.</summary>
    public async Task<(bool Found, int? UserId)> ActingUserAsync(DraftActor a, int draftId, CancellationToken ct = default)
    {
        var d = await Find(a, draftId).AsNoTracking().Select(x => new { x.UserId }).FirstOrDefaultAsync(ct);
        return d == null ? (false, null) : (true, d.UserId);
    }

    private IQueryable<LeagueClubDraft> Find(DraftActor a, int? id)
    {
        var q = db.LeagueClubDrafts.Where(d => d.ClubId == a.ClubId);
        if (a.UserId is int u)
            return id is int i ? q.Where(d => d.Id == i && (a.Manager || d.UserId == u)) : q.Where(_ => false);
        var key = a.Key ?? "";
        return q.Where(d => d.UserId == null && d.AccessKey == key && key != "");
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        var cutoff = Now - Retention;
        var old = await db.LeagueClubDrafts.Where(d => d.UpdatedAt < cutoff).ToListAsync(ct);
        if (old.Count == 0) return;
        db.LeagueClubDrafts.RemoveRange(old);
        await db.SaveChangesAsync(ct);
    }

    // Für Listen OHNE Rohtext und Stand — die können Megabytes groß sein.
    private static readonly System.Linq.Expressions.Expression<Func<LeagueClubDraft, LeagueClubDraft>> Light = d => new LeagueClubDraft
    {
        Id = d.Id, ClubId = d.ClubId, UserId = d.UserId, AccessKey = d.AccessKey, Source = d.Source, Label = d.Label, GameCount = d.GameCount,
        Imported = d.Imported, CreatedAt = d.CreatedAt, UpdatedAt = d.UpdatedAt,
    };

    private static LeagueClubDraftDto ToDto(LeagueClubDraft d, string? owner) => new()
    {
        Id = d.Id, Source = d.Source, Label = d.Label, GameCount = d.GameCount, ImportedCount = ParseImported(d.Imported).Count,
        CreatedAt = d.CreatedAt, UpdatedAt = d.UpdatedAt, Owner = owner, ViaShareLink = d.UserId == null,
    };

    private static List<int> ParseImported(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out var i) ? i : 0).Where(i => i > 0).ToList();

    private static string? Clip(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length > max ? s.Trim()[..max] : s.Trim();
}
