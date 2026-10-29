using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Brett-Ausschneide-Aufträge über RookHub (0.874.0) statt über Dateien im WebDAV-Bereich. Die Opus-Schleife
/// (<c>ebook-board-training/fs/remote.py</c>) legt Aufträge an (<see cref="CreateAsync"/>, Admin); ein externer Worker
/// (<c>tools/DiagramWorker</c>, derselbe Token wie für <c>/api/diagram-worker</c>) leiht den ältesten wartenden
/// (<see cref="ClaimAsync"/>), lädt das PDF, schneidet aus und liefert <c>cells.npz</c>, <c>prep.json</c> und die Brett-Bilder
/// als ZIP (<see cref="SubmitAsync"/>). Abgelegt wird unter <c>&lt;Ebooks:Root&gt;/board-cells/&lt;EbookId&gt;/</c> — dieselben
/// Dateien wie bisher, damit die Schleife unverändert weiterliest. Vorhandene Bretter werden überschrieben, nie gelöscht
/// (Regel „Bretter nie löschen“); <c>prep.json</c> kommt zuletzt, denn sein Dasein heißt „fertig“.
/// Leihe wie bei den Diagramm-Workern: <see cref="EbookDiagramLease.ExternalLease"/>, per Lebenszeichen verlängert.
/// </summary>
public sealed partial class BoardCutJobService
{
    public const string ResultFolder = "board-cells";
    /// <summary>Obergrenze des Auftrags-JSON (Riesenbände: einige Tausend Diagramme à ~80 Zeichen).</summary>
    public const int MaxPayloadChars = 4_000_000;
    /// <summary>Obergrenze der ausgepackten Brett-Bilder je Auftrag.</summary>
    public const long MaxCropBytes = 2L * 1024 * 1024 * 1024;

    private readonly AppDbContext _db;
    private readonly EbookStorage _storage;
    private readonly ILogger<BoardCutJobService> _logger;

    public BoardCutJobService(AppDbContext db, EbookStorage storage, ILogger<BoardCutJobService> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    public static string ResultPath(int ebookId) => $"{ResultFolder}/{ebookId}";

    // ── Admin ────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Auftrag anlegen; ein noch wartender älterer Auftrag für dasselbe Buch wird zurückgezogen.</summary>
    public async Task<BoardCutJobStatusDto> CreateAsync(BoardCutJobCreateDto dto, CancellationToken ct)
    {
        var book = await _db.Ebooks.Include(b => b.Files).FirstOrDefaultAsync(b => b.Id == dto.EbookId, ct)
            ?? throw new NotFoundException("E-book not found.");
        if (dto.Payload.ValueKind != JsonValueKind.Object || !dto.Payload.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Object)
            throw new DomainValidationException("Payload must be an object with 'pages'.");
        var payload = dto.Payload.GetRawText();
        if (payload.Length > MaxPayloadChars) throw new DomainValidationException("Payload too large.");
        if (dto.FileId is { } fileId && !book.Files.Any(f => f.Id == fileId && f.Format == "pdf"))
            throw new DomainValidationException("File is not a PDF of this e-book.");
        if (PdfFile(book, dto.FileId) == null) throw new DomainValidationException("E-book has no PDF.");

        var now = DateTime.UtcNow;
        foreach (var old in await _db.BoardCutJobs.Where(j => j.EbookId == book.Id && j.State == BoardCutState.Pending).ToListAsync(ct))
        {
            old.State = BoardCutState.Cancelled;
            old.FinishedAt = now;
            old.Error = "replaced by a newer job";
        }
        var job = new BoardCutJob { EbookId = book.Id, FileId = dto.FileId, PayloadJson = payload, CreatedAt = now };
        _db.BoardCutJobs.Add(job);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Brett-Auftrag {JobId} für E-Book {EbookId} angelegt", job.Id, book.Id);
        return Status(job);
    }

    public async Task<BoardCutJobStatusDto> GetAsync(Guid jobId, CancellationToken ct)
        => Status(await _db.BoardCutJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new NotFoundException("Job not found."));

    public async Task<List<BoardCutJobStatusDto>> ListAsync(BoardCutState? state, int take, CancellationToken ct)
    {
        var q = _db.BoardCutJobs.AsNoTracking();
        if (state is { } s) q = q.Where(j => j.State == s);
        var rows = await q.OrderByDescending(j => j.CreatedAt).Take(Math.Clamp(take, 1, 1000)).ToListAsync(ct);
        return rows.Select(Status).ToList();
    }

    /// <summary>Wartenden Auftrag zurückziehen (ein schon fertiger bleibt, wie er ist).</summary>
    public async Task CancelAsync(Guid jobId, CancellationToken ct)
    {
        var job = await _db.BoardCutJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct) ?? throw new NotFoundException("Job not found.");
        if (job.State != BoardCutState.Pending) return;
        job.State = BoardCutState.Cancelled;
        job.FinishedAt = DateTime.UtcNow;
        job.LeaseOwner = null;
        job.LeaseUntil = null;
        await _db.SaveChangesAsync(ct);
    }

