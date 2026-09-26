using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public enum CourseTranslationRequestStatus
{
    /// <summary>Neuer Auftrag angelegt (202).</summary>
    Created,
    /// <summary>Fuer (Kurs, Sprache) gab es schon einen offenen Auftrag — der kommt zurueck (200), kein neuer.</summary>
    Existing,
    NotFound,
    UnsupportedLanguage,
    SameLanguage,
    NothingToTranslate,
    UserLimit,
    NotConfigured,
}

public sealed record CourseTranslationRequestResult(CourseTranslationRequestStatus Status,
    CourseTranslationJobDto? Job = null, CourseTranslationMyJobDto? OpenJob = null)
{
    /// <summary>Der Grund fuer die Oberflaeche (sie formuliert den Satz) — <c>null</c> bei Erfolg.</summary>
    public string? Reason => Status switch
    {
        CourseTranslationRequestStatus.UnsupportedLanguage => "unsupported-language",
        CourseTranslationRequestStatus.SameLanguage => "same-language",
        CourseTranslationRequestStatus.NothingToTranslate => "nothing-to-translate",
        CourseTranslationRequestStatus.UserLimit => "user-limit",
        CourseTranslationRequestStatus.NotConfigured => "not-configured",
        _ => null,
    };
}

public enum CourseTranslationWithdrawStatus { Withdrawn, NotFound, Forbidden, NotWaiting }

public enum CourseCommentLanguageStatus { Set, NotFound, Forbidden, Invalid }

/// <summary>Was aus einem Lauf des Hintergrunddienstes geworden ist.</summary>
public enum CourseTranslationJobOutcome
{
    Done,
    /// <summary>Keine einzige Linie ging durch (alle gescheitert, Buch weg, Sprache ungueltig).</summary>
    Failed,
    NothingToDo,
    /// <summary>Zielsprache = Quellsprache (erst beim Lauf bestimmt oder inzwischen korrigiert) — Auftrag verworfen.</summary>
    SameLanguage,
    /// <summary>Kein Text-Modell — der Auftrag steht wieder in der Schlange.</summary>
    NotConfigured,
    /// <summary>Waehrend des Laufs zurueckgezogen (Admin, Konto geloescht) — nichts mehr geschrieben.</summary>
    Withdrawn,
    /// <summary>Der Auftrag lief gar nicht (mehr).</summary>
    NotRunning,
}

/// <summary>
/// Auftraege „diesen Kurs in diese Sprache uebersetzen" (Plan „Kurs-Kommentare mehrsprachig", Abschnitte 5 und 6):
/// anfordern, zurueckziehen, anzeigen — und die Seite, die der <see cref="CourseTranslationWorker"/> braucht
/// (naechsten nehmen, laufen lassen, Automatik). Die Uebersetzung selbst macht <see cref="CourseTranslationService"/>.
///
/// <para><b>Regeln beim Anfordern</b> (Entscheidungen des Nutzers vom 2026-09-26): jeder mit Kurs-Zugang, in jede der
/// 25 Oberflaechensprachen (<see cref="CourseTranslationLanguages"/>), hoechstens EIN offener angeforderter Auftrag je
/// Nutzer (wartend oder laufend), Admin unbegrenzt. Gibt es fuer (Kurs, Sprache) schon einen offenen Auftrag, kommt
/// DER zurueck — egal, wer ihn angelegt hat, und ohne gegen das Limit zu zaehlen. Beides erzwingt der Dienst und nicht
/// die Datenbank (MariaDB kennt keinen gefilterten eindeutigen Index); ein doppelter Auftrag prueft nur doppelt.
/// Waehrend der Sperrzeit der Spark wird ein Auftrag ANGENOMMEN und wartet — kein 503.</para>
///
/// <para><b>Reihenfolge</b> (<see cref="InQueueOrder"/>, EINE Stelle fuer Dienst und Anzeige): angeforderte vor der
/// Automatik, je Gruppe die aeltesten zuerst. Ein neu angeforderter Auftrag stellt einen LAUFENDEN Automatik-Auftrag
/// zurueck (<see cref="CourseTranslationSignal.PreemptAutomatic"/>): ein grosser Kurs rechnet sonst einen halben Tag,
/// und der Vorrang hiesse nur „danach". Weil alles inkrementell ist, verliert der zurueckgestellte nur die Linien,
/// die gerade beim Modell waren.</para>
///
/// <para><b>Automatik</b> (<c>CourseTranslation:AutoLanguages</c>, Vorgabe leer = aus; nur Prod bekommt <c>de,en</c>):
/// ist nichts angefordert und nichts wartet, legt der Dienst EINEN Auftrag fuer den naechsten Kurs an, dem die Sprache
/// fehlt — zuletzt benutzte Kurse zuerst (juengster Kursversuch), Kurse in dieser Quellsprache nicht. Grobe Vorauswahl
/// in SQL („eine Linie mit Text, aber ohne Satz in der Sprache"), den Fingerabdruck prueft der Lauf. Ein Kurs, fuer den
/// in den letzten <see cref="AutoCooldown"/> ein Auftrag der Sprache fertig wurde, bleibt draussen — sonst holte eine
/// Linie, die nie einen Satz bekommt (nur Leerzeichen, immer abgelehnt), die Automatik in eine Schleife.</para>
/// </summary>
public class CourseTranslationJobService
{
    /// <summary>So lange nimmt sich die Automatik einen Kurs nicht erneut vor, nachdem ein Auftrag der Sprache fertig wurde.</summary>
    public static readonly TimeSpan AutoCooldown = TimeSpan.FromDays(7);

