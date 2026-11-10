using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Scoresheet;

/// <summary>Ein Foto einer Einlesung: Seite ab 1, Foto auf der Seite ab 0 (0 = Hauptfoto) — dieselben Zahlen wie
/// <c>GET /api/admin/scoresheets/{id}/photo?page=&amp;view=</c>.</summary>
public sealed record ScoresheetPhotoRef(int Page, int View);

/// <summary>Aufteilung der Fotos einer Einlesung: Seitenzahl und je Seite die Zahl der WEITEREN Fotos (ohne Hauptfoto).</summary>
public sealed class ScoresheetLayoutDto
{
    public int Id { get; set; }
    public string Status { get; set; } = "pending";
    public int PageCount { get; set; }
    /// <summary>Je Seite (Index 0 = Seite 1) die Zahl der weiteren Fotos; Foto <c>view</c> läuft dort von 1 bis zu dieser Zahl.</summary>
    public List<int> ViewsPerPage { get; set; } = new();
}

/// <summary>
/// Fotos einer WARTENDEN Einlesung neu auf Seiten verteilen (Vorfall 2026-11-10, LeagueHub-Formular 113): zwei Fotos kamen als
/// „weiteres Foto derselben Seite" an, obwohl es Seite 1 und 2 waren — und noch dazu verkehrt herum (das Hauptfoto war das
/// Fortsetzungsblatt). Der Leser setzt Kästen nur aufs Hauptfoto einer Seite, also bekamen die Züge 1–60 keine. Mit
/// <see cref="RearrangeAsync"/> legt der Leser (Admin) die richtige Reihenfolge fest, BEVOR er liest. Kein Foto geht
/// verloren: jedes vorhandene muss genau einmal vorkommen.
/// </summary>
internal sealed class ScoresheetLayout(AppDbContext db, ILogger logger)
{
    /// <summary>Die aktuelle Aufteilung; <c>null</c> = unbekannte Einlesung.</summary>
    public async Task<ScoresheetLayoutDto?> GetAsync(int scanId, CancellationToken ct = default)
    {
        var scan = await db.ScoresheetScans.AsNoTracking().Where(s => s.Id == scanId)
            .Select(s => new { s.Id, s.Status, s.PageCount }).FirstOrDefaultAsync(ct);
        if (scan == null) return null;
        var pageCount = Math.Max(1, scan.PageCount);
        return new ScoresheetLayoutDto
        {
            Id = scan.Id, Status = scan.Status.ToString().ToLowerInvariant(), PageCount = pageCount,
            ViewsPerPage = await ScoresheetScanRows.ViewCountsAsync(db, scanId, pageCount, ct),
        };
    }

    /// <summary>
    /// Neue Aufteilung: <paramref name="pages"/> in Seitenreihenfolge, je Seite die Fotos — das erste wird das Hauptfoto
    /// der Seite, die weiteren ihre weiteren Fotos (in dieser Reihenfolge). Nur solange die Einlesung wartet oder gelesen
    /// wird. Gründe: <c>notFound</c>, <c>notPending</c>, <c>emptyLayout</c>, <c>emptyPage</c>, <c>tooManyPages</c>,
    /// <c>tooManyViews</c>, <c>unknownPhoto</c>, <c>duplicatePhoto</c>, <c>missingPhoto</c>.
    /// </summary>
    public async Task<(ScoresheetLayoutDto? Layout, string? Reason)> RearrangeAsync(int scanId,
        IReadOnlyList<IReadOnlyList<ScoresheetPhotoRef>>? pages, CancellationToken ct = default)
    {
        var head = await db.ScoresheetScans.AsNoTracking().Where(s => s.Id == scanId)
            .Select(s => new { s.Status, s.ContentType, s.FileName }).FirstOrDefaultAsync(ct);
        if (head == null) return (null, "notFound");
        if (!IsWaiting(head.Status)) return (null, "notPending");

        if (pages == null || pages.Count == 0) return (null, "emptyLayout");
        if (pages.Any(p => p == null || p.Count == 0)) return (null, "emptyPage");
        if (pages.Count > ScoresheetScanService.MaxPages) return (null, "tooManyPages");
        if (pages.Any(p => p.Count > ScoresheetScanService.MaxPhotosPerPage)) return (null, "tooManyViews");

        // Alle vorhandenen Fotos (ohne Verfolgung — geschrieben wird unten in einem eigenen, wiederholbaren Block).
        var photos = new Dictionary<ScoresheetPhotoRef, Photo>();
        var main = await db.ScoresheetScans.AsNoTracking().Where(s => s.Id == scanId).Select(s => s.Photo).FirstAsync(ct);
        if (main.Length > 0) photos[new(1, 0)] = new Photo(main, head.ContentType, head.FileName);
        foreach (var p in await db.ScoresheetScanPages.AsNoTracking().Where(p => p.ScoresheetScanId == scanId).ToListAsync(ct))
            photos[new(p.Page, 0)] = new Photo(p.Photo, p.ContentType, p.FileName);
        foreach (var v in await db.ScoresheetScanViews.AsNoTracking().Where(v => v.ScoresheetScanId == scanId).ToListAsync(ct))
            photos[new(v.Page, v.View)] = new Photo(v.Photo, v.ContentType, v.FileName);

        var used = new HashSet<ScoresheetPhotoRef>();
        foreach (var r in pages.SelectMany(p => p))
        {
            if (r == null || !photos.ContainsKey(r)) return (null, "unknownPhoto");
            if (!used.Add(r)) return (null, "duplicatePhoto");
        }
        if (used.Count != photos.Count) return (null, "missingPhoto");

        var layout = pages.Select(p => p.Select(r => photos[r]).ToList()).ToList();
        var applied = await WriteAsync(scanId, layout, ct);
        if (!applied) return (null, "notPending");

        logger.LogInformation("Formular-Einlesung {ScanId}: Fotos neu aufgeteilt ({Layout})", scanId,
            string.Join(" | ", pages.Select(p => string.Join(",", p.Select(r => $"{r.Page}/{r.View}")))));
        return (await GetAsync(scanId, ct), null);
    }

