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
        NotificationService notifications, ILogger<ScoresheetScanService> logger, IConfiguration? config = null)
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
    }

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
            Available = _vision.IsConfigured,
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
        if (!_vision.IsConfigured) return (null, "notConfigured");
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
        if (AllowanceFor(userId, spent).Blocked is { } blocked) return (null, blocked);

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

        ScoresheetReader.ReadOutcome outcome;
        try
        {
            outcome = await _reader.ReadPagesAsync(jpegs!, scan.NotationLanguage, ct,
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
            return;
        }
        scan.Model = _vision.Model;
        scan.Rounds = outcome.Rounds;
        if (outcome.Error != null) { await FailAsync(scan, outcome.Error, ct, outcome.Json); return; }

        var t = outcome.Transcription!;
        var r = outcome.Resolution!;
        if (r.Plies.Count == 0) { await FailAsync(scan, "noMoves", ct, outcome.Json); return; }

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
            Available = _vision.IsConfigured,
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
        string? fileName, string? language, string? ownerSide, string ipHash)
    {
        ScoresheetScanDto? scan;
        string? reason;
        var key = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        await AnonAdmission.WaitAsync();
        try
        {
            var since = DateTime.UtcNow.AddDays(-1);
            if (_vision.IsConfigured)
            {
                if (await AnonCountingSince(since).CountAsync(s => s.AnonIpHash == ipHash) >= AnonPerIpDailyLimit)
                    return (null, null, "dailyLimit");
                if (await AnonCountingSince(since).CountAsync() >= AnonDailyLimit) return (null, null, "anonDailyLimit");
                if (await _db.ScoresheetScans.CountAsync(s => s.UserId == null && s.AnonIpHash == ipHash
                        && (s.Status == ScoresheetScanStatus.Pending || s.Status == ScoresheetScanStatus.Running)) >= MaxOpenPerUser)
                    return (null, null, "tooManyOpen");
            }
            (scan, reason) = await CreateCoreAsync(null, new[] { new ScoresheetUpload(data, contentType, fileName) },
                language, ownerSide, ScoresheetScan.PurposeLeague, key, ipHash);
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
        (await LeagueOwned(ScanHeads(), ScanActor.User(userId)).Where(s => s.FileName != DiscardedMark)
            .OrderByDescending(s => s.CreatedAt).Take(10).ToListAsync()).Select(ToDto).ToList();

    /// <summary>Alle offenen Liga-Einlesungen (auch ohne Konto über einen Teilen-Link) — für die Verwalter, jüngste zuerst.</summary>
    public async Task<List<LeagueOpenScanDto>> LeagueOpenScansAsync(int viewerId, CancellationToken ct = default) =>
        (await ScanHeads().Where(s => s.Purpose == ScoresheetScan.PurposeLeague && s.FileName != DiscardedMark)
            .OrderByDescending(s => s.CreatedAt).Take(50).ToListAsync(ct))
        .Select(s => new LeagueOpenScanDto { Scan = ToDto(s), ViaShareLink = s.UserId == null, Mine = s.UserId == viewerId }).ToList();

    public async Task<List<(string Key, ScoresheetScanDto Scan)>> LeagueScansByKeysAsync(IEnumerable<string> keys)
    {
        var list = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().Take(20).ToList();
        var scans = await ScanHeads().Where(s => s.UserId == null && s.Purpose == ScoresheetScan.PurposeLeague
                && s.AccessKey != null && list.Contains(s.AccessKey) && s.FileName != DiscardedMark)
            .OrderByDescending(s => s.CreatedAt).ToListAsync();
        return scans.Select(s => (s.AccessKey!, ToDto(s))).ToList();
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
    public async Task<(byte[] Data, string ContentType)?> LeagueScanPhotoAsync(ScanActor actor, int? scanId)
    {
        var q = LeagueOwned(_db.ScoresheetScans.AsNoTracking(), actor);
        if (scanId is int id) q = q.Where(s => s.Id == id);
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
    public async Task<bool> CloseLeagueScanAsync(ScanActor actor, int? scanId)
    {
        var q = LeagueOwned(_db.ScoresheetScans, actor);
        if (scanId is int id) q = q.Where(s => s.Id == id);
        var key = await q.Select(s => new { s.Id, s.UserId, s.SavedGameId }).FirstOrDefaultAsync();
        if (key == null) return false;
        DetachWithoutLoading(_db, new[] { (key.Id, key.UserId, key.SavedGameId) });
        RemovePagesWithoutLoading(_db, await PageKeysAsync(_db, new[] { key.Id }));
        var scan = _db.ScoresheetScans.Local.First(s => s.Id == key.Id);
        scan.FileName = DiscardedMark;
        scan.AccessKey = null;
        _db.Entry(scan).Property(nameof(ScoresheetScan.AccessKey)).IsModified = true;
        await _db.SaveChangesAsync();
        return true;
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

/// <summary>Die Antwort des Modells, gelesen (Schema aus <see cref="ScoresheetPrompt.Schema"/>).</summary>
public sealed class ScoresheetTranscription
{
    public string? NotationLanguage { get; set; }
    public string? Event { get; set; }
    public string? Site { get; set; }
    public string? Date { get; set; }
    public string? DateIso { get; set; }
    public string? Round { get; set; }
    public string? White { get; set; }
    public string? Black { get; set; }
    public string? Result { get; set; }
    public List<Entry> Moves { get; set; } = new();
    public string? Remarks { get; set; }
    /// <summary>Maße des Bildes, das das Modell bekam (von uns an die gespeicherte Antwort gehängt, nicht vom Modell) —
    /// die Kästen stehen in dessen Pixeln. Fehlt bei Einlesungen vor 0.551.3.</summary>
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
    /// <summary>Maße JEDER Seite als [Breite, Höhe] — nur bei einem Formular über mehrere Fotos (0.600.0); die erste
    /// steht zusätzlich in <see cref="ImageWidth"/>/<see cref="ImageHeight"/>.</summary>
    public List<int[]>? PageSizes { get; set; }

    /// <summary>Über wie viele Fotos das Formular geht (1, solange <see cref="PageSizes"/> fehlt).</summary>
    public int PageCount => PageSizes is { Count: > 1 } p ? p.Count : 1;

    /// <summary>Die Antwort des Modells mit den Bildmaßen daneben (<see cref="ImageWidth"/>/<see cref="ImageHeight"/>);
    /// unverändert, wenn es keine Maße oder kein JSON-Objekt ist.</summary>
    public static string? WithImageSize(string? json, (int Width, int Height)? size)
        => WithImageSize(json, size is { } s ? new[] { s } : Array.Empty<(int, int)>());

    /// <summary>Dasselbe je Seite: die erste als <c>imageWidth</c>/<c>imageHeight</c>, bei mehreren Seiten alle als
    /// <c>pageSizes</c>.</summary>
    public static string? WithImageSize(string? json, IReadOnlyList<(int Width, int Height)> sizes)
    {
        if (json == null || sizes.Count == 0) return json;
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(json) is not System.Text.Json.Nodes.JsonObject o) return json;
            o["imageWidth"] = sizes[0].Width;
            o["imageHeight"] = sizes[0].Height;
            if (sizes.Count > 1)
                o["pageSizes"] = new System.Text.Json.Nodes.JsonArray(sizes
                    .Select(s => (System.Text.Json.Nodes.JsonNode?)new System.Text.Json.Nodes.JsonArray(s.Width, s.Height)).ToArray());
            return o.ToJsonString();
        }
        catch (JsonException) { return json; }
    }

    /// <summary>Je Eintrag seine Seite (1-basiert), in 1..<see cref="PageCount"/> geklemmt — bei einer Seite immer 1.</summary>
    public List<int> EntryPages() => Moves.Select(m => PageOf(m)).ToList();

    private int PageOf(Entry m) => PageCount > 1 ? Math.Clamp(m.Page ?? 1, 1, PageCount) : 1;

    /// <summary>
    /// Je Eintrag der Kasten in 0..1000 des aufrechten Fotos (so rechnet die Korrekturseite), <c>null</c> = unbrauchbar.
    /// Mit Bildmaßen: Pixel → Promille. Ohne (Einlesungen von 0.550.0): die Kästen SOLLTEN schon 0..1000 sein; greift
    /// aber auch nur einer darüber hinaus, war es in Wahrheit Pixel eines unbekannten Bildes — dann alle verwerfen statt
    /// an den Rand gequetschte Ausschnitte zu zeigen (so auf Dev passiert: y bis 1790).
    /// </summary>
    public List<int[]?> NormalizedBoxes()
    {
        // Mehrere Seiten: jeder Kasten in den Pixeln SEINER Seite.
        if (PageCount > 1)
            return Moves.Select(m => PageSizes![PageOf(m) - 1] is { Length: 2 } s ? m.NormalizedBox(s[0], s[1]) : null).ToList();
        if (ImageWidth is int w && w > 0 && ImageHeight is int h && h > 0)
            return Moves.Select(m => m.NormalizedBox(w, h)).ToList();
        var outOfRange = Moves.Any(m => m.Box is { Count: 4 } b && b.Any(v => v > 1000));
        return Moves.Select(m => outOfRange ? null : m.NormalizedBox()).ToList();
    }

    public sealed class Entry
    {
        public int MoveNumber { get; set; }
        public string? Color { get; set; }
        public string Written { get; set; } = string.Empty;
        public string? San { get; set; }
        public List<string>? Alternatives { get; set; }
        public string? Confidence { get; set; }
        public string? Note { get; set; }
        /// <summary>Wo der Eintrag auf dem Foto steht: [x0, y0, x1, y1] in PIXELN des Bildes, das das Modell bekam
        /// (<see cref="ImageWidth"/> × <see cref="ImageHeight"/>, aufrecht wie im Browser); bei Einlesungen von 0.550.0
        /// als 0..1000 angefordert. Fehlt davor und bei dots.ocr.</summary>
        public List<int>? Box { get; set; }
        /// <summary>Auf welchem Foto der Eintrag steht (1 = erstes) — nur bei einem Formular über mehrere Fotos.</summary>
        public int? Page { get; set; }

        /// <summary>Pixel eines <paramref name="width"/>×<paramref name="height"/>-Bildes → 0..1000; ein Kasten, der
        /// deutlich (über 3 %) aus dem Bild ragt, stammt nicht aus diesem Bild → <c>null</c>.</summary>
        public int[]? NormalizedBox(int width, int height)
        {
            if (Box is not { Count: 4 } || width <= 0 || height <= 0) return null;
            if (Box.Any(v => v < -0.03 * Math.Max(width, height))
                || Box[0] > width * 1.03 || Box[2] > width * 1.03 || Box[1] > height * 1.03 || Box[3] > height * 1.03)
                return null;
            return Promille(new[]
            {
                (int)Math.Round(Box[0] * 1000.0 / width), (int)Math.Round(Box[1] * 1000.0 / height),
                (int)Math.Round(Box[2] * 1000.0 / width), (int)Math.Round(Box[3] * 1000.0 / height),
            });
        }

        /// <summary>Der Kasten, wenn er brauchbar ist: vier Werte, in 0..1000 geklemmt, Ecken sortiert, nicht leer —
        /// sonst <c>null</c>. Ein Modell kann Ecken vertauschen oder über den Rand greifen; ein kaputter Kasten soll
        /// die Korrekturseite nicht mit einem leeren Ausschnitt füllen.</summary>
        public int[]? NormalizedBox() => Box is { Count: 4 } ? Promille(Box.ToArray()) : null;

        private static int[]? Promille(int[] box)
        {
            var c = box.Select(v => Math.Clamp(v, 0, 1000)).ToArray();
            int x0 = Math.Min(c[0], c[2]), x1 = Math.Max(c[0], c[2]), y0 = Math.Min(c[1], c[3]), y1 = Math.Max(c[1], c[3]);
            return x1 - x0 < 2 || y1 - y0 < 2 ? null : new[] { x0, y0, x1, y1 };
        }

        public ScannedPly ToScanned() => new(Written ?? string.Empty, string.IsNullOrWhiteSpace(San) ? null : San,
            Alternatives?.Where(a => !string.IsNullOrWhiteSpace(a)).Take(3).ToList(), Confidence);
    }

    public List<ScannedPly> Scanned() => Moves.Select(m => m.ToScanned()).ToList();

    /// <summary><c>null</c> bei kaputtem JSON.</summary>
    public static ScoresheetTranscription? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var t = JsonSerializer.Deserialize<ScoresheetTranscription>(json, ScoresheetScanService.Json);
            if (t == null) return null;
            t.Moves = t.Moves.Take(600).ToList();
            return t;
        }
        catch (JsonException) { return null; }
    }
}