    /// <summary>So viele erledigte Auftraege zeigt die Kursansicht hinter den offenen.</summary>
    public const int RecentJobsPerCourse = 5;

    /// <summary>So viele erledigte Auftraege zeigt die Admin-Ansicht.</summary>
    public const int AdminRecentJobs = 50;

    private readonly AppDbContext _db;
    private readonly CourseTranslationService _translation;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CourseTranslationJobService> _logger;
    private readonly CourseTranslationSignal? _signal;
    private readonly QuietHours? _quiet;

    public CourseTranslationJobService(AppDbContext db, CourseTranslationService translation, IServiceScopeFactory scopes,
        IConfiguration config, ILogger<CourseTranslationJobService> logger, CourseTranslationSignal? signal = null,
        QuietHours? quiet = null)
    {
        _db = db;
        _translation = translation;
        _scopes = scopes;
        _logger = logger;
        _signal = signal;
        _quiet = quiet;
        AutoLanguages = CourseTranslationLanguages.ParseList(config["CourseTranslation:AutoLanguages"]);
    }

    /// <summary>Ein Text-Modell ist konfiguriert.</summary>
    public bool IsAvailable => _translation.IsAvailable;

    /// <summary>Sprachen der Automatik — leer = aus.</summary>
    public IReadOnlyList<string> AutoLanguages { get; }

    private static bool IsOpen(CourseTranslationJobStatus s)
        => s is CourseTranslationJobStatus.Queued or CourseTranslationJobStatus.Running;

    /// <summary>Die Reihenfolge der Warteschlange: angeforderte vor der Automatik, je Gruppe die aeltesten zuerst.</summary>
    internal static IOrderedQueryable<CourseTranslationJob> InQueueOrder(IQueryable<CourseTranslationJob> jobs)
        => jobs.OrderByDescending(j => j.RequestedByUserId != null).ThenBy(j => j.CreatedAt).ThenBy(j => j.Id);

