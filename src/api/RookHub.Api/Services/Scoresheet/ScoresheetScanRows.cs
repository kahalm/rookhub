using System.Text.Json;
using System.Text.Json.Serialization;
using Chess;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.Social;
using RookHub.Api.DTOs.Scoresheet;
using RookHub.Api.DTOs.League;

namespace RookHub.Api.Services.Scoresheet;

/// <summary>
/// Zeilen der Einlesung (Zerlegung Z6 aus <see cref="ScoresheetScanService"/>): Zeile → DTO, gespeicherte Auflösung,
/// Kopf ohne Foto, weitere Fotos je Seite und das Löschen/Leeren von Einlesungen, ohne ihre Fotos zu laden.
/// </summary>
internal static class ScoresheetScanRows
{
    /// <summary>Vermerk im Dateinamen einer übernommenen/verworfenen Liga-Einlesung (das Foto ist dann leer).</summary>
    internal const string DiscardedMark = "\u2205";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Alles außer dem Foto (das ist das Schwergewicht der Zeile).</summary>
    internal static IQueryable<ScoresheetScan> ScanHeads(AppDbContext db) => db.ScoresheetScans.AsNoTracking().Select(s => new ScoresheetScan
    {
        Id = s.Id, UserId = s.UserId, AccessKey = s.AccessKey, SavedGameId = s.SavedGameId, ContentType = s.ContentType, FileName = s.FileName,
        NotationLanguage = s.NotationLanguage, OwnerSide = s.OwnerSide, Purpose = s.Purpose, Status = s.Status, Error = s.Error, ResolutionJson = s.ResolutionJson,
        Model = s.Model, Attempts = s.Attempts, Rounds = s.Rounds, CreatedAt = s.CreatedAt, StartedAt = s.StartedAt,
        FinishedAt = s.FinishedAt, PageCount = s.PageCount, ClubId = s.ClubId,
    });

