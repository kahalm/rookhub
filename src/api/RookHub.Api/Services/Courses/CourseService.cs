using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Chessable;
using RookHub.Api.Services.Puzzles;
using RookHub.Api.Services.Social;
using RookHub.Api.DTOs.Courses;

namespace RookHub.Api.Services.Courses;

/// <summary>
/// „Kurse" = importierte Bücher, die ein User puzzleweise durcharbeitet (Fortschritt user-bezogen
/// in der DB; Modus sequential/random bestimmt nur die Reihenfolge). Geschäftslogik vormals inline
/// im CourseController. Sichtbarkeit/Zugriff wird je Buch erzwungen — kein Zugriff → 404 via
/// <see cref="NotFoundException"/> (der globale DomainExceptionFilter bildet auf HTTP ab). `isAdmin` reicht der Controller
/// herein (HTTP-Concern).
/// <para><b>Aufteilung</b> (Codereview 2026-09-29, A7-010/Z4): hier liegen Lesen (Kapitel,
/// Puzzles, öffentliche Kurz-Links) und die Themen-Tags. Der Rest steht in eigenen Klassen, die dieser
/// Dienst als FASSADE durchreicht, damit Controller und Aufrufer unverändert bleiben:
/// <see cref="CourseListQuery"/> (Kursliste + Menü-Sichtbarkeit),
/// <see cref="CourseSolverService"/> (next/results/info/reset/Pool-Einstellungen/✓✗-Stand),
/// <see cref="CoursePersonalService"/> (Pins, Verknüpfungen), <see cref="CourseSharingService"/> (Teilen),
/// <see cref="CourseCreationService"/> (persönliche Kurse anlegen/löschen). PGN-Export:
/// <see cref="CoursePgnExportService"/>; Kurs ⇄ Repertoire: <see cref="CourseRepertoireConversionService"/>.
/// Gemeinsame Zählregeln: <see cref="CourseProgressRules"/>.</para>
/// </summary>
public class CourseService
{
    private readonly AppDbContext _db;
    private readonly CourseListQuery _list;
    private readonly CourseSolverService _solver;
    private readonly CoursePersonalService _personal;
    private readonly CourseSharingService _sharing;
    private readonly CourseCreationService _creation;

    // Alle Abhängigkeiten verpflichtend. Früher waren friends/notifications optional und wurden
    // sonst hier selbst gebaut — bequem für Tests, aber der Dienst verdrahtete damit an der DI
    // vorbei, kein Test konnte einen Doppelgänger einschleusen, und die Abhängigkeit war von
    // außen unsichtbar. Tests bauen jetzt über TestServices.Course(db).
    // chessableProxy optional (Default null), damit bestehende Test-Konstruktionen unverändert
    // kompilieren; ohne ihn entfällt nur das Nachtragen des Chessable-Trainingsstarts.
    // KEINE Abhängigkeit auf RepertoireService: seit das Umwandeln im
    // CourseRepertoireConversionService liegt, braucht dieser Dienst nur noch das STATISCHE
    // RepertoireService.LooksLikePgn — und eine Instanz weniger heisst eine Zyklus-Gefahr weniger.
    // Die Teildienste entstehen hier aus GENAU diesen Abhängigkeiten (kein Griff an der DI vorbei:
    // alles, was sie brauchen, kommt über diesen Konstruktor herein); sie loggen unter der Quelle
    // CourseService, damit Log-Filter unverändert greifen.

    public CourseService(AppDbContext db, ILogger<CourseService> logger, PgnImportService pgnImport, BookAdminService bookAdmin, FriendService friends, NotificationService notifications, ChessableProxyService? chessableProxy = null, IConfiguration? configuration = null)
    {
        _db = db;
        _list = new CourseListQuery(db, ChessableSwitch.IsEnabled(configuration));
        _solver = new CourseSolverService(db, logger);
        _personal = new CoursePersonalService(db, logger);
        _sharing = new CourseSharingService(db, friends, notifications);
        _creation = new CourseCreationService(db, logger, pgnImport, bookAdmin, chessableProxy);
    }

