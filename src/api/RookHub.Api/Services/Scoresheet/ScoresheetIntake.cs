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
using static RookHub.Api.Services.Scoresheet.ScoresheetScanRows;

namespace RookHub.Api.Services.Scoresheet;

/// <summary>
/// Annahme der Einlesungen (Zerlegung Z6 aus <see cref="ScoresheetScanService"/>): Fotos prüfen, nach Seiten ordnen,
/// verkleinert ablegen und einreihen — mit Konto, ohne Konto (Teilen-Link) oder mit fertiger Lesung (ohne Modell).
/// </summary>
internal sealed class ScoresheetIntake
{
    private readonly AppDbContext _db;
    private readonly IScoresheetVisionClient _vision;
    private readonly ILogger<ScoresheetScanService> _logger;
    private readonly ScoresheetQuota _quota;
    private readonly ScoresheetProcessor _processor;
    private readonly LeagueScoresheetScans _league;

    internal ScoresheetIntake(AppDbContext db, IScoresheetVisionClient vision, ILogger<ScoresheetScanService> logger,
        ScoresheetQuota quota, ScoresheetProcessor processor, LeagueScoresheetScans league, bool external)
    {
        _db = db;
        _vision = vision;
        _logger = logger;
        _quota = quota;
        _processor = processor;
        _league = league;
        External = external;
    }

    /// <summary>Liest von außen (<see cref="ScoresheetScanService.External"/>).</summary>
    private bool External { get; }

    /// <summary>Nimmt ein Foto an und reiht es ein. Absage als Grund-Code (<c>notConfigured</c>,
    /// <c>unsupportedImage</c>, <c>tooLarge</c>, <c>dailyLimit</c>, <c>tooManyOpen</c>, <c>invalidLanguage</c>).</summary>
    public async Task<(ScoresheetScanDto? Scan, string? Reason)> CreateAsync(int userId, byte[] data, string? contentType,
        string? fileName, string? language, string? ownerSide = null, string? purpose = null) =>
        await CreateCoreAsync(userId, new[] { new ScoresheetUpload(data, contentType, fileName) }, language, ownerSide,
            purpose, null, null);

    /// <summary>Ein Formular über mehrere Fotos (in Seitenreihenfolge, höchstens <see cref="ScoresheetScanService.MaxPages"/>) — EINE Einlesung.
    /// Zusätzlicher Absagegrund <c>tooManyPages</c>.</summary>
    /// <param name="clubId">Bei <see cref="ScoresheetScan.PurposeLeague"/>: der Verein, für den eingelesen wird (Mandanten-Schritt
    /// 2026-10-07) — sonst ohne Wirkung.</param>
    public async Task<(ScoresheetScanDto? Scan, string? Reason)> CreateAsync(int userId, IReadOnlyList<ScoresheetUpload> pages,
        string? language, string? ownerSide = null, string? purpose = null, int? clubId = null) =>
        await CreateCoreAsync(userId, pages, language, ownerSide, purpose, null, null, clubId);