    // ── Lesen ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Die Uebersetzungen eines Kurses (<c>GET /api/courses/{id}/translations</c>). Lesbar wie der Kurs selbst:
    /// angemeldet ueber <see cref="CourseAccess"/>, ohne Anmeldung nur ein oeffentlicher Kurs. Die Quellsprache wird
    /// dabei beim ersten Mal bestimmt und festgehalten (derselbe Schritt wie vor dem ersten Lauf).
    /// </summary>
    /// <returns><c>null</c>, wenn es den Kurs nicht gibt oder der Aufrufer ihn nicht sehen darf.</returns>
    public async Task<CourseTranslationsDto?> GetOverviewAsync(int bookId, int? userId, bool isAdmin,
        CancellationToken ct = default)
    {
        if (!await CanReadAsync(bookId, userId, isAdmin, ct)) return null;
        var source = await _translation.EnsureSourceLanguageAsync(bookId, ct);
        if (source is null) return null;

        var linesTotal = await _db.BookPuzzles.AsNoTracking().CountAsync(bp => bp.BookId == bookId
            && ((bp.Comment != null && bp.Comment != "") || (bp.MoveComments != null && bp.MoveComments != "")
                || (bp.Title != null && bp.Title != "") || (bp.Chapter != null && bp.Chapter != "")), ct);
        var perLanguage = await _db.CommentSets.AsNoTracking()
            .Where(s => s.BookPuzzleId != null && s.BookPuzzle!.BookId == bookId)
            .GroupBy(s => s.Language)
            .Select(g => new { Language = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var open = await _db.CourseTranslationJobs.AsNoTracking()
            .Where(j => j.BookId == bookId
                && (j.Status == CourseTranslationJobStatus.Queued || j.Status == CourseTranslationJobStatus.Running))
            .ToListAsync(ct);
        var recent = await _db.CourseTranslationJobs.AsNoTracking()
            .Where(j => j.BookId == bookId
                && j.Status != CourseTranslationJobStatus.Queued && j.Status != CourseTranslationJobStatus.Running)
            .OrderByDescending(j => j.FinishedAt ?? j.CreatedAt).ThenByDescending(j => j.Id)
            .Take(RecentJobsPerCourse)
            .ToListAsync(ct);
        var positions = open.Any(j => j.Status == CourseTranslationJobStatus.Queued)
            ? await QueuePositionsAsync(ct)
            : new Dictionary<int, int>();

        var mine = userId is int uid ? await MyOpenJobAsync(uid, ct) : null;
        var available = IsAvailable;
        return new CourseTranslationsDto
        {
            SourceLanguage = source,
            Languages = perLanguage
                .OrderByDescending(l => l.Count).ThenBy(l => l.Language, StringComparer.Ordinal)
                .Select(l => new CourseTranslationLanguageDto
                {
                    Language = l.Language, LinesTranslated = l.Count, LinesTotal = linesTotal,
                })
                .ToList(),
            Jobs = open
                .OrderByDescending(j => j.Status == CourseTranslationJobStatus.Running)
                .ThenBy(j => positions.TryGetValue(j.Id, out var p) ? p : int.MaxValue)
                .Concat(recent)
                .Select(j => Map(j, userId, positions))
                .ToList(),
            QuietUntil = _quiet?.QuietUntil(),
            MyOpenJob = mine,
            Available = available,
            CanRequest = userId is not null && available && (isAdmin || mine is null),
        };
    }

    /// <summary>Warteschlange und juengste erledigte Auftraege (<c>GET /api/admin/course-translations</c>).</summary>
    public async Task<AdminCourseTranslationsDto> GetAdminOverviewAsync(CancellationToken ct = default)
    {
        var positions = await QueuePositionsAsync(ct);
        var running = await AdminRows(_db.CourseTranslationJobs.Where(j => j.Status == CourseTranslationJobStatus.Running)
            .OrderBy(j => j.StartedAt).ThenBy(j => j.Id), positions, ct);
        var queued = await AdminRows(InQueueOrder(_db.CourseTranslationJobs
            .Where(j => j.Status == CourseTranslationJobStatus.Queued)), positions, ct);
        var recent = await AdminRows(_db.CourseTranslationJobs
            .Where(j => j.Status != CourseTranslationJobStatus.Queued && j.Status != CourseTranslationJobStatus.Running)
            .OrderByDescending(j => j.FinishedAt ?? j.CreatedAt).ThenByDescending(j => j.Id)
            .Take(AdminRecentJobs), positions, ct);
        return new AdminCourseTranslationsDto
        {
            Queue = running.Concat(queued).ToList(),
            Recent = recent,
            QuietUntil = _quiet?.QuietUntil(),
            Available = IsAvailable,
            AutoLanguages = AutoLanguages.ToList(),
        };
    }

    private async Task<List<AdminCourseTranslationJobDto>> AdminRows(IQueryable<CourseTranslationJob> query,
        IReadOnlyDictionary<int, int> positions, CancellationToken ct)
    {
        var rows = await query.AsNoTracking()
            .Select(j => new
            {
                Job = j,
                j.Book!.DisplayName,
                j.Book.FileName,
                Username = _db.AppUsers.Where(u => u.Id == j.RequestedByUserId).Select(u => u.Username).FirstOrDefault(),
            })
            .ToListAsync(ct);
        return rows.Select(r =>
        {
            var dto = new AdminCourseTranslationJobDto
            {
                BookName = BookName(r.DisplayName, r.FileName),
                RequestedByUserId = r.Job.RequestedByUserId,
                RequestedByUsername = r.Username,
            };
            Fill(dto, r.Job, null, positions);
            return dto;
        }).ToList();
    }

    /// <summary>Platz je wartendem Auftrag (1 = der naechste) in der Reihenfolge des Dienstes.</summary>
    private async Task<Dictionary<int, int>> QueuePositionsAsync(CancellationToken ct)
    {
        var ids = await InQueueOrder(_db.CourseTranslationJobs.AsNoTracking()
                .Where(j => j.Status == CourseTranslationJobStatus.Queued))
            .Select(j => j.Id)
            .ToListAsync(ct);
        return ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i + 1);
    }

    /// <summary>Der aelteste offene ANGEFORDERTE Auftrag des Nutzers (irgendein Kurs).</summary>
    private async Task<CourseTranslationMyJobDto?> MyOpenJobAsync(int userId, CancellationToken ct)
    {
        var row = await _db.CourseTranslationJobs.AsNoTracking()
            .Where(j => j.RequestedByUserId == userId
                && (j.Status == CourseTranslationJobStatus.Queued || j.Status == CourseTranslationJobStatus.Running))
            .OrderBy(j => j.CreatedAt).ThenBy(j => j.Id)
            .Select(j => new { j.Id, j.BookId, j.Language, j.Status, j.Book!.DisplayName, j.Book.FileName })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : new CourseTranslationMyJobDto
        {
            JobId = row.Id, BookId = row.BookId, BookName = BookName(row.DisplayName, row.FileName),
            Language = row.Language, Status = StatusText(row.Status),
        };
    }

    private async Task<bool> CanReadAsync(int bookId, int? userId, bool isAdmin, CancellationToken ct)
        => userId is int uid
            ? await CourseAccess.CanAccessAsync(_db, uid, bookId, isAdmin, ct)
            : await _db.Books.AnyAsync(b => b.Id == bookId && b.IsPublic, ct);

    private static string BookName(string? displayName, string? fileName)
        => !string.IsNullOrWhiteSpace(displayName) ? displayName : fileName ?? string.Empty;

    internal static string StatusText(CourseTranslationJobStatus s) => s.ToString().ToLowerInvariant();

    private static CourseTranslationJobDto Map(CourseTranslationJob job, int? userId, IReadOnlyDictionary<int, int> positions)
    {
        var dto = new CourseTranslationJobDto();
        Fill(dto, job, userId, positions);
        return dto;
    }

    private static void Fill(CourseTranslationJobDto dto, CourseTranslationJob job, int? userId,
        IReadOnlyDictionary<int, int> positions)
    {
        dto.Id = job.Id;
        dto.BookId = job.BookId;
        dto.Language = job.Language;
        dto.Status = StatusText(job.Status);
        dto.LinesTotal = job.LinesTotal;
        dto.LinesDone = job.LinesDone;
        dto.LinesFailed = job.LinesFailed;
        dto.Automatic = job.RequestedByUserId is null;
        dto.RequestedByMe = userId is not null && job.RequestedByUserId == userId;
        dto.QueuePosition = job.Status == CourseTranslationJobStatus.Queued && positions.TryGetValue(job.Id, out var p)
            ? p : null;
        dto.CreatedAt = job.CreatedAt;
        dto.StartedAt = job.StartedAt;
        dto.FinishedAt = job.FinishedAt;
        dto.LastError = job.LastError;
    }

    // ── Anfordern / zurueckziehen / Quellsprache ─────────────────────────────────────────────────

    /// <summary>
    /// Einen Auftrag anfordern (<c>POST /api/courses/{id}/translations</c>). Reihenfolge der Pruefungen: Zugang (404),
    /// Sprache (<c>unsupported-language</c>), Modell (<c>not-configured</c>), Quellsprache (<c>same-language</c>),
    /// vorhandener offener Auftrag (zurueckgeben), offene Arbeit (<c>nothing-to-translate</c>), Nutzer-Limit
    /// (<c>user-limit</c>, nicht fuer Admins).
    /// </summary>
    public async Task<CourseTranslationRequestResult> RequestAsync(int userId, bool isAdmin, int bookId, string? language,
        CancellationToken ct = default)
    {
        if (!await CourseAccess.CanAccessAsync(_db, userId, bookId, isAdmin, ct))
            return new(CourseTranslationRequestStatus.NotFound);
        var target = CourseTranslationLanguages.Normalize(language);
        if (target is null) return new(CourseTranslationRequestStatus.UnsupportedLanguage);
        if (!IsAvailable) return new(CourseTranslationRequestStatus.NotConfigured);

        var source = await _translation.EnsureSourceLanguageAsync(bookId, ct);
        if (source is null) return new(CourseTranslationRequestStatus.NotFound);
        if (source == target) return new(CourseTranslationRequestStatus.SameLanguage);

        // Ein offener Auftrag fuer (Kurs, Sprache) deckt die Anfrage — der laufende vor dem wartenden.
        var existing = await _db.CourseTranslationJobs.AsNoTracking()
            .Where(j => j.BookId == bookId && j.Language == target
                && (j.Status == CourseTranslationJobStatus.Queued || j.Status == CourseTranslationJobStatus.Running))
            .OrderByDescending(j => j.Status).ThenBy(j => j.Id)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            return new(CourseTranslationRequestStatus.Existing, Map(existing, userId, await QueuePositionsAsync(ct)));

        var (open, _) = await _translation.OpenWorkAsync(bookId, target, ct);
        if (open.Count == 0) return new(CourseTranslationRequestStatus.NothingToTranslate);

        if (!isAdmin && await MyOpenJobAsync(userId, ct) is { } mine)
            return new(CourseTranslationRequestStatus.UserLimit, OpenJob: mine);

        var job = new CourseTranslationJob
        {
            BookId = bookId, Language = target, RequestedByUserId = userId,
            Status = CourseTranslationJobStatus.Queued, LinesTotal = open.Count, CreatedAt = DateTime.UtcNow,
        };
        _db.CourseTranslationJobs.Add(job);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Kurs-Uebersetzung angefordert: Kurs {BookId} nach {Lang} von Nutzer {UserId} (Auftrag {JobId}, {Lines} Linien offen).",
            bookId, target, userId, job.Id, open.Count);
        _signal?.PreemptAutomatic();
        _signal?.Wake();
        return new(CourseTranslationRequestStatus.Created, Map(job, userId, await QueuePositionsAsync(ct)));
    }

