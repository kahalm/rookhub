using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using static RookHub.Api.Services.Scoresheet.ScoresheetScanRows;

namespace RookHub.Api.Services.Scoresheet;

/// <summary>
/// Löschweg und Fristen der Formular-Fotos (Codereview 2026-09-29 A6-016, Entscheidung 2026-10-25). Vorher blieben Fotos
/// gescheiterter Einlesungen und nie übernommener Liga-Einlesungen ohne Konto unbegrenzt liegen (Name und Unterschrift des
/// Gegners), und der Nutzer konnte eine Einlesung nicht löschen.
///
/// <para><b>Geleert, nicht gelöscht:</b> Foto(s), weitere Seiten/Fotos, Antwort des Modells und Stand gehen; die Zeile bleibt
/// mit Zeitpunkt, Tokens und Kosten fürs Tageskontingent und die Kostenbremse stehen (wie beim Löschen einer Partie,
/// <see cref="ScoresheetScanRows.DetachWithoutLoading"/>) — sonst umginge „löschen und neu hochladen" Tageszahl und Budget.
/// Die geleerte Zeile trägt den Vermerk <see cref="ScoresheetScanRows.DiscardedMark"/> und erscheint in keiner Liste mehr.</para>
///
/// <para>Fristen (<see cref="RetentionOptions"/>): gescheiterte Einlesungen nach <c>Retention:ScoresheetFailedDays</c> (30),
/// nie übernommene Liga-Einlesungen OHNE Konto nach <c>Retention:ScoresheetOpenAnonymousDays</c> (14); dazu das Archiv
/// (<see cref="ScoresheetScanArchive"/>) nach seinem <c>ExpiresAt</c> (365 Tage) — das lief bisher nur beim nächsten
/// Abschluss einer Liga-Einlesung mit. Fotos übernommener RookHub-Einlesungen bleiben an ihrer Partie (das Original für
/// „Korrigieren"); sie gehen mit der Partie oder per <see cref="DeleteOwnAsync"/>.</para>
/// </summary>
public static class ScoresheetPhotoRetention
{
    /// <summary>Höchstens so viele Einlesungen je Speichern.</summary>
    internal const int Batch = 500;

    /// <summary>Eigene Einlesung („Meine Partien") leeren: <c>null</c> = erledigt, <c>notFound</c> = fremd/unbekannt/schon
    /// gelöscht/Liga-Einlesung (die schließt der Liga-Weg), <c>busy</c> = wartet oder wird gerade gelesen.</summary>
    public static async Task<string?> DeleteOwnAsync(AppDbContext db, int userId, int id, CancellationToken ct = default)
    {
        var scan = await db.ScoresheetScans.AsNoTracking()
            .Where(s => s.Id == id && s.UserId == userId && s.Purpose == null && s.FileName != DiscardedMark)
            .Select(s => new { s.Id, s.UserId, s.SavedGameId, s.Status }).FirstOrDefaultAsync(ct);
        if (scan == null) return "notFound";
        if (scan.Status is ScoresheetScanStatus.Pending or ScoresheetScanStatus.Running) return "busy";
        await ClearAsync(db, new[] { (scan.Id, scan.UserId, scan.SavedGameId) }, ct);
        return null;
    }

    /// <summary>Ein Durchlauf der Fristen; liefert (geleerte Einlesungen, gelöschte Archiv-Einträge).</summary>
    public static async Task<(int Cleared, int Archive)> PruneAsync(AppDbContext db, DateTime nowUtc, RetentionOptions o,
        CancellationToken ct = default)
    {
        var failedCutoff = nowUtc.AddDays(-o.ScoresheetFailedDays);
        var openCutoff = nowUtc.AddDays(-o.ScoresheetOpenAnonymousDays);
        var cleared = await ClearAllAsync(db, db.ScoresheetScans.Where(s => s.FileName != DiscardedMark
            && s.Status == ScoresheetScanStatus.Failed && (s.FinishedAt ?? s.CreatedAt) < failedCutoff), ct);
        cleared += await ClearAllAsync(db, db.ScoresheetScans.Where(s => s.FileName != DiscardedMark
            && s.UserId == null && s.Purpose == ScoresheetScan.PurposeLeague
            && s.Status != ScoresheetScanStatus.Running && s.CreatedAt < openCutoff), ct);
        var archive = await RetentionDelete.AllAsync(db, db.ScoresheetScanArchives.Where(a => a.ExpiresAt < nowUtc),
            RetentionDelete.ChunkSize, ct);
        return (cleared, archive);
    }

    private static async Task<int> ClearAllAsync(AppDbContext db, IQueryable<ScoresheetScan> due, CancellationToken ct)
    {
        var total = 0;
        while (true)
        {
            var keys = await due.OrderBy(s => s.Id).Select(s => new { s.Id, s.UserId, s.SavedGameId }).Take(Batch).ToListAsync(ct);
            if (keys.Count == 0) return total;
            await ClearAsync(db, keys.Select(k => (k.Id, k.UserId, k.SavedGameId)).ToList(), ct);
            total += keys.Count;
            if (keys.Count < Batch) return total;
        }
    }

    /// <summary>Foto(s), Seiten, weitere Fotos, Antwort und Stand leeren, ohne sie zu laden; Vermerk setzen, Schlüssel weg.</summary>
    private static async Task ClearAsync(AppDbContext db, IReadOnlyList<(int Id, int? UserId, int? SavedGameId)> keys,
        CancellationToken ct)
    {
        var ids = keys.Select(k => k.Id).ToList();
        DetachWithoutLoading(db, keys);
        RemovePagesWithoutLoading(db, await PageKeysAsync(db, ids));
        RemoveViewsWithoutLoading(db, await ViewKeysAsync(db, ids));
        foreach (var id in ids)
        {
            var scan = db.ScoresheetScans.Local.First(s => s.Id == id);
            scan.FileName = DiscardedMark;
            scan.AccessKey = null;
            db.Entry(scan).Property(nameof(ScoresheetScan.AccessKey)).IsModified = true;
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }
}
