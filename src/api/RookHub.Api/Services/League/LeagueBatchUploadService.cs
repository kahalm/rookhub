using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Stapel-Upload von Partieformular-Bildern (0.651.0, Wunsch 2026-10-04: „beliebig viele Partien uploaden — diese sollen
/// nicht direkt verarbeitet, sondern nur am Server abgelegt und eine Admin-Nachricht darüber verfasst werden").
/// <para>Ablauf: <see cref="StartAsync"/> legt einen Stapel an und gibt seinen Schlüssel zurück, die Seite schickt jedes Bild
/// einzeln (<see cref="AddFileAsync"/> — so bleibt jede Anfrage unter der Grenze von nginx/Kestrel, egal wie viele Bilder),
/// <see cref="FinishAsync"/> schließt ab und meldet: mit Konto als Admin-Nachricht im Verlauf des Hochladenden, über einen
/// Teilen-Link ohne Konto als Glocke an alle mit <see cref="Permissions.MessagesAdmin"/> (eine Admin-Nachricht braucht ein
/// Konto). Beides verweist auf den Admin-Tab „Uploads" (<c>/admin?tab=uploads</c>): Liste, ZIP, Löschen.</para>
/// <para>Abgelegt in der Datenbank (Entscheidung 04.10.: kein neues Volume). Ein Bild über <see cref="MaxStoredBytes"/> wird
/// wie beim Partieformular verkleinert (eine Zeile über ~16 MB nimmt MariaDB nicht), sonst unverändert. Deckel, weil die
/// Platte knapp ist: <see cref="MaxFiles"/> und <see cref="MaxBatchBytes"/> je Stapel, ohne Konto zusätzlich
/// <see cref="AnonPerIpDailyBytes"/> je Adresse und <see cref="AnonDailyBytes"/> für alle zusammen je Tag.</para>
/// </summary>
public sealed class LeagueBatchUploadService(AppDbContext db, AdminMessageService messages, NotificationService notifications)
{
    public const int MaxFiles = 1000;
    public const long MaxBatchBytes = 2L * 1024 * 1024 * 1024;
    public const long AnonPerIpDailyBytes = 1L * 1024 * 1024 * 1024;
    public const long AnonDailyBytes = 3L * 1024 * 1024 * 1024;
    public const int MaxStoredBytes = ScoresheetScanService.MaxStoredBytes;
    public const int MaxCommentLength = 1000;
    public const string AdminLink = "/admin?tab=uploads";

    /// <summary>Wer hochlädt: ein Konto oder ein Teilen-Link (Hash) samt IP-Hash — und für welchen Verein (Mandanten-Schritt
    /// 2026-10-07: angemeldet der Verein der Anfrage, über einen Link dessen Verein). Ein Stapel gehört genau einem Verein.</summary>
    public sealed record Uploader(int? UserId, string? ShareHash, string? IpHash, int ClubId)
    {
        public static Uploader User(int id, int clubId) => new(id, null, null, clubId);
        public static Uploader Share(string token, string ipHash, int clubId) => new(null, LeagueClubService.ShareHashOf(token), ipHash, clubId);
    }

    public sealed record BatchState(string Key, int Files, long Bytes, bool Finished);

    public async Task<BatchState> StartAsync(Uploader who, string? comment, CancellationToken ct)
    {
        var c = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (c is { Length: > MaxCommentLength }) c = c[..MaxCommentLength];
        var b = new LeagueBatchUpload
        {
            Key = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            ClubId = who.ClubId, UserId = who.UserId, ShareHash = who.ShareHash, AnonIpHash = who.IpHash, Comment = c, CreatedAt = DateTime.UtcNow,
        };
        db.LeagueBatchUploads.Add(b);
        await db.SaveChangesAsync(ct);
        return new BatchState(b.Key, 0, 0, false);
    }

    /// <summary>Ein Bild dazu → Stand, oder ein Grund: <c>notFound</c>, <c>finished</c>, <c>type</c> (kein Bild/PDF),
    /// <c>tooMany</c>, <c>tooLarge</c> (Bild bzw. Stapel), <c>dailyLimit</c> (ohne Konto).</summary>
    public async Task<(BatchState? State, string? Reason)> AddFileAsync(Uploader who, string key, byte[] data, string? contentType,
        string? fileName, CancellationToken ct)
    {
        var b = await FindAsync(who, key, ct);
        if (b is null) return (null, "notFound");
        if (b.FinishedAt is not null) return (null, "finished");
        var type = (contentType ?? "").Trim().ToLowerInvariant();
        if (!(type.StartsWith("image/") || type == "application/pdf")) return (null, "type");
        if (b.FileCount >= MaxFiles) return (null, "tooMany");

        if (data.Length > MaxStoredBytes)
        {
            var smaller = type == "application/pdf" ? null : ScoresheetImage.Prepare(data, ScoresheetScanService.StoredEdge, 90);
            if (smaller is null || smaller.Length > MaxStoredBytes) return (null, "tooLarge");
            data = smaller;
            type = "image/jpeg";
            fileName = Path.ChangeExtension(fileName ?? "bild", ".jpg");
        }
        if (b.TotalBytes + data.Length > MaxBatchBytes) return (null, "tooLarge");
        if (b.UserId is null && b.AnonIpHash is { } ip)
        {
            var since = DateTime.UtcNow.AddDays(-1);
            var mine = await db.LeagueBatchUploads.Where(x => x.AnonIpHash == ip && x.CreatedAt >= since).SumAsync(x => x.TotalBytes, ct);
            var all = await db.LeagueBatchUploads.Where(x => x.UserId == null && x.CreatedAt >= since).SumAsync(x => x.TotalBytes, ct);
            if (mine + data.Length > AnonPerIpDailyBytes || all + data.Length > AnonDailyBytes) return (null, "dailyLimit");
        }

        var name = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? $"bild-{b.FileCount + 1}" : fileName.Trim());
        if (name.Length > 200) name = name[^200..];
        db.LeagueBatchUploadFiles.Add(new LeagueBatchUploadFile
        {
            BatchId = b.Id, FileName = name, ContentType = type.Length > 60 ? type[..60] : type, Data = data, Size = data.Length,
            CreatedAt = DateTime.UtcNow,
        });
        b.FileCount++;
        b.TotalBytes += data.Length;
        await db.SaveChangesAsync(ct);
        return (new BatchState(b.Key, b.FileCount, b.TotalBytes, false), null);
    }

    /// <summary>Abschließen und melden (einmal; ein zweiter Aufruf meldet nicht noch einmal). Ein leerer Stapel wird gelöscht
    /// → <c>empty</c>.</summary>
    public async Task<(BatchState? State, string? Reason)> FinishAsync(Uploader who, string key, CancellationToken ct)
    {
        var b = await FindAsync(who, key, ct);
        if (b is null) return (null, "notFound");
        if (b.FinishedAt is not null) return (new BatchState(b.Key, b.FileCount, b.TotalBytes, true), null);
        if (b.FileCount == 0)
        {
            db.LeagueBatchUploads.Remove(b);
            await db.SaveChangesAsync(ct);
            return (null, "empty");
        }
        b.FinishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        var clubName = await db.LeagueClubs.AsNoTracking().Where(c => c.Id == b.ClubId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        if (b.UserId is { } uid) await messages.SendFromUserAsync(uid, MessageBody(b, clubName));
        else
        {
            var admins = await PermissionResolver.UserIdsWithPermissionAsync(db, Permissions.MessagesAdmin);
            await notifications.CreateManyAsync(admins, NotificationType.LeagueBatchUploaded,
                new Dictionary<string, string> { ["count"] = b.FileCount.ToString(), ["club"] = clubName ?? "" }, AdminLink);
        }
        return (new BatchState(b.Key, b.FileCount, b.TotalBytes, true), null);
    }

    internal static string MessageBody(LeagueBatchUpload b, string? clubName = null)
    {
        var text = $"LeagueHub-Stapel-Upload{(clubName is null ? "" : $" ({clubName})")}: {b.FileCount} {(b.FileCount == 1 ? "Bild" : "Bilder")} ({Mb(b.TotalBytes)}) abgelegt, "
            + $"nicht eingelesen. Herunterladen im Admin-Bereich unter „Uploads“ ({AdminLink}), Stapel #{b.Id}.";
        return b.Comment is { } c ? $"{text}\n\nKommentar: {c}" : text;
    }

    private static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0:0.#} MB".Replace('.', ',');

    private Task<LeagueBatchUpload?> FindAsync(Uploader who, string key, CancellationToken ct) =>
        string.IsNullOrEmpty(key) || key.Length != 32 ? Task.FromResult<LeagueBatchUpload?>(null)
        : db.LeagueBatchUploads.FirstOrDefaultAsync(b => b.Key == key && b.ClubId == who.ClubId
            && (who.UserId != null ? b.UserId == who.UserId : b.UserId == null && b.ShareHash == who.ShareHash), ct);

    // ── Admin ──

    /// <param name="Club">Name des Vereins, für den hochgeladen wurde (Mandanten-Schritt 2026-10-07).</param>
    public sealed record AdminRow(int Id, DateTime CreatedAt, DateTime? FinishedAt, string? User, bool ViaShare, int Files, long Bytes,
        string? Comment, int ClubId = 0, string? Club = null);

    public async Task<List<AdminRow>> ListAsync(CancellationToken ct)
    {
        var rows = await db.LeagueBatchUploads.AsNoTracking().OrderByDescending(b => b.CreatedAt)
            .Select(b => new { b.Id, b.CreatedAt, b.FinishedAt, b.UserId, b.ShareHash, b.FileCount, b.TotalBytes, b.Comment, b.ClubId })
            .ToListAsync(ct);
        var ids = rows.Where(r => r.UserId != null).Select(r => r.UserId!.Value).Distinct().ToList();
        var names = await db.AppUsers.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        var clubs = await db.LeagueClubs.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return rows.Select(r => new AdminRow(r.Id, r.CreatedAt, r.FinishedAt, r.UserId is { } u ? names.GetValueOrDefault(u) : null,
            r.UserId is null, r.FileCount, r.TotalBytes, r.Comment, r.ClubId, clubs.GetValueOrDefault(r.ClubId))).ToList();
    }

    /// <summary>Den Stapel als ZIP in <paramref name="output"/> schreiben (Bild für Bild aus der Datenbank, nicht alles auf
    /// einmal im Speicher). Doppelte Dateinamen bekommen „-2", „-3" … → false, wenn es den Stapel nicht gibt.</summary>
    public async Task<bool> WriteZipAsync(int id, Stream output, CancellationToken ct)
    {
        if (!await db.LeagueBatchUploads.AnyAsync(b => b.Id == id, ct)) return false;
        var fileIds = await db.LeagueBatchUploadFiles.AsNoTracking().Where(f => f.BatchId == id).OrderBy(f => f.Id)
            .Select(f => f.Id).ToListAsync(ct);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var fid in fileIds)
        {
            var f = await db.LeagueBatchUploadFiles.AsNoTracking().Where(x => x.Id == fid)
                .Select(x => new { x.FileName, x.Data }).SingleAsync(ct);
            var name = f.FileName;
            for (var n = 2; !used.Add(name); n++)
                name = $"{Path.GetFileNameWithoutExtension(f.FileName)}-{n}{Path.GetExtension(f.FileName)}";
            var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);   // Fotos sind schon gepackt
            await using var s = entry.Open();
            await s.WriteAsync(f.Data, ct);
        }
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var b = await db.LeagueBatchUploads.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null) return false;
        db.LeagueBatchUploads.Remove(b);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
