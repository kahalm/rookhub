using System.Text.Json;
using System.Text.Json.Serialization;
using Chess;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// „Partieformular einlesen" (0.529.0): Foto hochladen → Claude liest → der Server macht daraus eine legale
/// Partie (<see cref="ScoresheetResolver"/>) → sie liegt in „Meine Partien", das Foto bleibt daneben liegen.
///
/// <para>Das Lesen dauert (Nachdenken + ggf. Nachfrage) typisch eine halbe bis zwei Minuten und läuft deshalb im
/// Hintergrund (<see cref="ScoresheetScanWorker"/>); der Zustand steht in der Datenbank, ein Neustart verliert
/// nichts. Geht die Lesung ab einer Stelle nicht mehr legal auf, fragt der Dienst beim Modell NACH — mit der
/// Stellung, den legalen Zügen dort und dem Hinweis, dass oft ein früherer Eintrag der falsche ist
/// (<see cref="MaxRounds"/> Durchgänge insgesamt). Was dann noch offen ist, steht am Ende der Partie als
/// Kommentar und auf der Korrekturseite.</para>
/// </summary>
public class ScoresheetScanService
{
    private readonly AppDbContext _db;
    private readonly IScoresheetVisionClient _vision;
    private readonly SavedGameService _games;
    private readonly NotificationService _notifications;
    private readonly ILogger<ScoresheetScanService> _logger;
    private readonly int _dailyLimit;
    private readonly int _leagueDailyLimit;
    private readonly string _ipSecret;
    private readonly ScoresheetBudget _budget;
    private readonly ScoresheetReader _reader;
    private readonly IScoresheetEngine? _engine;
    private readonly ScoresheetReadMode _startMode;

    /// <summary>Größter angenommener Upload (je Foto).</summary>
    public const int MaxUploadBytes = 30 * 1024 * 1024;

    /// <summary>So viele Fotos darf ein Formular haben (0.600.0, Wunsch 2026-09-29: „2. Bild für die 2. Seite, + 3. Seite") —
    /// eine lange Partie geht über mehrere Blätter. Gelesen wird in EINEM Aufruf, es bleibt EINE Einlesung (Tageszahl).</summary>
    public const int MaxPages = 3;

    /// <summary>Größte Anfrage beim Hochladen (alle Fotos zusammen) — so viel lässt auch der Frontend-nginx für
    /// <c>/api/scoresheets</c> durch (<c>DeploymentConfigTests</c>).</summary>
    public const int MaxUploadRequestBytes = 64 * 1024 * 1024;

    /// <summary>Größer wird ein Foto nicht abgelegt — darüber wird es verkleinert gespeichert (3000 px).</summary>
    public const int MaxStoredBytes = 12 * 1024 * 1024;

    /// <summary>Längste Bildseite, die das Modell bekommt.</summary>
    public const int ModelEdge = 2000;

    /// <summary>Längste Bildseite eines verkleinert abgelegten Fotos.</summary>
    public const int StoredEdge = 3000;

    /// <summary>Lese-Durchgänge insgesamt (1 + Nachfragen).</summary>
    public const int MaxRounds = 3;

    /// <summary>So viele festgelegte Halbzüge nimmt das Neu-Aufbereiten an (<see cref="ResolveRestAsync"/>) — wie das
    /// Speichern der Partie (<see cref="SavedGameService.UpdateAsync"/>).</summary>
    public const int MaxPrefixPlies = 600;

    /// <summary>So oft setzt der Worker an einer Einlesung an (ein Absturz mittendrin zählt mit).</summary>
    public const int MaxAttempts = 3;

    /// <summary>So viele Einlesungen dürfen je Nutzer gleichzeitig warten oder laufen.</summary>
    public const int MaxOpenPerUser = 3;

    /// <summary>Vorgabe für <c>Scoresheet:DailyLimit</c> — jede Einlesung kostet echtes Geld beim Modell. Seit 0.568.1
    /// EINE je 24 h für normale Nutzer (Wunsch 2026-09-27; vorher 20), Admins zählen nicht. Was mitzählt:
    /// <see cref="CountingSince"/>.</summary>
    public const int DefaultDailyLimit = 1;

    /// <summary>Vorgabe für <c>Scoresheet:LeagueDailyLimit</c>: Einlesungen für die Vereins-Datenbank (LeagueHub) zählen
    /// EIGENS — zehn je Nutzer und 24 h (Wunsch 2026-09-28: „Formularlimit in dem Modus auf 10/User erhöhen"); die
    /// Kostenbremse (<see cref="ScoresheetBudget"/>) gilt für beide Wege gemeinsam.</summary>
    public const int DefaultLeagueDailyLimit = 10;

    /// <summary>Einlesungen OHNE Konto (LeagueHub-Teilen-Link): höchstens so viele je IP und 24 h …</summary>
    public const int AnonPerIpDailyLimit = 10;
    /// <summary>… und so viele von allen zusammen in 24 h (Wunsch 2026-09-28: „10/IP und 100/Tag").</summary>
    public const int AnonDailyLimit = 100;

