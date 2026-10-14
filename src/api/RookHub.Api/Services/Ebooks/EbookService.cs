using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Die E-Book-Bibliothek: Übersicht mit Suche/Schlagwort/Seite, Hochladen (jeder Angemeldete, mit Kontingent),
/// Metadaten und Gruppen-Freigabe je Buch (Besitzer oder Verwalter), Löschen, Lesestand je Nutzer.
/// Sichtbarkeit kommt ausschließlich aus <see cref="EbookAccess"/>.
/// </summary>
public class EbookService
{
    /// <summary>Anfrage-Grenze des Uploads: 1 GiB Datei plus Multipart-Rahmen. nginx (<c>location ^~ /api/ebooks</c>)
    /// und <c>[RequestSizeLimit]</c> am Controller halten diesen Wert (DeploymentConfigTests).</summary>
    public const long MaxUploadRequestBytes = 1_100_000_000;

    public const int MaxPageSize = 100;

    private readonly AppDbContext _db;
    private readonly EbookStorage _storage;
    private readonly ILogger<EbookService> _logger;

    public EbookService(AppDbContext db, EbookStorage storage, ILogger<EbookService> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    // ── Übersicht ──────────────────────────────────────────────────────────────────────────────

    public async Task<EbookListDto> ListAsync(int userId, bool manageAll, EbookQuery q, CancellationToken ct = default)
    {
        var (page, pageSize) = Paging.Normalize(q.Page, q.PageSize, MaxPageSize);
        var visible = EbookAccess.Visible(_db, userId, manageAll);

        // Schlagwort-Facette über ALLES Sichtbare — die Filterzeile soll auch nach einem Filter vollständig bleiben.
        var tagColumns = await visible.Select(b => b.Tags).ToListAsync(ct);
        var facets = tagColumns
            .SelectMany(t => EbookFormats.ParseTagCsv(t))
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => new EbookTagFacetDto { Tag = g.First(), Count = g.Count() })
            .OrderByDescending(f => f.Count).ThenBy(f => f.Tag, StringComparer.OrdinalIgnoreCase)
            .Take(60)
            .ToList();

        var query = visible;
        if (q.Mine) query = query.Where(b => b.OwnerUserId == userId && b.Source == EbookSource.Upload);
        if (q.WithDiagrams) query = query.Where(b => b.DiagramState == EbookDiagramState.Done && b.DiagramCount > 0);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim().ToLowerInvariant();
            query = query.Where(b => b.Title.ToLower().Contains(term) || b.Author.ToLower().Contains(term) || b.Tags.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(q.Tag))
        {
            // CSV-Spalte: „,Tag," im ",Tags," — ohne Teilwort-Treffer (Endspiel ≠ Endspielstudien).
            var needle = "," + q.Tag.Trim().ToLowerInvariant() + ",";
            query = query.Where(b => ("," + b.Tags.ToLower() + ",").Contains(needle));
        }

        query = (q.Sort ?? "title").ToLowerInvariant() switch
        {
            "newest" => query.OrderByDescending(b => b.CreatedAt).ThenBy(b => b.Id),
            "recent" => query
                .OrderByDescending(b => _db.EbookProgresses
                    .Where(p => p.EbookId == b.Id && p.UserId == userId)
                    .Select(p => (DateTime?)p.UpdatedAt).FirstOrDefault())
                .ThenBy(b => b.Title),
            _ => query.OrderBy(b => b.Title).ThenBy(b => b.Id),
        };

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).Include(b => b.Files).ToListAsync(ct);
        var dtos = await MapAsync(items, userId, manageAll, ct);