    /// <summary>Darf der User dieses (existierende) Buch als Kurs SEHEN? Bearbeiten braucht zusätzlich
    /// Besitzer oder Admin (<see cref="CourseAccess.LoadManageableAsync"/>).</summary>
    // Regel liegt in CourseAccess (geteilt mit CalculationService), damit es genau EINE Definition gibt.
    public Task<bool> CanAccessAsync(int userId, int bookId, bool isAdmin) =>
        CourseAccess.CanAccessAsync(_db, userId, bookId, isAdmin);

    private Task EnsureAccessAsync(int userId, int bookId, bool isAdmin) =>
        CourseProgressRules.EnsureAccessAsync(_db, userId, bookId, isAdmin);

    /// <summary>Normalisiert einen rohen Kapitelwert: leer/Whitespace → <c>null</c> (Sammel-„ohne Kapitel").</summary>
    private static string? NormalizeChapter(string? raw) => ChapterOrder.NormalizeChapter(raw);

    // Buch-Themen-Semantik (Keys/Parsing/Default) zentral in BookThemeTags — geteilt mit dem
    // Trainingsziele-Routing in TrainingGoalService.

    /// <summary>
    /// Die eindeutigen Kapitel eines Buchs in Lesereihenfolge — geteilte Logik in
    /// <see cref="ChapterOrder"/>, damit der Index-Kontrakt mit <see cref="GetChaptersAsync"/>
    /// (liefert die Indizes ans Frontend) und dem Wochenpost-aus-Kapitel-Pfad IDENTISCH bleibt.
    /// Insbesondere zählen nur Quiz-Linien (<c>!IsInfoOnly</c>): ein reines Info-/Intro-Kapitel
    /// erscheint nicht in der Frontend-Liste und darf hier keinen Index belegen, sonst löst
    /// <c>?chapterIndex</c> ab diesem Kapitel auf das FALSCHE (um eins verschobene) Kapitel auf.
    /// </summary>
    private Task<List<string?>> GetOrderedChapterNamesAsync(int bookId)
        => ChapterOrder.GetOrderedChapterNamesAsync(_db, bookId);