    private async Task<(ScoresheetScanDto? Scan, string? Reason)> CreateCoreAsync(int? userId, IReadOnlyList<ScoresheetUpload> pages,
        string? language, string? ownerSide, string? purpose, string? accessKey, string? ipHash, int? clubId = null)
    {
        if (!External && !_vision.IsConfigured) return (null, "notConfigured");
        var lang = string.IsNullOrWhiteSpace(language) ? "auto" : language.Trim().ToLowerInvariant();
        if (!ScoresheetNotation.IsKnown(lang)) return (null, "invalidLanguage");
        if (pages.Count == 0) return (null, "noFile");
        if (SheetGroups(pages) is not { } groups) return (null, "invalidLayout");
        if (groups.Count > ScoresheetScanService.MaxPages) return (null, "tooManyPages");
        if (groups.Any(g => g.Count > ScoresheetScanService.MaxPhotosPerPage)) return (null, "tooManyViews");
        foreach (var page in pages)
        {
            if (page.Data.Length == 0 || page.Data.Length > ScoresheetScanService.MaxUploadBytes) return (null, "tooLarge");
            if (page.ContentType != null && !ScoresheetImage.AcceptedTypes.Contains(page.ContentType)) return (null, "unsupportedImage");
            if (!ScoresheetImage.CanDecode(page.Data)) return (null, "unsupportedImage");
        }

        purpose = ScoresheetQuota.CleanPurpose(purpose);
        var spent = await _quota.SpentAsync(userId);
        var admin = spent.IsAdmin;
        var since = DateTime.UtcNow.AddDays(-1);
        if (userId is int uid)
        {
            if (!admin && await _quota.CountingSince(uid, since, purpose).CountAsync() >= _quota.LimitFor(purpose))
                return (null, "dailyLimit");
            if (await _db.ScoresheetScans.CountAsync(s => s.UserId == uid
                    && (s.Status == ScoresheetScanStatus.Pending || s.Status == ScoresheetScanStatus.Running)) >= ScoresheetScanService.MaxOpenPerUser)
                return (null, "tooManyOpen");
        }
        if (!External && _quota.AllowanceFor(userId, spent).Blocked is { } blocked) return (null, blocked);   // von außen: keine Kosten

        var stored = new List<List<(byte[] Photo, string Type, string? Name)>>();
        foreach (var group in groups)
        {
            var photos = new List<(byte[] Photo, string Type, string? Name)>();
            foreach (var page in group)
            {
                var photo = page.Data;
                var type = page.ContentType ?? "image/jpeg";
                if (photo.Length > ScoresheetScanService.MaxStoredBytes)
                {
                    photo = ScoresheetImage.Prepare(page.Data, ScoresheetScanService.StoredEdge, 90) ?? page.Data;
                    type = "image/jpeg";
                    if (photo.Length > ScoresheetScanService.MaxStoredBytes) return (null, "tooLarge");
                }
                photos.Add((photo, type, CleanFileName(page.FileName)));
            }
            stored.Add(photos);
        }

        var scan = new ScoresheetScan
        {
            UserId = userId,
            AccessKey = accessKey,
            AnonIpHash = ipHash,
            Photo = stored[0][0].Photo,
            ContentType = stored[0][0].Type,
            FileName = stored[0][0].Name,
            PageCount = stored.Count,
            Pages = stored.Skip(1).Select((p, i) => new ScoresheetScanPage
            {
                Page = i + 2, Photo = p[0].Photo, ContentType = p[0].Type, FileName = p[0].Name,
            }).ToList(),
            Views = stored.SelectMany((p, i) => p.Skip(1).Select((v, j) => new ScoresheetScanView
            {
                Page = i + 1, View = j + 1, Photo = v.Photo, ContentType = v.Type, FileName = v.Name,
            })).ToList(),
            NotationLanguage = lang,
            OwnerSide = ownerSide is "white" or "black" ? ownerSide : "auto",
            Purpose = purpose,
            ClubId = purpose == ScoresheetScan.PurposeLeague ? clubId : null,
            Status = ScoresheetScanStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };
        _db.ScoresheetScans.Add(scan);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Formular-Einlesung {ScanId} von User {UserId} angenommen ({Pages} Seite(n), {Bytes} Bytes, Sprache {Language})",
            scan.Id, userId?.ToString() ?? "ohne Konto", stored.Count, stored.Sum(p => p.Sum(x => x.Photo.Length)), lang);
        return (ToDto(scan), null);
    }

    /// <summary>Die Fotos eines Uploads nach Seiten (0.736.0): ohne <see cref="ScoresheetUpload.SheetPage"/> ist jedes Foto
    /// eine eigene Seite (wie bisher); mit ihr gehören Fotos derselben Zahl zu einer Seite, das erste ist ihr Hauptfoto.
    /// <c>null</c> = gemischt oder Seiten nicht lückenlos ab 1.</summary>
    internal static List<List<ScoresheetUpload>>? SheetGroups(IReadOnlyList<ScoresheetUpload> uploads)
    {
        if (uploads.All(u => u.SheetPage <= 0)) return uploads.Select(u => new List<ScoresheetUpload> { u }).ToList();
        if (uploads.Any(u => u.SheetPage <= 0)) return null;
        var groups = uploads.GroupBy(u => u.SheetPage).OrderBy(g => g.Key).ToList();
        if (!groups.Select(g => g.Key).SequenceEqual(Enumerable.Range(1, groups.Count))) return null;
        return groups.Select(g => g.ToList()).ToList();
    }

    /// <summary>
    /// Eine Liga-Einlesung aus einer FERTIGEN Lesung anlegen, ohne Modell-Aufruf (Wunsch 2026-10-06: „mach die OCR für die
    /// hochgeladenen Formulare anstelle von mit Key"). <paramref name="transcriptionJson"/> hat die Form der Modell-Antwort
    /// (<see cref="ScoresheetPrompt.Schema"/>); Kästen in Pixeln des aufrechten, auf <see cref="ScoresheetScanService.ModelEdge"/> verkleinerten
    /// Fotos — so, wie das Modell es sähe. Danach derselbe Weg wie eine gelesene Einlesung: Auflösung, Engine-Prüfung,
    /// Stand je Halbzug — die Einlesung steht dem Besitzer in LeagueHub zum Prüfen offen wie eine selbst hochgeladene.
    /// Mit <paramref name="clubGameId"/> wird sie gleich als zu dieser (schon übernommenen) Vereinspartie gehörig
    /// archiviert — „Korrigieren" zeigt dann Foto und Lesarten. Keine Tageszahl, keine Kostenbremse (kein Modell).
    /// </summary>
    /// <param name="clubId">Der Verein, in dessen LeagueHub die Einlesung zum Prüfen liegt (der Verein des Stapels); mit
    /// <paramref name="clubGameId"/> zählt der Verein der Partie.</param>
    public async Task<(ScoresheetScanDto? Scan, string? Reason)> CreateManualAsync(int ownerUserId, IReadOnlyList<ScoresheetUpload> pages,
        string transcriptionJson, int? clubGameId, CancellationToken ct = default, int? clubId = null)
    {
        if (pages.Count == 0) return (null, "noFile");
        // Wie beim Hochladen (0.754.0): mit SheetPage gehören mehrere Fotos zu einer Seite, das erste ist ihr Hauptfoto
        // (Kästen beziehen sich darauf), die weiteren werden Ansichten (z. B. das zweite Exemplar auf demselben Foto).
        if (SheetGroups(pages) is not { } groups) return (null, "invalidLayout");
        if (groups.Count > ScoresheetScanService.MaxPages) return (null, "tooManyPages");
        if (groups.Any(g => g.Count > ScoresheetScanService.MaxPhotosPerPage)) return (null, "tooManyViews");
        if (pages.Any(p => p.Data.Length == 0 || !ScoresheetImage.CanDecode(p.Data))) return (null, "unsupportedImage");
        var mains = groups.Select(g => g[0]).ToList();
        string? finalPgn = null;
        if (clubGameId is int cg)
        {
            var game = await _db.LeagueClubGames.Where(g => g.Id == cg).Select(g => new { g.Pgn, g.ClubId }).FirstOrDefaultAsync(ct);
            if (game == null) return (null, "clubGameNotFound");
            finalPgn = game.Pgn;
            clubId = game.ClubId;
        }

        var sizes = mains.Select(p => ScoresheetImage.Prepare(p.Data, ScoresheetScanService.ModelEdge) is { } j ? ScoresheetImage.Size(j) : null).ToList();
        var json = ScoresheetTranscription.WithImageSize(transcriptionJson,
            sizes.All(x => x != null) ? sizes.Select(x => x!.Value).ToList() : new List<(int Width, int Height)>());
        var t = ScoresheetTranscription.Parse(json);
        if (t == null || t.Moves.Count == 0) return (null, "invalidTranscription");
        var language = ScoresheetReader.EffectiveLanguage("auto", t.NotationLanguage);
        var r = ScoresheetResolver.Resolve(t.Scanned(), new ScoresheetResolver.Options(ScoresheetNotation.Find(language)));
        if (r.Plies.Count == 0) return (null, "noMoves");

        var stored = groups.Select(g => g.Select(p => p.Data.Length > ScoresheetScanService.MaxStoredBytes
            ? (Photo: ScoresheetImage.Prepare(p.Data, ScoresheetScanService.StoredEdge, 90) ?? p.Data, Type: "image/jpeg", Name: CleanFileName(p.FileName))
            : (Photo: p.Data, Type: p.ContentType ?? "image/jpeg", Name: CleanFileName(p.FileName))).ToList()).ToList();
        var now = DateTime.UtcNow;
        var scan = new ScoresheetScan
        {
            UserId = ownerUserId,
            Photo = stored[0][0].Photo,
            ContentType = stored[0][0].Type,
            FileName = stored[0][0].Name,
            PageCount = stored.Count,
            Pages = stored.Skip(1).Select((p, i) => new ScoresheetScanPage
            {
                Page = i + 2, Photo = p[0].Photo, ContentType = p[0].Type, FileName = p[0].Name,
            }).ToList(),
            Views = stored.SelectMany((p, i) => p.Skip(1).Select((v, j) => new ScoresheetScanView
            {
                Page = i + 1, View = j + 1, Photo = v.Photo, ContentType = v.Type, FileName = v.Name,
            })).ToList(),
            NotationLanguage = "auto",
            OwnerSide = "auto",
            Purpose = ScoresheetScan.PurposeLeague,
            ClubId = clubId,
            Status = ScoresheetScanStatus.Running,
            Model = ScoresheetScanService.ManualModel,
            CreatedAt = now,
            StartedAt = now,
        };
        _db.ScoresheetScans.Add(scan);
        await _db.SaveChangesAsync(ct);

        r = await _processor.CheckWithEngineAsync(scan.Id, t, r, language, ct);
        scan.TranscriptionJson = json;
        scan.ResolutionJson = JsonSerializer.Serialize(new StoredResolution
        {
            Language = language, Plies = r.Plies, Unresolved = r.Unresolved, UnresolvedFrom = r.StuckAt,
        }, Json);
        scan.Status = ScoresheetScanStatus.Done;
        scan.FinishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Formular-Einlesung {ScanId} (ohne Modell, für User {UserId}) angelegt: {Plies} Halbzüge, {Uncertain} unsicher",
            scan.Id, ownerUserId, r.Plies.Count, r.Plies.Count(p => p.Uncertain));
        if (finalPgn != null) await _league.CloseLeagueScanAsync(ScoresheetScanService.ScanActor.ManagerOf(ownerUserId) with { ClubId = clubId }, scan.Id, finalPgn, clubGameId);
        return (ToDto(scan), null);
    }

    public async Task<ScoresheetScanDto?> GetAsync(int userId, int id)
    {
        var scan = await ScanHeads(_db).FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
        return scan == null ? null : ToDto(scan);
    }

    /// <summary>Die letzten Einlesungen des Nutzers (ohne Foto) — für die Seite, falls man sie verlassen hat.</summary>
    public async Task<List<ScoresheetScanDto>> ListAsync(int userId, int take = 20)
    {
        // Einlesungen einer gelöschten Partie bleiben nur fürs Kontingent liegen (ohne Foto) — in der Liste nicht.
        // Ebenso gelöschte bzw. nach Frist geleerte (Vermerk, ScoresheetPhotoRetention).
        var scans = await ScanHeads(_db).Where(s => s.UserId == userId && s.Purpose == null && s.FileName != DiscardedMark
                && !(s.Status == ScoresheetScanStatus.Done && s.SavedGameId == null))
            .OrderByDescending(s => s.CreatedAt).Take(Math.Clamp(take, 1, 50)).ToListAsync();
        var gameIds = scans.Where(s => s.SavedGameId != null).Select(s => s.SavedGameId!.Value).ToList();
        var names = await _db.SavedGames.Where(g => gameIds.Contains(g.Id))
            .Select(g => new { g.Id, g.White, g.Black }).ToDictionaryAsync(g => g.Id);
        return scans.Select(s =>
        {
            var dto = ToDto(s);
            if (s.SavedGameId is int gid && names.TryGetValue(gid, out var n)) { dto.White = n.White; dto.Black = n.Black; }
            return dto;
        }).ToList();
    }

    /// <summary>Annahme OHNE Konto nacheinander: Zählen und Anlegen sind zwei Schritte, und parallel abgeschickte Uploads
    /// zählten sonst alle denselben Stand und kamen gemeinsam durch (10/IP, 100/Tag, 3 offen; Codereview 2026-09-29,
    /// A6-003). Die API läuft als EINE Instanz — ein Semaphor im Prozess genügt.</summary>
    private static readonly SemaphoreSlim AnonAdmission = new(1, 1);

    /// <summary>
    /// Foto OHNE Konto hochladen (LeagueHub-Teilen-Link, immer für die Vereins-Datenbank). Grenzen:
    /// <see cref="ScoresheetScanService.AnonPerIpDailyLimit"/> je IP (<c>dailyLimit</c>), <see cref="ScoresheetScanService.AnonDailyLimit"/> für alle zusammen
    /// (<c>anonDailyLimit</c>), drei gleichzeitig je IP, dazu das Tagesbudget der Einlesungen ohne Konto und das
    /// Gesamtbudget (<c>globalBudget</c>). Liefert den geheimen Schlüssel, unter dem der Browser die Einlesung
    /// wiederfindet — sonst niemand.
    /// </summary>
    public async Task<(ScoresheetScanDto? Scan, string? Key, string? Reason)> CreateAnonymousAsync(byte[] data, string? contentType,
        string? fileName, string? language, string? ownerSide, string ipHash) =>
        await CreateAnonymousAsync(new[] { new ScoresheetUpload(data, contentType, fileName) }, language, ownerSide, ipHash);

    /// <summary>Wie oben, ein Formular über mehrere Fotos (Seitenreihenfolge, höchstens <see cref="ScoresheetScanService.MaxPages"/>, 0.690.1).</summary>
    public async Task<(ScoresheetScanDto? Scan, string? Key, string? Reason)> CreateAnonymousAsync(IReadOnlyList<ScoresheetUpload> pages,
        string? language, string? ownerSide, string ipHash, int? clubId = null)
    {
        ScoresheetScanDto? scan;
        string? reason;
        var key = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        await AnonAdmission.WaitAsync();
        try
        {
            var since = DateTime.UtcNow.AddDays(-1);
            if (External || _vision.IsConfigured)
            {
                if (await _quota.AnonCountingSince(since).CountAsync(s => s.AnonIpHash == ipHash) >= ScoresheetScanService.AnonPerIpDailyLimit)
                    return (null, null, "dailyLimit");
                if (await _quota.AnonCountingSince(since).CountAsync() >= ScoresheetScanService.AnonDailyLimit) return (null, null, "anonDailyLimit");
                if (await _db.ScoresheetScans.CountAsync(s => s.UserId == null && s.AnonIpHash == ipHash
                        && (s.Status == ScoresheetScanStatus.Pending || s.Status == ScoresheetScanStatus.Running)) >= ScoresheetScanService.MaxOpenPerUser)
                    return (null, null, "tooManyOpen");
            }
            (scan, reason) = await CreateCoreAsync(null, pages, language, ownerSide, ScoresheetScan.PurposeLeague, key, ipHash, clubId);
        }
        finally
        {
            AnonAdmission.Release();
        }
        if (scan != null) await ForgetOldIpHashesAsync();
        return (scan, scan == null ? null : key, reason);
    }

    /// <summary>Der IP-Vermerk wird nur fürs 24-h-Fenster gebraucht — nach zwei Tagen geleert.</summary>
    private async Task ForgetOldIpHashesAsync()
    {
        var cutoff = DateTime.UtcNow.AddDays(-2);
        var old = await _db.ScoresheetScans.Where(s => s.AnonIpHash != null && s.CreatedAt < cutoff)
            .Select(s => new { s.Id, s.UserId, s.SavedGameId }).Take(500).ToListAsync();
        if (old.Count == 0) return;
        foreach (var o in old)
        {
            var stub = _db.ScoresheetScans.Local.FirstOrDefault(s => s.Id == o.Id);
            if (stub == null)
            {
                stub = new ScoresheetScan { Id = o.Id, UserId = o.UserId, SavedGameId = o.SavedGameId, AnonIpHash = "x" };
                _db.ScoresheetScans.Attach(stub);
            }
            stub.AnonIpHash = null;
            _db.Entry(stub).Property(nameof(ScoresheetScan.AnonIpHash)).IsModified = true;
        }
        await _db.SaveChangesAsync();
    }
}
