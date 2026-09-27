using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Daten der Kinderseite: die Stufen-Leiter aus markierten Lichess-Puzzles (<see cref="KidsPuzzle"/>)
/// und die fuer Kinder freigegebenen Kurse (<see cref="Book.ForKids"/>). Alles ohne Anmeldung lesbar —
/// der Fortschritt bleibt auf dem Geraet des Kindes.
/// </summary>
public class KidsPuzzleService
{
    /// <summary>Vorgabe fuer <c>Kids:RequiredCourseLanguages</c>: ein Kinderkurs erscheint erst, wenn seine
    /// Kommentare Deutsch sind (Wunsch 2026-09-27: „ausblenden, bis sie deutsch sind").</summary>
    public const string DefaultRequiredCourseLanguages = "de";

    private readonly AppDbContext _db;
    private readonly string[] _requiredLanguages;

    /// <summary>Ohne Sprachbedingung (Tests, Werkzeuge).</summary>
    public KidsPuzzleService(AppDbContext db) : this(db, Array.Empty<string>()) { }

    /// <summary>Aus der Konfiguration: <c>Kids:RequiredCourseLanguages</c> (Komma-Liste, Vorgabe <c>de</c>, leer = keine).</summary>
    public KidsPuzzleService(AppDbContext db, IConfiguration config)
        : this(db, ParseLanguages(config["Kids:RequiredCourseLanguages"] ?? DefaultRequiredCourseLanguages)) { }

    internal KidsPuzzleService(AppDbContext db, string[] requiredLanguages)
    {
        _db = db;
        _requiredLanguages = requiredLanguages;
    }

    internal static string[] ParseLanguages(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.ToLowerInvariant()).Distinct().ToArray();

    /// <summary>
    /// Die Kinderkurse, die gerade gezeigt werden: freigegeben (<see cref="Book.ForKids"/>), kein
    /// Kalkulationsbuch — und in JEDER geforderten Sprache vorhanden: die Quelle ist diese Sprache, oder ein
    /// Uebersetzungsauftrag in sie ist fertig (<see cref="CourseTranslationJobStatus.Done"/>, auch „nichts zu tun").
    /// Ein Kurs, der noch uebersetzt wird, bleibt so lange unsichtbar — auch ueber den direkten Link.
    /// </summary>
    private IQueryable<Book> VisibleKidsBooks()
    {
        var books = _db.Books.Where(b => b.ForKids && !b.IsCalculation);
        foreach (var lang in _requiredLanguages)
            books = books.Where(b => b.CommentLanguage == lang
                || _db.CourseTranslationJobs.Any(j => j.BookId == b.Id && j.Language == lang
                                                      && j.Status == CourseTranslationJobStatus.Done));
        return books;
    }

    /// <summary>Steht eine Leiter im aktuellen Lehrplan-Stand da? Leer oder veraltet → nein.</summary>
    public async Task<bool> IsCurrentAsync(CancellationToken ct = default) =>
        await _db.KidsPuzzles.AnyAsync(ct)
        && !await _db.KidsPuzzles.AnyAsync(k => k.CurriculumVersion != KidsCurriculum.Version, ct);

    /// <summary>
    /// Rechnet die Leiter aus dem Standard-Puzzle-Bestand neu und ersetzt die alte vollstaendig. Der
    /// Vorfilter laeuft in der Datenbank (Rating-Index), die eigentliche Auswahl in
    /// <see cref="KidsCurriculum.Select"/>. Ohne Standard-Puzzles bleibt die Leiter leer.
    /// </summary>
    public async Task<KidsRebuildResultDto> RebuildAsync(CancellationToken ct = default)
    {
        var candidates = await _db.Puzzles.AsNoTracking()
            .Where(p => p.Rating <= KidsCurriculum.MaxRating
                        && p.RatingDeviation <= KidsCurriculum.MaxRatingDeviation
                        && p.Popularity >= KidsCurriculum.MinPopularity
                        && p.NbPlays >= KidsCurriculum.MinPlays
                        && p.Moves.Length <= KidsCurriculum.MaxMovesLength)
            .Select(p => new KidsCurriculum.Candidate(p.Id, p.LichessId, p.Rating, p.RatingDeviation,
                p.Popularity, p.NbPlays, p.Themes, p.Fen, p.Moves))
            .ToListAsync(ct);

        var placements = KidsCurriculum.Select(candidates);
        var rows = placements.Select(p => new KidsPuzzle
        {
            PuzzleId = p.PuzzleId,
            Level = p.Level,
            Position = p.Position,
            Theme = p.Theme,
            PieceCount = p.PieceCount,
            SolverMoves = p.SolverMoves,
            CurriculumVersion = KidsCurriculum.Version,
        }).ToList();

        await ReplaceAsync(rows, ct);
        return new KidsRebuildResultDto
        {
            Levels = rows.Select(r => r.Level).Distinct().Count(),
            Puzzles = rows.Count,
        };
    }

