using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// „Kurse" = importierte Bücher, die ein User puzzleweise durcharbeitet (Fortschritt user-bezogen
/// in der DB; Modus sequential/random bestimmt nur die Reihenfolge). Geschäftslogik vormals inline
/// im CourseController. Sichtbarkeit/Zugriff wird je Buch erzwungen — kein Zugriff → 404 via
/// <see cref="KeyNotFoundException"/> (Controller bildet auf HTTP ab). `isAdmin` reicht der Controller
/// herein (HTTP-Concern).
/// </summary>
public class CourseService
{
    private readonly AppDbContext _db;
    private readonly ILogger<CourseService> _logger;
    private readonly PgnImportService _pgnImport;
    private readonly BookAdminService _bookAdmin;
    private readonly RepertoireService _repertoire;
    private readonly FriendService _friends;
    private readonly NotificationService _notifications;

    // FriendService/NotificationService sind optional, damit bestehende Test-Konstruktionen ohne
    // Änderung kompilieren; im DI-Container werden immer die echten Instanzen injiziert. Ohne sie
    // werden sie aus dem DbContext aufgebaut (beide haben nur schlanke Abhängigkeiten).
    public CourseService(AppDbContext db, ILogger<CourseService> logger, PgnImportService pgnImport, BookAdminService bookAdmin, RepertoireService repertoire, FriendService? friends = null, NotificationService? notifications = null)
    {
        _db = db;
        _logger = logger;
        _pgnImport = pgnImport;
        _bookAdmin = bookAdmin;
        _repertoire = repertoire;
        _notifications = notifications ?? new NotificationService(db);
        _friends = friends ?? new FriendService(db, _notifications);
    }

    private static string NormalizeMode(string? mode) =>
        (mode ?? string.Empty).Trim().ToLowerInvariant() == "random" ? "random" : "sequential";

    private static int Percent(int solved, int total) =>
        total <= 0 ? 0 : (int)Math.Round(100.0 * Math.Min(solved, total) / total);

    /// <summary>Darf der User dieses (existierende) Buch als Kurs sehen/bearbeiten?</summary>
    /// <summary>Id der System-Gruppe „Everyone" (jeder implizit Mitglied), oder <c>null</c> falls (noch)
    /// keine existiert. Eine Buch-Freigabe an diese Gruppe gilt für alle Nutzer.</summary>
    private Task<int?> EveryoneGroupIdAsync() =>
        _db.Groups.Where(g => g.IsEveryone).Select(g => (int?)g.Id).FirstOrDefaultAsync();

