using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Services.Scoresheet;

namespace RookHub.Api.Services.League;

/// <summary>
/// Einen Stapel-Upload direkt einlesen (0.901.0, Wunsch 2026-11-08: „wärs fein wenn ich den Stack direkt wieder einlesen
/// könnte und nicht erst wieder herunterladen"). Statt ZIP holen, woanders lesen und neu hochladen legt der Admin-Tab
/// „Uploads" aus den abgelegten Bildern normale WARTENDE Liga-Einlesungen an (<see cref="ScoresheetScanService.CreateQueuedAsync"/>)
/// — dieselben, die ein Upload in LeagueHub erzeugt. Gelesen werden sie vom eingestellten Leser (<c>Scoresheet:Reader</c>;
/// <c>external</c> = der Watcher auf dem Server, der wartende Einlesungen von selbst abholt).
/// <para>Zwei Arten (<see cref="ModeOne"/>, <see cref="ModeEach"/>): alle Bilder = EINE Partie (Seiten in Upload-Reihenfolge,
/// höchstens <see cref="ScoresheetScanService.MaxPages"/>) oder jedes Bild eine eigene Einlesung (Seiten derselben Partie lassen
/// sich danach in LeagueHub zusammenführen). Besitzer ist der Hochladende, wenn er ein Konto hat, sonst der Admin, der klickt;
/// der Verein ist der des Stapels. Der Stapel bekommt einen Vermerk (<c>ReadQueuedAt</c>, <c>ReadScanIds</c>); ein zweiter
/// Klick legt nur mit <c>force</c> noch einmal an. Gelöscht wird nichts — die Einlesungen tragen eigene Kopien der Bilder.</para>
/// </summary>
public sealed class LeagueBatchReadService(AppDbContext db, ScoresheetScanService scans)
{
    public const string ModeOne = "one";
    public const string ModeEach = "each";

    /// <param name="ScanIds">Die eben angelegten Einlesungen.</param>
    /// <param name="Skipped">Bilder, die keine Einlesung werden können (PDF, HEIC …) — nur bei <see cref="ModeEach"/>.</param>
    /// <param name="PreviousScanIds">Bei <c>alreadyQueued</c>: was beim letzten Mal entstanden ist.</param>
    public sealed record ReadResult(List<int> ScanIds, int Skipped, DateTime? QueuedAt, List<int>? PreviousScanIds = null);

    /// <summary>
    /// Einreihen → Ergebnis oder Grund: <c>notFound</c>, <c>invalidMode</c>, <c>notFinished</c> (Upload läuft noch),
    /// <c>empty</c>, <c>alreadyQueued</c> (schon eingereiht, ohne <paramref name="force"/>; Ergebnis trägt die alten Ids),
    /// <c>tooManyPages</c> (<see cref="ModeOne"/> mit mehr als drei Bildern), <c>unsupportedImage</c> (kein lesbares Bild bzw.
    /// bei <see cref="ModeOne"/> eines nicht lesbar) oder ein Grund der Annahme.
    /// </summary>
    public async Task<(ReadResult? Result, string? Reason)> QueueAsync(int batchId, int adminUserId, string? mode, bool force,
        CancellationToken ct)
    {
        mode = string.IsNullOrWhiteSpace(mode) ? ModeEach : mode.Trim().ToLowerInvariant();
        if (mode is not (ModeOne or ModeEach)) return (null, "invalidMode");
        var b = await db.LeagueBatchUploads.AsNoTracking().Where(x => x.Id == batchId)
            .Select(x => new { x.Id, x.UserId, x.ClubId, x.FinishedAt, x.ReadQueuedAt, x.ReadScanIds }).FirstOrDefaultAsync(ct);
        if (b is null) return (null, "notFound");
        if (b.FinishedAt is null) return (null, "notFinished");
        if (b.ReadQueuedAt is not null && !force)
            return (new ReadResult([], 0, b.ReadQueuedAt, ParseIds(b.ReadScanIds)), "alreadyQueued");
        var fileIds = await db.LeagueBatchUploadFiles.AsNoTracking().Where(f => f.BatchId == batchId).OrderBy(f => f.Id)
            .Select(f => f.Id).ToListAsync(ct);
        if (fileIds.Count == 0) return (null, "empty");
        if (mode == ModeOne && fileIds.Count > ScoresheetScanService.MaxPages) return (null, "tooManyPages");

        var owner = b.UserId is { } uid && await db.AppUsers.AnyAsync(u => u.Id == uid, ct) ? uid : adminUserId;
        var created = new List<int>();
        var skipped = 0;
        if (mode == ModeOne)
        {
            var pages = new List<ScoresheetUpload>();
            foreach (var fid in fileIds)
            {
                var f = await LoadAsync(fid, ct);
                if (!Readable(f)) return (null, "unsupportedImage");
                pages.Add(f);
            }
            var (scan, reason) = await scans.CreateQueuedAsync(owner, pages, b.ClubId);
            if (scan is null) return (null, reason);
            created.Add(scan.Id);
        }
        else
        {
            foreach (var fid in fileIds)
            {
                var f = await LoadAsync(fid, ct);
                if (!Readable(f)) { skipped++; continue; }
                var (scan, _) = await scans.CreateQueuedAsync(owner, [f], b.ClubId);
                if (scan is null) { skipped++; continue; }
                created.Add(scan.Id);
                db.ChangeTracker.Clear();   // die Fotos nicht über 64 Einlesungen im Speicher sammeln
            }
            if (created.Count == 0) return (null, "unsupportedImage");
        }

        var now = DateTime.UtcNow;
        var row = await db.LeagueBatchUploads.FirstAsync(x => x.Id == batchId, ct);
        row.ReadQueuedAt = now;
        row.ReadScanIds = string.Join(',', ParseIds(row.ReadScanIds).Concat(created));
        await db.SaveChangesAsync(ct);
        return (new ReadResult(created, skipped, now), null);
    }

    private async Task<ScoresheetUpload> LoadAsync(int fileId, CancellationToken ct)
    {
        var f = await db.LeagueBatchUploadFiles.AsNoTracking().Where(x => x.Id == fileId)
            .Select(x => new { x.Data, x.ContentType, x.FileName }).SingleAsync(ct);
        return new ScoresheetUpload(f.Data, f.ContentType, f.FileName);
    }

    /// <summary>Kann daraus eine Einlesung werden? (JPEG/PNG/WebP, dekodierbar — PDF und HEIC nicht.)</summary>
    private static bool Readable(ScoresheetUpload f) =>
        f.Data.Length > 0 && (f.ContentType is null || ScoresheetImage.AcceptedTypes.Contains(f.ContentType)) && ScoresheetImage.CanDecode(f.Data);

    internal static List<int> ParseIds(string? ids) =>
        string.IsNullOrEmpty(ids) ? [] : ids.Split(',').Select(s => int.TryParse(s, out var n) ? n : 0).Where(n => n > 0).ToList();
}