    /// <summary>Die Kapitel eines (zugänglichen) Buchs in Lesereihenfolge inkl. nutzerbezogenem Fortschritt.</summary>
    public async Task<List<CourseChapterDto>> GetChaptersAsync(int userId, int bookId, bool isAdmin)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);

        // Info-/Erklärlinien sind kein Quiz → zählen nicht zum Kapitel-Fortschritt.
        var puzzles = await _db.BookPuzzles
            .Where(bp => bp.BookId == bookId && !bp.IsInfoOnly)
            .InReadingOrder()
            .Select(bp => new { bp.Id, bp.Chapter })
            .ToListAsync();
        var solvedSet = (await _db.CoursePuzzleResults
            .Where(cr => cr.UserId == userId && cr.BookId == bookId)
            .Select(cr => cr.BookPuzzleId)
            .ToListAsync()).ToHashSet();

        // In Lesereihenfolge gruppieren (erste-Erscheinung = Reihenfolge der nach Round sortierten Puzzles).
        var groups = new List<(string? Name, int Count, int Solved)>();
        var indexByKey = new Dictionary<string, int>();
        const string noneKey = "\0__none__";
        foreach (var p in puzzles)
        {
            var name = NormalizeChapter(p.Chapter);
            var key = name ?? noneKey;
            if (!indexByKey.TryGetValue(key, out var gi))
            {
                gi = groups.Count;
                indexByKey[key] = gi;
                groups.Add((name, 0, 0));
            }
            var g = groups[gi];
            groups[gi] = (g.Name, g.Count + 1, g.Solved + (solvedSet.Contains(p.Id) ? 1 : 0));
        }

        // Info-/Erklärlinien je Kapitel getrennt zählen (für den Klammer-Ausweis in der Übersicht).
        // Nur BESTEHENDE (quiz-tragende) Kapitel werden annotiert — ein reines Info-Kapitel bekommt
        // KEINE Gruppe/keinen Index (Index-Kontrakt mit GetOrderedChapterNamesAsync/?chapterIndex).
        var infoByKey = new Dictionary<string, int>();
        var infoChapters = await _db.BookPuzzles
            .Where(bp => bp.BookId == bookId && bp.IsInfoOnly)
            .Select(bp => bp.Chapter)
            .ToListAsync();
        foreach (var raw in infoChapters)
        {
            var key = NormalizeChapter(raw) ?? noneKey;
            if (indexByKey.ContainsKey(key))
                infoByKey[key] = infoByKey.GetValueOrDefault(key) + 1;
        }

        return groups.Select((g, idx) => new CourseChapterDto
        {
            Index = idx,
            Name = g.Name,
            PuzzleCount = g.Count,
            SolvedCount = Math.Min(g.Solved, g.Count),
            ProgressPercent = CourseProgressRules.Percent(g.Solved, g.Count),
            InfoCount = infoByKey.GetValueOrDefault(g.Name ?? noneKey),
        }).ToList();
    }

    /// <summary>Alle Puzzles eines (zugänglichen) Buchs am Stück — für das Offline-Speichern.
    /// <para>KALKULATIONSBÜCHER liefert dieser Pfad nicht aus (404, siehe
    /// <see cref="CourseAccess.IsCalculationBookAsync"/>): er reicht <see cref="BookPuzzle.Moves"/>
    /// durch, und ein öffentlich freigegebenes Kalkulationsbuch ist für JEDEN angemeldeten Nutzer
    /// zugänglich (<see cref="CourseAccess.CanAccessAsync"/>) — der Voll-Export wäre die Lösung.</para></summary>
    public async Task<List<BookPuzzleDto>> GetAllPuzzlesAsync(int userId, int bookId, bool isAdmin)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);
        if (await CourseAccess.IsCalculationBookAsync(_db, bookId))
            throw new NotFoundException("Book not found.");
        var puzzles = await PuzzlesWithBookInReadingOrder(_db, bookId).ToListAsync();
        return puzzles.Select(BookPuzzleService.MapToDto).ToList();
    }

    /// <summary>Alle Linien eines Buchs in Lesereihenfolge (Round-Länge → Round → Id), je Zeile MIT den
    /// Buch-Metadaten, die <see cref="BookPuzzleService.MapToDto"/> braucht. Gemeinsame Abfrage von
    /// <see cref="GetAllPuzzlesAsync"/> und <see cref="GetPublicCoursePuzzlesAsync"/>.
    /// <para>Das Roh-PGN des Buchs ist hier bewusst NICHT dabei (liegt in <see cref="BookSource"/>): als
    /// es noch an <see cref="Book"/> hing, zog dieses Include es in JEDE Puzzle-Zeile — 6 MB × 1.881 Linien
    /// = 11 GB aus der DB für einen Request. <c>BookSourceSplitSqlTests</c> prüft das generierte SQL.</para></summary>
    internal static IOrderedQueryable<BookPuzzle> PuzzlesWithBookInReadingOrder(AppDbContext db, int bookId) =>
        db.BookPuzzles
            .Include(bp => bp.Book)
            .Where(bp => bp.BookId == bookId)
            .InReadingOrder();

    /// <summary>Puzzles eines ÖFFENTLICHEN Kurses — ohne Login (kein User/Zugriffs-Kontext).
    /// Basis dafür, dass ein anonymer Besucher einen als <see cref="Book.IsPublic"/> markierten Kurs über
    /// den Direkt-Link clientseitig durchspielen kann (Fortschritt nur lokal im Browser).
    /// <para><paramref name="skip"/>/<paramref name="take"/> paginieren DB-seitig (stabile Lese-Reihenfolge
    /// Round→Id): der Client holt die erste kleine Seite → sofort spielbar, den Rest im Hintergrund.
    /// Ohne beide Parameter wird (rückwärtskompatibel) das ganze Buch geliefert.</para>
    /// Nicht öffentlich / nicht vorhanden → <see cref="NotFoundException"/> (404).
    /// <para><b>Kalkulationsbücher liefert dieser Pfad NICHT aus</b> (ebenfalls 404): ein
    /// Kalkulationsbuch ist kein Solver-Kurs, und der Solver-Pfad reicht über
    /// <see cref="BookPuzzleService.MapToDto"/> <see cref="BookPuzzle.Moves"/> durch — also genau
    /// die Lösung, die der Kalkulations-Modus auf dem Server behält. Ohne diese Sperre wäre das
    /// <see cref="Book.IsPublic"/>-Flag, das ein Kalkulationskurs für seinen Kurz-Link
    /// (<c>/{slug}</c>) BRAUCHT, zugleich das Tor zur Lösung — siehe
    /// <see cref="CourseAccess.IsCalculationBookAsync"/>.</para></summary>
    public async Task<List<BookPuzzleDto>> GetPublicCoursePuzzlesAsync(int bookId, int? skip = null, int? take = null)
    {
        if (!await _db.Books.AnyAsync(b => b.Id == bookId && b.IsPublic && !b.IsCalculation))
            throw new NotFoundException("Book not found.");
        IQueryable<BookPuzzle> query = PuzzlesWithBookInReadingOrder(_db, bookId);
        if (skip is int s && s > 0) query = query.Skip(s);
        if (take is int t && t > 0) query = query.Take(t);
        var puzzles = await query.ToListAsync();
        return puzzles.Select(BookPuzzleService.MapToDto).ToList();
    }

    /// <summary>Löst einen öffentlichen Kurz-Alias (<see cref="Book.PublicSlug"/>) auf sein Ziel auf —
    /// nur für öffentliche Bücher. Null = unbekannter/nicht-öffentlicher Alias. Basis für die
    /// Kurz-URL <c>/{slug}</c>, die anonym auf den Kurs weiterleitet.
    /// <para>Mitgeliefert wird <see cref="PublicSlugTargetDto.IsCalculation"/>, weil die Kurz-URL sonst
    /// bei einem KALKULATIONSBUCH ins Leere läuft: dessen Stellungen sind Info-Linien und aus allen
    /// Quiz-/Zufallspools ausgeschlossen — der Solver meldete sofort „abgeschlossen". Die Verzweigung
    /// gehört an die Auflösung, weil nur sie das Buch überhaupt kennt.</para></summary>
    public async Task<PublicSlugTargetDto?> ResolvePublicSlugAsync(string slug)
    {
        var s = (slug ?? string.Empty).Trim().ToLowerInvariant();
        if (s.Length == 0) return null;
        return await _db.Books
            .Where(b => b.IsPublic && b.PublicSlug == s)
            .Select(b => new PublicSlugTargetDto { BookId = b.Id, IsCalculation = b.IsCalculation })
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Löst <c>/{slug}/{kapitel}</c> auf: Alias → Buch, Kapitel-NAME → Kapitel. Der Kapitel-Teil der
    /// URL ist der Kapitelname selbst (kein zweites Konzept); verglichen wird getrimmt und ohne
    /// Groß-/Kleinschreibungs-Unterschied. Unbekannter Alias ODER unbekanntes Kapitel → <c>null</c> (404).
    /// <para>Gesucht wird über ALLE Linien des Buchs, auch <see cref="BookPuzzle.IsInfoOnly"/>: in einem
    /// Kalkulationsbuch sind sämtliche Stellungen Info-Linien, eine Suche nur über Quiz-Linien fände
    /// dort überhaupt kein Kapitel.</para>
    /// <para><see cref="PublicSlugChapterDto.ChapterIndex"/> ist der SOLVER-Index (<see cref="ChapterOrder"/>,
    /// nur Quiz-Linien) für die interne Route <c>courses/:bookId/chapter/:index/:mode</c> — <c>null</c>
    /// bei Kalkulationsbüchern (dort filtert der Kalkulations-Modus über den Namen) und bei reinen
    /// Info-Kapiteln, die im Solver gar keinen Index belegen.</para>
    /// </summary>
    public async Task<PublicSlugChapterDto?> ResolvePublicSlugChapterAsync(string slug, string chapter)
    {
        var target = await ResolvePublicSlugAsync(slug);
        if (target == null) return null;

        var wanted = (chapter ?? string.Empty).Trim();
        if (wanted.Length == 0) return null;

        var raw = await _db.BookPuzzles
            .Where(bp => bp.BookId == target.BookId)
            .InReadingOrder()
            .Select(bp => bp.Chapter)
            .ToListAsync();
        var name = raw.Select(NormalizeChapter).FirstOrDefault(n => Matches(n, wanted));
        if (name == null) return null;

        int? index = null;
        if (!target.IsCalculation)
        {
            var solverNames = await GetOrderedChapterNamesAsync(target.BookId);
            var i = solverNames.FindIndex(n => Matches(n, wanted));
            if (i >= 0) index = i;
        }

        return new PublicSlugChapterDto
        {
            BookId = target.BookId,
            IsCalculation = target.IsCalculation,
            Chapter = name,
            ChapterIndex = index,
        };
    }

    /// <summary>Kapitelname aus der URL ↔ gespeicherter Name: getrimmt, ohne Groß-/Kleinschreibung.
    /// „ohne Kapitel" (<c>null</c>) ist über die URL nicht adressierbar (leerer Pfadteil).</summary>
    private static bool Matches(string? stored, string wanted) =>
        stored != null && string.Equals(stored.Trim(), wanted, StringComparison.OrdinalIgnoreCase);

    // PGN-Export (Buch/Kapitel/Linie) liegt seit dem Codereview 2026-09-29 im CoursePgnExportService —
    // mit EINER Kalkulationssperre für alle Wege.

    // „Kurs → Repertoire umwandeln" liegt seit v0.499.4 im CourseRepertoireConversionService —
    // zusammen mit der Gegenrichtung, die vorher im RepertoireController ausgeschrieben stand.

    /// <inheritdoc cref="CourseListQuery.GetCoursesAsync"/>
    public Task<List<CourseListItemDto>> GetCoursesAsync(int userId, bool isAdmin) => _list.GetCoursesAsync(userId, isAdmin);


    /// <summary>Setzt die Themen-Tags eines Kurs-Buchs (gilt buch-global, für alle, die es trainieren).
    /// Nur der Admin (alle Bücher) bzw. der Besitzer eines persönlichen Kurses darf das — die Regel in
    /// <see cref="CourseAccess.LoadManageableAsync"/> (404 nicht zugänglich, 403 nicht berechtigt);
    /// <see cref="DomainValidationException"/> (→400) bei ungültigem Theme-Key.
    /// Leere/nur-ungültige Liste → <c>null</c> (Rückfall auf Default „tactics"). Gibt die effektiven
    /// (ggf. auf Default aufgelösten) Keys zurück.</summary>
    public async Task<List<string>> SetBookThemesAsync(int userId, int bookId, IEnumerable<string> themeKeys, bool isAdmin)
    {
        var book = await CourseAccess.LoadManageableAsync(_db, userId, bookId, isAdmin);

        // Keys validieren + normalisieren (Reihenfolge stabil, dedupliziert). Ungültiger Key → 400.
        var normalized = new List<string>();
        foreach (var raw in themeKeys ?? Enumerable.Empty<string>())
        {
            var key = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (key.Length == 0) continue;
            if (!BookThemeTags.IsValidKey(key))
                throw new DomainValidationException($"Unknown theme '{raw}'.");
            if (!normalized.Contains(key)) normalized.Add(key);
        }

        // Leer bzw. exakt der Default „tactics" → null speichern (spart Sonderfälle, Default greift).
        book.Themes = normalized.Count == 0 || (normalized.Count == 1 && normalized[0] == "tactics")
            ? null
            : string.Join(",", normalized);
        await _db.SaveChangesAsync();
        return BookThemeTags.ParseKeys(book.Themes);
    }

    /// <inheritdoc cref="CourseListQuery.HasAnyAccessAsync"/>
    public Task<bool> HasAnyAccessAsync(int userId, bool isAdmin) => _list.HasAnyAccessAsync(userId, isAdmin);

    // --- Fassade: Solver (CourseSolverService) -----------------------------------------------

    /// <inheritdoc cref="CourseSolverService.GetLineStatusAsync"/>
    public Task<CourseLineStatusDto> GetLineStatusAsync(int userId, int bookId, bool isAdmin) =>
        _solver.GetLineStatusAsync(userId, bookId, isAdmin);

    /// <inheritdoc cref="CourseSolverService.SetSkipInfoLinesAsync"/>
    public Task<bool> SetSkipInfoLinesAsync(int userId, int bookId, bool skip, bool isAdmin) =>
        _solver.SetSkipInfoLinesAsync(userId, bookId, skip, isAdmin);

    /// <inheritdoc cref="CourseSolverService.RetryFailedAsync"/>
    public Task RetryFailedAsync(int userId, int bookId, bool isAdmin) =>
        _solver.RetryFailedAsync(userId, bookId, isAdmin);

    /// <inheritdoc cref="CourseSolverService.GetNextAsync"/>
    public Task<CourseNextPuzzleDto> GetNextAsync(int userId, int bookId, string mode, int? after, int? exclude, bool isAdmin, int? chapterIndex = null) =>
        _solver.GetNextAsync(userId, bookId, mode, after, exclude, isAdmin, chapterIndex);

    /// <inheritdoc cref="CourseSolverService.RecordResultAsync"/>
    public Task<CourseProgressDto> RecordResultAsync(int userId, int bookId, RecordCourseResultDto dto, bool isAdmin) =>
        _solver.RecordResultAsync(userId, bookId, dto, isAdmin);

    /// <inheritdoc cref="CourseSolverService.MarkInfoSeenAsync"/>
    public Task MarkInfoSeenAsync(int userId, int bookId, int bookPuzzleId, bool isAdmin) =>
        _solver.MarkInfoSeenAsync(userId, bookId, bookPuzzleId, isAdmin);

    /// <inheritdoc cref="CourseSolverService.ResetAsync"/>
    public Task<CourseProgressDto> ResetAsync(int userId, int bookId, bool isAdmin) =>
        _solver.ResetAsync(userId, bookId, isAdmin);

    // --- Fassade: Pins + Verknüpfungen (CoursePersonalService) --------------------------------

    /// <inheritdoc cref="CoursePersonalService.PinCourseAsync"/>
    public Task PinCourseAsync(int userId, int bookId, bool isAdmin) => _personal.PinCourseAsync(userId, bookId, isAdmin);

    /// <inheritdoc cref="CoursePersonalService.UnpinCourseAsync"/>
    public Task UnpinCourseAsync(int userId, int bookId) => _personal.UnpinCourseAsync(userId, bookId);

    /// <inheritdoc cref="CoursePersonalService.LinkCoursesAsync"/>
    public Task LinkCoursesAsync(int userId, int bookId, int linkedBookId, bool isAdmin) =>
        _personal.LinkCoursesAsync(userId, bookId, linkedBookId, isAdmin);

    /// <inheritdoc cref="CoursePersonalService.UnlinkCourseAsync"/>
    public Task UnlinkCourseAsync(int userId, int bookId) => _personal.UnlinkCourseAsync(userId, bookId);

    /// <inheritdoc cref="CoursePersonalService.GetLinkAsync"/>
    public Task<CourseLinkDto> GetLinkAsync(int userId, int bookId, bool isAdmin) => _personal.GetLinkAsync(userId, bookId, isAdmin);

    // --- Fassade: Teilen (CourseSharingService) ----------------------------------------------

    /// <inheritdoc cref="CourseSharingService.ShareCourseAsync"/>
    public Task<CourseShareResultDto> ShareCourseAsync(int userId, int bookId, List<int> recipientUserIds, bool isAdmin) =>
        _sharing.ShareCourseAsync(userId, bookId, recipientUserIds, isAdmin);

    /// <inheritdoc cref="CourseSharingService.GetShareRecipientsAsync"/>
    public Task<List<CourseShareRecipientDto>> GetShareRecipientsAsync(int userId, int bookId) =>
        _sharing.GetShareRecipientsAsync(userId, bookId);

    /// <inheritdoc cref="CourseSharingService.UnshareCourseAsync"/>
    public Task UnshareCourseAsync(int userId, int bookId, int recipientId) =>
        _sharing.UnshareCourseAsync(userId, bookId, recipientId);

    // --- Fassade: persönliche Kurse anlegen/löschen (CourseCreationService) -------------------

    /// <inheritdoc cref="CourseCreationService.UploadPersonalCourseAsync"/>
    public Task<CourseListItemDto> UploadPersonalCourseAsync(int userId, string originalFileName, string pgn, string? displayName,
        CancellationToken ct = default, string? chessableBid = null) =>
        _creation.UploadPersonalCourseAsync(userId, originalFileName, pgn, displayName, ct, chessableBid);

    /// <inheritdoc cref="CourseCreationService.CreatePersonalCourseAsync"/>
    public Task<CourseListItemDto> CreatePersonalCourseAsync(int userId, string displayName, CancellationToken ct = default) =>
        _creation.CreatePersonalCourseAsync(userId, displayName, ct);

    /// <inheritdoc cref="CourseCreationService.DeletePersonalCourseAsync"/>
    public Task DeletePersonalCourseAsync(int userId, int bookId) => _creation.DeletePersonalCourseAsync(userId, bookId);
}