    // ── Worker ───────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Ältesten wartenden, freien Auftrag leihen → Auftrag, oder <c>null</c>, wenn nichts wartet.</summary>
    public async Task<BoardWorkerJobDto?> ClaimAsync(string owner, string? worker, CancellationToken ct)
    {
        for (var round = 0; round < 3; round++)
        {
            var now = DateTime.UtcNow;
            var ids = await _db.BoardCutJobs
                .Where(j => j.State == BoardCutState.Pending && (j.LeaseUntil == null || j.LeaseUntil < now))
                .OrderBy(j => j.CreatedAt).Select(j => j.Id).Take(5).ToListAsync(ct);
            if (ids.Count == 0) return null;
            foreach (var id in ids)
            {
                if (!await TryClaimAsync(id, owner, Clip(worker, 64), ct)) continue;
                _db.ChangeTracker.Clear();
                var job = await _db.BoardCutJobs.AsNoTracking().Include(j => j.Ebook).ThenInclude(b => b.Files).FirstAsync(j => j.Id == id, ct);
                var file = PdfFile(job.Ebook, job.FileId);
                if (file == null)
                {
                    await FailAsync(id, "E-book has no PDF any more.", ct);
                    continue;
                }
                _logger.LogInformation("Brett-Worker {Worker} ({Owner}): Auftrag {JobId} für E-Book {EbookId} übernommen",
                    Clip(worker, 60) ?? "?", owner, id, job.EbookId);
                using var payload = JsonDocument.Parse(job.PayloadJson);
                return new BoardWorkerJobDto
                {
                    JobId = id, EbookId = job.EbookId, Title = job.Ebook.Title, Payload = payload.RootElement.Clone(),
                    LeaseUntil = job.LeaseUntil ?? DateTime.UtcNow + EbookDiagramLease.ExternalLease,
                    HeartbeatSeconds = EbookDiagramWorkerService.HeartbeatSeconds,
                    File = new DiagramWorkerFileDto
                    {
                        FileId = file.Id, Role = "primary", Format = file.Format, SizeBytes = file.SizeBytes,
                        Url = $"api/board-worker/jobs/{id}/file",
                    },
                };
            }
        }
        return null;
    }

