using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;

namespace RookHub.Api.Services.Courses;

/// <summary>
/// Die Kursliste (<c>GET /api/courses</c>) und die Frage „hat der User überhaupt Kurse?" (Menüeintrag) —
/// beide aus DERSELBEN Buchmenge (<see cref="ListedCourses"/>). Aus <see cref="CourseService"/> herausgelöst
/// (Codereview 2026-09-29, A7-010/Z4), <see cref="GetCoursesAsync"/> dabei in benannte Teilschritte zerlegt:
/// Bücher laden → (!)-Markierung → Kennzahlen → persönliche Merkmale → Besitzer-Namen → Einträge bauen.
/// Der <see cref="CourseService"/> reicht beides als Fassade durch.
/// </summary>
internal sealed class CourseListQuery
{
    private readonly AppDbContext _db;
    /// <summary>Läuft der RookHub-EIGENE Chessable-Weg? Entscheidet mit, ob ein veralteter Kurs noch
    /// holbar ist oder ein Showstopper (siehe <see cref="StaleContentRule"/>).</summary>
    private readonly bool _chessableEnabled;

    internal CourseListQuery(AppDbContext db, bool chessableEnabled)
    {
        _db = db;
        _chessableEnabled = chessableEnabled;
    }

    /// <summary>Hat der User mindestens einen Kurs in seiner Kursliste? (Basis für die Menü-Sichtbarkeit.)
    /// Dieselbe Menge wie <see cref="GetCoursesAsync"/> — vorher fehlten hier Verteiler und Pins, und ein
    /// Konto, das Kurse nur darüber sah, bekam keinen Menüeintrag „Kurse" (Codereview A7-006).</summary>
    internal Task<bool> HasAnyAccessAsync(int userId, bool isAdmin) =>
        ListedCourses(userId, isAdmin).AnyAsync();

    /// <summary>
    /// Die Bücher, die in der Kursliste des Users stehen (Admin: alle) — EINE Quelle für
    /// <see cref="GetCoursesAsync"/> und <see cref="HasAnyAccessAsync"/> (Menüeintrag „Kurse"), damit
    /// der Menüeintrag genau dann erscheint, wenn die Liste etwas zeigt.
    ///
    /// <para>Immer eine TEILMENGE von <see cref="CourseAccess.CanAccessAsync"/>: dieselben Zweige
    /// (eigen, geteilt, Verteiler, Gruppe inkl. „Everyone"), nur ein öffentlicher Kurs steht bewusst
    /// NICHT für jeden in der Liste, sondern nur für den, der ihn angepinnt hat. Ein Pin allein öffnet
    /// dagegen nichts: er wird nur beim Anpinnen geprüft, und Freigabe zurücknehmen, aus dem Verteiler
    /// austragen oder die Serie privat schalten räumen ihn nicht ab — ohne die Bedingung
    /// <c>IsPublic</c> listete die Kursliste den Kurs danach weiter (Name, Beschreibung, Fortschritt),
    /// der Klick lief auf 404 (Codereview A7-006).</para>
    /// </summary>
    private IQueryable<Book> ListedCourses(int userId, bool isAdmin)
    {
        if (isAdmin) return _db.Books;
        var everyoneIds = _db.Groups.Where(g => g.IsEveryone).Select(g => g.Id);
        var myGroups = GroupMembership.GroupIdsOf(_db, userId);   // auch über enthaltene Gruppen
        return _db.Books.Where(b => b.OwnerUserId == userId
            || _db.CourseShares.Any(cs => cs.BookId == b.Id && cs.RecipientId == userId)
            // Verteiler der Kalkulations-Serie: genau dieser Pfad TRÄGT das „privat" der Serie
            // (sobald IsPublic aus ist, sehen nur noch Mitglieder den Kurs). Er fehlte hier —
            // ein Mitglied fand den Kurs weder unter /courses noch im Dashboard, sondern nur
            // über den Deep-Link aus der In-App-Ankündigung.
            || _db.CalcSeriesMembers.Any(m => m.BookId == b.Id && m.UserId == userId)
            // Selbst angepinnte öffentliche Kurse: anpinnen darf man JEDES zugängliche Buch (auch ein
            // öffentliches über den Direkt-Link), angezeigt wurden Pins aber nur aus dieser
            // Liste — der Pin blieb also unsichtbar. Alle anderen Zugänge stehen schon oben.
            || (b.IsPublic && _db.CoursePins.Any(p => p.BookId == b.Id && p.UserId == userId))
            || _db.BookGroupAccesses.Any(a => a.BookId == b.Id &&
                (everyoneIds.Contains(a.GroupId) ||
                 myGroups.Contains(a.GroupId))));
    }