    /// <summary>Alte Leiter raus, neue rein — relational in EINER Transaktion, damit die Kinderseite nie
    /// eine halbe Leiter sieht.</summary>
    private async Task ReplaceAsync(List<KidsPuzzle> rows, CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
        {
            _db.KidsPuzzles.RemoveRange(await _db.KidsPuzzles.ToListAsync(ct));
            _db.KidsPuzzles.AddRange(rows);
            await _db.SaveChangesAsync(ct);
            return;
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await _db.KidsPuzzles.ExecuteDeleteAsync(ct);
            _db.KidsPuzzles.AddRange(rows);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    /// <summary>Alle Stufen in Reihenfolge, mit Thema und Aufgabenzahl.</summary>
    public async Task<List<KidsLevelDto>> GetLevelsAsync(CancellationToken ct = default)
    {
        var rows = await _db.KidsPuzzles.AsNoTracking()
            .GroupBy(k => new { k.Level, k.Theme })
            .Select(g => new KidsLevelDto { Level = g.Key.Level, Theme = g.Key.Theme, PuzzleCount = g.Count() })
            .ToListAsync(ct);
        return rows.OrderBy(l => l.Level).ToList();
    }

    /// <summary>Eine Stufe mit ihren Aufgaben (leichteste zuerst); <c>null</c>, wenn es sie nicht gibt.</summary>
    public async Task<KidsLevelDetailDto?> GetLevelAsync(int level, CancellationToken ct = default)
    {
        var rows = await _db.KidsPuzzles.AsNoTracking()
            .Where(k => k.Level == level)
            .OrderBy(k => k.Position)
            .Select(k => new { k.Theme, k.Puzzle!.Id, k.Puzzle.Fen, k.Puzzle.Moves })
            .ToListAsync(ct);
        if (rows.Count == 0) return null;

        return new KidsLevelDetailDto
        {
            Level = level,
            Theme = rows[0].Theme,
            Puzzles = rows.Select(r => new KidsPuzzleDto { Id = r.Id, Fen = r.Fen, Moves = r.Moves }).ToList(),
        };
    }

    /// <summary>Die fuer Kinder freigegebenen Kurse. Kalkulationsbuecher nie: sie haben keine Loesung zum
    /// Nachspielen, und ihre Zugfolge gehoert nicht ausgeliefert.</summary>
    public async Task<List<KidsCourseDto>> GetCoursesAsync(string? lang = null, CancellationToken ct = default)
    {
        var rows = await VisibleKidsBooks().AsNoTracking()
            .Select(b => new { b.Id, b.DisplayName, b.KidsTitles, b.Description, PuzzleCount = b.Puzzles.Count(p => !p.IsInfoOnly) })
            .Where(c => c.PuzzleCount > 0)
            .ToListAsync(ct);
        // Titel (und damit die Reihenfolge) je Sprache: die Kindertitel stehen als JSON in einer Spalte.
        return rows
            .Select(r => new KidsCourseDto
            {
                BookId = r.Id,
                Title = KidsTitles.Pick(r.KidsTitles, lang, r.DisplayName),
                Description = r.Description,
                PuzzleCount = r.PuzzleCount,
            })
            .OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Die Aufgaben eines Kinderkurses in Lesereihenfolge — ohne reine Info-Linien (die Kinderseite
    /// fragt ab, sie erzaehlt nicht). Nicht freigegeben/Kalkulationsbuch/unbekannt →
    /// <see cref="KeyNotFoundException"/>. Die Freigabe <see cref="Book.ForKids"/> setzt nur ein Admin;
    /// sie oeffnet den Kurs bewusst auch ohne <see cref="Book.IsPublic"/>.
    /// </summary>
    public async Task<List<BookPuzzleDto>> GetCoursePuzzlesAsync(int bookId, string? lang = null, CancellationToken ct = default)
    {
        var book = await VisibleKidsBooks().AsNoTracking()
            .Where(b => b.Id == bookId)
            .Select(b => new { b.DisplayName, b.KidsTitles })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Course not found.");

        var puzzles = await CourseService.PuzzlesWithBookInReadingOrder(_db, bookId)
            .Where(bp => !bp.IsInfoOnly)
            .AsNoTracking()
            .ToListAsync(ct);
        // Die Kopfzeile des Kurses liest den Titel aus der Linie — dort der Kindertitel statt des Buchnamens.
        var title = KidsTitles.Pick(book.KidsTitles, lang, book.DisplayName);
        return puzzles.Select(BookPuzzleService.MapToDto).Select(dto => { dto.BookTitle = title; return dto; }).ToList();
    }
}