    public ScoresheetScanService(AppDbContext db, IScoresheetVisionClient vision, SavedGameService games,
        NotificationService notifications, ILogger<ScoresheetScanService> logger, IConfiguration? config = null,
        IScoresheetEngine? engine = null)
    {
        _db = db;
        _vision = vision;
        _games = games;
        _notifications = notifications;
        _logger = logger;
        _dailyLimit = int.TryParse(config?["Scoresheet:DailyLimit"], out var l) && l > 0 ? l : DefaultDailyLimit;
        _leagueDailyLimit = int.TryParse(config?["Scoresheet:LeagueDailyLimit"], out var ll) && ll > 0 ? ll : DefaultLeagueDailyLimit;
        _ipSecret = config?["Jwt:Key"] is { Length: > 0 } k ? k : "rookhub-scoresheet";
        // Scoresheet:Thinking — Vorgabe seit 0.533.2: NUR ABSCHREIBEN (Opus 5.5 mit effort low, die Schachlogik macht der
        // Auflöser): am Testsatz 93,1 % für 0,08 $ und 25 s je Formular, und als einzige Variante schlüssig am langen,
        // verbesserten Kufstein-Formular (10 Reparaturen statt 42–60). true = mit Nachdenken und dem vollen Auftrag,
        // Rückfall ohne.
        _startMode = bool.TryParse(config?["Scoresheet:Thinking"], out var thinking) && thinking
            ? ScoresheetReadMode.Full : ScoresheetReadMode.Transcribe;
        _budget = new ScoresheetBudget(config);
        _reader = new ScoresheetReader(vision);
        // Scoresheet:Plausibility — Engine-Prüfung nach dem Lesen (0.646.0, ScoresheetPlausibility), Vorgabe an.
        _engine = bool.TryParse(config?["Scoresheet:Plausibility"], out var plausible) && !plausible ? null : engine;
        External = string.Equals(config?["Scoresheet:Reader"], "external", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Wer liest (<c>Scoresheet:Reader</c>, 0.687.0, Wunsch 2026-10-06: „der Watcher soll das bisherige Verarbeiten via Key
    /// ersetzen — Key ganz abschalten"): <c>external</c> (gesetzt in <c>appsettings.json</c>) = VON AUSSEN — die Einlesung bleibt <c>pending</c>, der Worker rührt sie nicht an, und der Watcher auf
    /// dem Server (Claude in einer eingeschränkten Sitzung) liest die Fotos über <c>/api/admin/scoresheets</c> und gibt
    /// die Lesung mit <see cref="ProcessReadingAsync"/> zurück. Kein Schlüssel, keine Kosten, keine Kostenbremse. Alles andere
    /// (auch ohne Konfiguration, so laufen die Unit-Tests) = wie bisher das Modell über den Anthropic-Schlüssel.
    /// </summary>
    public bool External { get; }

    /// <summary>Die Kostenbremse (für Tests und die Statusanzeige).</summary>
    public ScoresheetBudget Budget => _budget;

    /// <summary>Was ein Nutzer heute und in 30 Tagen verbraucht hat, was alle zusammen heute — und ob er Admin ist.</summary>
    /// <para>Ohne Konto (<paramref name="userId"/> <c>null</c>, Teilen-Link) gibt es keine Nutzerbudgets — dort begrenzen
    /// die Zahlen je IP und je Tag (<see cref="AnonPerIpDailyLimit"/>, <see cref="AnonDailyLimit"/>), und an der Stelle des
    /// Nutzerverbrauchs steht, was ALLE Einlesungen ohne Konto heute verbraucht haben (ihr gemeinsames Tagesbudget,
    /// <see cref="ScoresheetBudget.AnonAllowance"/>); das Gesamtbudget gilt dazu.</para>
    private async Task<(long UserToday, long UserMonth, long GlobalToday, bool IsAdmin)> SpentAsync(int? userId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var day = now.AddDays(-1);
        var month = now.AddDays(-30);
        if (userId == null)
        {
            var anonToday = await _db.ScoresheetScans.Where(s => s.UserId == null && s.CreatedAt >= day)
                .SumAsync(s => (long?)s.CostMicroUsd, ct) ?? 0;
            var global0 = await _db.ScoresheetScans.Where(s => s.CreatedAt >= day).SumAsync(s => (long?)s.CostMicroUsd, ct) ?? 0;
            return (anonToday, 0, global0, false);
        }
        var userToday = await _db.ScoresheetScans.Where(s => s.UserId == userId && s.CreatedAt >= day)
            .SumAsync(s => (long?)s.CostMicroUsd, ct) ?? 0;
        var userMonth = await _db.ScoresheetScans.Where(s => s.UserId == userId && s.CreatedAt >= month)
            .SumAsync(s => (long?)s.CostMicroUsd, ct) ?? 0;
        var globalToday = await _db.ScoresheetScans.Where(s => s.CreatedAt >= day)
            .SumAsync(s => (long?)s.CostMicroUsd, ct) ?? 0;
        var isAdmin = await _db.AppUsers.Where(u => u.Id == userId).Select(u => u.IsAdmin).FirstOrDefaultAsync(ct);
        return (userToday, userMonth, globalToday, isAdmin);
    }

    /// <summary>
    /// Die Einlesungen eines Nutzers seit <paramref name="since"/>, die gegen das Tageskontingent zählen: alle außer den
    /// gescheiterten, die keinen Cent gekostet haben (Modell nicht erreichbar, Budget schon vor dem ersten Aufruf leer) —
    /// bei einer Einlesung am Tag sperrte ein Ausfall auf unserer Seite sonst für 24 h. Eine Einlesung, deren Partie
    /// gelöscht wurde, zählt weiter (<see cref="DetachWithoutLoading"/>), sonst hieße „Partie löschen“ „noch einmal lesen“.
    /// </summary>
    /// <para>Je Weg getrennt (<paramref name="purpose"/>): RookHubs „Meine Partien" und die Vereins-Datenbank haben
    /// eigene Tageszahlen (<see cref="LimitFor"/>).</para>
    private IQueryable<ScoresheetScan> CountingSince(int userId, DateTime since, string? purpose)
        => _db.ScoresheetScans.Where(s => s.UserId == userId && s.CreatedAt >= since && s.Purpose == purpose
            && !(s.Status == ScoresheetScanStatus.Failed && s.CostMicroUsd == 0));

    private int LimitFor(string? purpose) => purpose == ScoresheetScan.PurposeLeague ? _leagueDailyLimit : _dailyLimit;

    private static string? CleanPurpose(string? purpose) => purpose == ScoresheetScan.PurposeLeague ? purpose : null;

    /// <summary>Darf für diesen Nutzer jetzt noch ein Modell-Aufruf starten, und wie lang darf die Antwort werden?</summary>
    internal async Task<CallAllowance> AllowanceAsync(int? userId, CancellationToken ct = default)
        => AllowanceFor(userId, await SpentAsync(userId, ct));

    /// <summary>Mit Konto die Nutzerbudgets samt Gesamtbudget, ohne Konto das gemeinsame Tagesbudget der Einlesungen ohne
    /// Konto samt Gesamtbudget (<see cref="SpentAsync"/>).</summary>
    private CallAllowance AllowanceFor(int? userId, (long UserToday, long UserMonth, long GlobalToday, bool IsAdmin) spent)
        => userId == null
            ? _budget.AnonAllowance(spent.UserToday, spent.GlobalToday)
            : _budget.Allowance(spent.UserToday, spent.UserMonth, spent.GlobalToday, spent.IsAdmin);

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Hochladen + Stand ─────────────────────────────────────────────

    public async Task<ScoresheetStatusDto> StatusAsync(int userId, string? purpose = null)
    {
        purpose = CleanPurpose(purpose);
        var limit = LimitFor(purpose);
        var since = DateTime.UtcNow.AddDays(-1);
        var (today, month, global, admin) = await SpentAsync(userId);
        var dayShare = _budget.UserDailyMicroUsd > 0 ? (double)today / _budget.UserDailyMicroUsd : 1;
        var monthShare = _budget.UserMonthlyMicroUsd > 0 ? (double)month / _budget.UserMonthlyMicroUsd : 1;
        var used = await CountingSince(userId, since, purpose).CountAsync();
        // Das Fenster rollt über 24 h: frei wird es, wenn die älteste mitzählende Einlesung herausfällt.
        DateTime? next = null;
        if (!admin && used >= limit)
        {
            var oldest = await CountingSince(userId, since, purpose).OrderBy(s => s.CreatedAt).Skip(used - limit)
                .Select(s => s.CreatedAt).FirstOrDefaultAsync();
            next = DateTime.SpecifyKind(oldest.AddDays(1), DateTimeKind.Utc);
        }
        return new ScoresheetStatusDto
        {
            Available = External || _vision.IsConfigured,
            DailyLimit = limit,
            UsedToday = used,
            NextAllowedAt = next,
            BudgetUsedPercent = admin ? 0 : (int)Math.Clamp(Math.Round(Math.Max(dayShare, monthShare) * 100), 0, 100),
            Blocked = _budget.Check(today, month, global, admin),
            Unlimited = admin,
            Languages = ScoresheetNotation.Languages.Select(l => new ScoresheetLanguageDto
            {
                Code = l.Code,
                Name = l.Name,
                Pieces = $"{l.King} {l.Queen} {l.Rook} {l.Bishop} {l.Knight}",
            }).ToList(),
        };
    }

    /// <summary>Nimmt ein Foto an und reiht es ein. Absage als Grund-Code (<c>notConfigured</c>,
    /// <c>unsupportedImage</c>, <c>tooLarge</c>, <c>dailyLimit</c>, <c>tooManyOpen</c>, <c>invalidLanguage</c>).</summary>
    public async Task<(ScoresheetScanDto? Scan, string? Reason)> CreateAsync(int userId, byte[] data, string? contentType,
        string? fileName, string? language, string? ownerSide = null, string? purpose = null) =>
        await CreateCoreAsync(userId, new[] { new ScoresheetUpload(data, contentType, fileName) }, language, ownerSide,
            purpose, null, null);

    /// <summary>Ein Formular über mehrere Fotos (in Seitenreihenfolge, höchstens <see cref="MaxPages"/>) — EINE Einlesung.
    /// Zusätzlicher Absagegrund <c>tooManyPages</c>.</summary>
    public async Task<(ScoresheetScanDto? Scan, string? Reason)> CreateAsync(int userId, IReadOnlyList<ScoresheetUpload> pages,
        string? language, string? ownerSide = null, string? purpose = null) =>
        await CreateCoreAsync(userId, pages, language, ownerSide, purpose, null, null);

    private async Task<(ScoresheetScanDto? Scan, string? Reason)> CreateCoreAsync(int? userId, IReadOnlyList<ScoresheetUpload> pages,
        string? language, string? ownerSide, string? purpose, string? accessKey, string? ipHash)
    {
        if (!External && !_vision.IsConfigured) return (null, "notConfigured");
        var lang = string.IsNullOrWhiteSpace(language) ? "auto" : language.Trim().ToLowerInvariant();
        if (!ScoresheetNotation.IsKnown(lang)) return (null, "invalidLanguage");
        if (pages.Count == 0) return (null, "noFile");
        if (pages.Count > MaxPages) return (null, "tooManyPages");
        foreach (var page in pages)
        {
            if (page.Data.Length == 0 || page.Data.Length > MaxUploadBytes) return (null, "tooLarge");
            if (page.ContentType != null && !ScoresheetImage.AcceptedTypes.Contains(page.ContentType)) return (null, "unsupportedImage");
            if (!ScoresheetImage.CanDecode(page.Data)) return (null, "unsupportedImage");
        }

        purpose = CleanPurpose(purpose);
        var spent = await SpentAsync(userId);
        var admin = spent.IsAdmin;
        var since = DateTime.UtcNow.AddDays(-1);
        if (userId is int uid)
        {
            if (!admin && await CountingSince(uid, since, purpose).CountAsync() >= LimitFor(purpose))
                return (null, "dailyLimit");
            if (await _db.ScoresheetScans.CountAsync(s => s.UserId == uid
                    && (s.Status == ScoresheetScanStatus.Pending || s.Status == ScoresheetScanStatus.Running)) >= MaxOpenPerUser)
                return (null, "tooManyOpen");
        }
        if (!External && AllowanceFor(userId, spent).Blocked is { } blocked) return (null, blocked);   // von außen: keine Kosten

        var stored = new List<(byte[] Photo, string Type)>();
        foreach (var page in pages)
        {
            var photo = page.Data;
            var type = page.ContentType ?? "image/jpeg";
            if (photo.Length > MaxStoredBytes)
            {
                photo = ScoresheetImage.Prepare(page.Data, StoredEdge, 90) ?? page.Data;
                type = "image/jpeg";
                if (photo.Length > MaxStoredBytes) return (null, "tooLarge");
            }
            stored.Add((photo, type));
        }

        var scan = new ScoresheetScan
        {
            UserId = userId,
            AccessKey = accessKey,
            AnonIpHash = ipHash,
            Photo = stored[0].Photo,
            ContentType = stored[0].Type,
            FileName = CleanFileName(pages[0].FileName),
            PageCount = pages.Count,
            Pages = stored.Skip(1).Select((p, i) => new ScoresheetScanPage
            {
                Page = i + 2, Photo = p.Photo, ContentType = p.Type, FileName = CleanFileName(pages[i + 1].FileName),
            }).ToList(),
            NotationLanguage = lang,
            OwnerSide = ownerSide is "white" or "black" ? ownerSide : "auto",
            Purpose = purpose,
            Status = ScoresheetScanStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };
        _db.ScoresheetScans.Add(scan);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Formular-Einlesung {ScanId} von User {UserId} angenommen ({Pages} Seite(n), {Bytes} Bytes, Sprache {Language})",
            scan.Id, userId?.ToString() ?? "ohne Konto", pages.Count, stored.Sum(p => p.Photo.Length), lang);
        return (ToDto(scan), null);
    }

    /// <summary>Wartende Einlesungen für den Leser von außen (<see cref="External"/>), älteste zuerst — ohne Fotos.</summary>
    public async Task<List<ExternalPendingScanDto>> PendingForExternalAsync(CancellationToken ct = default) =>
        await _db.ScoresheetScans.AsNoTracking()
            .Where(s => s.Status == ScoresheetScanStatus.Pending && s.Photo.Length > 0)
            .OrderBy(s => s.CreatedAt)
            .Select(s => new ExternalPendingScanDto
            {
                Id = s.Id, Purpose = s.Purpose ?? "own", UserId = s.UserId, Anonymous = s.UserId == null,
                PageCount = s.PageCount, NotationLanguage = s.NotationLanguage, OwnerSide = s.OwnerSide, CreatedAt = s.CreatedAt,
            }).ToListAsync(ct);

    /// <summary>Foto einer Seite (ab 1) einer Einlesung — für den Leser von außen.</summary>
    public async Task<(byte[] Data, string ContentType)?> PhotoForExternalAsync(int scanId, int page, CancellationToken ct = default)
    {
        if (page <= 1)
            return await _db.ScoresheetScans.AsNoTracking().Where(s => s.Id == scanId && s.Photo.Length > 0)
                .Select(s => new { s.Photo, s.ContentType }).FirstOrDefaultAsync(ct) is { } p ? (p.Photo, p.ContentType) : null;
        return await _db.ScoresheetScanPages.AsNoTracking().Where(x => x.ScoresheetScanId == scanId && x.Page == page)
            .Select(x => new { x.Photo, x.ContentType }).FirstOrDefaultAsync(ct) is { } q ? (q.Photo, q.ContentType) : null;
    }

    /// <summary>
    /// Die Lesung einer WARTENDEN Einlesung von außen übernehmen (<see cref="External"/>): <paramref name="transcriptionJson"/>
    /// in der Form der Modell-Antwort, Kästen in Pixeln des aufrechten, auf <see cref="ModelEdge"/> verkleinerten Fotos.
    /// Danach genau der Weg wie nach dem Lesen durch das Modell (<see cref="FinishAsync"/>). Reasons: <c>notFound</c>,
    /// <c>notPending</c>, <c>invalidTranscription</c>.
    /// </summary>
    public async Task<(ScoresheetScanDto? Scan, string? Reason)> ProcessReadingAsync(int scanId, string transcriptionJson,
        CancellationToken ct = default)
    {
        var scan = await _db.ScoresheetScans.FirstOrDefaultAsync(s => s.Id == scanId, ct);
        if (scan == null) return (null, "notFound");
        if (scan.Status is not (ScoresheetScanStatus.Pending or ScoresheetScanStatus.Running)) return (null, "notPending");
        var photos = new List<byte[]> { scan.Photo };
        if (scan.PageCount > 1)
            photos.AddRange(await _db.ScoresheetScanPages.AsNoTracking().Where(p => p.ScoresheetScanId == scan.Id)
                .OrderBy(p => p.Page).Select(p => p.Photo).ToListAsync(ct));
        var sizes = photos.Select(p => ScoresheetImage.Prepare(p, ModelEdge) is { } j ? ScoresheetImage.Size(j) : null).ToList();
        var json = ScoresheetTranscription.WithImageSize(transcriptionJson,
            sizes.All(x => x != null) ? sizes.Select(x => x!.Value).ToList() : new List<(int Width, int Height)>());
        var t = ScoresheetTranscription.Parse(json);
        if (t == null) return (null, "invalidTranscription");

        scan.Status = ScoresheetScanStatus.Running;
        scan.StartedAt ??= DateTime.UtcNow;
        scan.Attempts++;
        scan.Model = ManualModel;
        scan.Rounds = 1;
        if (t.Moves.Count == 0) { await FailAsync(scan, "noMoves", ct, json); return (ToDto(scan), null); }
        var language = ScoresheetReader.EffectiveLanguage(scan.NotationLanguage, t.NotationLanguage);
        var r = ScoresheetResolver.Resolve(t.Scanned(), new ScoresheetResolver.Options(ScoresheetNotation.Find(language)));
        await FinishAsync(scan, new ScoresheetReader.ReadOutcome(t, r, json, language, 1, null), ct);
        _logger.LogInformation("Formular-Einlesung {ScanId}: Lesung von außen übernommen", scan.Id);
        return (ToDto(scan), null);
    }

    /// <summary>Eine wartende Einlesung von außen als gescheitert abschließen (Foto unbrauchbar, keine Partie darauf,
    /// verdächtiger Inhalt) — mit Glocke wie ein gescheitertes Lesen. Erlaubte Gründe: <c>unreadable</c>, <c>noMoves</c>,
    /// <c>failed</c>.</summary>
    public async Task<string?> FailExternalAsync(int scanId, string? reason, CancellationToken ct = default)
    {
        if (reason is not ("unreadable" or "noMoves" or "failed")) return "invalidReason";
        var scan = await _db.ScoresheetScans.FirstOrDefaultAsync(s => s.Id == scanId, ct);
        if (scan == null) return "notFound";
        if (scan.Status is not (ScoresheetScanStatus.Pending or ScoresheetScanStatus.Running)) return "notPending";
        scan.Model = ManualModel;
        await FailAsync(scan, reason, ct);
        return null;
    }

    /// <summary>Kennung des „Lesers", wenn die Lesung nicht vom Modell kommt, sondern von Claude in einer Sitzung
    /// (Skill <c>/formulare</c>, 0.684.0) — steht an der Einlesung und im Archiv.</summary>
    public const string ManualModel = "claude-manual";

    /// <summary>
    /// Eine Liga-Einlesung aus einer FERTIGEN Lesung anlegen, ohne Modell-Aufruf (Wunsch 2026-10-06: „mach die OCR für die
    /// hochgeladenen Formulare anstelle von mit Key"). <paramref name="transcriptionJson"/> hat die Form der Modell-Antwort
    /// (<see cref="ScoresheetPrompt.Schema"/>); Kästen in Pixeln des aufrechten, auf <see cref="ModelEdge"/> verkleinerten
    /// Fotos — so, wie das Modell es sähe. Danach derselbe Weg wie eine gelesene Einlesung: Auflösung, Engine-Prüfung,
    /// Stand je Halbzug — die Einlesung steht dem Besitzer in LeagueHub zum Prüfen offen wie eine selbst hochgeladene.
    /// Mit <paramref name="clubGameId"/> wird sie gleich als zu dieser (schon übernommenen) Vereinspartie gehörig
    /// archiviert — „Korrigieren" zeigt dann Foto und Lesarten. Keine Tageszahl, keine Kostenbremse (kein Modell).
    /// </summary>
    public async Task<(ScoresheetScanDto? Scan, string? Reason)> CreateManualAsync(int ownerUserId, IReadOnlyList<ScoresheetUpload> pages,
        string transcriptionJson, int? clubGameId, CancellationToken ct = default)
    {
        if (pages.Count == 0) return (null, "noFile");
        if (pages.Count > MaxPages) return (null, "tooManyPages");
        if (pages.Any(p => p.Data.Length == 0 || !ScoresheetImage.CanDecode(p.Data))) return (null, "unsupportedImage");
        string? finalPgn = null;
        if (clubGameId is int cg)
        {
            finalPgn = await _db.LeagueClubGames.Where(g => g.Id == cg).Select(g => g.Pgn).FirstOrDefaultAsync(ct);
            if (finalPgn == null) return (null, "clubGameNotFound");
        }

        var sizes = pages.Select(p => ScoresheetImage.Prepare(p.Data, ModelEdge) is { } j ? ScoresheetImage.Size(j) : null).ToList();
        var json = ScoresheetTranscription.WithImageSize(transcriptionJson,
            sizes.All(x => x != null) ? sizes.Select(x => x!.Value).ToList() : new List<(int Width, int Height)>());
        var t = ScoresheetTranscription.Parse(json);
        if (t == null || t.Moves.Count == 0) return (null, "invalidTranscription");
        var language = ScoresheetReader.EffectiveLanguage("auto", t.NotationLanguage);
        var r = ScoresheetResolver.Resolve(t.Scanned(), new ScoresheetResolver.Options(ScoresheetNotation.Find(language)));
        if (r.Plies.Count == 0) return (null, "noMoves");

        var stored = pages.Select(p => p.Data.Length > MaxStoredBytes
            ? (Photo: ScoresheetImage.Prepare(p.Data, StoredEdge, 90) ?? p.Data, Type: "image/jpeg")
            : (Photo: p.Data, Type: p.ContentType ?? "image/jpeg")).ToList();
        var now = DateTime.UtcNow;
        var scan = new ScoresheetScan
        {
            UserId = ownerUserId,
            Photo = stored[0].Photo,
            ContentType = stored[0].Type,
            FileName = CleanFileName(pages[0].FileName),
            PageCount = pages.Count,
            Pages = stored.Skip(1).Select((p, i) => new ScoresheetScanPage
            {
                Page = i + 2, Photo = p.Photo, ContentType = p.Type, FileName = CleanFileName(pages[i + 1].FileName),
            }).ToList(),
            NotationLanguage = "auto",
            OwnerSide = "auto",
            Purpose = ScoresheetScan.PurposeLeague,
            Status = ScoresheetScanStatus.Running,
            Model = ManualModel,
            CreatedAt = now,
            StartedAt = now,
        };
        _db.ScoresheetScans.Add(scan);
        await _db.SaveChangesAsync(ct);

        r = await CheckWithEngineAsync(scan.Id, t, r, language, ct);
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
        if (finalPgn != null) await CloseLeagueScanAsync(ScanActor.ManagerOf(ownerUserId), scan.Id, finalPgn, clubGameId);
        return (ToDto(scan), null);
    }

    public async Task<ScoresheetScanDto?> GetAsync(int userId, int id)
    {
        var scan = await ScanHeads().FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
        return scan == null ? null : ToDto(scan);
    }

    /// <summary>Die letzten Einlesungen des Nutzers (ohne Foto) — für die Seite, falls man sie verlassen hat.</summary>
    public async Task<List<ScoresheetScanDto>> ListAsync(int userId, int take = 20)
    {
        // Einlesungen einer gelöschten Partie bleiben nur fürs Kontingent liegen (ohne Foto) — in der Liste nicht.
        var scans = await ScanHeads().Where(s => s.UserId == userId && s.Purpose == null
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

    /// <summary>Alles außer dem Foto (das ist das Schwergewicht der Zeile).</summary>
    private IQueryable<ScoresheetScan> ScanHeads() => _db.ScoresheetScans.AsNoTracking().Select(s => new ScoresheetScan
    {
        Id = s.Id, UserId = s.UserId, AccessKey = s.AccessKey, SavedGameId = s.SavedGameId, ContentType = s.ContentType, FileName = s.FileName,
        NotationLanguage = s.NotationLanguage, OwnerSide = s.OwnerSide, Purpose = s.Purpose, Status = s.Status, Error = s.Error, ResolutionJson = s.ResolutionJson,
        Model = s.Model, Attempts = s.Attempts, Rounds = s.Rounds, CreatedAt = s.CreatedAt, StartedAt = s.StartedAt,
        FinishedAt = s.FinishedAt, PageCount = s.PageCount,
    });

    /// <summary>Das Foto einer eigenen Partie (Seite <paramref name="page"/>, ab 1); <c>null</c>, wenn es keins gibt, die
    /// Seite fehlt oder die Partie fremd ist.</summary>
    public async Task<(byte[] Data, string ContentType, string FileName, int PageCount)?> PhotoForGameAsync(int userId, int gameId,
        int page = 1)
    {
        var head = await _db.ScoresheetScans.AsNoTracking()
            .Where(s => s.SavedGameId == gameId && s.UserId == userId).Select(s => new { s.Id, s.PageCount }).FirstOrDefaultAsync();
        if (head == null) return null;
        var id = head.Id;
        var p = page <= 1
            ? await _db.ScoresheetScans.AsNoTracking().Where(s => s.Id == id)
                .Select(s => new { s.Photo, s.ContentType, s.FileName }).FirstOrDefaultAsync()
            : await _db.ScoresheetScanPages.AsNoTracking().Where(x => x.ScoresheetScanId == id && x.Page == page)
                .Select(x => new { x.Photo, x.ContentType, x.FileName }).FirstOrDefaultAsync();
        if (p == null || p.Photo.Length == 0) return null;
        var ext = p.ContentType switch { "image/png" => ".png", "image/webp" => ".webp", _ => ".jpg" };
        var suffix = page <= 1 ? "" : $"-{page}";
        return (p.Photo, p.ContentType, p.FileName ?? $"partieformular-{id}{suffix}{ext}", Math.Max(1, head.PageCount));
    }

    // ── Lesen (Worker) ────────────────────────────────────────────────

    /// <summary>Beim Start: was beim letzten Herunterfahren mitten im Lesen war, kommt zurück in die Schlange.</summary>
    public async Task<int> RequeueInterruptedAsync(CancellationToken ct)
    {
        var running = await _db.ScoresheetScans.Where(s => s.Status == ScoresheetScanStatus.Running).ToListAsync(ct);
        foreach (var s in running) s.Status = ScoresheetScanStatus.Pending;
        await _db.SaveChangesAsync(ct);
        return running.Count;
    }

    /// <summary>Die älteste wartende Einlesung übernehmen (Status → Running); <c>null</c> = nichts zu tun.</summary>
    public async Task<int?> ClaimNextAsync(CancellationToken ct)
    {
        while (true)
        {
            var next = await _db.ScoresheetScans
                .Where(s => s.Status == ScoresheetScanStatus.Pending)
                .OrderBy(s => s.CreatedAt).ThenBy(s => s.Id)
                .Select(s => new { s.Id, s.Attempts })
                .FirstOrDefaultAsync(ct);
            if (next == null) return null;

            var scan = await _db.ScoresheetScans.FirstAsync(s => s.Id == next.Id, ct);
            if (scan.Attempts >= MaxAttempts)
            {
                // Dreimal mittendrin gestorben — ein viertes Mal wäre eine Endlosschleife auf Kosten des Kontos.
                scan.Status = ScoresheetScanStatus.Failed;
                scan.Error = "failed";
                scan.FinishedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                continue;
            }
            scan.Status = ScoresheetScanStatus.Running;
            scan.Attempts++;
            scan.StartedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return scan.Id;
        }
    }

    /// <summary>Liest eine übernommene Einlesung und legt die Partie an. Scheitert sie, steht der Grund an der Zeile.</summary>
    /// <param name="ct">Bricht die Einlesung ab — beim Herunterfahren ODER am Laufzeit-Deckel des Workers.</param>
    /// <param name="shutdown">Der Token des Herunterfahrens: nur er lässt die Einlesung auf <c>Running</c> stehen (sie
    /// kommt beim nächsten Start zurück). Jeder andere Abbruch ist der Laufzeit-Deckel → gescheitert mit
    /// <c>timeout</c>, Glocke, und der laufende Aufruf wird mit seinem ungünstigsten Fall verbucht.</param>
    public async Task ProcessAsync(int scanId, CancellationToken ct, CancellationToken shutdown = default)
    {
        var scan = await _db.ScoresheetScans.FirstOrDefaultAsync(s => s.Id == scanId, ct);
        if (scan == null || scan.Status != ScoresheetScanStatus.Running) return;

        // Alle Seiten in Reihenfolge, jede aufrecht und auf die Kante des Modells verkleinert.
        var photos = new List<byte[]> { scan.Photo };
        if (scan.PageCount > 1)
            photos.AddRange(await _db.ScoresheetScanPages.AsNoTracking().Where(p => p.ScoresheetScanId == scan.Id)
                .OrderBy(p => p.Page).Select(p => p.Photo).ToListAsync(ct));
        var jpegs = photos.Select(p => ScoresheetImage.Prepare(p, ModelEdge)).ToList();
        if (jpegs.Any(j => j == null)) { await FailAsync(scan, "unreadable", ct); return; }

        // Ein Lesedurchgang; null = am Laufzeit-Deckel abgebrochen (die Einlesung ist dann schon gescheitert).
        async Task<ScoresheetReader.ReadOutcome?> ReadAsync(IReadOnlyList<byte[]> pages)
        {
            try
            {
                return await _reader.ReadPagesAsync(pages, scan.NotationLanguage, ct,
                    beforeCall: token => AllowanceAsync(scan.UserId, token),
                    afterCall: async (input, output, token) =>
                    {
                        // SOFORT verbuchen: stürzt der Worker danach ab, ist das Geld trotzdem ausgegeben.
                        scan.InputTokens += input;
                        scan.OutputTokens += output;
                        scan.CostMicroUsd += _budget.CostMicroUsd(input, output);
                        await _db.SaveChangesAsync(token);
                    },
                    startMode: _startMode);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested && !shutdown.IsCancellationRequested)
            {
                // Laufzeit-Deckel mitten in einem Aufruf: was er gekostet hat, meldet die API nicht mehr. Verbucht wird
                // der ungünstigste Fall — lieber zu viel als eine Kostenbremse, die abgebrochene Aufrufe übersieht.
                if (_reader.InFlightMaxTokens is int max)
                {
                    scan.InputTokens += ScoresheetBudget.ReserveInputTokens;
                    scan.OutputTokens += max;
                    scan.CostMicroUsd += _budget.WorstCaseMicroUsd(max);
                }
                scan.Model = _vision.Model;
                await FailAsync(scan, "timeout", CancellationToken.None);
                return null;
            }
        }

        if (await ReadAsync(jpegs!) is not { } outcome) return;
        scan.Model = _vision.Model;
        scan.Rounds = outcome.Rounds;
        if (outcome.Error != null) { await FailAsync(scan, outcome.Error, ct, outcome.Json); return; }

        // Quer oder kopfüber fotografiert (ScoresheetOrientation, 0.672.6)? Dann die Fotos aufrecht drehen und noch einmal
        // lesen — aufrecht liest das Modell sicherer, und der Ausschnitt je Zug liegt nicht mehr quer. Übernommen wird die
        // zweite Lesung nur, wenn sie aufgeht; sonst bleiben erste Lesung und Fotos, wie sie waren.
        var turns = Enumerable.Range(1, photos.Count).Select(p => ScoresheetOrientation.Detect(outcome.Transcription!, p)).ToList();
        if (turns.Any(d => d != 0))
        {
            var upright = photos.Select((p, i) => turns[i] == 0 ? p : ScoresheetImage.Rotate(p, turns[i])).ToList();
            var uprightJpegs = upright.Select(p => p == null ? null : ScoresheetImage.Prepare(p, ModelEdge)).ToList();
            if (uprightJpegs.All(j => j != null))
            {
                _logger.LogInformation("Formular-Einlesung {ScanId}: Foto gedreht ({Turns}°) — lese aufrecht neu",
                    scan.Id, string.Join("/", turns));
                if (await ReadAsync(uprightJpegs!) is not { } second) return;
                if (second.Error == null && second.Resolution!.Plies.Count > 0)
                {
                    outcome = second;
                    scan.Rounds += second.Rounds;
                    scan.Photo = upright[0]!;
                    scan.ContentType = "image/jpeg";
                    if (photos.Count > 1)
                    {
                        var pages = await _db.ScoresheetScanPages.Where(p => p.ScoresheetScanId == scan.Id)
                            .OrderBy(p => p.Page).ToListAsync(ct);
                        for (var i = 0; i < pages.Count && i + 1 < upright.Count; i++)
                            if (turns[i + 1] != 0) { pages[i].Photo = upright[i + 1]!; pages[i].ContentType = "image/jpeg"; }
                    }
                }
                else
                {
                    _logger.LogWarning("Formular-Einlesung {ScanId}: aufrechte Lesung ging nicht auf ({Error}) — erste bleibt",
                        scan.Id, second.Error ?? "noMoves");
                }
            }
        }

        await FinishAsync(scan, outcome, ct);
    }

    /// <summary>Nach dem Lesen — gleich, ob das Modell gelesen hat (<see cref="ProcessAsync"/>) oder die Lesung von außen
    /// kam (<see cref="ProcessReadingAsync"/>): Engine-Prüfung, dann Liga-Einlesung fertig bzw. Partie in „Meine Partien"
    /// samt Glocke.</summary>
    private async Task FinishAsync(ScoresheetScan scan, ScoresheetReader.ReadOutcome outcome, CancellationToken ct)
    {
        var t = outcome.Transcription!;
        var r = outcome.Resolution!;
        if (r.Plies.Count == 0) { await FailAsync(scan, "noMoves", ct, outcome.Json); return; }
        r = await CheckWithEngineAsync(scan.Id, t, r, outcome.Language, ct);

        if (scan.Purpose == ScoresheetScan.PurposeLeague)
        {
            // Vereins-Datenbank: keine Partie in „Meine Partien" — LeagueHub korrigiert und übernimmt selbst. Keine
            // Glocke: die Seite fragt nach, und ein Link aus RookHub nach LeagueHub wäre eine fremde Adresse.
            scan.TranscriptionJson = outcome.Json;
            scan.ResolutionJson = JsonSerializer.Serialize(new StoredResolution
            {
                Language = outcome.Language, Plies = r.Plies, Unresolved = r.Unresolved, UnresolvedFrom = r.StuckAt,
            }, Json);
            scan.Status = ScoresheetScanStatus.Done;
            scan.Error = null;
            scan.FinishedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Formular-Einlesung {ScanId} (Vereins-Datenbank) fertig: {Plies} Halbzüge, {Uncertain} unsicher",
                scan.Id, r.Plies.Count, r.Plies.Count(p => p.Uncertain));
            return;
        }

        var comments = CommentsFor(r);
        if (scan.UserId is not int owner) { await FailAsync(scan, "failed", ct, outcome.Json); return; }   // ohne Konto nur Liga
        var side = scan.OwnerSide is "white" or "black" ? scan.OwnerSide : await GuessOwnerSideAsync(owner, t, ct);
        var game = await _games.CreateGeneratedAsync(owner, SavedGameService.ScoresheetSource,
            r.Plies.Select(p => p.San).ToList(), comments,
            new GameHeaderInput(Blank(t.Event), Blank(t.Site), Blank(t.DateIso) ?? Blank(t.Date), Blank(t.Round),
                Blank(t.White), Blank(t.Black), t.Result), side);

        scan.SavedGameId = game.Id;
        scan.TranscriptionJson = outcome.Json;
        scan.ResolutionJson = JsonSerializer.Serialize(new StoredResolution
        {
            Language = outcome.Language,
            Plies = r.Plies,
            Unresolved = r.Unresolved,
            UnresolvedFrom = r.StuckAt,
        }, Json);
        scan.Status = ScoresheetScanStatus.Done;
        scan.Error = null;
        scan.FinishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Formular-Einlesung {ScanId} fertig: Partie {GameId}, {Plies} Halbzüge, {Uncertain} unsicher, {Unresolved} offen, {Rounds} Durchgänge",
            scan.Id, game.Id, r.Plies.Count, r.Plies.Count(p => p.Uncertain), r.Unresolved.Count, outcome.Rounds);
        // Glocke (und Web-Push, wo eingerichtet): das Lesen dauert Minuten, und wer die Seite verlassen hat,
        // erfährt sonst nicht, dass die Partie da ist. Direkt auf die Korrekturseite — dort ist die Arbeit.
        await NotifyAsync(owner, NotificationType.ScoresheetRead, new Dictionary<string, string>
        {
            ["white"] = game.White ?? "?",
            ["black"] = game.Black ?? "?",
            ["moves"] = r.Plies.Count.ToString(),
            ["uncertain"] = r.Plies.Count(p => p.Uncertain).ToString(),
            ["unresolved"] = r.Unresolved.Count.ToString(),
        }, $"/games/{game.Id}/edit");
    }

    /// <summary>
    /// Engine-Prüfung der Lesung (<see cref="ScoresheetPlausibility"/>): ein Zickzack in der Bewertung — ein Halbzug nach
    /// dem anderen verliert Gewinnchance — ist meist ein falsch gelesener legaler Zug. Ohne Engine, mit abgeschalteter
    /// Prüfung oder bei einem Fehler bleibt die Lesung, wie sie ist: die Prüfung ist eine Zugabe, kein Teil des Lesens.
    /// </summary>
    private async Task<ScoresheetResolution> CheckWithEngineAsync(int scanId, ScoresheetTranscription t, ScoresheetResolution r,
        string? language, CancellationToken ct)
    {
        if (_engine == null) return r;
        try
        {
            var options = new ScoresheetResolver.Options(ScoresheetNotation.Find(language));
            var o = await ScoresheetPlausibility.ImproveAsync(t.Scanned(), options, r, _engine, ct: ct);
            if (o.Replaced.Count > 0 || o.Flagged.Count > 0)
                _logger.LogInformation(
                    "Formular-Einlesung {ScanId}: Engine-Prüfung ersetzte {Replaced} Zug/Züge, zweifelt an {Flagged} (Halbzüge {ReplacedPlies} / {FlaggedPlies})",
                    scanId, o.Replaced.Count, o.Flagged.Count, string.Join(",", o.Replaced), string.Join(",", o.Flagged));
            return o.Resolution;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Formular-Einlesung {ScanId}: Engine-Prüfung gescheitert, Lesung bleibt unverändert", scanId);
            return r;
        }
    }

    /// <summary>
    /// Kommentare im PGN: wo der Zug vom Formular abweicht (Lesefehler repariert, Eintrag erschlossen), steht,
    /// was DASTAND — dieselbe Auskunft, die man beim Nachspielen braucht, um dem Zug zu misstrauen. Die nicht
    /// aufgelösten Einträge hängen am letzten Zug.
    /// </summary>
    internal static Dictionary<int, string> CommentsFor(ScoresheetResolution r)
    {
        var comments = new Dictionary<int, string>();
        for (var i = 0; i < r.Plies.Count; i++)
        {
            var p = r.Plies[i];
            if (p.Match is ScoresheetResolver.Matches.Fuzzy or ScoresheetResolver.Matches.Guess)
                comments[i] = $"sheet: {(string.IsNullOrWhiteSpace(p.Written) ? "?" : p.Written)}";
            else if (p.Match == ScoresheetResolver.Matches.Inserted)
                comments[i] = "sheet: —"; // stand nicht auf dem Formular
        }
        // Doppelt notierte Einträge hängen am Zug davor.
        foreach (var skip in r.Skipped)
        {
            var at = Math.Max(0, skip.AfterPly - 1);
            if (at >= r.Plies.Count) continue;
            var text = "sheet, extra: " + skip.Written;
            comments[at] = comments.TryGetValue(at, out var c0) ? c0 + " | " + text : text;
        }
        if (r.Unresolved.Count > 0 && r.Plies.Count > 0)
        {
            var text = "sheet, not resolved: " + string.Join(' ', r.Unresolved);
            var last = r.Plies.Count - 1;
            comments[last] = comments.TryGetValue(last, out var c) ? c + " | " + text : text;
        }
        return comments;
    }

    private async Task FailAsync(ScoresheetScan scan, string reason, CancellationToken ct, string? json = null)
    {
        scan.Status = ScoresheetScanStatus.Failed;
        scan.Error = reason;
        scan.TranscriptionJson = json ?? scan.TranscriptionJson;
        scan.FinishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogWarning("Formular-Einlesung {ScanId} gescheitert: {Reason}", scan.Id, reason);
        if (scan.Purpose == ScoresheetScan.PurposeLeague || scan.UserId is not int owner) return;   // LeagueHub fragt selbst nach
        await NotifyAsync(owner, NotificationType.ScoresheetFailed,
            new Dictionary<string, string> { ["reason"] = reason }, "/games/scoresheet");
    }

    /// <summary>Benachrichtigen, ohne dass ein Fehler dabei die Einlesung scheitern lässt.</summary>
    private async Task NotifyAsync(int userId, string type, Dictionary<string, string> data, string link)
    {
        try { await _notifications.CreateAsync(userId, type, data, link); }
        catch (Exception ex) { _logger.LogWarning(ex, "Benachrichtigung {Type} an User {UserId} fehlgeschlagen", type, userId); }
    }

    /// <summary>„Ich spielte: automatisch" — welcher gelesene Spielername passt zum Profil?</summary>
    private async Task<string?> GuessOwnerSideAsync(int userId, ScoresheetTranscription t, CancellationToken ct)
    {
        var profile = await _db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var username = await _db.AppUsers.Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync(ct);
        return GuessOwnerSide(t.White, t.Black,
            new[] { profile?.LastName, profile?.DisplayName, profile?.FirstName, username });
    }

    /// <summary>
    /// Welche Seite gehört dem Nutzer? Seine Namen (Nachname, Anzeigename, Vorname, Benutzername — in dieser Reihenfolge)
    /// werden in den vom Formular gelesenen Spielernamen gesucht, als ganzes Wort und ohne Groß/klein und Akzente. Nur
    /// wenn GENAU EINE Seite passt, gilt sie; sonst <c>null</c> (lieber keine Drehung als eine falsche).
    /// </summary>
    public static string? GuessOwnerSide(string? white, string? black, IEnumerable<string?> myNames)
    {
        static string Norm(string? s) => new string((s ?? string.Empty).Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ').ToArray());
        var w = Norm(white).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var b = Norm(black).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        foreach (var name in myNames)
        {
            var words = Norm(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(x => x.Length >= 3).ToList();
            if (words.Count == 0) continue;
            var inWhite = words.Any(w.Contains);
            var inBlack = words.Any(b.Contains);
            if (inWhite && !inBlack) return "white";
            if (inBlack && !inWhite) return "black";
        }
        return null;
    }

    // ── Korrekturseite ────────────────────────────────────────────────

    /// <summary>Formular-Einträge + Stand je Halbzug einer eigenen, eingelesenen Partie.</summary>
    public async Task<ScoresheetEditStateDto?> EditStateAsync(int userId, int gameId)
    {
        var scan = await _db.ScoresheetScans.AsNoTracking()
            .Where(s => s.SavedGameId == gameId && s.UserId == userId)
            .Select(s => new { s.Id, s.NotationLanguage, s.TranscriptionJson, s.ResolutionJson, s.PageCount })
            .FirstOrDefaultAsync();
        if (scan == null) return null;
        var stored = Deserialize(scan.ResolutionJson);
        var t = ScoresheetTranscription.Parse(scan.TranscriptionJson);
        return new ScoresheetEditStateDto
        {
            ScanId = scan.Id,
            NotationLanguage = stored?.Language ?? scan.NotationLanguage,
            Written = t?.Moves.Select(m => m.Written).ToList() ?? new(),
            Boxes = t?.NormalizedBoxes() ?? new(),
            PageCount = Math.Max(1, scan.PageCount),
            Pages = t?.EntryPages() ?? new(),
            Plies = stored?.Plies ?? new(),
            Unresolved = stored?.Unresolved ?? new(),
            UnresolvedFrom = stored?.UnresolvedFrom,
        };
    }

    /// <summary>
    /// Den Rest ab einer festgelegten Stelle neu aufbereiten: die Züge in <paramref name="prefix"/> stehen fest
    /// (bestätigt, gewählt oder selbst eingegeben), die Formular-Einträge ab <paramref name="writtenFrom"/> werden
    /// neu aufgelöst — samt Unsicherheiten und Ausgängen. Kein Modell-Aufruf: das ist schnell und kostet nichts.
    /// </summary>
    /// <returns><c>null</c>, wenn es keine Einlesung zur Partie gibt; <see cref="ArgumentException"/> bei einem
    /// illegalen Präfix.</returns>
    public async Task<ScoresheetResolveResultDto?> ResolveRestAsync(int userId, int gameId, IReadOnlyList<string> prefix,
        int writtenFrom)
    {
        CheckPrefix(prefix);
        var scan = await _db.ScoresheetScans.AsNoTracking()
            .Where(s => s.SavedGameId == gameId && s.UserId == userId)
            .Select(s => new { s.NotationLanguage, s.TranscriptionJson, s.ResolutionJson })
            .FirstOrDefaultAsync();
        if (scan == null) return null;
        var t = ScoresheetTranscription.Parse(scan.TranscriptionJson);
        if (t == null) return null;
        var scanned = t.Scanned();
        var language = Deserialize(scan.ResolutionJson)?.Language ?? scan.NotationLanguage;

        var legalPrefix = SavedGameService.LegalSans(prefix);
        var from = Math.Clamp(writtenFrom, 0, scanned.Count);
        var r = ScoresheetResolver.Resolve(scanned, new ScoresheetResolver.Options(ScoresheetNotation.Find(language)),
            legalPrefix, from);
        return new ScoresheetResolveResultDto { Plies = r.Plies, Unresolved = r.Unresolved, UnresolvedFrom = r.StuckAt };
    }

    /// <summary>Das Präfix wird Halbzug für Halbzug mit allen legalen Zügen samt SAN nachgespielt, und Pendelzüge enden
    /// nie von selbst — ohne Deckel kostete eine Anfrage (auch ohne Konto, mit Teilen-Link und Schlüssel) so viel CPU,
    /// wie der Rumpf Züge fasst (Codereview 2026-09-29, A6-014). <see cref="ArgumentException"/> wie beim illegalen Zug.</summary>
    private static void CheckPrefix(IReadOnlyList<string> prefix)
    {
        if (prefix.Count > MaxPrefixPlies) throw new ArgumentException($"Too many moves (max {MaxPrefixPlies} plies).");
    }

    // ── Einlesungen für die Vereins-Datenbank (LeagueHub) ────────────

    /// <summary>Wem eine Liga-Einlesung gehört: einem Konto ODER (ohne Anmeldung, Teilen-Link) dem Browser, der den
    /// geheimen Schlüssel hat. <see cref="Manager"/> = Verwalter der Vereins-Datenbank: darf JEDE Liga-Einlesung prüfen,
    /// übernehmen oder verwerfen (Wunsch 2026-09-28: was hochgeladen, aber nie geprüft wurde, soll nicht im Limbo hängen).</summary>
    public readonly record struct ScanActor(int? UserId, string? Key, bool Manager = false)
    {
        public static ScanActor User(int userId) => new(userId, null);
        public static ScanActor Anonymous(string key) => new(null, key);
        public static ScanActor ManagerOf(int userId) => new(userId, null, true);
    }

    private IQueryable<ScoresheetScan> LeagueOwned(IQueryable<ScoresheetScan> q, ScanActor a)
    {
        q = q.Where(s => s.Purpose == ScoresheetScan.PurposeLeague);
        if (a.Manager) return q;
        if (a.UserId is int uid) return q.Where(s => s.UserId == uid);
        var key = a.Key ?? "";
        return q.Where(s => s.UserId == null && s.AccessKey == key && key != "");
    }

    /// <summary>HMAC der IP-Adresse — die Adresse selbst wird nie gespeichert. Gezählt wird je Anschluss
    /// (<see cref="IpKey"/>).</summary>
    public string AnonIpHash(System.Net.IPAddress? ip)
    {
        var bytes = System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_ipSecret),
            System.Text.Encoding.UTF8.GetBytes("scoresheet-ip:" + IpKey(ip)));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }

    /// <summary>
    /// Wofür die Grenzen je IP gelten: IPv4 (auch als IPv6 verpackt) die Adresse, echtes IPv6 das /64-Netz — ein Anschluss
    /// bekommt ein ganzes /64 und wählt darin beliebig viele Adressen. Vorher nahm <c>MapToIPv4</c> bei echtem IPv6 nur die
    /// letzten 32 Bit: aus einem /64 wurden beliebig viele „IPs“, und fremde Anschlüsse mit gleichem Ende teilten sich
    /// eine (Codereview 2026-09-29, A6-003).
    /// </summary>
    internal static string IpKey(System.Net.IPAddress? ip)
    {
        if (ip == null) return "?";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return ip.ToString();
        var bytes = ip.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new System.Net.IPAddress(bytes) + "/64";
    }

    private IQueryable<ScoresheetScan> AnonCountingSince(DateTime since) =>
        _db.ScoresheetScans.Where(s => s.UserId == null && s.CreatedAt >= since
            && !(s.Status == ScoresheetScanStatus.Failed && s.CostMicroUsd == 0));

    /// <summary>Stand für eine Einlesung OHNE Konto (Teilen-Link): Tageszahl je IP, gesperrt wenn alle zusammen die
    /// Tagesgrenze erreicht haben (<c>anonDailyLimit</c>) oder das Gesamtbudget leer ist.</summary>
    public async Task<ScoresheetStatusDto> AnonStatusAsync(string ipHash)
    {
        var since = DateTime.UtcNow.AddDays(-1);
        var spent = await SpentAsync(null);
        var used = await AnonCountingSince(since).CountAsync(s => s.AnonIpHash == ipHash);
        DateTime? next = null;
        if (used >= AnonPerIpDailyLimit)
        {
            var oldest = await AnonCountingSince(since).Where(s => s.AnonIpHash == ipHash).OrderBy(s => s.CreatedAt)
                .Skip(used - AnonPerIpDailyLimit).Select(s => s.CreatedAt).FirstOrDefaultAsync();
            next = DateTime.SpecifyKind(oldest.AddDays(1), DateTimeKind.Utc);
        }
        var all = await AnonCountingSince(since).CountAsync();
        return new ScoresheetStatusDto
        {
            Available = External || _vision.IsConfigured,
            DailyLimit = AnonPerIpDailyLimit,
            UsedToday = used,
            NextAllowedAt = next,
            Blocked = all >= AnonDailyLimit ? "anonDailyLimit" : AllowanceFor(null, spent).Blocked,
            Languages = ScoresheetNotation.Languages.Select(l => new ScoresheetLanguageDto
            {
                Code = l.Code, Name = l.Name, Pieces = $"{l.King} {l.Queen} {l.Rook} {l.Bishop} {l.Knight}",
            }).ToList(),
        };
    }

    /// <summary>Annahme OHNE Konto nacheinander: Zählen und Anlegen sind zwei Schritte, und parallel abgeschickte Uploads
    /// zählten sonst alle denselben Stand und kamen gemeinsam durch (10/IP, 100/Tag, 3 offen; Codereview 2026-09-29,
    /// A6-003). Die API läuft als EINE Instanz — ein Semaphor im Prozess genügt.</summary>
    private static readonly SemaphoreSlim AnonAdmission = new(1, 1);

    /// <summary>
    /// Foto OHNE Konto hochladen (LeagueHub-Teilen-Link, immer für die Vereins-Datenbank). Grenzen:
    /// <see cref="AnonPerIpDailyLimit"/> je IP (<c>dailyLimit</c>), <see cref="AnonDailyLimit"/> für alle zusammen
    /// (<c>anonDailyLimit</c>), drei gleichzeitig je IP, dazu das Tagesbudget der Einlesungen ohne Konto und das
    /// Gesamtbudget (<c>globalBudget</c>). Liefert den geheimen Schlüssel, unter dem der Browser die Einlesung
    /// wiederfindet — sonst niemand.
    /// </summary>
    public async Task<(ScoresheetScanDto? Scan, string? Key, string? Reason)> CreateAnonymousAsync(byte[] data, string? contentType,
        string? fileName, string? language, string? ownerSide, string ipHash) =>
        await CreateAnonymousAsync(new[] { new ScoresheetUpload(data, contentType, fileName) }, language, ownerSide, ipHash);

    /// <summary>Wie oben, ein Formular über mehrere Fotos (Seitenreihenfolge, höchstens <see cref="MaxPages"/>, 0.690.1).</summary>
    public async Task<(ScoresheetScanDto? Scan, string? Key, string? Reason)> CreateAnonymousAsync(IReadOnlyList<ScoresheetUpload> pages,
        string? language, string? ownerSide, string ipHash)
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
                if (await AnonCountingSince(since).CountAsync(s => s.AnonIpHash == ipHash) >= AnonPerIpDailyLimit)
                    return (null, null, "dailyLimit");
                if (await AnonCountingSince(since).CountAsync() >= AnonDailyLimit) return (null, null, "anonDailyLimit");
                if (await _db.ScoresheetScans.CountAsync(s => s.UserId == null && s.AnonIpHash == ipHash
                        && (s.Status == ScoresheetScanStatus.Pending || s.Status == ScoresheetScanStatus.Running)) >= MaxOpenPerUser)
                    return (null, null, "tooManyOpen");
            }
            (scan, reason) = await CreateCoreAsync(null, pages, language, ownerSide, ScoresheetScan.PurposeLeague, key, ipHash);
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

    /// <summary>Die offenen Liga-Einlesungen (neueste zuerst) — eines Kontos bzw. OHNE Konto die, deren Schlüssel der
    /// Browser mitbringt. Verworfene/übernommene tragen kein Foto mehr und fehlen.</summary>
    public async Task<List<ScoresheetScanDto>> LeagueScansAsync(int userId) =>
        await WithNamesAsync((await LeagueOwned(ScanHeads(), ScanActor.User(userId)).Where(s => s.FileName != DiscardedMark)
            .OrderByDescending(s => s.CreatedAt).Take(10).ToListAsync()).Select(ToDto).ToList());

    /// <summary>
    /// Namen der gelesenen Partie in die Liste (0.659.3, gemeldet 2026-10-05: „Deine Formulare" zeigte „? – ?", obwohl die
    /// Prüfseite die Namen kannte). <see cref="ScanHeads"/> lässt die Antwort des Modells absichtlich weg; für die fertigen
    /// Einlesungen der Liste werden nur deren Weiß/Schwarz nachgelesen.
    /// </summary>
    private async Task<List<ScoresheetScanDto>> WithNamesAsync(List<ScoresheetScanDto> dtos, CancellationToken ct = default)
    {
        var ids = dtos.Where(d => d.Status == "done" && d.White == null && d.Black == null).Select(d => d.Id).ToList();
        if (ids.Count == 0) return dtos;
        var json = await _db.ScoresheetScans.AsNoTracking().Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.TranscriptionJson }).ToDictionaryAsync(s => s.Id, s => s.TranscriptionJson, ct);
        foreach (var d in dtos)
        {
            if (!json.TryGetValue(d.Id, out var j) || ScoresheetTranscription.Parse(j) is not { } t) continue;
            d.White = Blank(t.White);
            d.Black = Blank(t.Black);
        }
        return dtos;
    }

    /// <summary>Alle offenen Liga-Einlesungen (auch ohne Konto über einen Teilen-Link) — für die Verwalter, jüngste zuerst.</summary>
    public async Task<List<LeagueOpenScanDto>> LeagueOpenScansAsync(int viewerId, CancellationToken ct = default)
    {
        var rows = (await ScanHeads().Where(s => s.Purpose == ScoresheetScan.PurposeLeague && s.FileName != DiscardedMark)
                .OrderByDescending(s => s.CreatedAt).Take(50).ToListAsync(ct))
            .Select(s => new LeagueOpenScanDto { Scan = ToDto(s), ViaShareLink = s.UserId == null, Mine = s.UserId == viewerId }).ToList();
        await WithNamesAsync(rows.Select(r => r.Scan).ToList(), ct);
        return rows;
    }

    public async Task<List<(string Key, ScoresheetScanDto Scan)>> LeagueScansByKeysAsync(IEnumerable<string> keys)
    {
        var list = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().Take(20).ToList();
        var scans = await ScanHeads().Where(s => s.UserId == null && s.Purpose == ScoresheetScan.PurposeLeague
                && s.AccessKey != null && list.Contains(s.AccessKey) && s.FileName != DiscardedMark)
            .OrderByDescending(s => s.CreatedAt).ToListAsync();
        var pairs = scans.Select(s => (s.AccessKey!, ToDto(s))).ToList();
        await WithNamesAsync(pairs.Select(p => p.Item2).ToList());
        return pairs;
    }

    /// <summary>Vermerk im Dateinamen einer übernommenen/verworfenen Liga-Einlesung (das Foto ist dann leer).</summary>
    internal const string DiscardedMark = "\u2205";

    /// <summary>Stand einer Liga-Einlesung für die Korrektur in LeagueHub: Formular-Einträge, Züge, Kopfdaten wie gelesen
    /// und welche Seite der Nutzer vermutlich spielte. <c>null</c> = fremd, unbekannt oder verworfen.</summary>
    public async Task<LeagueScanStateDto?> LeagueScanStateAsync(ScanActor actor, int? scanId, CancellationToken ct = default)
    {
        var q = LeagueOwned(ScanHeads(), actor).Where(s => s.FileName != DiscardedMark);
        if (scanId is int id) q = q.Where(s => s.Id == id);
        var scan = await q.FirstOrDefaultAsync(ct);
        if (scan == null) return null;
        var dto = new LeagueScanStateDto { Scan = ToDto(scan) };
        if (scan.Status != ScoresheetScanStatus.Done) return dto;
        var json = await _db.ScoresheetScans.AsNoTracking().Where(s => s.Id == scan.Id)
            .Select(s => s.TranscriptionJson).FirstOrDefaultAsync(ct);
        var stored = Deserialize(scan.ResolutionJson);
        var t = ScoresheetTranscription.Parse(json);
        dto.NotationLanguage = stored?.Language ?? scan.NotationLanguage;
        dto.Written = t?.Moves.Select(m => m.Written).ToList() ?? new();
        dto.Boxes = t?.NormalizedBoxes() ?? new();
        dto.Pages = t?.EntryPages() ?? new();
        dto.PageCount = Math.Max(1, scan.PageCount);
        dto.Plies = stored?.Plies ?? new();
        dto.Unresolved = stored?.Unresolved ?? new();
        dto.UnresolvedFrom = stored?.UnresolvedFrom;
        dto.White = Blank(t?.White);
        dto.Black = Blank(t?.Black);
        dto.Event = Blank(t?.Event);
        dto.Date = Blank(t?.DateIso) ?? Blank(t?.Date);
        dto.Result = t?.Result;
        // Beim Hochladen gewählt schlägt geraten (wie beim Einlesen in „Meine Partien").
        dto.OwnerSide = scan.OwnerSide is "white" or "black" ? scan.OwnerSide
            : t == null || scan.UserId is not int uid ? null : await GuessOwnerSideAsync(uid, t, ct);
        return dto;
    }

    /// <summary>Das Foto einer Liga-Einlesung.</summary>
    public async Task<(byte[] Data, string ContentType)?> LeagueScanPhotoAsync(ScanActor actor, int? scanId, int page = 1)
    {
        var q = LeagueOwned(_db.ScoresheetScans.AsNoTracking(), actor);
        if (scanId is int id) q = q.Where(s => s.Id == id);
        if (page > 1)
        {
            // Seite 2+ (0.690.1): erst die Einlesung über die Eigentumsregel finden, dann ihr Blatt
            var owned = await q.Select(s => (int?)s.Id).FirstOrDefaultAsync();
            if (owned is not int sid) return null;
            var extra = await _db.ScoresheetScanPages.AsNoTracking().Where(x => x.ScoresheetScanId == sid && x.Page == page)
                .Select(x => new { x.Photo, x.ContentType }).FirstOrDefaultAsync();
            return extra == null || extra.Photo.Length == 0 ? null : (extra.Photo, extra.ContentType);
        }
        var p = await q.Select(s => new { s.Photo, s.ContentType }).FirstOrDefaultAsync();
        return p == null || p.Photo.Length == 0 ? null : (p.Photo, p.ContentType);
    }

    /// <summary>Wie <see cref="ResolveRestAsync"/>, für eine Liga-Einlesung (ohne gespeicherte Partie).</summary>
    public async Task<ScoresheetResolveResultDto?> ResolveLeagueRestAsync(ScanActor actor, int? scanId, IReadOnlyList<string> prefix,
        int writtenFrom)
    {
        CheckPrefix(prefix);
        var q = LeagueOwned(_db.ScoresheetScans.AsNoTracking(), actor);
        if (scanId is int id) q = q.Where(s => s.Id == id);
        var scan = await q.Select(s => new { s.NotationLanguage, s.TranscriptionJson, s.ResolutionJson }).FirstOrDefaultAsync();
        var t = ScoresheetTranscription.Parse(scan?.TranscriptionJson);
        if (scan == null || t == null) return null;
        var scanned = t.Scanned();
        var language = Deserialize(scan.ResolutionJson)?.Language ?? scan.NotationLanguage;
        var r = ScoresheetResolver.Resolve(scanned, new ScoresheetResolver.Options(ScoresheetNotation.Find(language)),
            SavedGameService.LegalSans(prefix), Math.Clamp(writtenFrom, 0, scanned.Count));
        return new ScoresheetResolveResultDto { Plies = r.Plies, Unresolved = r.Unresolved, UnresolvedFrom = r.StuckAt };
    }

    /// <summary>
    /// Liga-Einlesung abschließen (übernommen ODER verworfen): Foto, Antwort des Modells und Stand gehen, die Zeile bleibt
    /// mit Zeitpunkt und Kosten fürs Tageskontingent und die Kostenbremse stehen — wie beim Löschen einer Partie.
    /// Danach verbindet nichts mehr die Einlesung mit der Partie, die daraus wurde (auch der Schlüssel geht).
    /// <c>false</c> = fremd/unbekannt.
    /// </summary>
    public async Task<bool> CloseLeagueScanAsync(ScanActor actor, int? scanId, string? finalPgn = null, int? clubGameId = null)
    {
        var q = LeagueOwned(_db.ScoresheetScans, actor);
        if (scanId is int id) q = q.Where(s => s.Id == id);
        var key = await q.Select(s => new { s.Id, s.UserId, s.SavedGameId }).FirstOrDefaultAsync();
        if (key == null) return false;
        await ArchiveAsync(key.Id, finalPgn, clubGameId);
        DetachWithoutLoading(_db, new[] { (key.Id, key.UserId, key.SavedGameId) });
        RemovePagesWithoutLoading(_db, await PageKeysAsync(_db, new[] { key.Id }));
        var scan = _db.ScoresheetScans.Local.First(s => s.Id == key.Id);
        scan.FileName = DiscardedMark;
        scan.AccessKey = null;
        _db.Entry(scan).Property(nameof(ScoresheetScan.AccessKey)).IsModified = true;
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>Wie lange <see cref="ScoresheetScanArchive"/> Foto und Erkennung aufbewahrt (Wunsch 2026-10-04: „vorerst 365 Tage").</summary>
    public static readonly TimeSpan ArchiveRetention = TimeSpan.FromDays(365);

    /// <summary>
    /// Foto(s), Antwort des Modells und Auflösung einer Liga-Einlesung ins Archiv kopieren, bevor
    /// <see cref="CloseLeagueScanAsync"/> sie an der Einlesung leert (0.655.0) — einmal je Einlesung (ein zweiter Abschluss
    /// findet nichts mehr). Räumt dabei Abgelaufenes weg. Speichert nicht selbst.
    /// </summary>
    private async Task ArchiveAsync(int scanId, string? finalPgn, int? clubGameId = null)
    {
        var now = DateTime.UtcNow;
        await PurgeExpiredArchiveAsync(now);
        if (await _db.ScoresheetScanArchives.AnyAsync(a => a.ScoresheetScanId == scanId)) return;
        var s = await _db.ScoresheetScans.AsNoTracking().Where(x => x.Id == scanId)
            .Select(x => new { x.Photo, x.ContentType, x.TranscriptionJson, x.ResolutionJson, x.Model, x.NotationLanguage })
            .FirstOrDefaultAsync();
        if (s == null || (s.Photo.Length == 0 && s.TranscriptionJson == null)) return;
        var outcome = finalPgn != null ? "saved" : "discarded";
        _db.ScoresheetScanArchives.Add(new ScoresheetScanArchive
        {
            ScoresheetScanId = scanId, Page = 1, Photo = s.Photo, ContentType = s.ContentType,
            TranscriptionJson = s.TranscriptionJson, ResolutionJson = s.ResolutionJson, FinalPgn = finalPgn, Outcome = outcome,
            LeagueClubGameId = finalPgn != null ? clubGameId : null,
            Model = s.Model, NotationLanguage = s.NotationLanguage, ArchivedAt = now, ExpiresAt = now + ArchiveRetention,
        });
        var pages = await _db.ScoresheetScanPages.AsNoTracking().Where(p => p.ScoresheetScanId == scanId)
            .Select(p => new { p.Page, p.Photo, p.ContentType }).ToListAsync();
        foreach (var p in pages)
            _db.ScoresheetScanArchives.Add(new ScoresheetScanArchive
            {
                ScoresheetScanId = scanId, Page = p.Page, Photo = p.Photo, ContentType = p.ContentType, Outcome = outcome,
                Model = s.Model, NotationLanguage = s.NotationLanguage, ArchivedAt = now, ExpiresAt = now + ArchiveRetention,
            });
    }

    // ── Formular einer Vereinspartie wieder öffnen (0.660.0, „Korrigieren" wie beim ersten Beheben) ──

    private IQueryable<ScoresheetScanArchive> ClubArchive(int clubGameId)
    {
        var now = DateTime.UtcNow;
        var scanIds = _db.ScoresheetScanArchives.Where(a => a.LeagueClubGameId == clubGameId && a.Page == 1 && a.ExpiresAt > now)
            .Select(a => a.ScoresheetScanId);
        return _db.ScoresheetScanArchives.Where(a => scanIds.Contains(a.ScoresheetScanId));
    }

    /// <summary>Gibt es zur Vereinspartie noch das aufbewahrte Formular (Foto + Lesung)?</summary>
    public Task<bool> HasClubSheetAsync(int clubGameId, CancellationToken ct = default) =>
        ClubArchive(clubGameId).AnyAsync(ct);

    /// <summary>
    /// Formular-Einträge + Stand je Halbzug der Vereinspartie aus dem Archiv — dieselbe Form wie bei einer eigenen Partie
    /// (<see cref="EditStateAsync"/>), damit dieselbe Korrektur-Oberfläche sie öffnet. <c>null</c> = nichts aufbewahrt.
    /// </summary>
    public async Task<ScoresheetEditStateDto?> ClubEditStateAsync(int clubGameId, CancellationToken ct = default)
    {
        var rows = await ClubArchive(clubGameId).AsNoTracking()
            .Select(a => new { a.Page, a.ScoresheetScanId, a.NotationLanguage, a.TranscriptionJson, a.ResolutionJson }).ToListAsync(ct);
        var first = rows.FirstOrDefault(r => r.Page == 1);
        if (first == null) return null;
        var stored = Deserialize(first.ResolutionJson);
        var t = ScoresheetTranscription.Parse(first.TranscriptionJson);
        return new ScoresheetEditStateDto
        {
            ScanId = first.ScoresheetScanId,
            NotationLanguage = stored?.Language ?? first.NotationLanguage ?? "auto",
            Written = t?.Moves.Select(m => m.Written).ToList() ?? new(),
            Boxes = t?.NormalizedBoxes() ?? new(),
            PageCount = Math.Max(1, rows.Count),
            Pages = t?.EntryPages() ?? new(),
            Plies = stored?.Plies ?? new(),
            Unresolved = stored?.Unresolved ?? new(),
            UnresolvedFrom = stored?.UnresolvedFrom,
        };
    }

    /// <summary>Foto einer Seite (ab 1) des aufbewahrten Formulars der Vereinspartie.</summary>
    public async Task<(byte[] Data, string ContentType, int PageCount)?> ClubPhotoAsync(int clubGameId, int page, CancellationToken ct = default)
    {
        var count = await ClubArchive(clubGameId).CountAsync(ct);
        if (count == 0) return null;
        var p = await ClubArchive(clubGameId).AsNoTracking().Where(a => a.Page == Math.Max(1, page))
            .Select(a => new { a.Photo, a.ContentType }).FirstOrDefaultAsync(ct);
        return p == null || p.Photo.Length == 0 ? null : (p.Photo, p.ContentType, count);
    }

    /// <summary>Wie <see cref="ResolveRestAsync"/>, über das aufbewahrte Formular der Vereinspartie.</summary>
    public async Task<ScoresheetResolveResultDto?> ResolveClubRestAsync(int clubGameId, IReadOnlyList<string> prefix, int writtenFrom,
        CancellationToken ct = default)
    {
        CheckPrefix(prefix);
        var a = await ClubArchive(clubGameId).AsNoTracking().Where(x => x.Page == 1)
            .Select(x => new { x.NotationLanguage, x.TranscriptionJson, x.ResolutionJson }).FirstOrDefaultAsync(ct);
        var t = ScoresheetTranscription.Parse(a?.TranscriptionJson);
        if (a == null || t == null) return null;
        var scanned = t.Scanned();
        var language = Deserialize(a.ResolutionJson)?.Language ?? a.NotationLanguage;
        var r = ScoresheetResolver.Resolve(scanned, new ScoresheetResolver.Options(ScoresheetNotation.Find(language)),
            SavedGameService.LegalSans(prefix), Math.Clamp(writtenFrom, 0, scanned.Count));
        return new ScoresheetResolveResultDto { Plies = r.Plies, Unresolved = r.Unresolved, UnresolvedFrom = r.StuckAt };
    }

    /// <summary>Nach einer Korrektur den Stand je Halbzug und die PGN im Archiv nachziehen (die Lesung selbst bleibt).</summary>
    public async Task SaveClubEditStateAsync(int clubGameId, List<ScoresheetPly>? plies, string finalPgn, CancellationToken ct = default)
    {
        var a = await ClubArchive(clubGameId).Where(x => x.Page == 1).FirstOrDefaultAsync(ct);
        if (a == null) return;
        a.FinalPgn = finalPgn;
        if (plies != null)
        {
            var stored = Deserialize(a.ResolutionJson) ?? new StoredResolution { Language = a.NotationLanguage };
            stored.Plies = plies.Take(600).ToList();
            a.ResolutionJson = JsonSerializer.Serialize(stored, Json);
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Abgelaufene Archiv-Einträge löschen (ohne die Fotos zu laden).</summary>
    private async Task PurgeExpiredArchiveAsync(DateTime now)
    {
        var expired = await _db.ScoresheetScanArchives.Where(a => a.ExpiresAt < now).Select(a => a.Id).Take(500).ToListAsync();
        foreach (var id in expired)
        {
            var stub = _db.ScoresheetScanArchives.Local.FirstOrDefault(a => a.Id == id) ?? new ScoresheetScanArchive { Id = id };
            if (_db.Entry(stub).State == EntityState.Detached) _db.ScoresheetScanArchives.Attach(stub);
            _db.ScoresheetScanArchives.Remove(stub);
        }
    }

    /// <summary>Nach dem Speichern der Korrekturseite: deren Stand je Halbzug ablegen (Anzeige-Zustand).</summary>
    public async Task SaveEditStateAsync(int userId, int gameId, List<ScoresheetPly> plies)
    {
        var scan = await _db.ScoresheetScans.FirstOrDefaultAsync(s => s.SavedGameId == gameId && s.UserId == userId);
        if (scan == null) return;
        var stored = Deserialize(scan.ResolutionJson) ?? new StoredResolution { Language = scan.NotationLanguage };
        stored.Plies = plies.Take(600).ToList();
        // Was nach dem letzten gespeicherten Zug noch offen ist, bestimmt die Seite nicht mehr — das Speichern
        // übernimmt nur legale Züge; offene Einträge dahinter bleiben stehen, wie sie waren.
        scan.ResolutionJson = JsonSerializer.Serialize(stored, Json);
        await _db.SaveChangesAsync();
    }

    private static StoredResolution? Deserialize(string? json)
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

    /// <summary>Die Schlüssel der Einlesungen, die <see cref="RemoveWithoutLoading"/> braucht.</summary>
    public static async Task<List<(int Id, int? UserId, int? SavedGameId)>> KeysAsync(IQueryable<ScoresheetScan> query)
    {
        var rows = await query.Select(s => new { s.Id, s.UserId, s.SavedGameId }).ToListAsync();
        return rows.Select(x => (x.Id, x.UserId, x.SavedGameId)).ToList();
    }

    private static ScoresheetScanDto ToDto(ScoresheetScan s)
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
            FinishedAt = s.FinishedAt,
            Rounds = s.Rounds,
            PageCount = Math.Max(1, s.PageCount),
            MoveCount = stored?.Plies.Count ?? 0,
            UncertainCount = stored?.Plies.Count(p => p.Uncertain && !p.Confirmed) ?? 0,
            UnresolvedCount = stored?.Unresolved.Count ?? 0,
        };
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? CleanFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = Path.GetFileName(name.Trim()).Replace("\"", "").Replace("\r", "").Replace("\n", "");
        return n.Length == 0 ? null : n.Length > 200 ? n[..200] : n;
    }
}

/// <summary>Ein hochgeladenes Foto (eine Seite des Formulars).</summary>
public sealed record ScoresheetUpload(byte[] Data, string? ContentType, string? FileName);

/// <summary>Eine wartende Einlesung für den Leser von außen (<c>GET /api/admin/scoresheets/pending</c>).</summary>
public sealed class ExternalPendingScanDto
{
    public int Id { get; set; }
    /// <summary><c>own</c> (RookHub, legt eine Partie in „Meine Partien" an) oder <c>league</c> (LeagueHub).</summary>
    public string Purpose { get; set; } = "own";
    public int? UserId { get; set; }
    public bool Anonymous { get; set; }
    public int PageCount { get; set; }
    public string NotationLanguage { get; set; } = "auto";
    public string? OwnerSide { get; set; }
    public DateTime CreatedAt { get; set; }
}