    /// <summary>Die Buchfelder, die die Kursliste braucht (ohne das große Quell-PGN).</summary>
    private sealed class ListedBook
    {
        public int Id { get; init; }
        public string FileName { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string? Difficulty { get; init; }
        public int? Rating { get; init; }
        public string? Tags { get; init; }
        public string? Description { get; init; }
        public int? OwnerUserId { get; init; }
        public string? Themes { get; init; }
        public bool IsCalculation { get; init; }
    }

    /// <summary>Kennzahlen der sichtbaren Bücher, je Buch-Id. Kalkulationsbücher zählen anders
    /// (siehe <see cref="LoadCalculationCountsAsync"/>).</summary>
    private sealed record ListCounts(
        Dictionary<int, int> PuzzleCount, Dictionary<int, int> InfoCount, Dictionary<int, int> Solved,
        Dictionary<int, int> CalcTotal, Dictionary<int, int> CalcDone, Dictionary<int, int> CalcPoints);

    /// <summary>Was am USER hängt: zuletzt genutzter Modus, Pins, „mit mir geteilt", Verknüpfungen.</summary>
    private sealed record ListPersonal(
        Dictionary<int, (string? LastMode, DateTime UpdatedAt)> Progress, HashSet<int> Pinned,
        Dictionary<int, string> SharedBy, Dictionary<int, int> LinkedBook, Dictionary<int, string> LinkedName);

    /// <summary>Sichtbare Bücher als Kurse inkl. Fortschritt des Users (Admin: alle).</summary>
    internal async Task<List<CourseListItemDto>> GetCoursesAsync(int userId, bool isAdmin)
    {
        var books = await ListedCourses(userId, isAdmin)
            .OrderBy(b => b.DisplayName)
            .Select(b => new ListedBook
            {
                Id = b.Id, FileName = b.FileName, DisplayName = b.DisplayName, Difficulty = b.Difficulty,
                Rating = b.Rating, Tags = b.Tags, Description = b.Description,
                OwnerUserId = b.OwnerUserId, Themes = b.Themes, IsCalculation = b.IsCalculation,
            })
            .ToListAsync();

        // Puzzle-Anzahl (ohne Info-/Erklärlinien) je Buch in EINER gruppierten Query — statt einer
        // korrelierten Unterabfrage pro Buch (bei Admins mit hunderten Büchern sonst mehrere Sekunden).
        // Auf die SICHTBAREN Bücher gefiltert (Where mit IN-Liste VOR dem GroupBy — übersetzungssicher):
        // ohne den Filter aggregierte jeder Aufruf der Kursliste die KOMPLETTE BookPuzzles-Tabelle,
        // auch wenn der User nur eine Handvoll Bücher sieht (skaliert mit der Buchzahl, nicht der Userzahl).
        var bookIds = books.Select(b => b.Id).ToList();

        var needsReimportBookIds = await LoadNeedsReimportAsync(bookIds);
        var counts = await LoadListCountsAsync(userId, books, bookIds);
        var personal = await LoadListPersonalAsync(userId);
        var ownerNameById = await LoadOwnerNamesAsync(books, isAdmin);

        return books.Select(b => ToListItem(b, userId, counts, personal, needsReimportBookIds, ownerNameById)).ToList();
    }

    /// <summary>
    /// (!)-Markierung: welche dieser Kurse sind veraltet UND hier nicht aufbereitbar? Eine eigene,
    /// auf die VERALTETEN eingeschränkte Abfrage — das LIKE läuft damit nicht über das (große)
    /// SourcePgn jedes sichtbaren Buchs. Dieselbe Regel wie der Aktualisieren-Lauf (StaleContentRule),
    /// sonst markiert die Liste etwas, das der Knopf längst erledigen würde.
    /// <para>ZWEI Schritte, nicht einer (gemessen 2026-10-06 auf Prod): filtert MariaDB über `ImportVersion` und liest
    /// in DERSELBEN Abfrage `SourcePgn`, holt es die ausgelagerten Quelltexte JEDES durchsuchten Buchs (390 Bücher,
    /// 190 MB) — 1,1 s, auch für ein bloßes `IS NOT NULL`. Erst die Ids, dann gezielt per Primärschlüssel: 0,1 s.</para>
    /// </summary>
    private async Task<HashSet<int>> LoadNeedsReimportAsync(List<int> bookIds)
    {
        var staleIds = await _db.Books
            .Where(b => bookIds.Contains(b.Id) && b.ImportVersion < ImportPipeline.CurrentVersion)
            .Select(b => b.Id).ToListAsync();
        return (await _db.Books
            .Where(b => staleIds.Contains(b.Id))
            .Select(b => new
            {
                b.Id, b.Tags, b.FileName,
                HasSource = b.Source.SourcePgn != null && b.Source.SourcePgn != "",
                SourceModern = b.Source.SourcePgn != null && b.Source.SourcePgn.Contains(StaleContentRule.ModernMarker),
                CacheMissed = b.CacheMissAt != null && b.CacheMissAt >= b.UpdatedAt,   // Book.CacheMissAt gilt, solange nicht älter als UpdatedAt
            })
            .ToListAsync())
            .Where(b => StaleContentRule.ActionForBook(b.HasSource, b.SourceModern, b.Tags, b.FileName, _chessableEnabled,
                    b.CacheMissed)
                == StaleAction.Manual)
            .Select(b => b.Id)
            .ToHashSet();
    }