    internal static StoredResolution? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<StoredResolution>(json, Json); }
        catch (JsonException) { return null; }
    }

    /// <summary>Was an der Einlesung als Auflösung liegt.</summary>
    internal sealed class StoredResolution
    {
        public string? Language { get; set; }
        public List<ScoresheetPly> Plies { get; set; } = new();
        public List<string> Unresolved { get; set; } = new();
        public int? UnresolvedFrom { get; set; }
    }

    // ── Helfer ────────────────────────────────────────────────────────

    /// <summary>Einlesungen löschen, ohne ihr Foto zu laden (das Schwergewicht der Zeile). Schon geladene werden
    /// direkt entfernt — ein Platzhalter mit derselben Id wäre für EF ein zweites Objekt und wirft. Der Platzhalter
    /// trägt seine Fremdschlüssel: ohne sie kennt EF die Abhängigkeit nicht und löscht womöglich zuerst die Partie —
    /// MariaDB räumt die Einlesung dann per Cascade selbst weg, und das DELETE danach trifft keine Zeile mehr
    /// (gefunden vom Integrationstest).</summary>
    public static void RemoveWithoutLoading(AppDbContext db, IEnumerable<(int Id, int? UserId, int? SavedGameId)> scans)
    {
        foreach (var (id, userId, gameId) in scans)
        {
            var tracked = db.ScoresheetScans.Local.FirstOrDefault(s => s.Id == id);
            if (tracked != null) { db.ScoresheetScans.Remove(tracked); continue; }
            var stub = new ScoresheetScan { Id = id, UserId = userId, SavedGameId = gameId };
            db.ScoresheetScans.Attach(stub);
            db.ScoresheetScans.Remove(stub);
        }
    }

    /// <summary>
    /// Die Partie wird gelöscht, die Einlesung bleibt als Zeile fürs Kontingent und die Kostenbremse stehen (Zeitpunkt,
    /// Tokens, Kosten) — Foto, Antwort des Modells und Stand je Halbzug gehen mit der Partie. Ohne das Foto zu laden. Vorher
    /// ging die ganze Zeile, und mit ihr der Verbrauch: löschen und neu hochladen umging Tageszahl UND Budget.
    /// </summary>
    public static void DetachWithoutLoading(AppDbContext db, IEnumerable<(int Id, int? UserId, int? SavedGameId)> scans)
    {
        foreach (var (id, userId, gameId) in scans)
        {
            var scan = db.ScoresheetScans.Local.FirstOrDefault(s => s.Id == id);
            if (scan == null)
            {
                scan = new ScoresheetScan { Id = id, UserId = userId, SavedGameId = gameId };
                db.ScoresheetScans.Attach(scan);
            }
            scan.SavedGameId = null;
            scan.Photo = Array.Empty<byte>();
            scan.FileName = null;
            scan.TranscriptionJson = null;
            scan.ResolutionJson = null;
            var entry = db.Entry(scan);
            // Am Stub gleichen die neuen Werte den Vorgaben — ohne ausdrückliche Markierung schriebe EF nichts.
            foreach (var p in new[] { nameof(ScoresheetScan.SavedGameId), nameof(ScoresheetScan.Photo),
                         nameof(ScoresheetScan.FileName), nameof(ScoresheetScan.TranscriptionJson),
                         nameof(ScoresheetScan.ResolutionJson) })
                entry.Property(p).IsModified = true;
        }
    }

    /// <summary>Die Seiten 2+ dieser Einlesungen (Id, Einlesung) — für <see cref="RemovePagesWithoutLoading"/>.</summary>
    public static async Task<List<(int Id, int ScanId)>> PageKeysAsync(AppDbContext db, IReadOnlyCollection<int> scanIds)
    {
        if (scanIds.Count == 0) return new();
        var rows = await db.ScoresheetScanPages.Where(p => scanIds.Contains(p.ScoresheetScanId))
            .Select(p => new { p.Id, p.ScoresheetScanId }).ToListAsync();
        return rows.Select(x => (x.Id, x.ScoresheetScanId)).ToList();
    }

    /// <summary>Seiten 2+ löschen, ohne ihr Foto zu laden — mit der Partie (<see cref="DetachWithoutLoading"/> behält die
    /// Einlesung, die Fotos gehen) und mit dem Konto. In MariaDB nähme sie beim Löschen der EINLESUNG auch der
    /// Fremdschlüssel mit; die Partie zu löschen löscht die Einlesung aber nicht mehr. Der Platzhalter trägt seine
    /// Einlesung, damit EF die Reihenfolge kennt.</summary>
    public static void RemovePagesWithoutLoading(AppDbContext db, IEnumerable<(int Id, int ScanId)> pages)
    {
        foreach (var (id, scanId) in pages)
        {
            var tracked = db.ScoresheetScanPages.Local.FirstOrDefault(p => p.Id == id);
            if (tracked != null) { db.ScoresheetScanPages.Remove(tracked); continue; }
            var stub = new ScoresheetScanPage { Id = id, ScoresheetScanId = scanId };
            db.ScoresheetScanPages.Attach(stub);
            db.ScoresheetScanPages.Remove(stub);
        }
    }

    /// <summary>Die weiteren Fotos dieser Einlesungen (Id, Einlesung) — für <see cref="RemoveViewsWithoutLoading"/>.</summary>
    public static async Task<List<(int Id, int ScanId)>> ViewKeysAsync(AppDbContext db, IReadOnlyCollection<int> scanIds)
    {
        if (scanIds.Count == 0) return new();
        var rows = await db.ScoresheetScanViews.Where(v => scanIds.Contains(v.ScoresheetScanId))
            .Select(v => new { v.Id, v.ScoresheetScanId }).ToListAsync();
        return rows.Select(x => (x.Id, x.ScoresheetScanId)).ToList();
    }

    /// <summary>Weitere Fotos löschen, ohne sie zu laden — überall, wo <see cref="RemovePagesWithoutLoading"/> die Seiten
    /// abräumt (0.736.0).</summary>
    public static void RemoveViewsWithoutLoading(AppDbContext db, IEnumerable<(int Id, int ScanId)> views)
    {
        foreach (var (id, scanId) in views)
        {
            var tracked = db.ScoresheetScanViews.Local.FirstOrDefault(v => v.Id == id);
            if (tracked != null) { db.ScoresheetScanViews.Remove(tracked); continue; }
            var stub = new ScoresheetScanView { Id = id, ScoresheetScanId = scanId };
            db.ScoresheetScanViews.Attach(stub);
            db.ScoresheetScanViews.Remove(stub);
        }
    }

    internal static async Task<(byte[] Data, string ContentType)?> ViewPhotoAsync(AppDbContext db, int scanId, int page, int view, CancellationToken ct = default)
    {
        var v = await db.ScoresheetScanViews.AsNoTracking()
            .Where(x => x.ScoresheetScanId == scanId && x.Page == Math.Max(1, page) && x.View == view)
            .Select(x => new { x.Photo, x.ContentType }).FirstOrDefaultAsync(ct);
        return v == null || v.Photo.Length == 0 ? null : (v.Photo, v.ContentType);
    }

    internal static async Task<List<int>> ViewCountsAsync(AppDbContext db, int scanId, int pageCount, CancellationToken ct)
    {
        var counts = await db.ScoresheetScanViews.AsNoTracking().Where(v => v.ScoresheetScanId == scanId)
            .GroupBy(v => v.Page).Select(g => new { Page = g.Key, Count = g.Count() }).ToListAsync(ct);
        return Enumerable.Range(1, pageCount).Select(p => counts.FirstOrDefault(c => c.Page == p)?.Count ?? 0).ToList();
    }

    /// <summary>Die Schlüssel der Einlesungen, die <see cref="RemoveWithoutLoading"/> braucht.</summary>
    public static async Task<List<(int Id, int? UserId, int? SavedGameId)>> KeysAsync(IQueryable<ScoresheetScan> query)
    {
        var rows = await query.Select(s => new { s.Id, s.UserId, s.SavedGameId }).ToListAsync();
        return rows.Select(x => (x.Id, x.UserId, x.SavedGameId)).ToList();
    }

    internal static ScoresheetScanDto ToDto(ScoresheetScan s)
    {
        var stored = Deserialize(s.ResolutionJson);
        return new ScoresheetScanDto
        {
            Id = s.Id,
            Status = s.Status.ToString().ToLowerInvariant(),
            Error = s.Error,
            SavedGameId = s.SavedGameId,
            NotationLanguage = s.NotationLanguage,
            OwnerSide = s.OwnerSide,
            FileName = s.FileName,
            CreatedAt = s.CreatedAt,
            StartedAt = s.StartedAt,
            FinishedAt = s.FinishedAt,
            Rounds = s.Rounds,
            PageCount = Math.Max(1, s.PageCount),
            MoveCount = stored?.Plies.Count ?? 0,
            UncertainCount = stored?.Plies.Count(p => p.Uncertain && !p.Confirmed) ?? 0,
            UnresolvedCount = stored?.Unresolved.Count ?? 0,
        };
    }

    internal static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    internal static string? CleanFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = Path.GetFileName(name.Trim()).Replace("\"", "").Replace("\r", "").Replace("\n", "");
        return n.Length == 0 ? null : n.Length > 200 ? n[..200] : n;
    }
}