    private static bool IsWaiting(ScoresheetScanStatus s) => s is ScoresheetScanStatus.Pending or ScoresheetScanStatus.Running;

    private sealed record Photo(byte[] Data, string ContentType, string? FileName);

    /// <summary>Schreibt die Aufteilung; <c>false</c> = die Einlesung wartet inzwischen nicht mehr (der Leser war schneller).</summary>
    private async Task<bool> WriteAsync(int scanId, List<List<Photo>> layout, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return await ApplyAsync(scanId, layout, relational: false, ct);

        // Die Transaktion MUSS in der Execution-Strategy laufen (docs/konventionen.md, EnableRetryOnFailure). Der Block ist
        // wiederholbar: er vergisst den Stand eines abgebrochenen Versuchs (ChangeTracker.Clear), löscht ALLE Seiten 2+ und
        // weiteren Fotos der Einlesung und schreibt sie aus den vorher gelesenen Fotos neu.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var ok = await ApplyAsync(scanId, layout, relational: true, ct);
            if (ok) await tx.CommitAsync(ct);
            return ok;
        });
    }

    private async Task<bool> ApplyAsync(int scanId, List<List<Photo>> layout, bool relational, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var scan = await db.ScoresheetScans.FirstAsync(s => s.Id == scanId, ct);
        if (!IsWaiting(scan.Status)) return false;

        if (relational)
        {
            await db.ScoresheetScanPages.Where(p => p.ScoresheetScanId == scanId).ExecuteDeleteAsync(ct);
            await db.ScoresheetScanViews.Where(v => v.ScoresheetScanId == scanId).ExecuteDeleteAsync(ct);
        }
        else
        {
            db.ScoresheetScanPages.RemoveRange(await db.ScoresheetScanPages.Where(p => p.ScoresheetScanId == scanId).ToListAsync(ct));
            db.ScoresheetScanViews.RemoveRange(await db.ScoresheetScanViews.Where(v => v.ScoresheetScanId == scanId).ToListAsync(ct));
        }

        var first = layout[0][0];
        scan.Photo = first.Data;
        scan.ContentType = first.ContentType;
        scan.FileName = first.FileName;
        scan.PageCount = layout.Count;
        for (var i = 0; i < layout.Count; i++)
        {
            var page = i + 1;
            if (page >= 2)
                db.ScoresheetScanPages.Add(new ScoresheetScanPage
                {
                    ScoresheetScanId = scanId, Page = page, Photo = layout[i][0].Data, ContentType = layout[i][0].ContentType,
                    FileName = layout[i][0].FileName,
                });
            for (var j = 1; j < layout[i].Count; j++)
                db.ScoresheetScanViews.Add(new ScoresheetScanView
                {
                    ScoresheetScanId = scanId, Page = page, View = j, Photo = layout[i][j].Data, ContentType = layout[i][j].ContentType,
                    FileName = layout[i][j].FileName,
                });
        }
        await db.SaveChangesAsync(ct);
        return true;
    }
}