    /// <summary>
    /// Einen Auftrag zurueckziehen (<c>DELETE /api/courses/{id}/translations/{jobId}</c>): der Nutzer seinen eigenen
    /// WARTENDEN, der Admin jeden offenen — einen laufenden bricht der Dienst sofort ab.
    /// </summary>
    public async Task<CourseTranslationWithdrawStatus> WithdrawAsync(int userId, bool isAdmin, int bookId, int jobId,
        CancellationToken ct = default)
    {
        var job = await _db.CourseTranslationJobs.FirstOrDefaultAsync(j => j.Id == jobId && j.BookId == bookId, ct);
        if (job is null) return CourseTranslationWithdrawStatus.NotFound;
        if (!isAdmin)
        {
            if (!await CourseAccess.CanAccessAsync(_db, userId, bookId, isAdmin: false, ct))
                return CourseTranslationWithdrawStatus.NotFound;
            if (job.RequestedByUserId != userId) return CourseTranslationWithdrawStatus.Forbidden;
            if (job.Status != CourseTranslationJobStatus.Queued) return CourseTranslationWithdrawStatus.NotWaiting;
        }
        else if (!IsOpen(job.Status)) return CourseTranslationWithdrawStatus.NotWaiting;

        var wasRunning = job.Status == CourseTranslationJobStatus.Running;
        job.Status = CourseTranslationJobStatus.Cancelled;
        job.FinishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        if (wasRunning) _signal?.Cancel(job.Id, CourseTranslationStop.Withdrawn);
        _logger.LogInformation("Kurs-Uebersetzung {JobId} (Kurs {BookId}, {Lang}) zurueckgezogen von Nutzer {UserId}{Running}.",
            job.Id, bookId, job.Language, userId, wasRunning ? " (lief)" : "");
        return CourseTranslationWithdrawStatus.Withdrawn;
    }

