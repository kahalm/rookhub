using RookHub.Api.Services.Accounts;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Social;

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

    /// <summary>„Mit Diagrammen“ zeigt nur Bücher mit mindestens so vielen erkannten Diagrammen (0.780.4, Nutzerwunsch):
    /// Partiensammlungen und Lehrbücher mit ein paar Stellungen sind keine Aufgabenbücher.</summary>
    public const int MinDiagramsForFilter = 100;

    private readonly AppDbContext _db;
    private readonly EbookStorage _storage;
    private readonly ILogger<EbookService> _logger;
    private readonly EbookOptions _options;
    private readonly NotificationService? _notifications;

    public EbookService(AppDbContext db, EbookStorage storage, ILogger<EbookService> logger,
        Microsoft.Extensions.Options.IOptions<EbookOptions>? options = null, NotificationService? notifications = null)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
        _options = options?.Value ?? new EbookOptions();
        _notifications = notifications;
    }

    /// <summary>Kontingent eines Besuchers ohne Anmeldung (0.778.0); 0 = anonymes Hochladen aus.</summary>
    private long AnonQuota => _options.AnonymousUploads ? Math.Max(0, _options.AnonymousQuotaBytes) : 0;

    // ── Übersicht ──────────────────────────────────────────────────────────────────────────────

    public async Task<EbookListDto> ListAsync(EbookCaller caller, bool manageAll, EbookQuery q, CancellationToken ct = default)
    {
        if (caller.IsAnonymous) manageAll = false;
        var (page, pageSize) = Paging.Normalize(q.Page, q.PageSize, MaxPageSize);
        var visible = EbookAccess.Visible(_db, caller, manageAll);
        var userId = caller.UserId ?? 0;
        var anon = caller.AnonId;

        // Schlagwort-Facette über ALLES Sichtbare — die Filterzeile soll auch nach einem Filter vollständig bleiben.
        var tagColumns = await visible.Select(b => b.Tags).ToListAsync(ct);
        var facets = tagColumns
            .SelectMany(t => EbookFormats.ParseTagCsv(t))
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => new EbookTagFacetDto { Tag = g.First(), Count = g.Count() })
            .OrderByDescending(f => f.Count).ThenBy(f => f.Tag, StringComparer.OrdinalIgnoreCase)
            .Take(60)
            .ToList();
        IQueryable<Ebook> Filter(bool language, bool translation)
        {
            var query = visible;
            if (q.Mine)
                query = caller.IsAnonymous
                    ? query.Where(b => anon != null && b.OwnerUserId == null && b.AnonymousSessionId == anon)
                    : query.Where(b => b.OwnerUserId == userId && b.Source == EbookSource.Upload);
            if (q.WithDiagrams) query = query.Where(b => b.DiagramState == EbookDiagramState.Done && b.DiagramCount >= MinDiagramsForFilter);
            if (q.MinElo is int minElo) query = query.Where(b => b.TargetMinElo != null && b.TargetMinElo >= minElo);
            if (q.MaxElo is int maxElo) query = query.Where(b => b.TargetMinElo != null && b.TargetMinElo <= maxElo);
            if (language && !string.IsNullOrWhiteSpace(q.Language))
            {
                var lang = q.Language.Trim().ToLowerInvariant();
                query = lang == "unknown" ? query.Where(b => b.Language == null) : query.Where(b => b.Language == lang);
            }
            if (translation && !string.IsNullOrWhiteSpace(q.Translation))
            {
                var target = q.Translation.Trim().ToLowerInvariant();
                query = query.Where(b => _db.EbookTranslations.Any(t => t.EbookId == b.Id && t.Language == target && t.State == EbookTranslationState.Done));
            }
            if (!string.IsNullOrWhiteSpace(q.Search))
            {
                var term = q.Search.Trim().ToLowerInvariant();
                query = query.Where(b => b.Title.ToLower().Contains(term) || b.Author.ToLower().Contains(term) || b.Tags.ToLower().Contains(term));
            }
            if (!string.IsNullOrWhiteSpace(q.Author))
            {
                var name = q.Author.Trim().ToLowerInvariant();
                query = query.Where(b => b.Author.ToLower().Contains(name));
            }
            if (!string.IsNullOrWhiteSpace(q.Tag))
            {
                // CSV-Spalte: „,Tag," im ",Tags," — ohne Teilwort-Treffer (Endspiel ≠ Endspielstudien).
                var needle = "," + q.Tag.Trim().ToLowerInvariant() + ",";
                query = query.Where(b => ("," + b.Tags.ToLower() + ",").Contains(needle));
            }
            return query;
        }

        // Sprach-Facetten (0.877.0): Buchsprache und fertige Übersetzungen. Gezählt unter allen ANDEREN Filtern (0.879.1,
        // UI-Sweep): sonst zeigte „es 23“ neben einem Schlagwortfilter und die Kombination ergab 0 Bücher.
        var languages = (await Filter(language: false, translation: true).GroupBy(b => b.Language).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
            .Select(g => new EbookLanguageFacetDto { Language = g.Key, Count = g.Count })
            .OrderByDescending(f => f.Count).ThenBy(f => f.Language ?? "~", StringComparer.Ordinal).ToList();
        // (EbookId, Language) ist eindeutig — Count zählt Bücher.
        var forTranslations = Filter(language: true, translation: false);
        var translations = (await _db.EbookTranslations
                .Where(t => t.State == EbookTranslationState.Done && forTranslations.Any(b => b.Id == t.EbookId))
                .GroupBy(t => t.Language).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
            .Select(g => new EbookLanguageFacetDto { Language = g.Key, Count = g.Count })
            .OrderByDescending(f => f.Count).ThenBy(f => f.Language, StringComparer.Ordinal).ToList();

        var query = Filter(language: true, translation: true);

        // Sammlungen (0.787.0): geöffnet → nur ihre Bücher; sonst steht je Sammlung EIN Buch (das älteste passende) als
        // Ordner da — außer bei einer Suche oder einem Autor, die sollen das einzelne Heft finden.
        var filtered = query;
        var collapse = q.Collection == null && string.IsNullOrWhiteSpace(q.Search) && string.IsNullOrWhiteSpace(q.Author);
        // Geöffnet zählen auch Bücher, die die Sammlung nur als weitere haben (0.861.0); der Ordner in der Liste hängt an der
        // Hauptsammlung.
        if (q.Collection != null) query = query.Where(b => b.CollectionId == q.Collection || b.ExtraCollections.Any(l => l.CollectionId == q.Collection));
        else if (collapse)
            query = query.Where(b => b.CollectionId == null || b.Id == filtered.Where(x => x.CollectionId == b.CollectionId).Min(x => x.Id));

        query = (string.IsNullOrWhiteSpace(q.Sort) ? "newest" : q.Sort).ToLowerInvariant() switch   // Standard „Neueste zuerst“
        {
            // „Neueste zuerst“ = Erscheinungsjahr (0.778.1, Nutzerwunsch); ohne Jahr ans Ende, sonst nach Aufnahme.
            "newest" => query.OrderByDescending(b => b.Year != null).ThenByDescending(b => b.Year)
                .ThenByDescending(b => b.CreatedAt).ThenBy(b => b.Id),
            "recent" when caller.IsAnonymous => query
                .OrderByDescending(b => _db.EbookAnonymousProgresses
                    .Where(p => p.EbookId == b.Id && p.SessionId == anon)
                    .Select(p => (DateTime?)p.UpdatedAt).FirstOrDefault())
                .ThenBy(b => b.Title),
            "recent" => query
                .OrderByDescending(b => _db.EbookProgresses
                    .Where(p => p.EbookId == b.Id && p.UserId == userId)
                    .Select(p => (DateTime?)p.UpdatedAt).FirstOrDefault())
                .ThenBy(b => b.Title),
            _ => query.OrderBy(b => b.Title).ThenBy(b => b.Id),
        };

        var total = await query.CountAsync(ct);
        // Gesamtmenge zur Filterung (0.877.0): Sammlungen zählen mit jedem Buch, nicht als ein Ordner; Dateien ohne die
        // abgeleiteten (DJVU → PDF), je Format.
        var counted = q.Collection != null ? query : filtered;
        var bookCount = collapse ? await filtered.CountAsync(ct) : total;
        var fileCounts = (await _db.EbookFiles.Where(f => !f.Derived && counted.Any(b => b.Id == f.EbookId))
                .GroupBy(f => f.Format).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
            .GroupBy(g => g.Key.ToLowerInvariant())
            .Select(g => new EbookFormatCountDto { Format = g.Key, Count = g.Sum(x => x.Count) })
            .OrderByDescending(f => f.Count).ThenBy(f => f.Format, StringComparer.Ordinal).ToList();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).Include(b => b.Files).ToListAsync(ct);
        var dtos = await MapAsync(items, caller, manageAll, ct);
        var collIds = items.Where(b => b.CollectionId != null).Select(b => b.CollectionId!.Value).Distinct().ToList();
        if (q.Collection != null && !collIds.Contains(q.Collection.Value)) collIds.Add(q.Collection.Value);
        var colls = new Dictionary<int, EbookCollectionDto>();
        if (collIds.Count > 0)
        {
            var counts = await filtered.Where(b => b.CollectionId != null && collIds.Contains(b.CollectionId.Value))
                .GroupBy(b => b.CollectionId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            counts.AddRange(await _db.EbookCollectionLinks.Where(l => collIds.Contains(l.CollectionId) && filtered.Any(b => b.Id == l.EbookId))
                .GroupBy(l => l.CollectionId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct));
            colls = await _db.EbookCollections.Where(c => collIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => new EbookCollectionDto { Id = c.Id, Name = c.Name }, ct);
            foreach (var c in counts) if (colls.TryGetValue(c.Key, out var dto)) dto.BookCount += c.Count;
        }
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].CollectionId is not { } cid || !colls.TryGetValue(cid, out var coll)) continue;
            dtos[i].Collection = coll;
            dtos[i].CollectionStub = collapse;
        }

        var used = await UsedBytesAsync(caller, ct);
        return new EbookListDto
        {
            Items = dtos,
            Collection = q.Collection is { } open && colls.TryGetValue(open, out var openColl) ? openColl : null,
            Total = total,
            Page = page,
            PageSize = pageSize,
            Tags = facets,
            BookCount = bookCount,
            FileCounts = fileCounts,
            Languages = languages,
            Translations = translations,
            UploadEnabled = _storage.UploadsEnabled && (!caller.IsAnonymous || (AnonQuota > 0 && anon != null)),
            QuotaUsedBytes = used,
            QuotaLimitBytes = caller.IsAnonymous ? AnonQuota : manageAll || _storage.UserQuotaBytes <= 0 ? null : _storage.UserQuotaBytes,
            MaxFileBytes = caller.IsAnonymous ? Math.Min(_storage.MaxFileBytes, AnonQuota) : _storage.MaxFileBytes,
            CanManageAll = manageAll,
            CanImportLibrary = manageAll && _storage.LibraryEnabled,
        };
    }

    public async Task<EbookDto> GetAsync(EbookCaller caller, bool manageAll, int id, CancellationToken ct = default)
    {
        if (caller.IsAnonymous) manageAll = false;
        var ebook = await EbookAccess.Visible(_db, caller, manageAll)
            .Include(b => b.Files)
            .FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("E-book not found.");
        return (await MapAsync([ebook], caller, manageAll, ct))[0];
    }

    /// <summary>Summe der eigenen Uploads (fürs Kontingent) — angemeldet je Konto, sonst je anonymer Kennung.</summary>
    public async Task<long> UsedBytesAsync(EbookCaller caller, CancellationToken ct = default)
    {
        IQueryable<EbookFile> files;
        if (caller.UserId is int userId)
            files = _db.EbookFiles.Where(f => f.Ebook.OwnerUserId == userId && f.Ebook.Source == EbookSource.Upload);
        else if (caller.AnonId is string anon)
            files = _db.EbookFiles.Where(f => f.Ebook.OwnerUserId == null && f.Ebook.AnonymousSessionId == anon);
        else
            return 0;
        return await files.SumAsync(f => (long?)f.SizeBytes, ct) ?? 0L;
    }

    private async Task<List<EbookDto>> MapAsync(List<Ebook> items, EbookCaller caller, bool manageAll, CancellationToken ct)
    {
        var userId = caller.UserId ?? 0;
        if (items.Count == 0) return [];
        var ids = items.Select(b => b.Id).ToList();
        var ownerIds = items.Where(b => b.OwnerUserId != null).Select(b => b.OwnerUserId!.Value).Distinct().ToList();
        var owners = ownerIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.AppUsers.Where(u => ownerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        var anon = caller.AnonId;
        var progress = caller.IsAnonymous
            ? anon == null ? new Dictionary<int, (int Page, int? PageCount, string? Locator, DateTime UpdatedAt)>()
                : (await _db.EbookAnonymousProgresses.Where(p => p.SessionId == anon && ids.Contains(p.EbookId)).ToListAsync(ct))
                    .ToDictionary(p => p.EbookId, p => (p.Page, p.PageCount, p.Locator, p.UpdatedAt))
            : (await _db.EbookProgresses.Where(p => p.UserId == userId && ids.Contains(p.EbookId)).ToListAsync(ct))
                .ToDictionary(p => p.EbookId, p => (p.Page, p.PageCount, p.Locator, p.UpdatedAt));
        var translations = (await _db.EbookTranslations.Where(t => ids.Contains(t.EbookId) && t.State == EbookTranslationState.Done)
                .Select(t => new { t.EbookId, t.Language }).ToListAsync(ct))
            .GroupBy(t => t.EbookId).ToDictionary(g => g.Key, g => g.Select(t => t.Language).OrderBy(l => l).ToList());
        var manageableIds = items.Where(b => EbookAccess.CanManage(b, caller, manageAll)).Select(b => b.Id).ToList();
        var groups = manageableIds.Count == 0
            ? new Dictionary<int, List<int>>()
            : (await _db.EbookGroupAccesses.Where(a => manageableIds.Contains(a.EbookId)).ToListAsync(ct))
                .GroupBy(a => a.EbookId).ToDictionary(g => g.Key, g => g.Select(a => a.GroupId).OrderBy(x => x).ToList());

        return items.Select(b =>
        {
            var canManage = EbookAccess.CanManage(b, caller, manageAll);
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
                DefaultBoardMode = b.DefaultBoardMode ?? EbookFormats.DefaultBoardMode,
                Source = b.Source == EbookSource.Library ? "library" : "upload",
                PageCount = b.PageCount,
                HasCover = b.CoverState == EbookCoverState.Done,
                DiagramState = b.DiagramState.ToString().ToLowerInvariant(),
                DiagramCount = b.DiagramState == EbookDiagramState.Done ? b.DiagramCount : 0,
                ConversionState = b.ConversionState.ToString().ToLowerInvariant(),
                Processing = b.DiagramState == EbookDiagramState.Pending || b.DiagramsBacklog,   // Schild nur, solange Diagramme erkannt oder ergänzt werden (0.803.4, 0.806.1) — Titelbild/Lösungsprüfung laufen still
                OwnerUserId = b.OwnerUserId,
                OwnerName = b.OwnerUserId is int oid && owners.TryGetValue(oid, out var n) ? n : null,
                IsOwner = caller.IsAnonymous ? anon != null && b.OwnerUserId == null && b.AnonymousSessionId == anon : b.OwnerUserId == userId,
                CanManage = canManage,
                GroupIds = canManage ? (groups.TryGetValue(b.Id, out var g) ? g : []) : null,
                Files = files.Select(f => new EbookFileDto { Id = f.Id, Format = f.Format, FileName = f.FileName, SizeBytes = f.SizeBytes, Derived = f.Derived }).ToList(),
                PrimaryFormat = EbookFormats.Primary(files)?.Format,
                Progress = progress.TryGetValue(b.Id, out var p)
                    ? new EbookProgressDto { Page = p.Page, PageCount = p.PageCount ?? b.PageCount, Locator = p.Locator, UpdatedAt = p.UpdatedAt }
                    : null,
                Language = b.Language,
                Translations = translations.TryGetValue(b.Id, out var tr) ? tr : [],
                CreatedAt = b.CreatedAt,
                UpdatedAt = b.UpdatedAt,
            };
        }).ToList();
    }

    // ── Hochladen / Ändern / Löschen ───────────────────────────────────────────────────────────

    /// <summary>
    /// Nimmt eine Datei an: Format geprüft, Grenze und Kontingent geprüft, auf die Platte geschrieben (mit Hash),
    /// Dublette erkannt (409: gleiche Bytes, seit 0.863.0 auch gleiche ISBN bzw. fast gleicher Text — außer mit
    /// <paramref name="forceNew"/>), dann das Buch mit Metadaten und Freigabe angelegt. Ein Verwalter hat kein Kontingent.
    /// </summary>
    public async Task<EbookDto> UploadAsync(EbookCaller caller, bool manageAll, Stream content, string fileName, long declaredLength,
        EbookMetaDto meta, CancellationToken ct = default, bool forceNew = false)
    {
        if (!_storage.UploadsEnabled)
            throw new DomainValidationException("Uploads are not configured on this server.") { Code = ApiErrorCodes.EbooksNotConfigured };
        // Ohne Anmeldung (0.778.0): nur mit gültiger anonymer Kennung, eigenes kleines Kontingent, nie für Gruppen freigegeben.
        if (caller.IsAnonymous)
        {
            manageAll = false;
            if (caller.AnonId == null || AnonQuota <= 0)
                throw new ForbiddenException("Sign in to upload e-books.") { Code = ApiErrorCodes.EbookAnonymousUploadDisabled };
            meta.GroupIds = null;
        }
        var quota = caller.IsAnonymous ? AnonQuota : manageAll ? 0 : _storage.UserQuotaBytes;
        var maxFile = caller.IsAnonymous ? Math.Min(_storage.MaxFileBytes, AnonQuota) : _storage.MaxFileBytes;
        var format = EbookFormats.FormatOf(fileName)
            ?? throw new DomainValidationException($"Allowed formats: {string.Join(", ", EbookFormats.Allowed)}.") { Code = ApiErrorCodes.EbookFormatUnsupported };
        if (declaredLength > maxFile)
            throw new DomainValidationException($"The file is larger than {maxFile / (1024 * 1024)} MB.") { Code = ApiErrorCodes.EbookFileTooLarge };
        if (quota > 0 && await UsedBytesAsync(caller, ct) + Math.Max(declaredLength, 0) > quota)
            throw new DomainValidationException("Your upload quota is used up.") { Code = ApiErrorCodes.EbookQuotaExceeded };
        if (caller.IsAnonymous && await AnonymousTotalBytesAsync(ct) + Math.Max(declaredLength, 0) > _options.AnonymousTotalBytes)
            throw new DomainValidationException("Uploads without an account are full right now — please sign in.") { Code = ApiErrorCodes.EbookQuotaExceeded };

        var saved = await _storage.SaveUploadAsync(content, format, maxFile, ct);
        try
        {
            var sameBytes = await _db.EbookFiles.Where(f => f.Sha256 == saved.Sha256).Select(f => f.EbookId).Distinct().ToListAsync(ct);
            if (sameBytes.Count > 0)
            {
                // Das vorhandene Buch nur nennen, wenn der Hochladende es sehen darf (0.863.0: „Zum vorhandenen Buch“).
                var visibleSame = await EbookAccess.Visible(_db, caller, manageAll).Where(b => sameBytes.Contains(b.Id)).Select(b => b.Id).ToListAsync(ct);
                var books = await EbookDuplicateFinder.DescribeAsync(_db, visibleSame.Select(id => new EbookDuplicateMatch(id, "file", 1, true)).ToList(), ct);
                throw new ConflictException("This file is already in the library.")
                {
                    Code = ApiErrorCodes.EbookDuplicateFile,
                    Details = books.Count > 0 ? new { books } : null,
                };
            }
            if (quota > 0 && await UsedBytesAsync(caller, ct) + saved.SizeBytes > quota)
                throw new DomainValidationException("Your upload quota is used up.") { Code = ApiErrorCodes.EbookQuotaExceeded };

            var title = string.IsNullOrWhiteSpace(meta.Title) ? EbookFormats.TitleFromFileName(fileName) : meta.Title.Trim();

            // Dasselbe Buch in einer anderen Datei (0.863.0): ISBN, Text-Fingerabdruck, Titel + Autor — nur unter den
            // sichtbaren Büchern. Sicher → 409 mit dem vorhandenen Buch; wahrscheinlich → anlegen und in der Antwort nennen.
            var fingerprint = EbookFingerprint.Compute(_storage.ResolvePath(EbookSource.Upload, saved.RelativePath), format, ct);
            List<EbookDuplicateDto>? possible = null;
            if (!forceNew)
            {
                var matches = await EbookDuplicateFinder.FindAsync(_db, EbookAccess.Visible(_db, caller, manageAll), fingerprint, title, meta.Author, ct);
                var certain = matches.Where(m => m.Certain).ToList();
                if (certain.Count > 0)
                {
                    var books = await EbookDuplicateFinder.DescribeAsync(_db, certain, ct);
                    throw new ConflictException("This book is already in the library.") { Code = ApiErrorCodes.EbookDuplicateBook, Details = new { books } };
                }
                if (matches.Count > 0) possible = await EbookDuplicateFinder.DescribeAsync(_db, matches, ct);
            }
            var ebook = new Ebook
            {
                Title = Clip(title, 300),
                Author = Clip(meta.Author?.Trim() ?? string.Empty, 300),
                Year = ValidYear(meta.Year),
                Tags = EbookFormats.JoinTags(EbookFormats.NormalizeTags(meta.Tags)),
                Description = string.IsNullOrWhiteSpace(meta.Description) ? null : Clip(meta.Description.Trim(), 2000),
                TargetMinElo = ValidElo(meta.TargetMinElo),
                DefaultBoardMode = EbookFormats.NormalizeBoardMode(meta.DefaultBoardMode),
                OwnerUserId = caller.UserId,
                AnonymousSessionId = caller.IsAnonymous ? caller.AnonId : null,
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
                Isbns = fingerprint.IsbnCsv,
                TextFingerprint = fingerprint.MinHash is { } mh ? EbookFingerprint.Encode(mh) : null,
                FingerprintedAt = DateTime.UtcNow,
            });
            if (EbookFormats.NeedsPdfConversion(ebook.Files)) ebook.ConversionState = EbookConversionState.Pending;
            _db.Ebooks.Add(ebook);
            await _db.SaveChangesAsync(ct);
            if (meta.GroupIds != null && caller.UserId is int uid)
            {
                await ApplyGroupsAsync([ebook], meta.GroupIds, uid, manageAll, ct);
                await _db.SaveChangesAsync(ct);
            }
            _logger.LogInformation("E-Book {EbookId} hochgeladen: {Format}, {Bytes} Bytes von {Caller}", ebook.Id, format, saved.SizeBytes,
                caller.UserId is int u ? $"Nutzer {u}" : "anonym");
            if (!manageAll) await NotifyAdminsOfUploadAsync(ebook, caller, ct);
            var dto = await GetAsync(caller, manageAll, ebook.Id, ct);
            dto.PossibleDuplicates = possible is { Count: > 0 } ? possible : null;
            return dto;
        }
        catch
        {
            _storage.DeleteUpload(new EbookFile { RelativePath = saved.RelativePath }, EbookSource.Upload);
            throw;
        }
    }

    /// <summary>Upload von jemandem ohne E-Book-Verwaltung (Nutzer oder anonym) → Glocke aller Admins mit Link aufs Buch
    /// (0.788.0, Wunsch 15.10.2026: die Bibliothek ist offen, Admins sollen jeden fremden Upload sehen). Best-effort — ein
    /// Fehler hier kippt den schon gespeicherten Upload nicht.</summary>
    private async Task NotifyAdminsOfUploadAsync(Ebook ebook, EbookCaller caller, CancellationToken ct)
    {
        if (_notifications == null) return;
        try
        {
            var admins = await PermissionResolver.UserIdsWithPermissionAsync(_db, Permissions.MessagesAdmin, ct);
            if (caller.UserId is int self) admins.Remove(self);
            var username = caller.UserId is int uid
                ? await _db.AppUsers.Where(u => u.Id == uid).Select(u => u.Username).FirstOrDefaultAsync(ct)
                : null;
            await _notifications.CreateManyAsync(admins, NotificationType.EbookUploaded,
                new Dictionary<string, string> { ["title"] = ebook.Title, ["username"] = username ?? string.Empty },
                $"/ebooks/{ebook.Id}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "E-Book {EbookId}: Admin-Benachrichtigung zum Upload fehlgeschlagen", ebook.Id);
        }
    }

    public async Task<EbookDto> UpdateAsync(EbookCaller caller, bool manageAll, int id, EbookMetaDto meta, CancellationToken ct = default)
    {
        if (caller.IsAnonymous) manageAll = false;
        var ebook = await EbookAccess.LoadManageableAsync(_db, caller, id, manageAll, ct);
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
        if (meta.DefaultBoardMode != null) ebook.DefaultBoardMode = EbookFormats.NormalizeBoardMode(meta.DefaultBoardMode);
        if (meta.GroupIds != null && caller.UserId is int uid) await ApplyGroupsAsync([ebook], meta.GroupIds, uid, manageAll, ct);
        ebook.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await GetAsync(caller, manageAll, id, ct);
    }

    /// <summary>Jahr 0 oder absurd → keine Angabe (Beiblätter der Bibliothek tragen auch <c>null</c>).</summary>
    private static int? ValidYear(int? year) => year is >= 1400 and <= 2200 ? year : null;
    /// <summary>0 = für Anfänger (0.822.0, vorher erst ab 100); außerhalb 0–3500 → keine Angabe.</summary>
    private static int? ValidElo(int? elo) => elo is >= 0 and <= 3500 ? elo : null;
    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    public async Task DeleteAsync(EbookCaller caller, bool manageAll, int id, CancellationToken ct = default)
    {
        if (caller.IsAnonymous) manageAll = false;
        var ebook = await EbookAccess.LoadManageableAsync(_db, caller, id, manageAll, ct);
        await RemoveBookAsync(ebook, ct);
    }

    /// <summary>Buch samt Dateien, Titelbild und allen abhängigen Zeilen löschen (Löschen-Knopf und Aufräumen anonymer Uploads).</summary>
    internal async Task RemoveBookAsync(Ebook ebook, CancellationToken ct)
    {
        var id = ebook.Id;
        await _db.Entry(ebook).Collection(b => b.Files).LoadAsync(ct);
        await _db.Entry(ebook).Collection(b => b.GroupAccess).LoadAsync(ct);
        var files = ebook.Files.ToList();
        // InMemory kaskadiert nicht — abhängige Zeilen ausdrücklich räumen (gilt auch für MariaDB: kein Schaden).
        _db.EbookProgresses.RemoveRange(_db.EbookProgresses.Where(p => p.EbookId == id));
        _db.EbookAnonymousProgresses.RemoveRange(_db.EbookAnonymousProgresses.Where(p => p.EbookId == id));
        _db.EbookDiagrams.RemoveRange(_db.EbookDiagrams.Where(d => d.EbookId == id));
        _db.EbookGames.RemoveRange(_db.EbookGames.Where(g => g.EbookId == id));
        _db.CourseLineEbookRefs.RemoveRange(_db.CourseLineEbookRefs.Where(r => r.EbookId == id));
        _db.EbookDiagramReports.RemoveRange(_db.EbookDiagramReports.Where(r => r.EbookId == id));
        _db.EbookMissedDiagrams.RemoveRange(_db.EbookMissedDiagrams.Where(m => m.EbookId == id));
        _db.EbookDiagramCorrections.RemoveRange(_db.EbookDiagramCorrections.Where(c => c.EbookId == id));
        _db.EbookTranslations.RemoveRange(_db.EbookTranslations.Where(t => t.EbookId == id));
        _db.EbookGroupAccesses.RemoveRange(ebook.GroupAccess);
        _db.EbookFiles.RemoveRange(ebook.Files);
        _db.Ebooks.Remove(ebook);
        await _db.SaveChangesAsync(ct);
        foreach (var f in files) _storage.DeleteUpload(f, ebook.Source);
        _storage.DeleteCover(id);
        _storage.DeleteTranslations(id);
    }

    // ── Freigabe ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Gruppen, die der Aufrufer freigeben darf: Verwalter alle, sonst die eigenen plus „Everyone".</summary>
    public async Task<List<EbookGroupOptionDto>> GroupOptionsAsync(int userId, bool manageAll, CancellationToken ct = default)
    {
        var myGroups = GroupMembership.GroupIdsOf(_db, userId);
        var groups = manageAll
            ? _db.Groups
            : _db.Groups.Where(g => g.IsEveryone || myGroups.Contains(g.Id));
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
        var myGroups = GroupMembership.GroupIdsOf(_db, userId);
        if (!manageAll) q = q.Where(g => g.IsEveryone || myGroups.Contains(g.Id));
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

    public async Task<EbookProgressDto?> GetProgressAsync(EbookCaller caller, bool manageAll, int id, CancellationToken ct = default)
    {
        if (caller.IsAnonymous) manageAll = false;
        if (!await EbookAccess.CanAccessAsync(_db, caller, id, manageAll, ct)) throw new NotFoundException("E-book not found.");
        if (caller.UserId is not int userId)
        {
            var a = caller.AnonId == null ? null
                : await _db.EbookAnonymousProgresses.FirstOrDefaultAsync(x => x.EbookId == id && x.SessionId == caller.AnonId, ct);
            return a == null ? null : new EbookProgressDto { Page = a.Page, PageCount = a.PageCount, Locator = a.Locator, UpdatedAt = a.UpdatedAt };
        }
        var p = await _db.EbookProgresses.FirstOrDefaultAsync(x => x.EbookId == id && x.UserId == userId, ct);
        return p == null ? null : new EbookProgressDto { Page = p.Page, PageCount = p.PageCount, Locator = p.Locator, UpdatedAt = p.UpdatedAt };
    }

    public async Task<EbookProgressDto> SetProgressAsync(EbookCaller caller, bool manageAll, int id, int page, int? pageCount, CancellationToken ct = default, string? locator = null)
    {
        if (caller.IsAnonymous) manageAll = false;
        if (!await EbookAccess.CanAccessAsync(_db, caller, id, manageAll, ct)) throw new NotFoundException("E-book not found.");
        if (page < 1) throw new DomainValidationException("Page must be at least 1.");
        if (caller.UserId is not int userId)
        {
            if (caller.AnonId == null) throw new DomainValidationException("A visitor id is required to remember the reading position.");
            var a = await _db.EbookAnonymousProgresses.FirstOrDefaultAsync(x => x.EbookId == id && x.SessionId == caller.AnonId, ct);
            var newLocator = locator == null ? a?.Locator : locator.Length > 300 ? null : locator;
            if (a != null && IsUnchanged(a.Page, a.PageCount, a.Locator, a.UpdatedAt, page, pageCount, newLocator))
                return new EbookProgressDto { Page = a.Page, PageCount = a.PageCount, Locator = a.Locator, UpdatedAt = a.UpdatedAt };
            if (a == null)
            {
                a = new EbookAnonymousProgress { EbookId = id, SessionId = caller.AnonId };
                _db.EbookAnonymousProgresses.Add(a);
            }
            a.Page = page;
            if (pageCount is > 0) a.PageCount = pageCount;
            a.Locator = newLocator;
            a.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new EbookProgressDto { Page = a.Page, PageCount = a.PageCount, Locator = a.Locator, UpdatedAt = a.UpdatedAt };
        }
        var p = await _db.EbookProgresses.FirstOrDefaultAsync(x => x.EbookId == id && x.UserId == userId, ct);
        var locatorToStore = locator == null ? p?.Locator : locator.Length > 300 ? null : locator;
        if (p != null && IsUnchanged(p.Page, p.PageCount, p.Locator, p.UpdatedAt, page, pageCount, locatorToStore))
            return new EbookProgressDto { Page = p.Page, PageCount = p.PageCount, Locator = p.Locator, UpdatedAt = p.UpdatedAt };
        if (p == null)
        {
            p = new EbookProgress { EbookId = id, UserId = userId };
            _db.EbookProgresses.Add(p);
        }
        p.Page = page;
        if (pageCount is > 0) p.PageCount = pageCount;
        p.Locator = locatorToStore;
        p.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new EbookProgressDto { Page = p.Page, PageCount = p.PageCount, Locator = p.Locator, UpdatedAt = p.UpdatedAt };
    }

    /// <summary>
    /// Gleicher Lesestand, vor kurzem schon gespeichert → nicht schreiben. Mehrere Fenster/Geräte schicken beim Öffnen und
    /// Wechseln denselben Stand immer wieder; jedes reine „UpdatedAt“-UPDATE nahm die Zeilensperre, und im UI-Sweep
    /// 23.10.2026 hingen zwei davon 32 s bis zum Datenbank-Timeout (500). <see cref="ProgressTouchInterval"/> hält
    /// „zuletzt gelesen“ trotzdem aktuell.
    /// </summary>
    private static bool IsUnchanged(int page, int? pageCount, string? locator, DateTime updatedAt, int newPage, int? newPageCount, string? newLocator)
        => page == newPage && (newPageCount is not > 0 || pageCount == newPageCount) && locator == newLocator
           && DateTime.UtcNow - updatedAt < ProgressTouchInterval;

    internal static readonly TimeSpan ProgressTouchInterval = TimeSpan.FromMinutes(1);

    // ── Diagramme ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Alle erkannten Diagramme eines sichtbaren Buchs in Lesereihenfolge (Seite, dann von oben nach unten).</summary>
    public async Task<EbookDiagramListDto> GetDiagramsAsync(EbookCaller caller, bool manageAll, int id, CancellationToken ct = default)
    {
        if (caller.IsAnonymous) manageAll = false;
        var userId = caller.UserId ?? 0;
        var state = await EbookAccess.Visible(_db, caller, manageAll).Where(b => b.Id == id)
            .Select(b => (EbookDiagramState?)b.DiagramState).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("E-book not found.");
        var items = state == EbookDiagramState.Done
            ? await _db.EbookDiagrams.Where(d => d.EbookId == id).Readable().OrderBy(d => d.Page).ThenBy(d => d.Y).ThenBy(d => d.X)
                .Select(d => new EbookDiagramDto
                {
                    Id = d.Id, Page = d.Page, X = d.X, Y = d.Y, W = d.W, H = d.H, Fen = d.Fen, Label = d.Label, Caption = d.Caption, SideKnown = d.SideKnown, ImageRef = d.ImageRef, EpubChapter = d.EpubChapter,
                    SolutionUci = d.SolutionUci, SolutionSan = d.SolutionSan, SolutionText = d.SolutionText, SolutionPage = d.SolutionPage,
                    SolutionSource = d.SolutionSource, SolutionCheck = d.SolutionCheck.ToString().ToLower(), EngineEval = d.EngineEval,
                    HasGame = d.GameId != null,
                }).ToListAsync(ct)
            : [];
        if (items.Count > 0)
        {
            var reports = await _db.EbookDiagramReports.Where(r => r.EbookId == id)
                .Select(r => new { r.DiagramId, r.UserId }).ToListAsync(ct);
            var mine = reports.Where(r => caller.UserId != null && r.UserId == userId).Select(r => r.DiagramId).ToHashSet();
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
        var diagram = await _db.EbookDiagrams.Readable().FirstOrDefaultAsync(d => d.Id == diagramId && d.EbookId == ebookId, ct)
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
        var books = await _db.Ebooks.Where(b => b.DiagramState != EbookDiagramState.Pending && !b.DiagramsCurated && b.Files.Any(f => f.Format == "pdf")).ToListAsync(ct);
        foreach (var b in books) b.DiagramState = EbookDiagramState.Pending;
        await _db.SaveChangesAsync(ct);
        return books.Count;
    }

    /// <summary>
    /// „Ergänzen statt ersetzen“ für EIN Buch (0.811.0): <see cref="Ebook.DiagramSupplement"/> setzen und die Erkennung einreihen.
    /// Wirkt auch bei <see cref="Ebook.DiagramsCurated"/> — der Worker führt dann zusammen statt zu ersetzen. Mit Vorrang
    /// (<see cref="Ebook.DiagramPriority"/>, 0.870.0): das Buch springt an den Anfang der Warteschlange. Unbekannt → 404.
    /// </summary>
    public async Task QueueDiagramSupplementAsync(int id, CancellationToken ct = default)
    {
        var book = await _db.Ebooks.FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("E-book not found.");
        book.DiagramSupplement = true;
        book.DiagramPriority = true;   // 0.870.0: der Admin wartet darauf — vor der übrigen Warteschlange
        book.DiagramState = EbookDiagramState.Pending;
        book.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("E-Book {EbookId}: Diagramme ergänzen eingereiht (kuratiert {Curated})", id, book.DiagramsCurated);
    }

    /// <summary>„Ist hier ein Diagramm, das nicht erkannt wurde?“ — Antwort zur späteren Auswertung speichern (0.788.0).</summary>
    public async Task RecordMissedDiagramAsync(int userId, bool manageAll, int id, EbookMissedDiagramDto dto, CancellationToken ct = default)
    {
        if (!await EbookAccess.CanAccessAsync(_db, userId, id, manageAll, ct)) throw new NotFoundException("E-book not found.");
        // PDF: Seite ≥ 1. EPUB (0.798.0): Seite 0, dafür Bildpfad und/oder Kapitel.
        var epubRef = string.IsNullOrWhiteSpace(dto.EpubRef) ? null : Clip(dto.EpubRef.Trim(), 500);
        var epubChapter = string.IsNullOrWhiteSpace(dto.EpubChapter) ? null : Clip(dto.EpubChapter.Trim(), 500);
        if (dto.Page < 1 && epubRef == null && epubChapter == null)
            throw new DomainValidationException("Either a page or an EPUB location is required.");
        _db.EbookMissedDiagrams.Add(new EbookMissedDiagram
        {
            EbookId = id, Page = Math.Max(0, dto.Page), X = Math.Clamp(dto.X, 0, 1), Y = Math.Clamp(dto.Y, 0, 1), IsDiagram = dto.IsDiagram,
            EpubRef = epubRef, EpubChapter = epubChapter, UserId = userId,
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// „Stellung korrigieren“ (0.798.0): neue FEN für ein Diagramm (nur wer das Buch verwalten darf). Die Korrektur wird IMMER
    /// in <see cref="EbookDiagramCorrection"/> mitgeschrieben (alte/neue FEN, Font, Sicherheit) — Material für die Erkennung.
    /// Das Buch gilt danach als kuratiert (kein Rescan überschreibt die Hand-Korrektur), und Stockfish prüft die Lösungen neu.
    /// Die Buchlösung wird ab der neuen Stellung neu nachgespielt; eine Engine-Lösung verfällt (die Prüfung rechnet sie neu).
    /// </summary>
    public async Task<EbookDiagramDto> CorrectDiagramPositionAsync(EbookCaller caller, bool manageAll, int id, int diagramId, string fen, CancellationToken ct = default)
    {
        if (caller.IsAnonymous || caller.UserId is not int userId) throw new ForbiddenException(EbookAccess.ManageForbiddenMessage);
        var ebook = await EbookAccess.LoadManageableAsync(_db, caller, id, manageAll, ct);
        var d = await _db.EbookDiagrams.FirstOrDefaultAsync(x => x.Id == diagramId && x.EbookId == id, ct)
            ?? throw new NotFoundException("Diagram not found.");
        var newFen = NormalizeCorrectedFen(fen);
        _db.EbookDiagramCorrections.Add(new EbookDiagramCorrection
        {
            EbookId = id, DiagramId = d.Id, UserId = userId, Page = d.Page, OldFen = d.Fen, NewFen = newFen,
            FontFamily = d.FontFamily, Confidence = d.Confidence,
        });
        if (d.Fen != newFen)
        {
            d.Fen = newFen;
            ResetSolutionForNewPosition(d);
        }
        // Zugrecht wurde im Editor ausdrücklich gesetzt (der Editor schickt es immer mit) — jetzt bekannt.
        d.SideKnown = true;
        // Ein ungelesenes Diagramm (0.811.0) ist nach der Korrektur gelesen — ab jetzt für Nutzer sichtbar.
        if (d.FontFamily == EbookDiagramVisibility.Unread) d.FontFamily = "hand";
        ebook.DiagramsCurated = true;
        ebook.SolutionCheckState = EbookDiagramState.Pending;
        ebook.SolutionCheckQueuedAt = DateTime.UtcNow;
        ebook.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        var all = await GetDiagramsAsync(caller, manageAll, id, ct);
        return all.Items.FirstOrDefault(x => x.Id == d.Id) ?? throw new NotFoundException("Diagram not found.");
    }

    /// <summary>FEN prüfen und vervollständigen: 8 Reihen à 8 Felder, genau ein weißer und ein schwarzer König, Zugrecht w/b;
    /// Rochade/e.p./Zähler ergänzt bzw. übernommen. Ungültig → 400.</summary>
    internal static string NormalizeCorrectedFen(string? fen)
    {
        var parts = (fen ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new DomainValidationException("The FEN is empty.");
        var ranks = parts[0].Split('/');
        if (ranks.Length != 8) throw new DomainValidationException("The FEN needs 8 ranks.");
        int whiteKings = 0, blackKings = 0;
        foreach (var rank in ranks)
        {
            var squares = 0;
            foreach (var ch in rank)
            {
                if (ch is >= '1' and <= '8') squares += ch - '0';
                else if ("KQRBNPkqrbnp".Contains(ch))
                {
                    squares++;
                    if (ch == 'K') whiteKings++;
                    if (ch == 'k') blackKings++;
                }
                else throw new DomainValidationException($"Invalid character '{ch}' in the FEN.");
            }
            if (squares != 8) throw new DomainValidationException("Every rank needs exactly 8 squares.");
        }
        if (whiteKings != 1 || blackKings != 1) throw new DomainValidationException("The position needs exactly one king of each colour.");
        var side = parts.Length > 1 ? parts[1] : "w";
        if (side is not ("w" or "b")) throw new DomainValidationException("The side to move must be w or b.");
        var castling = parts.Length > 2 && System.Text.RegularExpressions.Regex.IsMatch(parts[2], "^(-|[KQkq]{1,4})$") ? parts[2] : "-";
        var ep = parts.Length > 3 && System.Text.RegularExpressions.Regex.IsMatch(parts[3], "^(-|[a-h][36])$") ? parts[3] : "-";
        var half = parts.Length > 4 && int.TryParse(parts[4], out var h) && h >= 0 ? h : 0;
        var full = parts.Length > 5 && int.TryParse(parts[5], out var f) && f >= 1 ? f : 1;
        var result = $"{parts[0]} {side} {castling} {ep} {half} {full}";
        if (result.Length > 100) throw new DomainValidationException("The FEN is too long.");
        return result;
    }

    /// <summary>Neue Stellung: Buchlösung ab ihr neu nachspielen (nicht spielbar → keine Linie, Text bleibt), Engine-Lösung
    /// verwerfen; Prüfung und Bewertung zurück auf „ungeprüft“.</summary>
    internal static void ResetSolutionForNewPosition(EbookDiagram d)
    {
        if (d.SolutionSource == "engine" || string.IsNullOrEmpty(d.SolutionText))
        {
            d.SolutionUci = null;
            d.SolutionSan = null;
            if (d.SolutionSource == "engine") { d.SolutionSource = null; d.SolutionText = null; d.SolutionPage = null; }
        }
        else
        {
            var found = EbookSolutionFinder.TryParse(d.Fen, true, d.SolutionText, d.SolutionPage ?? d.Page);
            d.SolutionUci = found == null ? null : Clip(string.Join(' ', found.Uci), 1200);
            d.SolutionSan = found == null ? null : Clip(string.Join(' ', found.San), 1500);
            d.SolutionSource = "book";
        }
        d.SolutionCheck = EbookSolutionCheck.Unchecked;
        d.EngineEval = null;
    }

    // ── Dateien ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Die Datei eines sichtbaren Buchs (ohne <paramref name="fileId"/> die Hauptdatei) samt vollem Pfad —
    /// unsichtbar/unbekannt → 404, auf der Platte verschwunden → 404 mit eigenem Code.</summary>
    public async Task<(Ebook Ebook, EbookFile File, string FullPath)> ResolveFileAsync(EbookCaller caller, bool manageAll, int id, int? fileId, CancellationToken ct = default)
    {
        if (caller.IsAnonymous) manageAll = false;
        var ebook = await EbookAccess.Visible(_db, caller, manageAll)
            .Include(b => b.Files)
            .FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("E-book not found.");
        var file = (fileId == null ? EbookFormats.Primary(ebook.Files) : ebook.Files.FirstOrDefault(f => f.Id == fileId))
            ?? throw new NotFoundException("File not found.");
        var full = _storage.ResolveFile(ebook, file);
        if (!System.IO.File.Exists(full))
            throw new NotFoundException("The file is missing on the server.") { Code = ApiErrorCodes.EbookFileMissing };
        return (ebook, file, full);
    }

    /// <summary>Pfad der EPUB-Datei eines sichtbaren Buchs (für den Leser „als Ordner“); kein EPUB → 404.</summary>
    public async Task<string> ResolveEpubAsync(EbookCaller caller, bool manageAll, int id, CancellationToken ct = default)
    {
        if (caller.IsAnonymous) manageAll = false;
        var ebook = await EbookAccess.Visible(_db, caller, manageAll)
            .Include(b => b.Files)
            .FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("E-book not found.");
        var file = EbookFormats.Primary(ebook.Files.Where(f => f.Format == "epub"))
            ?? throw new NotFoundException("This book has no EPUB file.");
        var full = _storage.ResolveFile(ebook, file);
        if (!System.IO.File.Exists(full))
            throw new NotFoundException("The file is missing on the server.") { Code = ApiErrorCodes.EbookFileMissing };
        return full;
    }

    /// <summary>Das übersetzte EPUB (0.876.0) eines sichtbaren Buchs: Buch + voller Pfad. Unsichtbar, keine fertige
    /// Übersetzung in <paramref name="language"/> oder Datei weg → 404.</summary>
    public async Task<(Ebook Ebook, string FullPath)> ResolveTranslationAsync(EbookCaller caller, bool manageAll, int id, string language, CancellationToken ct = default)
    {
        if (caller.IsAnonymous) manageAll = false;
        var ebook = await EbookAccess.Visible(_db, caller, manageAll).FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("E-book not found.");
        var lang = (language ?? "").Trim().ToLowerInvariant();
        var tr = await _db.EbookTranslations.AsNoTracking()
            .FirstOrDefaultAsync(t => t.EbookId == id && t.Language == lang && t.State == EbookTranslationState.Done, ct);
        if (tr?.RelativePath == null) throw new NotFoundException("This book has no translation in that language.");
        var full = _storage.ResolvePath(EbookSource.Upload, tr.RelativePath);
        if (!System.IO.File.Exists(full))
            throw new NotFoundException("The file is missing on the server.") { Code = ApiErrorCodes.EbookFileMissing };
        return (ebook, full);
    }

    /// <summary>Pfad des Titelbilds eines sichtbaren Buchs; <c>null</c>, wenn (noch) keines da ist.</summary>
    public async Task<string?> ResolveCoverAsync(EbookCaller caller, bool manageAll, int id, CancellationToken ct = default)
    {
        if (caller.IsAnonymous) manageAll = false;
        var state = await EbookAccess.Visible(_db, caller, manageAll).Where(b => b.Id == id).Select(b => (EbookCoverState?)b.CoverState).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("E-book not found.");
        if (state != EbookCoverState.Done || !_storage.CoverExists(id)) return null;
        return _storage.CoverPath(id);
    }

    // ── Ohne Anmeldung (0.778.0) ────────────────────────────────────────────────────────────────

    /// <summary>Summe ALLER anonymen Uploads — Deckel gegen Missbrauch (<see cref="EbookOptions.AnonymousTotalBytes"/>).</summary>
    public async Task<long> AnonymousTotalBytesAsync(CancellationToken ct = default)
        => await _db.EbookFiles.Where(f => f.Ebook.OwnerUserId == null && f.Ebook.AnonymousSessionId != null)
            .SumAsync(f => (long?)f.SizeBytes, ct) ?? 0L;

    /// <summary>
    /// Beim Anmelden: die anonymen Uploads dieser Kennung gehören jetzt dem Konto, der anonyme Lesestand wird übernommen
    /// (je Buch gewinnt der jüngere Stand) und gelöscht. Antwort = Zahl der übernommenen Bücher.
    /// </summary>
    public async Task<int> ClaimAnonymousAsync(int userId, string anonId, CancellationToken ct = default)
    {
        if (!EbookCaller.IsValidAnonId(anonId)) throw new DomainValidationException("Invalid visitor id.");
        var books = await _db.Ebooks.Where(b => b.OwnerUserId == null && b.AnonymousSessionId == anonId).ToListAsync(ct);
        foreach (var b in books)
        {
            b.OwnerUserId = userId;
            b.AnonymousSessionId = null;
            b.UpdatedAt = DateTime.UtcNow;
        }
        var anonProgress = await _db.EbookAnonymousProgresses.Where(p => p.SessionId == anonId).ToListAsync(ct);
        if (anonProgress.Count > 0)
        {
            var ids = anonProgress.Select(p => p.EbookId).ToList();
            var mine = await _db.EbookProgresses.Where(p => p.UserId == userId && ids.Contains(p.EbookId)).ToDictionaryAsync(p => p.EbookId, ct);
            foreach (var a in anonProgress)
            {
                if (mine.TryGetValue(a.EbookId, out var p))
                {
                    if (a.UpdatedAt <= p.UpdatedAt) continue;
                }
                else
                {
                    p = new EbookProgress { EbookId = a.EbookId, UserId = userId };
                    _db.EbookProgresses.Add(p);
                }
                p.Page = a.Page;
                p.PageCount = a.PageCount;
                p.Locator = a.Locator;
                p.UpdatedAt = a.UpdatedAt;
            }
            _db.EbookAnonymousProgresses.RemoveRange(anonProgress);
        }
        await _db.SaveChangesAsync(ct);
        if (books.Count > 0 || anonProgress.Count > 0)
            _logger.LogInformation("E-Books ohne Anmeldung übernommen: {Books} Bücher, {Progress} Lesestände → Nutzer {UserId}", books.Count, anonProgress.Count, userId);
        return books.Count;
    }

    /// <summary>Nie übernommene anonyme Uploads, älter als <paramref name="olderThan"/>, löschen (Dateien inklusive)
    /// und verwaiste anonyme Lesestände gleich mit. Antwort = Zahl der gelöschten Bücher.</summary>
    public async Task<int> CleanupAnonymousAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        var books = await _db.Ebooks.Where(b => b.OwnerUserId == null && b.AnonymousSessionId != null && b.CreatedAt < cutoff)
            .OrderBy(b => b.Id).Take(50).ToListAsync(ct);
        foreach (var b in books) await RemoveBookAsync(b, ct);
        var stale = await _db.EbookAnonymousProgresses.Where(p => p.UpdatedAt < cutoff).Take(1000).ToListAsync(ct);
        if (stale.Count > 0)
        {
            _db.EbookAnonymousProgresses.RemoveRange(stale);
            await _db.SaveChangesAsync(ct);
        }
        if (books.Count > 0) _logger.LogInformation("E-Books ohne Anmeldung aufgeräumt: {Count} Bücher", books.Count);
        return books.Count;
    }
}
