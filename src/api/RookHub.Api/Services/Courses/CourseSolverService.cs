using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Puzzles;

namespace RookHub.Api.Services.Courses;

/// <summary>
/// Der Kurs-Solver: nächste Linie ziehen (<c>/next</c>), Ergebnis aufzeichnen (<c>/results</c>),
/// Info-Linien merken, Kurs zurücksetzen, „Offene nochmal", „Info-Linien überspringen" und der ✓/✗-Stand
/// je Linie. Aus <see cref="CourseService"/> herausgelöst (Codereview 2026-09-29, A7-010/Z4); der
/// <see cref="CourseService"/> bleibt die Fassade für den Controller und reicht diese Aufrufe durch.
/// <para>Geloggt wird unter der Quelle des <see cref="CourseService"/> (Logger kommt von dort) — so bleiben
/// Log-Filter und Kibana-Abfragen auf „CoursePuzzleAttempt" unverändert.</para>
/// </summary>
public class CourseSolverService
{
    private readonly AppDbContext _db;
    private readonly ILogger _logger;

    public CourseSolverService(AppDbContext db, ILogger logger)
    {
        _db = db;
        _logger = logger;
    }

    private Task EnsureAccessAsync(int userId, int bookId, bool isAdmin) =>
        CourseProgressRules.EnsureAccessAsync(_db, userId, bookId, isAdmin);