    /// <summary>Die Quellsprache korrigieren (<c>PUT /api/courses/{id}/comment-language</c>): Besitzer oder Admin. Macht
    /// vorhandene Saetze NICHT ungueltig (sie haengen am Fingerabdruck, nicht an der Quellsprache); ein offener Auftrag in
    /// der neuen Quellsprache wird beim Lauf verworfen.</summary>
    public async Task<CourseCommentLanguageStatus> SetCommentLanguageAsync(int userId, bool isAdmin, int bookId,
        string? language, CancellationToken ct = default)
    {
        // Getrackt OHNE Source (Tabellensplitting): geschrieben wird nur CommentLanguage.
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == bookId, ct);
        if (book is null) return CourseCommentLanguageStatus.NotFound;
        if (!isAdmin)
        {
            if (!await CourseAccess.CanAccessAsync(_db, userId, bookId, isAdmin: false, ct))
                return CourseCommentLanguageStatus.NotFound;
            if (book.OwnerUserId != userId) return CourseCommentLanguageStatus.Forbidden;
        }
        var lang = CourseCommentLocalizer.NormalizeLanguage(language);
        if (lang is null) return CourseCommentLanguageStatus.Invalid;
        book.CommentLanguage = lang;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Kurs {BookId}: Quellsprache der Kommentare auf {Lang} gesetzt (Nutzer {UserId}).",
            bookId, lang, userId);
        return CourseCommentLanguageStatus.Set;
    }

    // ── Nachziehen nach einer Aenderung des Kurses ───────────────────────────────────────────────

    /// <summary>
    /// Der Kurs hat sich geaendert (Aktualisieren, Neu-Aufbereiten, angehaengte Linien, Kapitel umbenannt): fuer jede
    /// Sprache, in der er schon Saetze hat, einen Automatik-Auftrag einreihen — der erledigt nur Veraltetes und kostet
    /// sonst nichts. Ein schon WARTENDER Auftrag der Sprache genuegt; ein LAUFENDER nicht, der hat seine offene Arbeit
    /// vor der Aenderung bestimmt.
    /// </summary>
    /// <returns>Wie viele Auftraege neu eingereiht wurden.</returns>
    public async Task<int> EnqueueRefreshAsync(int bookId, CancellationToken ct = default)
    {
        var source = await _db.Books.AsNoTracking().Where(b => b.Id == bookId).Select(b => b.CommentLanguage)
            .FirstOrDefaultAsync(ct);
        var languages = await _db.CommentSets.AsNoTracking()
            .Where(s => s.BookPuzzleId != null && s.BookPuzzle!.BookId == bookId)
            .Select(s => s.Language)
            .Distinct()
            .ToListAsync(ct);
        if (languages.Count == 0) return 0;
        var waiting = await _db.CourseTranslationJobs.AsNoTracking()
            .Where(j => j.BookId == bookId && j.Status == CourseTranslationJobStatus.Queued)
            .Select(j => j.Language)
            .ToListAsync(ct);

        var added = new List<string>();
        foreach (var lang in languages.OrderBy(l => l, StringComparer.Ordinal))
        {
            if (lang == source || waiting.Contains(lang)) continue;
            _db.CourseTranslationJobs.Add(new CourseTranslationJob
            {
                BookId = bookId, Language = lang, RequestedByUserId = null,
                Status = CourseTranslationJobStatus.Queued, CreatedAt = DateTime.UtcNow,
            });
            added.Add(lang);
        }
        if (added.Count == 0) return 0;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Kurs {BookId} geaendert: Uebersetzung nach {Langs} zum Nachziehen eingereiht.",
            bookId, string.Join(",", added));
        _signal?.Wake();
        return added.Count;
    }

    /// <summary><see cref="EnqueueRefreshAsync"/> fuer die Aenderungswege (Import, Kapitel umbenennen): ein Fehler hier
    /// darf die Aenderung selbst nicht scheitern lassen — er wird geloggt, der naechste Anlass holt es nach.</summary>
    public async Task NotifyCourseChangedAsync(int bookId, CancellationToken ct = default)
    {
        try { await EnqueueRefreshAsync(bookId, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kurs {BookId}: Uebersetzungen nachziehen fehlgeschlagen.", bookId);
        }
    }

    // ── Hintergrunddienst ────────────────────────────────────────────────────────────────────────

    /// <summary>Beim Start: was auf <c>Running</c> haengen blieb (Neustart mitten im Lauf), kommt zurueck in die Schlange.</summary>
    public async Task<int> RequeueInterruptedAsync(CancellationToken ct = default)
    {
        var stuck = await _db.CourseTranslationJobs.Where(j => j.Status == CourseTranslationJobStatus.Running)
            .ToListAsync(ct);
        foreach (var job in stuck) job.Status = CourseTranslationJobStatus.Queued;
        if (stuck.Count > 0) await _db.SaveChangesAsync(ct);
        return stuck.Count;
    }

    /// <summary>
    /// Den naechsten Auftrag nehmen und auf <c>Running</c> setzen: erst angeforderte, dann wartende Automatik-Auftraege,
    /// sonst (mit Automatik) einen neuen fuer den naechsten Kurs, dem eine Sprache fehlt. <c>null</c> = nichts zu tun.
    /// </summary>
    public async Task<(int Id, bool Automatic)?> ClaimNextAsync(CancellationToken ct = default)
    {
        var next = await InQueueOrder(_db.CourseTranslationJobs.Where(j => j.Status == CourseTranslationJobStatus.Queued))
                       .FirstOrDefaultAsync(ct)
                   ?? await CreateAutomaticAsync(ct);
        if (next is null) return null;
        next.Status = CourseTranslationJobStatus.Running;
        next.StartedAt = DateTime.UtcNow;
        next.FinishedAt = null;
        next.LastError = null;
        next.LinesDone = 0;
        next.LinesFailed = 0;
        await _db.SaveChangesAsync(ct);
        return (next.Id, next.RequestedByUserId is null);
    }

    /// <summary>Der naechste Automatik-Auftrag (angelegt, noch nicht gespeichert) — oder <c>null</c>, wenn die Automatik
    /// aus ist oder kein Kurs mehr eine Sprache braucht.</summary>
    private async Task<CourseTranslationJob?> CreateAutomaticAsync(CancellationToken ct)
    {
        if (AutoLanguages.Count == 0) return null;
        var cutoff = DateTime.UtcNow - AutoCooldown;
        (int BookId, DateTime? LastUsed, string Language)? best = null;
        foreach (var lang in AutoLanguages)
        {
            var candidate = await _db.Books.AsNoTracking()
                .Where(b => b.CommentLanguage == null || b.CommentLanguage != lang)
                .Where(b => !_db.CourseTranslationJobs.Any(j => j.BookId == b.Id && j.Language == lang
                    && (j.Status == CourseTranslationJobStatus.Queued || j.Status == CourseTranslationJobStatus.Running
                        || (j.FinishedAt != null && j.FinishedAt > cutoff))))
                .Where(b => _db.BookPuzzles.Any(bp => bp.BookId == b.Id
                    && ((bp.Comment != null && bp.Comment != "") || (bp.MoveComments != null && bp.MoveComments != "")
                        || (bp.Title != null && bp.Title != "") || (bp.Chapter != null && bp.Chapter != ""))
                    && !_db.CommentSets.Any(s => s.BookPuzzleId == bp.Id && s.Language == lang)))
                .Select(b => new
                {
                    b.Id,
                    LastUsed = _db.CourseAttempts.Where(a => a.BookId == b.Id).Max(a => (DateTime?)a.AttemptedAt),
                })
                .OrderByDescending(x => x.LastUsed != null).ThenByDescending(x => x.LastUsed).ThenBy(x => x.Id)
                .FirstOrDefaultAsync(ct);
            if (candidate is null) continue;
            // Der zuletzt benutzte Kurs gewinnt ueber die Sprachen hinweg; bei Gleichstand die Reihenfolge der Einstellung.
            if (best is null || (candidate.LastUsed ?? DateTime.MinValue) > (best.Value.LastUsed ?? DateTime.MinValue))
                best = (candidate.Id, candidate.LastUsed, lang);
        }
        if (best is not { } pick) return null;

        var job = new CourseTranslationJob
        {
            BookId = pick.BookId, Language = pick.Language, RequestedByUserId = null,
            Status = CourseTranslationJobStatus.Queued, CreatedAt = DateTime.UtcNow,
        };
        _db.CourseTranslationJobs.Add(job);
        _logger.LogInformation("Kurs-Uebersetzung Automatik: Kurs {BookId} nach {Lang} (zuletzt benutzt {LastUsed}).",
            pick.BookId, pick.Language, pick.LastUsed);
        return job;
    }

    /// <summary>
    /// Einen genommenen Auftrag laufen lassen: <see cref="CourseTranslationService.TranslateCourseAsync"/> mit dem
    /// Fortschritt am Auftrag (je Zwischenstand in einem EIGENEN Kontext geschrieben — nur die Zaehler, der Zustand bleibt
    /// unberuehrt). Steht der Auftrag dabei nicht mehr auf <c>Running</c> (zurueckgezogen, Konto geloescht), bricht der
    /// Lauf ab. Ein Abbruch ueber <paramref name="ct"/> (Sperrzeit, Vorrang, Dienst stoppt) fliegt als
    /// <see cref="OperationCanceledException"/> — den Auftrag stellt dann der Dienst zurueck.
    /// </summary>
    public async Task<CourseTranslationJobOutcome> RunAsync(int jobId, CancellationToken ct = default)
    {
        var job = await _db.CourseTranslationJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || job.Status != CourseTranslationJobStatus.Running) return CourseTranslationJobOutcome.NotRunning;

        using var run = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var withdrawn = false;
        async Task ProgressAsync(CourseTranslationProgress p, CancellationToken token)
        {
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.CourseTranslationJobs.FirstOrDefaultAsync(j => j.Id == jobId, token);
            if (row is null || row.Status != CourseTranslationJobStatus.Running)
            {
                withdrawn = true;
                await run.CancelAsync();
                return;
            }
            row.LinesTotal = p.LinesTotal;
            row.LinesDone = p.LinesDone;
            row.LinesFailed = p.LinesFailed;
            await db.SaveChangesAsync(token);
        }

        CourseTranslationRun result;
        try
        {
            result = await _translation.TranslateCourseAsync(job.BookId, job.Language, ProgressAsync, ct: run.Token);
        }
        catch (OperationCanceledException) when (withdrawn && !ct.IsCancellationRequested)
        {
            _logger.LogInformation("Kurs-Uebersetzung {JobId}: waehrend des Laufs zurueckgezogen.", jobId);
            return CourseTranslationJobOutcome.Withdrawn;
        }
        return await FinishAsync(jobId, result);
    }

    /// <summary>Das Ergebnis eines Laufs am Auftrag festhalten — nur, solange er noch auf <c>Running</c> steht.</summary>
    private async Task<CourseTranslationJobOutcome> FinishAsync(int jobId, CourseTranslationRun run)
    {
        // Ohne Token: ein fertiger Lauf soll festgehalten werden, auch wenn der Dienst gerade stoppt.
        var row = await _db.CourseTranslationJobs.FirstOrDefaultAsync(j => j.Id == jobId, CancellationToken.None);
        if (row is null || row.Status != CourseTranslationJobStatus.Running) return CourseTranslationJobOutcome.Withdrawn;

        var outcome = CourseTranslationJobOutcome.Done;
        switch (run.Status)
        {
            case CourseTranslationRunStatus.Done:
                row.LinesTotal = run.LinesTotal;
                row.LinesDone = run.LinesDone;
                row.LinesFailed = run.LinesFailed;
                if (run.LinesDone == 0 && run.LinesFailed > 0) outcome = CourseTranslationJobOutcome.Failed;
                row.LastError = Describe(run);
                break;
            case CourseTranslationRunStatus.NothingToDo:
                outcome = CourseTranslationJobOutcome.NothingToDo;
                row.LinesTotal = row.LinesDone = row.LinesFailed = 0;
                break;
            case CourseTranslationRunStatus.SameLanguage:
                outcome = CourseTranslationJobOutcome.SameLanguage;
                row.LastError = "same-language";
                break;
            case CourseTranslationRunStatus.NotConfigured:
                // Kein Modell mehr (Einstellung weg): zurueck in die Schlange, der Dienst schlaeft, bis eines da ist.
                row.Status = CourseTranslationJobStatus.Queued;
                await _db.SaveChangesAsync(CancellationToken.None);
                return CourseTranslationJobOutcome.NotConfigured;
            default:
                outcome = CourseTranslationJobOutcome.Failed;
                row.LastError = run.Status == CourseTranslationRunStatus.NotFound ? "course not found" : "invalid language";
                break;
        }
        row.Status = outcome switch
        {
            CourseTranslationJobOutcome.Failed => CourseTranslationJobStatus.Failed,
            CourseTranslationJobOutcome.SameLanguage => CourseTranslationJobStatus.Cancelled,
            _ => CourseTranslationJobStatus.Done,
        };
        row.FinishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(CancellationToken.None);
        _logger.LogInformation("Kurs-Uebersetzung {JobId} (Kurs {BookId}, {Lang}): {Outcome}, {Done}/{Total} Linien, {Failed} gescheitert.",
            jobId, row.BookId, row.Language, outcome, row.LinesDone, row.LinesTotal, row.LinesFailed);
        return outcome;
    }

    private static string? Describe(CourseTranslationRun run)
    {
        var parts = new List<string>();
        if (run.LinesFailed > 0) parts.Add($"{run.LinesFailed} lines failed");
        if (run.ChaptersMissing > 0) parts.Add($"{run.ChaptersMissing} chapter names without translation");
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    /// <summary>Einen laufenden Auftrag zurueck in die Schlange stellen (Sperrzeit, Vorrang, Dienst stoppt) — der
    /// naechste Lauf ueberspringt das Fertige.</summary>
    public async Task RequeueAsync(int jobId, CancellationToken ct = default)
    {
        var row = await _db.CourseTranslationJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (row is null || row.Status != CourseTranslationJobStatus.Running) return;
        row.Status = CourseTranslationJobStatus.Queued;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Ein unerwarteter Fehler im Lauf: Auftrag gescheitert (mit Grund), der Dienst macht mit dem naechsten weiter.</summary>
    public async Task MarkFailedAsync(int jobId, string error, CancellationToken ct = default)
    {
        var row = await _db.CourseTranslationJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (row is null || row.Status != CourseTranslationJobStatus.Running) return;
        row.Status = CourseTranslationJobStatus.Failed;
        row.FinishedAt = DateTime.UtcNow;
        row.LastError = error.Length > 500 ? error[..500] : error;
        await _db.SaveChangesAsync(ct);
    }
}