    /// <summary>Atomar leihen: auf MariaDB ein <c>UPDATE … WHERE</c> auf wartend + freie Leihe, in den InMemory-Tests getrackt.</summary>
    private async Task<bool> TryClaimAsync(Guid id, string owner, string? worker, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var until = now + EbookDiagramLease.ExternalLease;
        if (_db.Database.IsRelational())
            return await _db.BoardCutJobs
                .Where(j => j.Id == id && j.State == BoardCutState.Pending && (j.LeaseUntil == null || j.LeaseUntil < now))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.LeaseOwner, owner)
                    .SetProperty(j => j.LeaseUntil, until)
                    .SetProperty(j => j.Worker, worker), ct) == 1;
        var job = await _db.BoardCutJobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job == null || job.State != BoardCutState.Pending || (job.LeaseUntil != null && job.LeaseUntil >= now)) return false;
        job.LeaseOwner = owner; job.LeaseUntil = until; job.Worker = worker;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Der geliehene Auftrag (getrackt, mit Buch und Dateien); fremde oder abgelaufene Leihe → 409, unbekannt → 404.</summary>
    private async Task<BoardCutJob> HeldAsync(Guid jobId, string owner, CancellationToken ct)
    {
        var job = await _db.BoardCutJobs.Include(j => j.Ebook).ThenInclude(b => b.Files).FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new NotFoundException("Job not found.");
        if (job.State != BoardCutState.Pending || job.LeaseOwner != owner || job.LeaseUntil is not { } until || until < DateTime.UtcNow)
            throw new ConflictException("Lease is no longer valid (expired, held by someone else, or job finished).");
        return job;
    }

    public async Task<string> FileAsync(Guid jobId, string owner, CancellationToken ct)
    {
        var job = await HeldAsync(jobId, owner, ct);
        var file = PdfFile(job.Ebook, job.FileId) ?? throw new NotFoundException("E-book has no PDF.");
        var path = _storage.ResolveFile(job.Ebook, file);
        if (!File.Exists(path)) throw new NotFoundException("File is missing on the server.") { Code = ApiErrorCodes.EbookFileMissing };
        return path;
    }

    public async Task<DateTime> HeartbeatAsync(Guid jobId, string owner, CancellationToken ct)
    {
        var job = await HeldAsync(jobId, owner, ct);
        job.LeaseUntil = DateTime.UtcNow + EbookDiagramLease.ExternalLease;
        await _db.SaveChangesAsync(ct);
        return job.LeaseUntil.Value;
    }

    /// <summary>Auftrag zurückgeben: ohne Fehler wieder frei für den nächsten Worker, mit Fehler „fehlgeschlagen“.
    /// Eine schon verlorene Leihe ist kein Fehler (der Worker räumt nur auf).</summary>
    public async Task ReleaseAsync(Guid jobId, string owner, string? error, CancellationToken ct)
    {
        var job = await _db.BoardCutJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null || job.State != BoardCutState.Pending || job.LeaseOwner != owner) return;
        job.LeaseOwner = null;
        job.LeaseUntil = null;
        if (!string.IsNullOrWhiteSpace(error))
        {
            job.State = BoardCutState.Failed;
            job.Error = Clip(error, 2000);
            job.FinishedAt = DateTime.UtcNow;
            _logger.LogWarning("Brett-Auftrag {JobId} (E-Book {EbookId}) gescheitert: {Error}", job.Id, job.EbookId, job.Error);
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task FailAsync(Guid jobId, string error, CancellationToken ct)
    {
        var job = await _db.BoardCutJobs.FirstAsync(j => j.Id == jobId, ct);
        job.State = BoardCutState.Failed; job.Error = error; job.FinishedAt = DateTime.UtcNow; job.LeaseOwner = null; job.LeaseUntil = null;
        await _db.SaveChangesAsync(ct);
    }

    [GeneratedRegex(@"^\d{1,12}\.png$")]
    private static partial Regex CropName();

    /// <summary>Ergebnis ablegen: Brett-Bilder aus dem ZIP (nur <c>&lt;Diagramm-Id&gt;.png</c>, keine Pfade), dann
    /// <c>cells.npz</c>, zuletzt <c>prep.json</c>. Je Datei erst <c>.part</c>, dann umbenennen — ein abgebrochener Upload
    /// hinterlässt kein halbes „fertig“.</summary>
    public async Task<BoardWorkerResultAcceptedDto> SubmitAsync(Guid jobId, string owner, Stream cells, Stream prep, Stream cropsZip, CancellationToken ct)
    {
        var job = await HeldAsync(jobId, owner, ct);
        var root = _storage.Root ?? throw new DomainValidationException("Ebooks:Root is not configured.");
        var dir = Path.Combine(root, ResultFolder, job.EbookId.ToString());
        var cropDir = Path.Combine(dir, "crops");
        Directory.CreateDirectory(cropDir);

        var prepBytes = await ReadAllAsync(prep, 1024 * 1024, ct);
        try
        {
            using var doc = JsonDocument.Parse(prepBytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
        }
        catch (JsonException) { throw new DomainValidationException("prep.json is not a JSON object."); }

        var crops = 0;
        long unpacked = 0;
        using (var zip = new ZipArchive(cropsZip, ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;
                if (!CropName().IsMatch(entry.FullName)) throw new DomainValidationException($"Unexpected file in crops: {Clip(entry.FullName, 80)}");
                unpacked += entry.Length;
                if (unpacked > MaxCropBytes) throw new DomainValidationException("Crops too large.");
                var target = Path.Combine(cropDir, entry.FullName);
                await using (var src = entry.Open())
                await using (var dst = File.Create(target + ".part"))
                    await src.CopyToAsync(dst, ct);
                File.Move(target + ".part", target, overwrite: true);
                crops++;
            }
        }

        await WriteAtomicAsync(Path.Combine(dir, "cells.npz"), cells, ct);
        await WriteAtomicAsync(Path.Combine(dir, "prep.json"), new MemoryStream(prepBytes), ct);

        job.State = BoardCutState.Done;
        job.FinishedAt = DateTime.UtcNow;
        job.LeaseOwner = null;
        job.LeaseUntil = null;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Brett-Worker ({Owner}): Auftrag {JobId} für E-Book {EbookId} fertig — {Crops} Bretter",
            owner, job.Id, job.EbookId, crops);
        return new BoardWorkerResultAcceptedDto { EbookId = job.EbookId, Crops = crops, ResultPath = ResultPath(job.EbookId) };
    }

    private static async Task WriteAtomicAsync(string path, Stream content, CancellationToken ct)
    {
        await using (var dst = File.Create(path + ".part"))
            await content.CopyToAsync(dst, ct);
        File.Move(path + ".part", path, overwrite: true);
    }

    private static async Task<byte[]> ReadAllAsync(Stream s, int max, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = await s.ReadAsync(buffer, ct)) > 0)
        {
            ms.Write(buffer, 0, n);
            if (ms.Length > max) throw new DomainValidationException("prep.json too large.");
        }
        return ms.ToArray();
    }

    /// <summary>Zu rendernde Datei: die angegebene, sonst Original-PDF vor abgeleitetem (wie <c>prep.book_file</c>).</summary>
    internal static EbookFile? PdfFile(Ebook book, int? fileId) => fileId is { } id
        ? book.Files.FirstOrDefault(f => f.Id == id && f.Format == "pdf")
        : book.Files.Where(f => f.Format == "pdf").OrderBy(f => f.Derived).ThenBy(f => f.Id).FirstOrDefault();

    private static BoardCutJobStatusDto Status(BoardCutJob j) => new()
    {
        JobId = j.Id, EbookId = j.EbookId, State = j.State.ToString(), Worker = j.Worker, LeaseOwner = j.LeaseOwner, LeaseUntil = j.LeaseUntil,
        Error = j.Error, CreatedAt = j.CreatedAt, FinishedAt = j.FinishedAt, ResultPath = ResultPath(j.EbookId),
    };

    private static string? Clip(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Length <= max ? s : s[..max];
}