    public async Task<bool> CanAccessAsync(int userId, int bookId, bool isAdmin)
    {
        if (!await _db.Books.AnyAsync(b => b.Id == bookId)) return false;
        if (isAdmin) return true;
        // Öffentlicher Kurs: für JEDEN (auch eingeloggt ohne Gruppen-Freigabe) über den Direkt-Link
        // nutzbar — der eingeloggte Nutzer bekommt dabei serverseitigen Fortschritt.
        if (await _db.Books.AnyAsync(b => b.Id == bookId && b.IsPublic)) return true;
        // Persönliches Buch des Users (z. B. eigener Chessable-Import) ist immer sichtbar.
        if (await _db.Books.AnyAsync(b => b.Id == bookId && b.OwnerUserId == userId)) return true;
        // Ein anderer Nutzer hat mir diesen Kurs direkt geteilt.
        if (await _db.CourseShares.AnyAsync(cs => cs.BookId == bookId && cs.RecipientId == userId)) return true;
        var everyoneId = await EveryoneGroupIdAsync();
        return await _db.BookGroupAccesses.AnyAsync(a => a.BookId == bookId &&
            (a.GroupId == everyoneId ||
             _db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == a.GroupId)));
    }

    private async Task EnsureAccessAsync(int userId, int bookId, bool isAdmin)
    {
        if (!await CanAccessAsync(userId, bookId, isAdmin))
            throw new KeyNotFoundException("Book not found.");
    }

    /// <summary>Normalisiert einen rohen Kapitelwert: leer/Whitespace → <c>null</c> (Sammel-„ohne Kapitel").</summary>
    private static string? NormalizeChapter(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw;

    /// <summary>Gültige Buch-Themen-Keys (= <see cref="Models.ChessableTheme"/>-Namen, kleingeschrieben).</summary>
    private static readonly string[] ValidThemeKeys = { "opening", "middlegame", "endgame", "tactics", "other" };

    /// <summary>Parst die CSV-Themen-Tags eines Buchs zu einer Key-Liste (Reihenfolge stabil, dedupliziert).
    /// Leer/unset → Default <c>["tactics"]</c> (jedes Buch ist standardmäßig Taktik).</summary>
    private static List<string> ParseThemeKeys(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return new List<string> { "tactics" };
        var seen = new List<string>();
        foreach (var raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var key = raw.ToLowerInvariant();
            if (Array.IndexOf(ValidThemeKeys, key) >= 0 && !seen.Contains(key)) seen.Add(key);
        }
        return seen.Count > 0 ? seen : new List<string> { "tactics" };
    }

    /// <summary>
    /// Die eindeutigen Kapitel eines Buchs in Lesereihenfolge (= aufsteigend nach kleinstem
    /// <see cref="BookPuzzle.Round"/> je Kapitel; die Chessable-Zeilennummer ist die maßgebliche
    /// Quell-Reihenfolge — die DB-<c>Id</c> nicht, da nachträglich re-gefetchte Linien höhere Ids
    /// bekommen). Index in dieser Liste = stabiler Kapitel-Selektor; muss mit der Reihenfolge in
    /// <see cref="GetChaptersAsync"/> übereinstimmen.
    /// </summary>
    private async Task<List<string?>> GetOrderedChapterNamesAsync(int bookId)
    {
        var rows = await _db.BookPuzzles
            .Where(bp => bp.BookId == bookId)
            .GroupBy(bp => bp.Chapter)
            .Select(g => new { Chapter = g.Key, MinRound = g.Min(bp => bp.Round), MinId = g.Min(bp => bp.Id) })
            .ToListAsync();
        // null und "" (und Whitespace) auf dieselbe „ohne Kapitel"-Gruppe zusammenführen; je Gruppe
        // das kleinste Round (dann Id) für die Lesereihenfolge.
        return rows
            .Select(r => new { Name = NormalizeChapter(r.Chapter), r.MinRound, r.MinId })
            .GroupBy(x => x.Name)
            .Select(g => new { Name = g.Key, MinRound = g.Min(x => x.MinRound), MinId = g.Min(x => x.MinId) })
            .OrderBy(x => x.MinRound, StringComparer.Ordinal).ThenBy(x => x.MinId)
            .Select(x => x.Name)
            .ToList();
    }

    /// <summary>Schränkt eine Puzzle-Query auf ein Kapitel ein (null = Sammel-„ohne Kapitel").</summary>
    private static IQueryable<BookPuzzle> FilterByChapter(IQueryable<BookPuzzle> q, string? chapterName) =>
        chapterName == null
            ? q.Where(bp => bp.Chapter == null || bp.Chapter == "")
            : q.Where(bp => bp.Chapter == chapterName);

    /// <summary>Löst einen Kapitel-Index in den (normalisierten) Kapitelnamen auf. Ungültiger/keiner
    /// Index → <c>(null, false)</c> = buchweit (kein Kapitel-Scope).</summary>
    private async Task<(string? Name, bool Scoped)> ResolveChapterAsync(int bookId, int? chapterIndex)
    {
        if (!chapterIndex.HasValue) return (null, false);
        var names = await GetOrderedChapterNamesAsync(bookId);
        return chapterIndex.Value >= 0 && chapterIndex.Value < names.Count
            ? (names[chapterIndex.Value], true)
            : (null, false);
    }

    /// <summary>Die Kapitel eines (zugänglichen) Buchs in Lesereihenfolge inkl. nutzerbezogenem Fortschritt.</summary>
    public async Task<List<CourseChapterDto>> GetChaptersAsync(int userId, int bookId, bool isAdmin)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);

        // Info-/Erklärlinien sind kein Quiz → zählen nicht zum Kapitel-Fortschritt.
        var puzzles = await _db.BookPuzzles
            .Where(bp => bp.BookId == bookId && !bp.IsInfoOnly)
            .OrderBy(bp => bp.Round).ThenBy(bp => bp.Id)
            .Select(bp => new { bp.Id, bp.Chapter })
            .ToListAsync();
        var solvedSet = (await _db.CoursePuzzleResults
            .Where(cr => cr.UserId == userId && cr.BookId == bookId)
            .Select(cr => cr.BookPuzzleId)
            .ToListAsync()).ToHashSet();

        // In Lesereihenfolge gruppieren (erste-Erscheinung = Reihenfolge der nach Round sortierten Puzzles).
        var groups = new List<(string? Name, int Count, int Solved)>();
        var indexByKey = new Dictionary<string, int>();
        const string noneKey = " __none__";
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

        return groups.Select((g, idx) => new CourseChapterDto
        {
            Index = idx,
            Name = g.Name,
            PuzzleCount = g.Count,
            SolvedCount = Math.Min(g.Solved, g.Count),
            ProgressPercent = Percent(g.Solved, g.Count),
        }).ToList();
    }

    /// <summary>Pro-Linien-Bearbeitungsstatus eines (zugänglichen) Buchs für die „Linien durchsehen"-Ansicht:
    /// gelöste (✓) und versucht-aber-nicht-gelöste (✗) Linien des Users. Gelöst = aktueller
    /// <see cref="CoursePuzzleResult"/>-Stand (respektiert Reset); „gescheitert" = mind. ein
    /// <see cref="CourseAttempt"/> vorhanden, aber (aktuell) nicht in der Gelöst-Menge.</summary>
    public async Task<CourseLineStatusDto> GetLineStatusAsync(int userId, int bookId, bool isAdmin)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);
        var solved = (await _db.CoursePuzzleResults
            .Where(cr => cr.UserId == userId && cr.BookId == bookId)
            .Select(cr => cr.BookPuzzleId)
            .ToListAsync()).ToHashSet();
        var attempted = (await _db.CourseAttempts
            .Where(ca => ca.UserId == userId && ca.BookId == bookId)
            .Select(ca => ca.BookPuzzleId)
            .Distinct()
            .ToListAsync()).ToHashSet();
        return new CourseLineStatusDto
        {
            SolvedIds = solved.ToList(),
            FailedIds = attempted.Where(id => !solved.Contains(id)).ToList(),
        };
    }

    /// <summary>Alle Puzzles eines (zugänglichen) Buchs am Stück — für das Offline-Speichern.</summary>
    public async Task<List<BookPuzzleDto>> GetAllPuzzlesAsync(int userId, int bookId, bool isAdmin)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);
        var puzzles = await _db.BookPuzzles
            .Include(bp => bp.Book)
            .Where(bp => bp.BookId == bookId)
            .OrderBy(bp => bp.Round).ThenBy(bp => bp.Id)
            .ToListAsync();
        return puzzles.Select(BookPuzzleService.MapToDto).ToList();
    }

    /// <summary>Alle Puzzles eines ÖFFENTLICHEN Kurses am Stück — ohne Login (kein User/Zugriffs-Kontext).
    /// Basis dafür, dass ein anonymer Besucher einen als <see cref="Book.IsPublic"/> markierten Kurs über
    /// den Direkt-Link komplett clientseitig durchspielen kann (Fortschritt nur lokal im Browser).
    /// Nicht öffentlich / nicht vorhanden → <see cref="KeyNotFoundException"/> (404).</summary>
    public async Task<List<BookPuzzleDto>> GetPublicCoursePuzzlesAsync(int bookId)
    {
        if (!await _db.Books.AnyAsync(b => b.Id == bookId && b.IsPublic))
            throw new KeyNotFoundException("Book not found.");
        var puzzles = await _db.BookPuzzles
            .Include(bp => bp.Book)
            .Where(bp => bp.BookId == bookId)
            .OrderBy(bp => bp.Round).ThenBy(bp => bp.Id)
            .ToListAsync();
        return puzzles.Select(BookPuzzleService.MapToDto).ToList();
    }

    /// <summary>Exportiert ein (zugängliches) Buch als PGN. Liefert PGN-Text + Dateiname.
    /// <para>Bevorzugt das gespeicherte Roh-PGN (<see cref="Book.SourcePgn"/>) — es enthält die
    /// vollständige Originalstruktur inkl. <b>Varianten und Kommentaren</b>. Nur für Altbestand ohne
    /// Quelle (JSON-Import / vor der SourcePgn-Pipeline) wird ersatzweise aus den gespeicherten
    /// <see cref="BookPuzzle"/> rekonstruiert (Hauptlinie + Zug-Kommentare, aber ohne Varianten —
    /// die liegen nicht in der DB).</para></summary>
    public async Task<(string Pgn, string FileName)> GetBookPgnAsync(int userId, int bookId, bool isAdmin)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);
        var book = await _db.Books.FirstAsync(b => b.Id == bookId);
        var safe = new string((book.DisplayName ?? "course").Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        var fileName = $"{(string.IsNullOrWhiteSpace(safe) ? "course" : safe)}.pgn";

        // Roh-PGN vorhanden → verbatim ausliefern (Varianten + Kommentare bleiben erhalten).
        if (!string.IsNullOrWhiteSpace(book.SourcePgn))
            return (book.SourcePgn, fileName);

        // Fallback (Altbestand ohne Quelle): aus den BookPuzzles rekonstruieren (Round-Lesereihenfolge).
        var puzzles = await _db.BookPuzzles
            .Where(bp => bp.BookId == bookId)
            .OrderBy(bp => bp.Round).ThenBy(bp => bp.Id)
            .ToListAsync();
        return (CoursePgnExporter.ToPgn(book.DisplayName, puzzles), fileName);
    }

    /// <summary>„Kurs → Repertoire umwandeln" (Verschieben): legt aus dem Kurs-PGN (inkl. Varianten/
    /// Kommentaren, wenn <see cref="Book.SourcePgn"/> vorhanden) ein neues Repertoire des Users an und
    /// ENTFERNT den Original-Kurs, sofern es ein persönlicher (eigener) Kurs ist
    /// (<c>Book.OwnerUserId == userId</c>). Geteilte Gruppen-/Admin-Bücher werden NICHT gelöscht (gehören
    /// dem User nicht) — dann bleibt der Kurs bestehen. Zugriff wird geprüft (kein Zugriff → 404).</summary>
    public async Task<RepertoireDto> ConvertToRepertoireAsync(int userId, int bookId, bool isAdmin)
    {
        var (pgn, fileName) = await GetBookPgnAsync(userId, bookId, isAdmin); // prüft Zugriff
        var book = await _db.Books.FirstAsync(b => b.Id == bookId);
        var repo = await _repertoire.CreateFromPgnAsync(userId, book.DisplayName ?? "Kurs", fileName, pgn);
        // Verschieben statt Kopieren: eigenen Kurs nach erfolgreicher Umwandlung entfernen.
        if (book.OwnerUserId == userId)
            await _bookAdmin.DeleteBookAsync(bookId);
        return repo;
    }

    /// <summary>Sichtbare Bücher als Kurse inkl. Fortschritt des Users (Admin: alle).</summary>
    public async Task<List<CourseListItemDto>> GetCoursesAsync(int userId, bool isAdmin)
    {
        IQueryable<Book> booksQuery = _db.Books;
        if (!isAdmin)
        {
            var everyoneId = await EveryoneGroupIdAsync();
            booksQuery = booksQuery.Where(b => b.OwnerUserId == userId
                || _db.CourseShares.Any(cs => cs.BookId == b.Id && cs.RecipientId == userId)
                || _db.BookGroupAccesses.Any(a => a.BookId == b.Id &&
                    (a.GroupId == everyoneId ||
                     _db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == a.GroupId))));
        }

        var books = await booksQuery
            .OrderBy(b => b.DisplayName)
            .Select(b => new
            {
                b.Id, b.FileName, b.DisplayName, b.Difficulty, b.Rating, b.Tags, b.Description,
                b.OwnerUserId, b.Themes,
            })
            .ToListAsync();

        // Puzzle-Anzahl (ohne Info-/Erklärlinien) je Buch in EINER gruppierten Query — statt einer
        // korrelierten Unterabfrage pro Buch (bei Admins mit hunderten Büchern sonst mehrere Sekunden).
        var puzzleCountByBook = await _db.BookPuzzles
            .Where(bp => !bp.IsInfoOnly)
            .GroupBy(bp => bp.BookId)
            .Select(g => new { BookId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BookId, x => x.Count);

        var solvedByBook = await _db.CoursePuzzleResults
            .Where(cr => cr.UserId == userId)
            .GroupBy(cr => cr.BookId)
            .Select(g => new { BookId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BookId, x => x.Count);

        var progressByBook = await _db.CourseProgresses
            .Where(cp => cp.UserId == userId)
            .ToDictionaryAsync(cp => cp.BookId, cp => new { cp.LastMode, cp.UpdatedAt });

        var pinnedBookIds = (await _db.CoursePins
            .Where(p => p.UserId == userId)
            .Select(p => p.BookId)
            .ToListAsync()).ToHashSet();

        // Kurse, die ANDERE mit mir geteilt haben (bookId → Benutzername des Teilenden) — für die
        // Sektion „Mit mir geteilt" + das „von X"-Badge.
        var sharedByBook = (await _db.CourseShares
            .Where(cs => cs.RecipientId == userId)
            .Select(cs => new { cs.BookId, OwnerName = cs.Owner.Username })
            .ToListAsync())
            .GroupBy(x => x.BookId)
            .ToDictionary(g => g.Key, g => g.First().OwnerName);

        // Persönliche Kurs-Verknüpfungen (Buch↔Workbook): bookId → Partner-bookId + dessen Name.
        var linkByBook = (await _db.CourseLinks
            .Where(l => l.UserId == userId)
            .Select(l => new { l.BookId, l.LinkedBookId })
            .ToListAsync())
            .GroupBy(x => x.BookId)
            .ToDictionary(g => g.Key, g => g.First().LinkedBookId);
        var linkedNameById = linkByBook.Count == 0 ? new Dictionary<int, string>()
            : await _db.Books.Where(b => linkByBook.Values.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.DisplayName);

        return books.Select(b =>
        {
            var puzzleCount = puzzleCountByBook.TryGetValue(b.Id, out var pc) ? pc : 0;
            var solved = solvedByBook.TryGetValue(b.Id, out var c) ? c : 0;
            var progress = progressByBook.TryGetValue(b.Id, out var p) ? p : null;
            return new CourseListItemDto
            {
                BookId = b.Id,
                FileName = b.FileName,
                DisplayName = b.DisplayName,
                Difficulty = b.Difficulty,
                Rating = b.Rating,
                Tags = b.Tags,
                Description = b.Description,
                PuzzleCount = puzzleCount,
                SolvedCount = Math.Min(solved, puzzleCount),
                ProgressPercent = Percent(solved, puzzleCount),
                LastMode = progress?.LastMode,
                LastActivityAt = progress?.UpdatedAt,
                IsOwned = b.OwnerUserId == userId,
                IsPinned = pinnedBookIds.Contains(b.Id),
                IsShared = sharedByBook.ContainsKey(b.Id),
                SharedByUsername = sharedByBook.TryGetValue(b.Id, out var sharedBy) ? sharedBy : null,
                LinkedBookId = linkByBook.TryGetValue(b.Id, out var linkedId) ? linkedId : null,
                LinkedDisplayName = linkByBook.TryGetValue(b.Id, out var lid) && linkedNameById.TryGetValue(lid, out var ln) ? ln : null,
                Themes = ParseThemeKeys(b.Themes),
            };
        }).ToList();
    }

    /// <summary>Pinnt einen (zugänglichen) Kurs fürs Dashboard des Users an. Idempotent —
    /// ein zweiter Aufruf ändert nichts. Kein Zugriff → <see cref="KeyNotFoundException"/> (404).</summary>
    public async Task PinCourseAsync(int userId, int bookId, bool isAdmin)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);
        if (await _db.CoursePins.AnyAsync(p => p.UserId == userId && p.BookId == bookId)) return;
        _db.CoursePins.Add(new CoursePin { UserId = userId, BookId = bookId, PinnedAt = DateTime.UtcNow });
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) { /* Race: paralleler Pin desselben (User,Buch) → Unique-Index, ignorieren. */ }
    }

    /// <summary>Löst einen angepinnten Kurs wieder. Idempotent (kein Pin → No-op).</summary>
    public async Task UnpinCourseAsync(int userId, int bookId)
    {
        var pin = await _db.CoursePins.FirstOrDefaultAsync(p => p.UserId == userId && p.BookId == bookId);
        if (pin == null) return;
        _db.CoursePins.Remove(pin);
        await _db.SaveChangesAsync();
    }

    /// <summary>Setzt die Themen-Tags eines Kurs-Buchs (gilt buch-global, für alle, die es trainieren).
    /// Nur der Admin (alle Bücher) bzw. der Besitzer eines persönlichen Kurses darf das —
    /// <see cref="UnauthorizedAccessException"/> (→403) sonst; <see cref="KeyNotFoundException"/> (→404)
    /// wenn nicht zugänglich; <see cref="InvalidOperationException"/> (→400) bei ungültigem Theme-Key.
    /// Leere/nur-ungültige Liste → <c>null</c> (Rückfall auf Default „tactics"). Gibt die effektiven
    /// (ggf. auf Default aufgelösten) Keys zurück.</summary>
    public async Task<List<string>> SetBookThemesAsync(int userId, int bookId, IEnumerable<string> themeKeys, bool isAdmin)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == bookId)
            ?? throw new KeyNotFoundException("Book not found.");
        if (!isAdmin && book.OwnerUserId != userId)
            throw new UnauthorizedAccessException("Only an admin or the course owner can set themes.");

        // Keys validieren + normalisieren (Reihenfolge stabil, dedupliziert). Ungültiger Key → 400.
        var normalized = new List<string>();
        foreach (var raw in themeKeys ?? Enumerable.Empty<string>())
        {
            var key = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (key.Length == 0) continue;
            if (Array.IndexOf(ValidThemeKeys, key) < 0)
                throw new InvalidOperationException($"Unknown theme '{raw}'.");
            if (!normalized.Contains(key)) normalized.Add(key);
        }

        // Leer bzw. exakt der Default „tactics" → null speichern (spart Sonderfälle, Default greift).
        book.Themes = normalized.Count == 0 || (normalized.Count == 1 && normalized[0] == "tactics")
            ? null
            : string.Join(",", normalized);
        await _db.SaveChangesAsync();
        return ParseThemeKeys(book.Themes);
    }

    /// <summary>Hat der User Zugriff auf mindestens einen Kurs? (Basis für die Menü-Sichtbarkeit.)</summary>
    public async Task<bool> HasAnyAccessAsync(int userId, bool isAdmin)
    {
        if (isAdmin)
            return await _db.Books.AnyAsync();
        if (await _db.Books.AnyAsync(b => b.OwnerUserId == userId)) return true;
        if (await _db.CourseShares.AnyAsync(cs => cs.RecipientId == userId)) return true;
        var everyoneId = await EveryoneGroupIdAsync();
        // Freigabe an „Everyone" gilt universell, sobald überhaupt ein Buch freigegeben ist.
        if (everyoneId != null && await _db.BookGroupAccesses.AnyAsync(a => a.GroupId == everyoneId)) return true;
        return await _db.BookGroupAccesses.AnyAsync(a =>
            _db.UserGroups.Any(ug => ug.UserId == userId && ug.GroupId == a.GroupId));
    }

    // --- Kurs mit ausgewählten Personen teilen -----------------------------------------------
    // Nur der Besitzer eines PERSÖNLICHEN Kurses (Book.OwnerUserId == userId) darf ihn teilen; die
    // Empfänger müssen befreundet sein (wie bei Puzzle-Challenges) — sonst wird der Empfänger mit
    // Grund übersprungen. Gruppen-/globale Bücher werden über die Gruppen-Freigabe geteilt, nicht hier.

    /// <summary>Wirft <see cref="KeyNotFoundException"/> (→404), wenn das Buch fehlt, und
    /// <see cref="UnauthorizedAccessException"/> (→403), wenn der User nicht der Besitzer ist.</summary>
    private async Task<Book> EnsureOwnedBookAsync(int userId, int bookId)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == bookId)
            ?? throw new KeyNotFoundException("Book not found.");
        if (book.OwnerUserId != userId)
            throw new UnauthorizedAccessException("Only the owner can share this course.");
        return book;
    }

    /// <summary>Teilt einen eigenen Kurs mit mehreren (befreundeten) Nutzern. Idempotent: bereits
    /// geteilte Empfänger werden übersprungen (Grund <c>duplicate</c>), Fremde als <c>not_friends</c>,
    /// unbekannte als <c>not_found</c>, man selbst als <c>self</c>. Legt je neuem Empfänger eine
    /// In-App-Benachrichtigung an.</summary>
    public async Task<CourseShareResultDto> ShareCourseAsync(int userId, int bookId, List<int> recipientUserIds, bool isAdmin)
    {
        var book = await EnsureOwnedBookAsync(userId, bookId);

        var result = new CourseShareResultDto();
        var distinct = recipientUserIds.Distinct().ToList();
        if (distinct.Count == 0) return result;

        var existing = (await _db.AppUsers.Where(u => distinct.Contains(u.Id)).Select(u => u.Id).ToListAsync()).ToHashSet();
        var friendIds = await _friends.GetAcceptedFriendIdsAsync(userId, distinct);
        var alreadyShared = (await _db.CourseShares
            .Where(cs => cs.BookId == bookId && distinct.Contains(cs.RecipientId))
            .Select(cs => cs.RecipientId)
            .ToListAsync()).ToHashSet();

        var toNotify = new List<int>();
        foreach (var rid in distinct)
        {
            if (rid == userId) { result.Skipped.Add(new CourseShareSkipDto { UserId = rid, Reason = "self" }); continue; }
            if (!existing.Contains(rid)) { result.Skipped.Add(new CourseShareSkipDto { UserId = rid, Reason = "not_found" }); continue; }
            // Admins dürfen auch an Nicht-Freunde teilen; normale Nutzer nur an bestätigte Freunde.
            if (!isAdmin && !friendIds.Contains(rid)) { result.Skipped.Add(new CourseShareSkipDto { UserId = rid, Reason = "not_friends" }); continue; }
            if (alreadyShared.Contains(rid)) { result.Skipped.Add(new CourseShareSkipDto { UserId = rid, Reason = "duplicate" }); continue; }

            _db.CourseShares.Add(new CourseShare { BookId = bookId, OwnerId = userId, RecipientId = rid, SharedAt = DateTime.UtcNow });
            toNotify.Add(rid);
            result.Shared++;
        }

        if (result.Shared > 0)
        {
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                // Race: derselbe (Buch, Empfänger) parallel geteilt → Unique-Index. Idempotent behandeln.
                _db.ChangeTracker.Clear();
                return await ShareCourseAsync(userId, bookId, recipientUserIds, isAdmin);
            }

            var ownerName = await _db.AppUsers.Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync() ?? "?";
            await _notifications.CreateManyAsync(toNotify, NotificationType.CourseShared,
                new Dictionary<string, string> { ["username"] = ownerName, ["courseName"] = book.DisplayName }, "/courses");
        }

        return result;
    }

    /// <summary>Mit welchen Nutzern ist dieser eigene Kurs aktuell geteilt? (Für den Teilen-Dialog.)</summary>
    public async Task<List<CourseShareRecipientDto>> GetShareRecipientsAsync(int userId, int bookId)
    {
        await EnsureOwnedBookAsync(userId, bookId);
        return await _db.CourseShares
            .Where(cs => cs.BookId == bookId && cs.OwnerId == userId)
            .OrderBy(cs => cs.SharedAt)
            .Select(cs => new CourseShareRecipientDto
            {
                UserId = cs.RecipientId,
                Username = cs.Recipient.Username,
                DisplayName = cs.Recipient.Profile != null ? cs.Recipient.Profile.DisplayName : null,
                SharedAt = cs.SharedAt,
            })
            .ToListAsync();
    }

    /// <summary>Nimmt die Freigabe eines eigenen Kurses für einen Empfänger zurück (idempotent).
    /// Nur der Besitzer darf das; fremdes/nicht existierendes Buch → 403/404.</summary>
    public async Task UnshareCourseAsync(int userId, int bookId, int recipientId)
    {
        await EnsureOwnedBookAsync(userId, bookId);
        var share = await _db.CourseShares.FirstOrDefaultAsync(cs =>
            cs.BookId == bookId && cs.OwnerId == userId && cs.RecipientId == recipientId);
        if (share == null) return;
        _db.CourseShares.Remove(share);
        await _db.SaveChangesAsync();
    }

    // --- Kurse verknüpfen (Buch ↔ Workbook), persönlich, für den Schnellwechsel ---------------

    /// <summary>Verknüpft zwei (für den User zugängliche) Kurse symmetrisch. Ersetzt etwaige
    /// bestehende Verknüpfungen BEIDER Bücher (je Buch max. ein Partner). 404 wenn ein Buch nicht
    /// zugänglich ist, 400 wenn beide identisch sind.</summary>
    public async Task LinkCoursesAsync(int userId, int bookId, int linkedBookId, bool isAdmin)
    {
        if (bookId == linkedBookId)
            throw new InvalidOperationException("Cannot link a course to itself.");
        if (!await CanAccessAsync(userId, bookId, isAdmin) || !await CanAccessAsync(userId, linkedBookId, isAdmin))
            throw new KeyNotFoundException("Book not found.");

        // Alte Verknüpfungen entfernen, die eines der beiden Bücher betreffen (1:1 je Buch).
        var stale = await _db.CourseLinks
            .Where(l => l.UserId == userId &&
                (l.BookId == bookId || l.LinkedBookId == bookId || l.BookId == linkedBookId || l.LinkedBookId == linkedBookId))
            .ToListAsync();
        if (stale.Count > 0) _db.CourseLinks.RemoveRange(stale);

        var now = DateTime.UtcNow;
        _db.CourseLinks.Add(new CourseLink { UserId = userId, BookId = bookId, LinkedBookId = linkedBookId, CreatedAt = now });
        _db.CourseLinks.Add(new CourseLink { UserId = userId, BookId = linkedBookId, LinkedBookId = bookId, CreatedAt = now });
        await _db.SaveChangesAsync();
    }

    /// <summary>Hebt die Verknüpfung eines Kurses auf (entfernt beide symmetrischen Zeilen). Idempotent.</summary>
    public async Task UnlinkCourseAsync(int userId, int bookId)
    {
        var rows = await _db.CourseLinks
            .Where(l => l.UserId == userId && (l.BookId == bookId || l.LinkedBookId == bookId))
            .ToListAsync();
        if (rows.Count == 0) return;
        _db.CourseLinks.RemoveRange(rows);
        await _db.SaveChangesAsync();
    }

    /// <summary>Der aktuell verknüpfte Partner-Kurs eines (zugänglichen) Buchs — für den
    /// Schnellwechsel im Solver. Liefert leere Felder, wenn keine Verknüpfung besteht.</summary>
    public async Task<CourseLinkDto> GetLinkAsync(int userId, int bookId, bool isAdmin)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);
        var linkedId = await _db.CourseLinks
            .Where(l => l.UserId == userId && l.BookId == bookId)
            .Select(l => (int?)l.LinkedBookId)
            .FirstOrDefaultAsync();
        if (linkedId == null) return new CourseLinkDto();
        var name = await _db.Books.Where(b => b.Id == linkedId.Value).Select(b => b.DisplayName).FirstOrDefaultAsync();
        return new CourseLinkDto { LinkedBookId = linkedId, LinkedDisplayName = name };
    }

    /// <summary>
    /// Legt aus einem hochgeladenen PGN einen persönlichen Kurs des Users an (eigenes <see cref="Book"/>
    /// mit <c>OwnerUserId</c>, sofort nur für diesen User als Kurs sichtbar). Wiederverwendet die
    /// Import-Pipeline (<see cref="PgnImportService.ImportFileAsync"/>); der interne Dateiname ist
    /// pro-User eindeutig und kollisionsfrei, der Anzeigename kommt vom User (oder dem Dateinamen).
    /// Kind = Study → die Kurszeit zählt in die Trainingsziel-Kategorie „Buch/Kurs".
    /// </summary>
    public async Task<CourseListItemDto> UploadPersonalCourseAsync(int userId, string originalFileName, string pgn, string? displayName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pgn) || !RepertoireService.LooksLikePgn(pgn))
            throw new InvalidOperationException("The file does not look like a valid PGN.");

        // Pro-User-eindeutiger interner Dateiname (NICHT der Anzeigename) → kollisionsfrei mit
        // globalen Büchern und Chessable-Importen (chessable-u{userId}-{bid}.pgn).
        var fileName = $"user-u{userId}-{Guid.NewGuid():N}.pgn";
        var res = await _pgnImport.ImportFileAsync(fileName, pgn, ct);

        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == res.BookId, ct)
            ?? throw new InvalidOperationException("Import failed.");

        // Nichts Quiz-bares importiert → leeres Buch wieder entfernen statt einen leeren Kurs anzulegen.
        var puzzleCount = await _db.BookPuzzles.CountAsync(bp => bp.BookId == book.Id && !bp.IsInfoOnly, ct);
        if (puzzleCount == 0)
        {
            _db.BookPuzzles.RemoveRange(_db.BookPuzzles.Where(bp => bp.BookId == book.Id));
            _db.Books.Remove(book);
            await _db.SaveChangesAsync(ct);
            throw new InvalidOperationException("No playable lines found in the PGN.");
        }

        var name = string.IsNullOrWhiteSpace(displayName)
            ? PgnImportService.CleanDisplayName(originalFileName)
            : displayName.Trim();
        if (name.Length > 200) name = name[..200];

        book.OwnerUserId = userId;
        book.DisplayName = name;
        book.Kind = BookKind.Study;
        book.Tags = "personal";
        book.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new CourseListItemDto
        {
            BookId = book.Id,
            FileName = book.FileName,
            DisplayName = book.DisplayName,
            Difficulty = book.Difficulty,
            Rating = book.Rating,
            Tags = book.Tags,
            Description = book.Description,
            PuzzleCount = puzzleCount,
            SolvedCount = 0,
            ProgressPercent = 0,
            LastMode = null,
            LastActivityAt = null,
            IsOwned = true,
        };
    }

    /// <summary>Löscht einen eigenen (selbst hochgeladenen bzw. importierten) Kurs des Users samt
    /// Fortschritt/Puzzles. Nur der Besitzer darf löschen; fremde/globale Bücher → 404.</summary>
    public async Task DeletePersonalCourseAsync(int userId, int bookId)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == bookId);
        if (book is null || book.OwnerUserId != userId)
            throw new KeyNotFoundException("Book not found.");
        await _bookAdmin.DeleteBookAsync(bookId);
    }

    /// <summary>
    /// Nächstes ungelöstes Puzzle des Kurses. sequential: Buchreihenfolge (Id), mit <paramref name="after"/>
    /// das nächste danach; random: zufällig, <paramref name="exclude"/> vermeidet direkte Wiederholung.
    /// Aktualisiert den zuletzt genutzten Modus.
    /// </summary>
    public async Task<CourseNextPuzzleDto> GetNextAsync(int userId, int bookId, string mode, int? after, int? exclude, bool isAdmin, int? chapterIndex = null)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);

        mode = NormalizeMode(mode);
        await UpsertProgressAsync(userId, bookId, mode);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Race: zwei (fast) gleichzeitige .../next-Aufrufe legen den CourseProgress parallel an
            // (Unique (UserId, BookId)). Der LastMode-Upsert ist nur Nebeneffekt → Konflikt verwerfen.
            _db.ChangeTracker.Clear();
        }

        // Kapitel-Modus: Fortschritt + Pool auf das gewählte Kapitel beschränken; sonst buchweit.
        var (chapterName, chapterScoped) = await ResolveChapterAsync(bookId, chapterIndex);
        IQueryable<BookPuzzle> scope = _db.BookPuzzles.Where(bp => bp.BookId == bookId);
        if (chapterScoped) scope = FilterByChapter(scope, chapterName);

        // Fortschritt/Random beziehen sich NUR auf echte Quiz-Linien — Info-/Erklärlinien (IsInfoOnly)
        // werden nicht abgefragt: sie zählen nicht zum Total, erscheinen in keinem Zufallstopf und
        // sind im sequenziellen Modus nur zum Durchklicken da.
        IQueryable<BookPuzzle> quizScope = scope.Where(bp => !bp.IsInfoOnly);
        var total = await quizScope.CountAsync();
        var solvedCount = await quizScope.CountAsync(bp =>
            _db.CoursePuzzleResults.Any(cr => cr.UserId == userId && cr.BookPuzzleId == bp.Id));
        // Kurs gilt als durch, wenn alle Quiz-Linien gelöst sind (etwaige Info-Reste bleiben unberücksichtigt).
        bool completed = solvedCount >= total;

        // Seit dem letzten Reset GESCHEITERTE (aufgegebene) Quiz-Linien fallen bis zum nächsten Reset aus
        // dem Pool — in BEIDEN Modi. Erst nach `POST /reset` (ResetAt rückt vor + gelöste Menge geleert)
        // tauchen sie wieder auf. ResetAt == null (nie zurückgesetzt) ⇒ jeder bisherige Versuch zählt.
        var resetAt = await _db.CourseProgresses
            .Where(cp => cp.UserId == userId && cp.BookId == bookId)
            .Select(cp => cp.ResetAt)
            .FirstOrDefaultAsync() ?? DateTime.MinValue;

        BookPuzzle? puzzle;
        if (mode == "random")
        {
            // Ungelöste Quiz-Linien (NOT EXISTS statt großer IN-Liste); Info-Linien sind via quizScope raus.
            IQueryable<BookPuzzle> unsolved = quizScope
                .Include(bp => bp.Book)
                .Where(bp => !_db.CoursePuzzleResults.Any(cr => cr.UserId == userId && cr.BookPuzzleId == bp.Id));
            // Random-Pool: bis zum Reset jedes Puzzle nur EINMAL. Gelöste sind über CoursePuzzleResults
            // ohnehin raus; zusätzlich auch GESCHEITERTE seit dem letzten Reset ausschließen (resetAt oben).
            var fresh = unsolved.Where(bp =>
                !_db.CourseAttempts.Any(a => a.UserId == userId && a.BookPuzzleId == bp.Id && a.AttemptedAt >= resetAt));

            var pool = exclude.HasValue ? fresh.Where(bp => bp.Id != exclude.Value) : fresh;
            var count = await pool.CountAsync();
            if (count == 0 && exclude.HasValue)
            {
                // Nur noch das ausgeschlossene Puzzle übrig — dann doch dieses zeigen.
                pool = fresh;
                count = await pool.CountAsync();
            }
            puzzle = count == 0
                ? null
                : await pool.OrderBy(bp => bp.Id).Skip(Random.Shared.Next(count)).FirstOrDefaultAsync();
            completed = puzzle == null;   // Random: Topf leer = durch (ggf. erst nach Reset wieder voll)
        }
        else if (completed)
        {
            // Alle Quiz-Linien gelöst → fertig; trailing Info-Linien nicht endlos weiterzeigen.
            puzzle = null;
        }
        else
        {
            // Sequenziell: noch ungelöste Quiz-Linien + noch NICHT durchgeklickte Info-Linien,
            // in Buchreihenfolge. Gelöste Quiz-Linien fallen raus; eine bereits durchgeklickte
            // Info-Linie (CourseInfoView) wird beim Wiedereinstieg übersprungen, sodass der Kurs
            // hinter der zuletzt gesehenen Info-Linie fortsetzt statt sie erneut zu zeigen.
            IQueryable<BookPuzzle> seqPool = scope
                .Include(bp => bp.Book)
                .Where(bp => bp.IsInfoOnly
                    ? !_db.CourseInfoViews.Any(iv => iv.UserId == userId && iv.BookPuzzleId == bp.Id)
                    // Quiz-Linien: gelöste UND seit dem letzten Reset gescheiterte (aufgegebene) raus, damit
                    // ein aufgegebenes Puzzle beim Neustart/Wiedereinstieg nicht sofort wieder erscheint.
                    : (!_db.CoursePuzzleResults.Any(cr => cr.UserId == userId && cr.BookPuzzleId == bp.Id)
                       && !_db.CourseAttempts.Any(a => a.UserId == userId && a.BookPuzzleId == bp.Id && a.AttemptedAt >= resetAt)));
            // Lesereihenfolge = Round (Chessable-Zeilennummer), dann Id. Der „Weiter"-Cursor (after)
            // ist die zuletzt gezeigte Puzzle-Id; sie kann selbst schon aus dem Pool raus sein (gelöst/
            // gesehen), daher wird ihr (Round, Id) separat aufgelöst und die erste Pool-Linie danach
            // gewählt. Nur die Schlüssel (Id, Round) laden — String-Vergleich passiert in-memory
            // (provider-unabhängig; SQL sortiert nur nach der Round-Spalte).
            var keys = await seqPool.OrderBy(bp => bp.Round).ThenBy(bp => bp.Id)
                .Select(bp => new { bp.Id, bp.Round })
                .ToListAsync();
            int? pickId = null;
            if (after.HasValue)
            {
                var cur = await _db.BookPuzzles.Where(bp => bp.Id == after.Value)
                    .Select(bp => new { bp.Id, bp.Round })
                    .FirstOrDefaultAsync();
                if (cur != null)
                    pickId = keys.FirstOrDefault(k =>
                        string.CompareOrdinal(k.Round, cur.Round) > 0 ||
                        (k.Round == cur.Round && k.Id > cur.Id))?.Id;
            }
            pickId ??= keys.Select(k => (int?)k.Id).FirstOrDefault();
            puzzle = pickId.HasValue
                ? await seqPool.FirstOrDefaultAsync(bp => bp.Id == pickId.Value)
                : null;
            // Pool leer, obwohl nicht alle gelöst (Rest nur aufgegeben) → Runde durch (wie Random);
            // die UI zeigt dann „Runde durch, noch N übrig" mit „Von vorn" statt der Abschluss-Trophäe.
            if (puzzle == null) completed = true;
        }

        // Kapitel des aktuellen Puzzles (im Ganz-Buch-Modus wechselt es mit dem Fortschritt);
        // ist der Kurs abgeschlossen, fällt im Kapitel-Modus auf das gescopte Kapitel zurück.
        var currentChapter = puzzle != null ? NormalizeChapter(puzzle.Chapter) : (chapterScoped ? chapterName : null);
        var bundle = await ComputeStatsAsync(userId, bookId, currentChapter, puzzle != null || chapterScoped);

        return new CourseNextPuzzleDto
        {
            Puzzle = puzzle == null ? null : BookPuzzleService.MapToDto(puzzle),
            SolvedCount = solvedCount,
            Total = total,
            Completed = completed,
            Book = bundle.Book,
            Chapter = bundle.Chapter,
            ChapterName = bundle.ChapterName,
        };
    }

    /// <summary>Zeichnet einen Lösungsversuch auf. Bei Solved wird das Puzzle (idempotent) als gelöst markiert.</summary>
    public async Task<CourseProgressDto> RecordResultAsync(int userId, int bookId, RecordCourseResultDto dto, bool isAdmin)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);

        var puzzleChapter = await _db.BookPuzzles
            .Where(bp => bp.Id == dto.BookPuzzleId && bp.BookId == bookId)
            .Select(bp => new { bp.Chapter })
            .FirstOrDefaultAsync();
        if (puzzleChapter == null)
            throw new KeyNotFoundException("Puzzle does not belong to this book.");

        var solvedAt = DateTime.UtcNow;
        var timeSeconds = Math.Clamp(dto.TimeSeconds, 0, 86400);
        var startedAt = solvedAt.AddSeconds(-timeSeconds);
        _logger.LogInformation(
            "CoursePuzzleAttempt: User {UserId} {Result} course-puzzle {PuzzleId} in book {BookId} StartedAt={StartedAt:o} SolvedAt={SolvedAt:o} in {TimeSeconds}s",
            userId, dto.Solved ? "solved" : "failed", dto.BookPuzzleId, bookId, startedAt, solvedAt, timeSeconds);

        // JEDEN Versuch (gelöst/fehlgeschlagen/Wiederholung) ins append-only Zeit-Log schreiben —
        // Grundlage für die akkumulierte Kurs-/Studienzeit im Trainingsziele-Tracker. Eigenes
        // SaveChanges, damit ein späterer CourseProgress-Konflikt das Log nicht zurückrollt.
        _db.CourseAttempts.Add(new CourseAttempt
        {
            UserId = userId,
            BookId = bookId,
            BookPuzzleId = dto.BookPuzzleId,
            Solved = dto.Solved,
            TimeSeconds = timeSeconds,
            AttemptedAt = solvedAt,
            HintsUsed = Math.Clamp(dto.HintsUsed, 0, 3),
        });
        await _db.SaveChangesAsync();

        // Solve in EIGENEM SaveChanges aufzeichnen — damit ein späterer CourseProgress-Konflikt
        // (paralleler Erstinsert) die gültige Lösung NICHT mit zurückrollt (sonst stiller Solve-Verlust).
        if (dto.Solved)
        {
            var already = await _db.CoursePuzzleResults
                .AnyAsync(cr => cr.UserId == userId && cr.BookPuzzleId == dto.BookPuzzleId);
            if (!already)
            {
                _db.CoursePuzzleResults.Add(new CoursePuzzleResult
                {
                    UserId = userId,
                    BookId = bookId,
                    BookPuzzleId = dto.BookPuzzleId,
                    SolvedAt = solvedAt,
                    TimeSeconds = timeSeconds,
                });
                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // Race: paralleles Aufzeichnen desselben Puzzles → Unique (UserId, BookPuzzleId). Idempotent.
                    _db.ChangeTracker.Clear();
                }
            }
        }

        // Fortschritt/LastMode getrennt upserten; ein paralleler Erstinsert (Unique (UserId, BookId)) ist hier unkritisch.
        await UpsertProgressAsync(userId, bookId, dto.Mode);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
        }

        // Im Kapitel-Modus den zurückgegebenen Fortschritt aufs Kapitel beschränken (sonst buchweit);
        // die Kapitel-Statistik bezieht sich auf das gerade bearbeitete Puzzle.
        return await BuildProgressAsync(userId, bookId, dto.ChapterIndex, NormalizeChapter(puzzleChapter.Chapter), true);
    }

    /// <summary>
    /// Merkt eine sequenziell durchgeklickte Info-/Erklärlinie (idempotent), damit sie beim nächsten
    /// Wiedereinstieg übersprungen wird. Validiert, dass das Puzzle zum Buch gehört UND tatsächlich
    /// eine Info-Linie ist (Quiz-Linien werden über <see cref="CoursePuzzleResult"/> gemerkt, nicht hier).
    /// </summary>
    public async Task MarkInfoSeenAsync(int userId, int bookId, int bookPuzzleId, bool isAdmin)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);

        var isInfo = await _db.BookPuzzles
            .AnyAsync(bp => bp.Id == bookPuzzleId && bp.BookId == bookId && bp.IsInfoOnly);
        if (!isInfo)
            throw new KeyNotFoundException("Info line does not belong to this book.");

        var already = await _db.CourseInfoViews
            .AnyAsync(iv => iv.UserId == userId && iv.BookPuzzleId == bookPuzzleId);
        if (already) return;

        _db.CourseInfoViews.Add(new CourseInfoView
        {
            UserId = userId,
            BookId = bookId,
            BookPuzzleId = bookPuzzleId,
            SeenAt = DateTime.UtcNow,
        });
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Race: paralleles Aufzeichnen derselben Info-Linie → Unique (UserId, BookPuzzleId). Idempotent.
            _db.ChangeTracker.Clear();
        }
    }

    /// <summary>Setzt den Fortschritt eines Kurses zurück (löscht alle gelösten Markierungen).</summary>
    public async Task<CourseProgressDto> ResetAsync(int userId, int bookId, bool isAdmin)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);

        _db.CoursePuzzleResults.RemoveRange(
            _db.CoursePuzzleResults.Where(cr => cr.UserId == userId && cr.BookId == bookId));
        // Auch die durchgeklickten Info-Linien vergessen — nach dem Reset wird der Kurs komplett
        // von vorn durchgespielt (inkl. der Erklärlinien).
        _db.CourseInfoViews.RemoveRange(
            _db.CourseInfoViews.Where(iv => iv.UserId == userId && iv.BookId == bookId));
        await _db.SaveChangesAsync();

        // Reset-Marker setzen: ab jetzt gilt jedes Puzzle wieder als „erster Versuch" — die im Kurs
        // angezeigte Zeit + Trefferquote zählen nur noch Versuche ab jetzt. Die CourseAttempts selbst
        // bleiben erhalten (sonst würde der Trainingsziel-Zeit-Tracker rückwirkend verfälscht).
        var now = DateTime.UtcNow;
        var progress = await _db.CourseProgresses.FirstOrDefaultAsync(cp => cp.UserId == userId && cp.BookId == bookId);
        if (progress == null)
            _db.CourseProgresses.Add(new CourseProgress { UserId = userId, BookId = bookId, CreatedAt = now, UpdatedAt = now, ResetAt = now });
        else { progress.ResetAt = now; progress.UpdatedAt = now; }
        await _db.SaveChangesAsync();

        return await BuildProgressAsync(userId, bookId);
    }

    private async Task UpsertProgressAsync(int userId, int bookId, string? mode)
    {
        var progress = await _db.CourseProgresses
            .FirstOrDefaultAsync(cp => cp.UserId == userId && cp.BookId == bookId);
        var now = DateTime.UtcNow;
        if (progress == null)
        {
            _db.CourseProgresses.Add(new CourseProgress
            {
                UserId = userId,
                BookId = bookId,
                LastMode = mode == null ? null : NormalizeMode(mode),
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            if (mode != null) progress.LastMode = NormalizeMode(mode);
            progress.UpdatedAt = now;
        }
    }

    private async Task<CourseProgressDto> BuildProgressAsync(int userId, int bookId, int? chapterIndex = null, string? currentChapter = null, bool currentChapterKnown = false)
    {
        var (chapterName, chapterScoped) = await ResolveChapterAsync(bookId, chapterIndex);
        // Info-/Erklärlinien zählen nicht zum Fortschritt (kein Quiz).
        IQueryable<BookPuzzle> scope = _db.BookPuzzles.Where(bp => bp.BookId == bookId && !bp.IsInfoOnly);
        if (chapterScoped) scope = FilterByChapter(scope, chapterName);

        var total = await scope.CountAsync();
        var solved = await scope.CountAsync(bp =>
            _db.CoursePuzzleResults.Any(cr => cr.UserId == userId && cr.BookPuzzleId == bp.Id));
        var lastMode = await _db.CourseProgresses
            .Where(cp => cp.UserId == userId && cp.BookId == bookId)
            .Select(cp => cp.LastMode)
            .FirstOrDefaultAsync();

        // Kontext-Kapitel: explizit übergeben (= Kapitel des bearbeiteten Puzzles), sonst das per Index gescopte.
        var bundle = await ComputeStatsAsync(userId, bookId,
            currentChapterKnown ? currentChapter : chapterName,
            currentChapterKnown || chapterScoped);

        return new CourseProgressDto
        {
            BookId = bookId,
            SolvedCount = Math.Min(solved, total),
            Total = total,
            ProgressPercent = Percent(solved, total),
            Completed = total > 0 && solved >= total,
            LastMode = lastMode,
            Book = bundle.Book,
            Chapter = bundle.Chapter,
            ChapterName = bundle.ChapterName,
        };
    }

    private sealed record CourseStatsBundle(CourseScopeStatsDto Book, CourseScopeStatsDto? Chapter, string? ChapterName);

    /// <summary>
    /// Berechnet Buch- und (optional) Kapitel-Statistik für den User: Fortschritt (gelöst/gesamt),
    /// akkumulierte Zeit und Erst-Versuch-Trefferquote. Zeit + Trefferquote zählen nur Versuche seit
    /// dem letzten Reset (<see cref="CourseProgress.ResetAt"/>); die gelöste Menge wird beim Reset
    /// physisch entfernt und braucht daher keinen Zeitfilter. Aggregation im Speicher (Versuchszahl je
    /// User/Buch ist klein) — robust gegen Provider-Unterschiede (InMemory vs. MariaDB).
    /// Ein Kapitel-Block wird nur geliefert, wenn das Buch tatsächlich mehrere Kapitel hat.
    /// </summary>
    private async Task<CourseStatsBundle> ComputeStatsAsync(int userId, int bookId, string? currentChapter, bool currentChapterKnown)
    {
        var resetAt = await _db.CourseProgresses
            .Where(cp => cp.UserId == userId && cp.BookId == bookId)
            .Select(cp => cp.ResetAt)
            .FirstOrDefaultAsync();

        // Info-/Erklärlinien zählen nicht zum Fortschritt (kein Quiz).
        var puzzles = await _db.BookPuzzles
            .Where(bp => bp.BookId == bookId && !bp.IsInfoOnly)
            .Select(bp => new { bp.Id, bp.Chapter })
            .ToListAsync();
        var chapterOf = puzzles.ToDictionary(p => p.Id, p => NormalizeChapter(p.Chapter));
        var distinctChapters = puzzles.Select(p => NormalizeChapter(p.Chapter)).Distinct().Count();

        var solvedIds = (await _db.CoursePuzzleResults
            .Where(cr => cr.UserId == userId && cr.BookId == bookId)
            .Select(cr => cr.BookPuzzleId)
            .ToListAsync()).ToHashSet();

        var attemptsQuery = _db.CourseAttempts.Where(a => a.UserId == userId && a.BookId == bookId);
        if (resetAt.HasValue) attemptsQuery = attemptsQuery.Where(a => a.AttemptedAt >= resetAt.Value);
        // Chronologisch sortiert → der erste Eintrag je Puzzle ist der „erste Versuch" (nach Reset).
        var attempts = await attemptsQuery
            .OrderBy(a => a.AttemptedAt).ThenBy(a => a.Id)
            .Select(a => new { a.BookPuzzleId, a.Solved, a.TimeSeconds })
            .ToListAsync();

        CourseScopeStatsDto BuildScope(Func<int, bool> inScope)
        {
            var total = puzzles.Count(p => inScope(p.Id));
            var solved = solvedIds.Count(inScope);
            var scoped = attempts.Where(a => inScope(a.BookPuzzleId)).ToList();
            var firstTry = new Dictionary<int, bool>();
            foreach (var a in scoped)
                if (!firstTry.ContainsKey(a.BookPuzzleId)) firstTry[a.BookPuzzleId] = a.Solved;
            var firstCorrect = firstTry.Count(kv => kv.Value);
            return new CourseScopeStatsDto
            {
                SolvedCount = Math.Min(solved, total),
                Total = total,
                ProgressPercent = Percent(solved, total),
                TotalSeconds = scoped.Sum(a => a.TimeSeconds),
                AttemptedCount = firstTry.Count,
                FirstTryCorrect = firstCorrect,
                AccuracyPercent = Percent(firstCorrect, firstTry.Count),
            };
        }

        var book = BuildScope(_ => true);
        CourseScopeStatsDto? chapter = null;
        string? chapterName = null;
        // Kapitel-Block nur, wenn das Buch wirklich mehrere Kapitel hat (sonst dupliziert es das Buch).
        if (currentChapterKnown && distinctChapters > 1)
        {
            chapterName = currentChapter;
            chapter = BuildScope(id => chapterOf.TryGetValue(id, out var c) && c == currentChapter);
        }
        return new CourseStatsBundle(book, chapter, chapterName);
    }

}