    private IQueryable<CourseAttempt> AttemptsSinceReset(int userId, int bookId, DateTime bookResetAt) =>
        CourseProgressRules.AttemptsSinceReset(_db, userId, bookId, bookResetAt);

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
        var resetAt = await _db.CourseProgresses
            .Where(cp => cp.UserId == userId && cp.BookId == bookId)
            .Select(cp => cp.ResetAt)
            .FirstOrDefaultAsync() ?? DateTime.MinValue;
        // Nur Versuche seit dem letzten Reset (Kurs oder Kapitel) — sonst stünde nach einem Reset jede
        // vorher gespielte Linie als „gescheitert" da.
        var attempted = (await AttemptsSinceReset(userId, bookId, resetAt)
            .Select(ca => ca.BookPuzzleId)
            .Distinct()
            .ToListAsync()).ToHashSet();
        return new CourseLineStatusDto
        {
            SolvedIds = solved.ToList(),
            FailedIds = attempted.Where(id => !solved.Contains(id)).ToList(),
        };
    }

    /// <summary>Schaltet „Info-Linien überspringen" für (User, Kurs) — persönlich, legt den
    /// <see cref="CourseProgress"/> bei Bedarf an. 404 wenn nicht zugänglich. Gibt den neuen Zustand zurück.</summary>
    public async Task<bool> SetSkipInfoLinesAsync(int userId, int bookId, bool skip, bool isAdmin)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);
        var progress = await TrackedProgressAsync(userId, bookId);
        progress.SkipInfoLines = skip;
        await _db.SaveChangesAsync();
        return skip;
    }

    /// <summary>„Offene nochmal": gescheiterte, ungelöste Linien kommen wieder in den Topf (beide Modi),
    /// gelöste bleiben gelöst. 404 wenn nicht zugänglich.</summary>
    public async Task RetryFailedAsync(int userId, int bookId, bool isAdmin)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);
        var progress = await TrackedProgressAsync(userId, bookId);
        progress.RetryFailedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    /// <summary>Der <see cref="CourseProgress"/> von (User, Kurs) zum Ändern — legt ihn bei Bedarf an
    /// (dann steht er nur im Change-Tracker, gespeichert wird beim nächsten SaveChanges).</summary>
    private async Task<CourseProgress> TrackedProgressAsync(int userId, int bookId)
    {
        await UpsertProgressAsync(userId, bookId, null);
        return _db.CourseProgresses.Local.FirstOrDefault(cp => cp.UserId == userId && cp.BookId == bookId)
            ?? await _db.CourseProgresses.FirstAsync(cp => cp.UserId == userId && cp.BookId == bookId);
    }

    /// <summary>Pool-Stand eines Users in einem Kurs: ab wann Versuche den Pool sperren und ob
    /// Info-Linien übersprungen werden.</summary>
    private sealed record PoolState(DateTime PoolSince, bool SkipInfo);

    /// <summary>
    /// Nächstes ungelöstes Puzzle des Kurses. sequential: Buchreihenfolge (Id), mit <paramref name="after"/>
    /// das nächste danach; random: zufällig, <paramref name="exclude"/> vermeidet direkte Wiederholung.
    /// Aktualisiert den zuletzt genutzten Modus.
    /// <para>KALKULATIONSBÜCHER → <see cref="NotFoundException"/> (404): Solver-Weg, liefert über
    /// <see cref="BookPuzzleService.MapToDto"/> die Züge (siehe <see cref="CourseAccess.IsCalculationBookAsync"/>).</para>
    /// </summary>
    public async Task<CourseNextPuzzleDto> GetNextAsync(int userId, int bookId, string mode, int? after, int? exclude, bool isAdmin, int? chapterIndex = null)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);
        if (await CourseAccess.IsCalculationBookAsync(_db, bookId))
            throw new NotFoundException("Book not found.");

        mode = CourseProgressRules.NormalizeOrderMode(mode);
        await UpsertProgressAsync(userId, bookId, mode);
        // Race: zwei (fast) gleichzeitige .../next-Aufrufe legen den CourseProgress parallel an
        // (Unique (UserId, BookId)). Der LastMode-Upsert ist nur Nebeneffekt → Konflikt verwerfen.
        await _db.SaveIgnoringUniqueRaceAsync(_logger);

        // Kapitel-Modus: Fortschritt + Pool auf das gewählte Kapitel beschränken; sonst buchweit.
        var (chapterName, chapterScoped) = await CourseProgressRules.ResolveChapterAsync(_db, bookId, chapterIndex);
        IQueryable<BookPuzzle> scope = _db.BookPuzzles.Where(bp => bp.BookId == bookId);
        if (chapterScoped) scope = CourseProgressRules.FilterByChapter(scope, chapterName);

        // Fortschritt/Random beziehen sich NUR auf echte Quiz-Linien — Info-/Erklärlinien (IsInfoOnly)
        // werden nicht abgefragt: sie zählen nicht zum Total, erscheinen in keinem Zufallstopf und
        // sind im sequenziellen Modus nur zum Durchklicken da.
        IQueryable<BookPuzzle> quizScope = scope.Where(bp => !bp.IsInfoOnly);
        var total = await quizScope.CountAsync();
        var solvedCount = await quizScope.CountAsync(bp =>
            _db.CoursePuzzleResults.Any(cr => cr.UserId == userId && cr.BookPuzzleId == bp.Id));
        // Kurs gilt als durch, wenn alle Quiz-Linien gelöst sind (etwaige Info-Reste bleiben unberücksichtigt).
        bool completed = solvedCount >= total;

        var pool = await LoadPoolStateAsync(userId, bookId);

        BookPuzzle? puzzle;
        if (mode == "random")
        {
            puzzle = await PickRandomAsync(quizScope, userId, bookId, pool.PoolSince, exclude);
            completed = puzzle == null;   // Random: Topf leer = durch (ggf. erst nach Reset wieder voll)
        }
        else if (completed)
        {
            // Alle Quiz-Linien gelöst → fertig; trailing Info-Linien nicht endlos weiterzeigen.
            puzzle = null;
        }
        else
        {
            puzzle = await PickSequentialAsync(pool.SkipInfo ? quizScope : scope, userId, bookId, pool.PoolSince, after);
            // Pool leer, obwohl nicht alle gelöst (Rest nur aufgegeben) → Runde durch (wie Random);
            // die UI zeigt dann „Runde durch, noch N übrig" mit „Von vorn" statt der Abschluss-Trophäe.
            if (puzzle == null) completed = true;
        }

        // Kapitel des aktuellen Puzzles (im Ganz-Buch-Modus wechselt es mit dem Fortschritt);
        // ist der Kurs abgeschlossen, fällt im Kapitel-Modus auf das gescopte Kapitel zurück.
        var currentChapter = puzzle != null ? ChapterOrder.NormalizeChapter(puzzle.Chapter) : (chapterScoped ? chapterName : null);
        var bundle = await CourseScopeStatistics.ComputeAsync(_db, userId, bookId, currentChapter, puzzle != null || chapterScoped);

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

    /// <summary>
    /// Seit dem letzten Reset GESCHEITERTE (aufgegebene) Quiz-Linien fallen bis zum nächsten Reset aus
    /// dem Pool — in BEIDEN Modi. Erst nach `POST /reset` (ResetAt rückt vor + gelöste Menge geleert)
    /// tauchen sie wieder auf. ResetAt == null (nie zurückgesetzt) ⇒ jeder bisherige Versuch zählt.
    /// </summary>
    private async Task<PoolState> LoadPoolStateAsync(int userId, int bookId)
    {
        var progressRow = await _db.CourseProgresses
            .Where(cp => cp.UserId == userId && cp.BookId == bookId)
            .Select(cp => new { cp.ResetAt, cp.SkipInfoLines, cp.RetryFailedAt })
            .FirstOrDefaultAsync();
        var resetAt = progressRow?.ResetAt ?? DateTime.MinValue;
        // „Offene nochmal": gescheiterte Linien zählen für den Pool erst ab diesem Zeitpunkt wieder als erledigt.
        var poolSince = progressRow?.RetryFailedAt is DateTime retry && retry > resetAt ? retry : resetAt;
        // Persönliche Kurs-Einstellung „Info-Linien überspringen": der sequenzielle Pool zeigt nur Quiz-Linien.
        var skipInfo = progressRow?.SkipInfoLines ?? false;
        return new PoolState(poolSince, skipInfo);
    }

    /// <summary>Zufallsmodus: eine zufällige ungelöste, seit <paramref name="poolSince"/> nicht gescheiterte
    /// Quiz-Linie; <paramref name="exclude"/> wird gemieden, solange es noch eine andere gibt.</summary>
    private async Task<BookPuzzle?> PickRandomAsync(IQueryable<BookPuzzle> quizScope, int userId, int bookId,
        DateTime poolSince, int? exclude)
    {
        // Ungelöste Quiz-Linien (NOT EXISTS statt großer IN-Liste); Info-Linien sind via quizScope raus.
        IQueryable<BookPuzzle> unsolved = quizScope
            .Include(bp => bp.Book)
            .Where(bp => !_db.CoursePuzzleResults.Any(cr => cr.UserId == userId && cr.BookPuzzleId == bp.Id));
        // Random-Pool: bis zum Reset jedes Puzzle nur EINMAL. Gelöste sind über CoursePuzzleResults
        // ohnehin raus; zusätzlich auch GESCHEITERTE seit dem letzten Reset ausschließen (resetAt oben).
        var fresh = unsolved.Where(bp =>
            !AttemptsSinceReset(userId, bookId, poolSince).Any(a => a.BookPuzzleId == bp.Id));

        var pool = exclude.HasValue ? fresh.Where(bp => bp.Id != exclude.Value) : fresh;
        var count = await pool.CountAsync();
        if (count == 0 && exclude.HasValue)
        {
            // Nur noch das ausgeschlossene Puzzle übrig — dann doch dieses zeigen.
            pool = fresh;
            count = await pool.CountAsync();
        }
        return count == 0
            ? null
            : await pool.OrderBy(bp => bp.Id).Skip(Random.Shared.Next(count)).FirstOrDefaultAsync();
    }

    /// <summary>Sequenzieller Modus: die erste offene Linie in Lesereihenfolge nach dem Cursor
    /// <paramref name="after"/> (ohne Cursor bzw. ohne Treffer danach: die erste überhaupt).</summary>
    private async Task<BookPuzzle?> PickSequentialAsync(IQueryable<BookPuzzle> lineScope, int userId, int bookId,
        DateTime poolSince, int? after)
    {
        // Sequenziell: noch ungelöste Quiz-Linien + noch NICHT durchgeklickte Info-Linien,
        // in Buchreihenfolge. Gelöste Quiz-Linien fallen raus; eine bereits durchgeklickte
        // Info-Linie (CourseInfoView) wird beim Wiedereinstieg übersprungen, sodass der Kurs
        // hinter der zuletzt gesehenen Info-Linie fortsetzt statt sie erneut zu zeigen.
        IQueryable<BookPuzzle> seqPool = lineScope
            .Include(bp => bp.Book)
            .Where(bp => bp.IsInfoOnly
                ? !_db.CourseInfoViews.Any(iv => iv.UserId == userId && iv.BookPuzzleId == bp.Id)
                // Quiz-Linien: gelöste UND seit dem letzten Reset gescheiterte (aufgegebene) raus, damit
                // ein aufgegebenes Puzzle beim Neustart/Wiedereinstieg nicht sofort wieder erscheint.
                : (!_db.CoursePuzzleResults.Any(cr => cr.UserId == userId && cr.BookPuzzleId == bp.Id)
                   && !AttemptsSinceReset(userId, bookId, poolSince).Any(a => a.BookPuzzleId == bp.Id)));
        // Lesereihenfolge = Round (Chessable-Zeilennummer), dann Id. Der „Weiter"-Cursor (after)
        // ist die zuletzt gezeigte Puzzle-Id; sie kann selbst schon aus dem Pool raus sein (gelöst/
        // gesehen), daher wird ihr (Round, Id) separat aufgelöst und die erste Pool-Linie danach
        // gewählt. Nur die Schlüssel (Id, Round) laden — String-Vergleich passiert in-memory
        // (provider-unabhängig; SQL sortiert nur nach der Round-Spalte).
        var keys = await seqPool.InReadingOrder()
            .Select(bp => new { bp.Id, bp.Round })
            .ToListAsync();
        int? pickId = null;
        if (after.HasValue)
        {
            var cur = await _db.BookPuzzles.Where(bp => bp.Id == after.Value)
                .Select(bp => new { bp.Id, bp.Round })
                .FirstOrDefaultAsync();
            if (cur != null)
                // Dieselbe Reihenfolge wie die Sortierung (inkl. Round.Length), siehe
                // ChapterOrder.Compare — sonst zeigte „Überspringen" bei Runden „9"/„10"
                // dieselbe Aufgabe endlos erneut.
                pickId = keys.FirstOrDefault(k => ChapterOrder.Compare(k.Round, k.Id, cur.Round, cur.Id) > 0)?.Id;
        }
        pickId ??= keys.Select(k => (int?)k.Id).FirstOrDefault();
        return pickId.HasValue
            ? await seqPool.FirstOrDefaultAsync(bp => bp.Id == pickId.Value)
            : null;
    }

    /// <summary>Zeichnet einen Lösungsversuch auf. Bei Solved wird das Puzzle (idempotent) als gelöst markiert.</summary>
    public async Task<CourseProgressDto> RecordResultAsync(int userId, int bookId, RecordCourseResultDto dto, bool isAdmin)
    {
        await EnsureAccessAsync(userId, bookId, isAdmin);
        var puzzleChapter = await LoadQuizLineChapterAsync(bookId, dto.BookPuzzleId);

        // Zeit/Tipps/Spielweise/Startzeit: eine Normalisierung für alle Recorder (siehe AttemptRecording).
        // dto.SolveMode ist die SPIELWEISE, NICHT dto.Mode (= sequential/random).
        var core = AttemptRecording.From(dto.Solved, dto.TimeSeconds, dto.HintsUsed, dto.SolveMode);
        _logger.LogInformation(
            "CoursePuzzleAttempt: User {UserId} {Result} course-puzzle {PuzzleId} in book {BookId} StartedAt={StartedAt:o} SolvedAt={SolvedAt:o} in {TimeSeconds}s",
            userId, core.Solved ? "solved" : "failed", dto.BookPuzzleId, bookId, core.StartedAt, core.AttemptedAt, core.TimeSeconds);

        await AppendAttemptAsync(userId, bookId, dto.BookPuzzleId, core);
        if (dto.Solved)
            await MarkSolvedAsync(userId, bookId, dto.BookPuzzleId, core);

        // Fortschritt/LastMode getrennt upserten; ein paralleler Erstinsert (Unique (UserId, BookId)) ist hier unkritisch.
        await UpsertProgressAsync(userId, bookId, dto.Mode);
        await _db.SaveIgnoringUniqueRaceAsync(_logger);

        // Im Kapitel-Modus den zurückgegebenen Fortschritt aufs Kapitel beschränken (sonst buchweit);
        // die Kapitel-Statistik bezieht sich auf das gerade bearbeitete Puzzle.
        return await BuildProgressAsync(userId, bookId, dto.ChapterIndex, ChapterOrder.NormalizeChapter(puzzleChapter), true);
    }

    /// <summary>Das (rohe) Kapitel einer QUIZ-Linie dieses Buchs; fremde Linie oder Info-Linie → 404.</summary>
    private async Task<string?> LoadQuizLineChapterAsync(int bookId, int bookPuzzleId)
    {
        var puzzleChapter = await _db.BookPuzzles
            .Where(bp => bp.Id == bookPuzzleId && bp.BookId == bookId)
            .Select(bp => new { bp.Chapter, bp.IsInfoOnly })
            .FirstOrDefaultAsync();
        if (puzzleChapter == null)
            throw new NotFoundException("Puzzle does not belong to this book.");
        // Eine Info-/Erklärlinie wird nicht gelöst, sondern durchgeklickt (MarkInfoSeenAsync, dort
        // umgekehrt geprüft). Ein Ergebnis daran zählte nirgends mit und machte die Zählungen nur
        // uneinheitlich (Codereview A7-009) — z. B. eine offline vorgemerkte Lösung, deren Linie ein
        // Re-Import inzwischen auf Info umgestellt hat.
        if (puzzleChapter.IsInfoOnly)
            throw new NotFoundException("Info lines are not solved.");
        return puzzleChapter.Chapter;
    }

    /// <summary>
    /// JEDEN Versuch (gelöst/fehlgeschlagen/Wiederholung) ins append-only Zeit-Log schreiben —
    /// Grundlage für die akkumulierte Kurs-/Studienzeit im Trainingsziele-Tracker. Eigenes
    /// SaveChanges, damit ein späterer CourseProgress-Konflikt das Log nicht zurückrollt.
    /// </summary>
    private async Task AppendAttemptAsync(int userId, int bookId, int bookPuzzleId, AttemptCore core)
    {
        _db.CourseAttempts.Add(new CourseAttempt
        {
            UserId = userId,
            BookId = bookId,
            BookPuzzleId = bookPuzzleId,
            Solved = core.Solved,
            TimeSeconds = core.TimeSeconds,
            AttemptedAt = core.AttemptedAt,
            HintsUsed = core.HintsUsed,
            Mode = core.Mode,
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Solve in EIGENEM SaveChanges aufzeichnen — damit ein späterer CourseProgress-Konflikt
    /// (paralleler Erstinsert) die gültige Lösung NICHT mit zurückrollt (sonst stiller Solve-Verlust).
    /// </summary>
    private async Task MarkSolvedAsync(int userId, int bookId, int bookPuzzleId, AttemptCore core)
    {
        var already = await _db.CoursePuzzleResults
            .AnyAsync(cr => cr.UserId == userId && cr.BookPuzzleId == bookPuzzleId);
        if (already) return;
        _db.CoursePuzzleResults.Add(new CoursePuzzleResult
        {
            UserId = userId,
            BookId = bookId,
            BookPuzzleId = bookPuzzleId,
            SolvedAt = core.AttemptedAt,
            TimeSeconds = core.TimeSeconds,
        });
        // Race: paralleles Aufzeichnen desselben Puzzles → Unique (UserId, BookPuzzleId). Idempotent.
        // Ein ANDERER Fehler darf hier nicht geschluckt werden — sonst geht der Solve still verloren.
        await _db.SaveIgnoringUniqueRaceAsync(_logger);
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
            throw new NotFoundException("Info line does not belong to this book.");

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
        // Race: paralleles Aufzeichnen derselben Info-Linie → Unique (UserId, BookPuzzleId). Idempotent.
        await _db.SaveIgnoringUniqueRaceAsync(_logger);
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
        // Kapitel-Resets dieses Buchs sind vom buchweiten Reset überholt (er liegt später).
        _db.CourseLineResets.RemoveRange(
            _db.CourseLineResets.Where(r => r.UserId == userId && r.BookId == bookId));
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
                LastMode = mode == null ? null : CourseProgressRules.NormalizeOrderMode(mode),
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            if (mode != null) progress.LastMode = CourseProgressRules.NormalizeOrderMode(mode);
            progress.UpdatedAt = now;
        }
    }

    private async Task<CourseProgressDto> BuildProgressAsync(int userId, int bookId, int? chapterIndex = null, string? currentChapter = null, bool currentChapterKnown = false)
    {
        var (chapterName, chapterScoped) = await CourseProgressRules.ResolveChapterAsync(_db, bookId, chapterIndex);
        // Info-/Erklärlinien zählen nicht zum Fortschritt (kein Quiz).
        IQueryable<BookPuzzle> scope = _db.BookPuzzles.Where(bp => bp.BookId == bookId && !bp.IsInfoOnly);
        if (chapterScoped) scope = CourseProgressRules.FilterByChapter(scope, chapterName);

        var total = await scope.CountAsync();
        var solved = await scope.CountAsync(bp =>
            _db.CoursePuzzleResults.Any(cr => cr.UserId == userId && cr.BookPuzzleId == bp.Id));
        var lastMode = await _db.CourseProgresses
            .Where(cp => cp.UserId == userId && cp.BookId == bookId)
            .Select(cp => cp.LastMode)
            .FirstOrDefaultAsync();

        // Kontext-Kapitel: explizit übergeben (= Kapitel des bearbeiteten Puzzles), sonst das per Index gescopte.
        var bundle = await CourseScopeStatistics.ComputeAsync(_db, userId, bookId,
            currentChapterKnown ? currentChapter : chapterName,
            currentChapterKnown || chapterScoped);

        return new CourseProgressDto
        {
            BookId = bookId,
            SolvedCount = Math.Min(solved, total),
            Total = total,
            ProgressPercent = CourseProgressRules.Percent(solved, total),
            Completed = total > 0 && solved >= total,
            LastMode = lastMode,
            Book = bundle.Book,
            Chapter = bundle.Chapter,
            ChapterName = bundle.ChapterName,
        };
    }
}