        var used = await UsedBytesAsync(userId, ct);
        return new EbookListDto
        {
            Items = dtos,
            Total = total,
            Page = page,
            PageSize = pageSize,
            Tags = facets,
            UploadEnabled = _storage.UploadsEnabled,
            QuotaUsedBytes = used,
            QuotaLimitBytes = manageAll || _storage.UserQuotaBytes <= 0 ? null : _storage.UserQuotaBytes,
            MaxFileBytes = _storage.MaxFileBytes,
            CanManageAll = manageAll,
            CanImportLibrary = manageAll && _storage.LibraryEnabled,
        };
    }

    public async Task<EbookDto> GetAsync(int userId, bool manageAll, int id, CancellationToken ct = default)
    {
        var ebook = await EbookAccess.Visible(_db, userId, manageAll)
            .Include(b => b.Files)
            .FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("E-book not found.");
        return (await MapAsync([ebook], userId, manageAll, ct))[0];
    }

    /// <summary>Summe der eigenen Uploads (fürs Kontingent).</summary>
    public Task<long> UsedBytesAsync(int userId, CancellationToken ct = default) =>
        _db.EbookFiles
            .Where(f => f.Ebook.OwnerUserId == userId && f.Ebook.Source == EbookSource.Upload)
            .SumAsync(f => (long?)f.SizeBytes, ct)
            .ContinueWith(t => t.Result ?? 0L, ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

    private async Task<List<EbookDto>> MapAsync(List<Ebook> items, int userId, bool manageAll, CancellationToken ct)
    {
        if (items.Count == 0) return [];
        var ids = items.Select(b => b.Id).ToList();
        var ownerIds = items.Where(b => b.OwnerUserId != null).Select(b => b.OwnerUserId!.Value).Distinct().ToList();
        var owners = ownerIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.AppUsers.Where(u => ownerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        var progress = await _db.EbookProgresses
            .Where(p => p.UserId == userId && ids.Contains(p.EbookId))
            .ToDictionaryAsync(p => p.EbookId, ct);
        var manageableIds = items.Where(b => EbookAccess.CanManage(b, userId, manageAll)).Select(b => b.Id).ToList();
        var groups = manageableIds.Count == 0
            ? new Dictionary<int, List<int>>()
            : (await _db.EbookGroupAccesses.Where(a => manageableIds.Contains(a.EbookId)).ToListAsync(ct))
                .GroupBy(a => a.EbookId).ToDictionary(g => g.Key, g => g.Select(a => a.GroupId).OrderBy(x => x).ToList());

        return items.Select(b =>
        {
            var canManage = EbookAccess.CanManage(b, userId, manageAll);
            var files = b.Files.OrderBy(f => EbookFormats.Rank(f.Format)).ToList();
            return new EbookDto
            {
                Id = b.Id,
                Title = b.Title,
                Author = b.Author,
                Year = b.Year,
                Tags = EbookFormats.ParseTagCsv(b.Tags),
                Description = b.Description,
                TargetMinElo = b.TargetMinElo,
                Source = b.Source == EbookSource.Library ? "library" : "upload",
                PageCount = b.PageCount,
                HasCover = b.CoverState == EbookCoverState.Done,
                DiagramState = b.DiagramState.ToString().ToLowerInvariant(),
                DiagramCount = b.DiagramState == EbookDiagramState.Done ? b.DiagramCount : 0,
                OwnerUserId = b.OwnerUserId,
                OwnerName = b.OwnerUserId is int oid && owners.TryGetValue(oid, out var n) ? n : null,
                IsOwner = b.OwnerUserId == userId,
                CanManage = canManage,
                GroupIds = canManage ? (groups.TryGetValue(b.Id, out var g) ? g : []) : null,
                Files = files.Select(f => new EbookFileDto { Id = f.Id, Format = f.Format, FileName = f.FileName, SizeBytes = f.SizeBytes }).ToList(),
                PrimaryFormat = EbookFormats.Primary(files)?.Format,
                Progress = progress.TryGetValue(b.Id, out var p)
                    ? new EbookProgressDto { Page = p.Page, PageCount = p.PageCount ?? b.PageCount, UpdatedAt = p.UpdatedAt }
                    : null,
                CreatedAt = b.CreatedAt,
                UpdatedAt = b.UpdatedAt,
            };
        }).ToList();
    }

    // ── Hochladen / Ändern / Löschen ───────────────────────────────────────────────────────────

    /// <summary>
    /// Nimmt eine Datei an: Format geprüft, Grenze und Kontingent geprüft, auf die Platte geschrieben (mit Hash),
    /// Dublette erkannt (409), dann das Buch mit Metadaten und Freigabe angelegt. Ein Verwalter hat kein Kontingent.
    /// </summary>
    public async Task<EbookDto> UploadAsync(int userId, bool manageAll, Stream content, string fileName, long declaredLength,
        EbookMetaDto meta, CancellationToken ct = default)
    {
        if (!_storage.UploadsEnabled)
            throw new DomainValidationException("Uploads are not configured on this server.") { Code = ApiErrorCodes.EbooksNotConfigured };
        var format = EbookFormats.FormatOf(fileName)
            ?? throw new DomainValidationException($"Allowed formats: {string.Join(", ", EbookFormats.Allowed)}.") { Code = ApiErrorCodes.EbookFormatUnsupported };
        if (declaredLength > _storage.MaxFileBytes)
            throw new DomainValidationException($"The file is larger than {_storage.MaxFileBytes / (1024 * 1024)} MB.") { Code = ApiErrorCodes.EbookFileTooLarge };
        if (!manageAll && _storage.UserQuotaBytes > 0)
        {
            var used = await UsedBytesAsync(userId, ct);
            if (used + Math.Max(declaredLength, 0) > _storage.UserQuotaBytes)
                throw new DomainValidationException("Your upload quota is used up.") { Code = ApiErrorCodes.EbookQuotaExceeded };
        }

        var saved = await _storage.SaveUploadAsync(content, format, _storage.MaxFileBytes, ct);
        try
        {
            if (await _db.EbookFiles.AnyAsync(f => f.Sha256 == saved.Sha256, ct))
                throw new ConflictException("This file is already in the library.") { Code = ApiErrorCodes.EbookDuplicateFile };
            if (!manageAll && _storage.UserQuotaBytes > 0 && await UsedBytesAsync(userId, ct) + saved.SizeBytes > _storage.UserQuotaBytes)
                throw new DomainValidationException("Your upload quota is used up.") { Code = ApiErrorCodes.EbookQuotaExceeded };

            var title = string.IsNullOrWhiteSpace(meta.Title) ? EbookFormats.TitleFromFileName(fileName) : meta.Title.Trim();
            var ebook = new Ebook
            {
                Title = Clip(title, 300),
                Author = Clip(meta.Author?.Trim() ?? string.Empty, 300),
                Year = ValidYear(meta.Year),
                Tags = EbookFormats.JoinTags(EbookFormats.NormalizeTags(meta.Tags)),
                Description = string.IsNullOrWhiteSpace(meta.Description) ? null : Clip(meta.Description.Trim(), 2000),
                TargetMinElo = ValidElo(meta.TargetMinElo),
                OwnerUserId = userId,
                Source = EbookSource.Upload,
                CoverState = EbookFormats.WithCover.Contains(format) ? EbookCoverState.Pending : EbookCoverState.Unsupported,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            ebook.Files.Add(new EbookFile
            {
                Format = format,
                RelativePath = saved.RelativePath,
                FileName = Clip(Path.GetFileName(fileName), 300),
                SizeBytes = saved.SizeBytes,
                Sha256 = saved.Sha256,
            });
            _db.Ebooks.Add(ebook);
            await _db.SaveChangesAsync(ct);
            if (meta.GroupIds != null)
            {
                await ApplyGroupsAsync([ebook], meta.GroupIds, userId, manageAll, ct);
                await _db.SaveChangesAsync(ct);
            }
            _logger.LogInformation("E-Book {EbookId} hochgeladen: {Format}, {Bytes} Bytes von Nutzer {UserId}", ebook.Id, format, saved.SizeBytes, userId);
            return await GetAsync(userId, manageAll, ebook.Id, ct);
        }
        catch
        {
            _storage.DeleteUpload(new EbookFile { RelativePath = saved.RelativePath }, EbookSource.Upload);
            throw;
        }
    }

    public async Task<EbookDto> UpdateAsync(int userId, bool manageAll, int id, EbookMetaDto meta, CancellationToken ct = default)
    {
        var ebook = await EbookAccess.LoadManageableAsync(_db, userId, id, manageAll, ct);
        if (meta.Title != null)
        {
            var t = meta.Title.Trim();
            if (t.Length == 0) throw new DomainValidationException("The title must not be empty.");
            ebook.Title = Clip(t, 300);
        }
        if (meta.Author != null) ebook.Author = Clip(meta.Author.Trim(), 300);
        if (meta.Year != null) ebook.Year = ValidYear(meta.Year);
        if (meta.Tags != null) ebook.Tags = EbookFormats.JoinTags(EbookFormats.NormalizeTags(meta.Tags));
        if (meta.Description != null) ebook.Description = string.IsNullOrWhiteSpace(meta.Description) ? null : Clip(meta.Description.Trim(), 2000);
        if (meta.TargetMinElo != null) ebook.TargetMinElo = ValidElo(meta.TargetMinElo);
        if (meta.GroupIds != null) await ApplyGroupsAsync([ebook], meta.GroupIds, userId, manageAll, ct);
        ebook.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await GetAsync(userId, manageAll, id, ct);
    }

    /// <summary>Jahr 0 oder absurd → keine Angabe (Beiblätter der Bibliothek tragen auch <c>null</c>).</summary>
    private static int? ValidYear(int? year) => year is >= 1400 and <= 2200 ? year : null;
    private static int? ValidElo(int? elo) => elo is >= 100 and <= 3500 ? elo : null;
    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    public async Task DeleteAsync(int userId, bool manageAll, int id, CancellationToken ct = default)
    {
        var ebook = await EbookAccess.LoadManageableAsync(_db, userId, id, manageAll, ct);
        // InMemory kaskadiert nicht — abhängige Zeilen ausdrücklich räumen (gilt auch für MariaDB: kein Schaden).
        _db.EbookProgresses.RemoveRange(_db.EbookProgresses.Where(p => p.EbookId == id));
        _db.EbookDiagrams.RemoveRange(_db.EbookDiagrams.Where(d => d.EbookId == id));
        _db.EbookDiagramReports.RemoveRange(_db.EbookDiagramReports.Where(r => r.EbookId == id));
        _db.EbookGroupAccesses.RemoveRange(ebook.GroupAccess);
        _db.EbookFiles.RemoveRange(ebook.Files);
        _db.Ebooks.Remove(ebook);
        await _db.SaveChangesAsync(ct);
        foreach (var f in ebook.Files) _storage.DeleteUpload(f, ebook.Source);
        _storage.DeleteCover(id);
    }

    // ── Freigabe ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Gruppen, die der Aufrufer freigeben darf: Verwalter alle, sonst die eigenen plus „Everyone".</summary>
    public async Task<List<EbookGroupOptionDto>> GroupOptionsAsync(int userId, bool manageAll, CancellationToken ct = default)
    {
        var groups = manageAll
            ? _db.Groups
            : _db.Groups.Where(g => g.IsEveryone || _db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == g.Id));
        var list = await groups.OrderBy(g => g.Name).ToListAsync(ct);
        return list.OrderByDescending(g => g.IsEveryone).ThenBy(g => g.Name)
            .Select(g => new EbookGroupOptionDto { Id = g.Id, Name = g.Name, IsEveryone = g.IsEveryone }).ToList();
    }

    /// <summary>Freigabe mehrerer Bücher setzen (ersetzt die bisherige). Nicht verwaltbare Bücher werden übersprungen;
    /// Antwort = Zahl der geänderten Bücher.</summary>
    public async Task<int> SetGroupsAsync(int userId, bool manageAll, IReadOnlyList<int> ebookIds, IReadOnlyList<int> groupIds, CancellationToken ct = default)
    {
        var ids = ebookIds.Distinct().ToList();
        if (ids.Count == 0) return 0;
        var books = await EbookAccess.Visible(_db, userId, manageAll)
            .Include(b => b.GroupAccess)
            .Where(b => ids.Contains(b.Id)).ToListAsync(ct);
        var manageable = books.Where(b => EbookAccess.CanManage(b, userId, manageAll)).ToList();
        await ApplyGroupsAsync(manageable, groupIds, userId, manageAll, ct);
        foreach (var b in manageable) b.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return manageable.Count;
    }

    /// <summary>Die erlaubte Teilmenge der gewünschten Gruppen (Verwalter: alle existierenden; sonst eigene + „Everyone").</summary>
    private async Task<List<int>> AllowedGroupIdsAsync(IEnumerable<int> requested, int userId, bool manageAll, CancellationToken ct)
    {
        var wanted = requested.Distinct().ToList();
        if (wanted.Count == 0) return [];
        var q = _db.Groups.Where(g => wanted.Contains(g.Id));
        if (!manageAll) q = q.Where(g => g.IsEveryone || _db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == g.Id));
        return await q.Select(g => g.Id).ToListAsync(ct);
    }

    private async Task ApplyGroupsAsync(IReadOnlyList<Ebook> books, IReadOnlyList<int> groupIds, int userId, bool manageAll, CancellationToken ct)
    {
        var allowed = await AllowedGroupIdsAsync(groupIds, userId, manageAll, ct);
        foreach (var book in books)
        {
            var existing = await _db.EbookGroupAccesses.Where(a => a.EbookId == book.Id).ToListAsync(ct);
            _db.EbookGroupAccesses.RemoveRange(existing.Where(a => !allowed.Contains(a.GroupId)));
            var have = existing.Select(a => a.GroupId).ToHashSet();
            foreach (var gid in allowed.Where(g => !have.Contains(g)))
                _db.EbookGroupAccesses.Add(new EbookGroupAccess { EbookId = book.Id, GroupId = gid });
            // KEIN book.GroupAccess.Clear(): die Navigation ist durch Fixup schon auf dem neuen Stand — ein Clear
            // markierte genau die eben hinzugefügten Zeilen wieder als gelöscht.
        }
    }

    // ── Lesestand ───────────────────────────────────────────────────────────────────────────────

    public async Task<EbookProgressDto?> GetProgressAsync(int userId, bool manageAll, int id, CancellationToken ct = default)
    {
        if (!await EbookAccess.CanAccessAsync(_db, userId, id, manageAll, ct)) throw new NotFoundException("E-book not found.");
        var p = await _db.EbookProgresses.FirstOrDefaultAsync(x => x.EbookId == id && x.UserId == userId, ct);
        return p == null ? null : new EbookProgressDto { Page = p.Page, PageCount = p.PageCount, UpdatedAt = p.UpdatedAt };
    }

    public async Task<EbookProgressDto> SetProgressAsync(int userId, bool manageAll, int id, int page, int? pageCount, CancellationToken ct = default)
    {
        if (!await EbookAccess.CanAccessAsync(_db, userId, id, manageAll, ct)) throw new NotFoundException("E-book not found.");
        if (page < 1) throw new DomainValidationException("Page must be at least 1.");
        var p = await _db.EbookProgresses.FirstOrDefaultAsync(x => x.EbookId == id && x.UserId == userId, ct);
        if (p == null)
        {
            p = new EbookProgress { EbookId = id, UserId = userId };
            _db.EbookProgresses.Add(p);
        }
        p.Page = page;
        if (pageCount is > 0) p.PageCount = pageCount;
        p.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new EbookProgressDto { Page = p.Page, PageCount = p.PageCount, UpdatedAt = p.UpdatedAt };
    }

    // ── Diagramme ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Alle erkannten Diagramme eines sichtbaren Buchs in Lesereihenfolge (Seite, dann von oben nach unten).</summary>
    public async Task<EbookDiagramListDto> GetDiagramsAsync(int userId, bool manageAll, int id, CancellationToken ct = default)
    {
        var state = await EbookAccess.Visible(_db, userId, manageAll).Where(b => b.Id == id)
            .Select(b => (EbookDiagramState?)b.DiagramState).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("E-book not found.");
        var items = state == EbookDiagramState.Done
            ? await _db.EbookDiagrams.Where(d => d.EbookId == id).OrderBy(d => d.Page).ThenBy(d => d.Y).ThenBy(d => d.X)
                .Select(d => new EbookDiagramDto
                {
                    Id = d.Id, Page = d.Page, X = d.X, Y = d.Y, W = d.W, H = d.H, Fen = d.Fen, Label = d.Label, Caption = d.Caption, SideKnown = d.SideKnown,
                }).ToListAsync(ct)
            : [];
        if (items.Count > 0)
        {
            var reports = await _db.EbookDiagramReports.Where(r => r.EbookId == id)
                .Select(r => new { r.DiagramId, r.UserId }).ToListAsync(ct);
            var mine = reports.Where(r => r.UserId == userId).Select(r => r.DiagramId).ToHashSet();
            var counts = reports.GroupBy(r => r.DiagramId).ToDictionary(g => g.Key, g => g.Count());
            foreach (var d in items)
            {
                d.ReportedByMe = mine.Contains(d.Id);
                if (manageAll) d.ReportCount = counts.GetValueOrDefault(d.Id);
            }
        }
        return new EbookDiagramListDto { State = state.ToString().ToLowerInvariant(), Items = items };
    }

    /// <summary>„Diagramm falsch": Meldung setzen (idempotent) bzw. mit <paramref name="reported"/>=false zurücknehmen.
    /// Nur für Diagramme eines sichtbaren Buchs; unbekannt → 404.</summary>
    public async Task<bool> ReportDiagramAsync(int userId, bool manageAll, int ebookId, int diagramId, bool reported, string? note, CancellationToken ct = default)
    {
        if (!await EbookAccess.CanAccessAsync(_db, userId, ebookId, manageAll, ct)) throw new NotFoundException("E-book not found.");
        var diagram = await _db.EbookDiagrams.FirstOrDefaultAsync(d => d.Id == diagramId && d.EbookId == ebookId, ct)
            ?? throw new NotFoundException("Diagram not found.");
        var existing = await _db.EbookDiagramReports.FirstOrDefaultAsync(r => r.DiagramId == diagramId && r.UserId == userId, ct);
        if (reported)
        {
            var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(300, note.Trim().Length)];
            if (existing == null)
            {
                _db.EbookDiagramReports.Add(new EbookDiagramReport
                {
                    EbookId = ebookId, DiagramId = diagramId, UserId = userId, Page = diagram.Page, Fen = diagram.Fen, Note = trimmed,
                });
                _logger.LogInformation("E-Book {EbookId}: Diagramm {DiagramId} (S. {Page}, {Font}) als falsch gemeldet", ebookId, diagramId, diagram.Page, diagram.FontFamily);
            }
            else if (trimmed != null) existing.Note = trimmed;
        }
        else if (existing != null) _db.EbookDiagramReports.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return reported;
    }

    /// <summary>Alle Meldungen (Verwalter), neueste zuerst.</summary>
    public async Task<List<EbookDiagramReportDto>> ListDiagramReportsAsync(CancellationToken ct = default)
    {
        var list = await _db.EbookDiagramReports.OrderByDescending(r => r.CreatedAt).Take(500)
            .Select(r => new EbookDiagramReportDto
            {
                Id = r.Id, EbookId = r.EbookId, EbookTitle = r.Ebook.Title, DiagramId = r.DiagramId, Page = r.Page, Fen = r.Fen,
                Note = r.Note, Username = r.User.Username, CreatedAt = r.CreatedAt,
            }).ToListAsync(ct);
        var ids = list.Select(r => r.DiagramId).Distinct().ToList();
        var fonts = await _db.EbookDiagrams.Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.FontFamily, ct);
        foreach (var r in list)
        {
            r.DiagramExists = fonts.ContainsKey(r.DiagramId);
            r.FontFamily = fonts.GetValueOrDefault(r.DiagramId);
        }
        return list;
    }

    /// <summary>Diagramm-Erkennung aller PDF-Bücher neu anstoßen (nach neuen Font-Belegungen); Antwort = Zahl der Bücher.</summary>
    public async Task<int> RescanDiagramsAsync(CancellationToken ct = default)
    {
        var books = await _db.Ebooks.Where(b => b.DiagramState != EbookDiagramState.Pending && b.Files.Any(f => f.Format == "pdf")).ToListAsync(ct);
        foreach (var b in books) b.DiagramState = EbookDiagramState.Pending;
        await _db.SaveChangesAsync(ct);
        return books.Count;
    }

    // ── Dateien ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Die Datei eines sichtbaren Buchs (ohne <paramref name="fileId"/> die Hauptdatei) samt vollem Pfad —
    /// unsichtbar/unbekannt → 404, auf der Platte verschwunden → 404 mit eigenem Code.</summary>
    public async Task<(Ebook Ebook, EbookFile File, string FullPath)> ResolveFileAsync(int userId, bool manageAll, int id, int? fileId, CancellationToken ct = default)
    {
        var ebook = await EbookAccess.Visible(_db, userId, manageAll)
            .Include(b => b.Files)
            .FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("E-book not found.");
        var file = (fileId == null ? EbookFormats.Primary(ebook.Files) : ebook.Files.FirstOrDefault(f => f.Id == fileId))
            ?? throw new NotFoundException("File not found.");
        var full = _storage.ResolvePath(ebook.Source, file.RelativePath);
        if (!System.IO.File.Exists(full))
            throw new NotFoundException("The file is missing on the server.") { Code = ApiErrorCodes.EbookFileMissing };
        return (ebook, file, full);
    }

    /// <summary>Pfad des Titelbilds eines sichtbaren Buchs; <c>null</c>, wenn (noch) keines da ist.</summary>
    public async Task<string?> ResolveCoverAsync(int userId, bool manageAll, int id, CancellationToken ct = default)
    {
        var state = await EbookAccess.Visible(_db, userId, manageAll).Where(b => b.Id == id).Select(b => (EbookCoverState?)b.CoverState).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("E-book not found.");
        if (state != EbookCoverState.Done || !_storage.CoverExists(id)) return null;
        return _storage.CoverPath(id);
    }
}