    /// <summary>Quiz-, Info- und Gelöst-Zahlen je Buch, dazu die Kalkulations-Kennzahlen.</summary>
    private async Task<ListCounts> LoadListCountsAsync(int userId, List<ListedBook> books, List<int> bookIds)
    {
        var puzzleCountByBook = await _db.BookPuzzles
            .Where(bp => bp.BookId != null && bookIds.Contains(bp.BookId.Value) && !bp.IsInfoOnly)
            .GroupBy(bp => bp.BookId!.Value)
            .Select(g => new { BookId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BookId, x => x.Count);

        // UI-Sweep 2026-10-11 (r2-rp-empty-course): Kurse OHNE Quiz-Linie zeigten nur „0 Puzzles" und graue
        // Knöpfe. Für genau diese (meist wenige) zählen wir die Info-Linien nach — die Karte bietet dann
        // „Partien durchsehen" an bzw. sagt, dass der Kurs leer ist.
        var noQuizBookIds = books.Where(b => !b.IsCalculation && !puzzleCountByBook.ContainsKey(b.Id))
            .Select(b => b.Id).ToList();
        var infoCountByBook = noQuizBookIds.Count == 0 ? new Dictionary<int, int>()
            : await _db.BookPuzzles
                .Where(bp => bp.BookId != null && noQuizBookIds.Contains(bp.BookId.Value) && bp.IsInfoOnly)
                .GroupBy(bp => bp.BookId!.Value)
                .Select(g => new { BookId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.BookId, x => x.Count);

        var solvedByBook = await CourseProgressRules.SolvedQuizResults(_db, userId)
            .GroupBy(cr => cr.BookId)
            .Select(g => new { BookId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BookId, x => x.Count);

        var calcBookIds = books.Where(b => b.IsCalculation).Select(b => b.Id).ToList();
        var (calcTotal, calcDone, calcPoints) = await LoadCalculationCountsAsync(userId, calcBookIds);
        return new ListCounts(puzzleCountByBook, infoCountByBook, solvedByBook, calcTotal, calcDone, calcPoints);
    }

    /// <summary>
    /// Kalkulationsbücher zählen ANDERS: dort gibt es nichts zu „lösen". Gesamtzahl = ALLE Linien
    /// (auch die zug-losen — genau die sind im Kalkulationsbuch die reinen Stellungen, und die
    /// fehlen in der Quiz-Zählung, die Info-Linien ausschließt); „erledigt" = Stellungen, zu denen
    /// der Nutzer einen eigenen Analysebaum gespeichert hat.
    /// </summary>
    private async Task<(Dictionary<int, int> Total, Dictionary<int, int> Done, Dictionary<int, int> Points)>
        LoadCalculationCountsAsync(int userId, List<int> calcBookIds)
    {
        var calcTotalByBook = new Dictionary<int, int>();
        var calcDoneByBook = new Dictionary<int, int>();
        var calcPointsByBook = new Dictionary<int, int>();
        if (calcBookIds.Count == 0) return (calcTotalByBook, calcDoneByBook, calcPointsByBook);

        calcTotalByBook = await _db.BookPuzzles
            .Where(bp => bp.BookId != null && calcBookIds.Contains(bp.BookId.Value))
            .GroupBy(bp => bp.BookId!.Value)
            .Select(g => new { BookId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BookId, x => x.Count);
        // „Bearbeitet" heißt: es gibt einen echten Analysebaum. Eine Zeile mit LEEREM TreeJson
        // trägt nur Rechenzeit/Festlegung/Punkte (PATCH ohne Baum) und zählt hier NICHT mit.
        calcDoneByBook = await _db.CalculationTrees
            .Where(t => t.UserId == userId && calcBookIds.Contains(t.BookId) && t.TreeJson != "")
            .GroupBy(t => t.BookId)
            .Select(g => new { BookId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BookId, x => x.Count);
        // Gesamtpunkte der Selbstbewertung je Kalkulationsbuch (unbewertete Stellungen = null
        // fließen nicht ein). Gespeichert ist die STUFE — die Punkte entstehen erst über
        // CalculationGrades.PointsFor, deshalb wird je Buch nach Stufe gezählt und die
        // Gewichtung hier angewandt statt in der DB summiert.
        calcPointsByBook = (await _db.CalculationTrees
            .Where(t => t.UserId == userId && calcBookIds.Contains(t.BookId) && t.Grade != null)
            .GroupBy(t => new { t.BookId, t.Grade })
            .Select(g => new { g.Key.BookId, g.Key.Grade, Count = g.Count() })
            .ToListAsync())
            .GroupBy(x => x.BookId)
            .ToDictionary(g => g.Key,
                g => g.Sum(x => CalculationGrades.PointsFor(x.Grade!.Value) * x.Count));
        return (calcTotalByBook, calcDoneByBook, calcPointsByBook);
    }

    /// <summary>Modus/Zeitpunkt, Pins, „mit mir geteilt" und Verknüpfungen des Users.</summary>
    private async Task<ListPersonal> LoadListPersonalAsync(int userId)
    {
        var progressByBook = await _db.CourseProgresses
            .Where(cp => cp.UserId == userId)
            .ToDictionaryAsync(cp => cp.BookId, cp => (cp.LastMode, cp.UpdatedAt));

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

        return new ListPersonal(progressByBook, pinnedBookIds, sharedByBook, linkByBook, linkedNameById);
    }

    /// <summary>UI-Sweep 2026-10-11 (r2-rp-course-dupes): Besitzer-Namen nur für Admins — sie sehen fremde Bücher
    /// gleichen Titels und brauchen ein Unterscheidungsmerkmal. Einer Abfrage für alle Besitzer.</summary>
    private async Task<Dictionary<int, string>> LoadOwnerNamesAsync(List<ListedBook> books, bool isAdmin)
    {
        var ownerIds = isAdmin
            ? books.Where(b => b.OwnerUserId != null).Select(b => b.OwnerUserId!.Value).Distinct().ToList()
            : new List<int>();
        return ownerIds.Count == 0 ? new Dictionary<int, string>()
            : await _db.AppUsers.Where(u => ownerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username);
    }

    /// <summary>Ein Listeneintrag aus den vorab geladenen Kennzahlen — rein, ohne Datenbank.</summary>
    private static CourseListItemDto ToListItem(ListedBook b, int userId, ListCounts counts, ListPersonal personal,
        HashSet<int> needsReimportBookIds, Dictionary<int, string> ownerNameById)
    {
        var puzzleCount = b.IsCalculation
            ? (counts.CalcTotal.TryGetValue(b.Id, out var ct) ? ct : 0)
            : (counts.PuzzleCount.TryGetValue(b.Id, out var pc) ? pc : 0);
        var solved = b.IsCalculation
            ? (counts.CalcDone.TryGetValue(b.Id, out var cd) ? cd : 0)
            : (counts.Solved.TryGetValue(b.Id, out var c) ? c : 0);
        var hasProgress = personal.Progress.TryGetValue(b.Id, out var progress);
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
            InfoLineCount = counts.InfoCount.TryGetValue(b.Id, out var ic) ? ic : 0,
            SolvedCount = Math.Min(solved, puzzleCount),
            ProgressPercent = CourseProgressRules.Percent(solved, puzzleCount),
            LastMode = hasProgress ? progress.LastMode : null,
            LastActivityAt = hasProgress ? progress.UpdatedAt : null,
            NeedsReimport = needsReimportBookIds.Contains(b.Id),
            IsOwned = b.OwnerUserId == userId,
            IsPinned = personal.Pinned.Contains(b.Id),
            IsShared = personal.SharedBy.ContainsKey(b.Id),
            SharedByUsername = personal.SharedBy.TryGetValue(b.Id, out var sharedBy) ? sharedBy : null,
            LinkedBookId = personal.LinkedBook.TryGetValue(b.Id, out var linkedId) ? linkedId : null,
            LinkedDisplayName = personal.LinkedBook.TryGetValue(b.Id, out var lid) && personal.LinkedName.TryGetValue(lid, out var ln) ? ln : null,
            Themes = BookThemeTags.ParseKeys(b.Themes),
            IsCalculation = b.IsCalculation,
            CalcPoints = b.IsCalculation
                ? (counts.CalcPoints.TryGetValue(b.Id, out var cp) ? cp : 0)
                : null,
            // Summe immer MIT Maximum ausliefern: „14" allein ist ohne die Zahl der
            // Stellungen nicht lesbar.
            CalcMaxPoints = b.IsCalculation ? CalculationGrades.MaxPointsFor(puzzleCount) : null,
            IsChessableImport = StaleContentRule.IsChessable(b.Tags, b.FileName),
            OwnerUsername = b.OwnerUserId is int oid && ownerNameById.TryGetValue(oid, out var on) ? on : null,
        };
    }
}
