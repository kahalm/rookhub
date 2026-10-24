using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Chessable;
using RookHub.Api.Services.Repertoires;

namespace RookHub.Api.Services.Courses;

/// <summary>
/// Persönliche Kurse anlegen und löschen: aus einem hochgeladenen PGN (inkl. „Chessable-Repertoire →
/// Kurs" mit nachgetragenem Trainingsstart) oder leer, und das Löschen eines eigenen Kurses. Aus
/// <see cref="CourseService"/> herausgelöst (Codereview 2026-09-29, A7-010/Z4); der
/// <see cref="CourseService"/> bleibt die Fassade (auch für <see cref="Ebooks.EbookCourseBuilder"/> und
/// <see cref="CourseRepertoireConversionService"/>).
/// <para>KEINE Abhängigkeit auf RepertoireService-Instanzen: gebraucht wird nur das STATISCHE
/// <see cref="RepertoireService.LooksLikePgn"/> — eine Instanz weniger heißt eine Zyklus-Gefahr weniger.</para>
/// </summary>
public class CourseCreationService
{
    private readonly AppDbContext _db;
    private readonly ILogger _logger;
    private readonly PgnImportService _pgnImport;
    private readonly BookAdminService _bookAdmin;
    /// <summary>Nur fürs Nachtragen des Chessable-Trainingsstarts beim Umwandeln; null = nicht verfügbar.</summary>
    private readonly ChessableProxyService? _chessableProxy;

    public CourseCreationService(AppDbContext db, ILogger logger, PgnImportService pgnImport, BookAdminService bookAdmin,
        ChessableProxyService? chessableProxy)
    {
        _db = db;
        _logger = logger;
        _pgnImport = pgnImport;
        _bookAdmin = bookAdmin;
        _chessableProxy = chessableProxy;
    }

    /// <summary>
    /// Legt aus einem hochgeladenen PGN einen persönlichen Kurs des Users an (eigenes <see cref="Book"/>
    /// mit <c>OwnerUserId</c>, sofort nur für diesen User als Kurs sichtbar). Wiederverwendet die
    /// Import-Pipeline (<see cref="PgnImportService.ImportFileAsync"/>); der interne Dateiname ist
    /// pro-User eindeutig und kollisionsfrei, der Anzeigename kommt vom User (oder dem Dateinamen).
    /// Kind = Study → die Kurszeit zählt in die Trainingsziel-Kategorie „Buch/Kurs".
    /// </summary>
    /// <param name="chessableBid">Chessable-Kurs, aus dem das PGN stammt (beim Umwandeln eines Chessable-Repertoires);
    /// nur damit lässt sich ein fehlender Trainingsstart aus dem Linien-Cache nachschlagen (siehe
    /// <see cref="WithChessableTrainingStartsAsync"/>).</param>
    public async Task<CourseListItemDto> UploadPersonalCourseAsync(int userId, string originalFileName, string pgn, string? displayName,
        CancellationToken ct = default, string? chessableBid = null)
    {
        if (string.IsNullOrWhiteSpace(pgn) || !RepertoireService.LooksLikePgn(pgn))
            throw new DomainValidationException("The file does not look like a valid PGN.");

        // Pro-User-eindeutiger interner Dateiname (NICHT der Anzeigename) → kollisionsfrei mit
        // globalen Büchern und Chessable-Importen (chessable-u{userId}-{bid}.pgn).
        var fileName = $"user-u{userId}-{Guid.NewGuid():N}.pgn";
        // Chessable-Linien ohne Trainingsmarker: fehlenden Trainingsstart nachtragen, BEVOR importiert
        // wird (ein Repertoire-PGN trägt keinen — sonst stünde im Kurs die falsche Seite am Zug).
        pgn = await WithChessableTrainingStartsAsync(pgn, chessableBid, ct);

        // Eigener Kurs des Nutzers: ein Eroeffnungsrepertoire aus der Grundstellung IST hier
        // spielbarer Inhalt (siehe PgnImportService.StartPlyForRepertoire).
        var res = await _pgnImport.ImportFileAsync(fileName, pgn, ct, playFromStartPosition: true, ownerUserId: userId);

        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == res.BookId, ct)
            ?? throw new InvalidOperationException("Import failed.");

        // Nichts Quiz-bares importiert → leeres Buch wieder entfernen statt einen leeren Kurs anzulegen.
        var puzzleCount = await _db.BookPuzzles.CountAsync(bp => bp.BookId == book.Id && !bp.IsInfoOnly, ct);
        if (puzzleCount == 0)
        {
            // Frisch angelegtes Buch — Abhängige kann es noch keine geben; der Aufruf hält trotzdem die
            // Regel „jeder Pfad, der BookPuzzles löscht, räumt ab, was daran hängt" (ein leerer Griff).
            await BookPuzzleDependents.RemoveForLinesAsync(_db,
                _db.BookPuzzles.Where(bp => bp.BookId == book.Id).Select(bp => bp.Id), ct);
            _db.BookPuzzles.RemoveRange(_db.BookPuzzles.Where(bp => bp.BookId == book.Id));
            _db.Books.Remove(book);
            await _db.SaveChangesAsync(ct);
            throw new DomainValidationException("No playable lines found in the PGN.") { Code = ApiErrorCodes.CourseNoLines };
        }

        var name = string.IsNullOrWhiteSpace(displayName)
            ? PgnImportService.CleanDisplayName(originalFileName)
            : displayName.Trim();
        if (name.Length > 200) name = name[..200];

        ApplyPersonalCourseMetadata(book, userId, name, DateTime.UtcNow);
        await _db.SaveChangesAsync(ct);

        return NewOwnedCourseItem(book, puzzleCount);
    }

    /// <summary>Legt einen LEEREN persönlichen Kurs an — die Stellungen kommen danach über die
    /// Kurs-Detailseite dazu (Kapitel + „Stellungen einfügen"). Bewusst ohne PGN-Zwang: nicht jeder Kurs
    /// entsteht aus einer Datei, viele werden Kapitel für Kapitel von Hand aufgebaut.
    /// <c>ImportVersion</c> steht sofort auf der aktuellen Pipeline-Version — ein handgepflegtes Buch hat
    /// kein Quell-PGN und soll nicht im „Aktualisieren"-Banner hängen (gleiche Regel wie beim Einfügen
    /// von Linien in <see cref="CourseAuthoringService"/>).</summary>
    public async Task<CourseListItemDto> CreatePersonalCourseAsync(int userId, string displayName, CancellationToken ct = default)
    {
        var name = displayName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainValidationException("A course name is required.");
        if (name.Length > 200) name = name[..200];

        var now = DateTime.UtcNow;
        var book = new Book
        {
            // Interner Dateiname wie beim PGN-Weg: pro User eindeutig, kollisionsfrei mit globalen
            // Büchern und Chessable-Importen. Er ist NICHT der Anzeigename.
            FileName = $"user-u{userId}-{Guid.NewGuid():N}.pgn",
            ImportVersion = ImportPipeline.CurrentVersion,
            CreatedAt = now,
            Source = new BookSource(),   // Pflicht-Navigation (Tabellensplitting): kein Quell-PGN
        };
        ApplyPersonalCourseMetadata(book, userId, name, now);
        _db.Books.Add(book);
        await _db.SaveChangesAsync(ct);

        return NewOwnedCourseItem(book, 0);
    }

    /// <summary>
    /// Trägt den fehlenden Trainingsstart Chessable-stämmiger Linien nach (siehe
    /// <see cref="ChessableTrainingStart"/>): für Linien MIT <c>[ChessableOid]</c>, aber ohne
    /// <c>[%tqu]</c> und ohne <c>[ChessableColor]</c> wird die Solverfarbe aus dem geteilten
    /// piratechess-Linien-Cache geholt und als Header eingetragen.
    /// <para>Ein gewöhnliches Nutzer-PGN hat keine oids und löst deshalb keinen Abruf aus. Ist
    /// piratechess nicht erreichbar oder kennt es die Linien nicht, bleibt das PGN unverändert und der
    /// Kurs entsteht wie bisher — die Umwandlung darf daran nicht scheitern.</para>
    /// <para>Nachgeschlagen wird nur mit bekanntem Kurs (<paramref name="chessableBid"/>): der Linien-Cache gibt Linien
    /// nur für den Kurs heraus, unter dem sie liegen. Ein hochgeladenes PGN ohne Kurs behält den bisherigen Start.</para>
    /// </summary>
    private async Task<string> WithChessableTrainingStartsAsync(string pgn, string? chessableBid, CancellationToken ct)
    {
        if (_chessableProxy is null) return pgn;
        var oids = ChessableTrainingStart.OidsWithoutStart(pgn);
        if (oids.Count == 0) return pgn;
        if (string.IsNullOrWhiteSpace(chessableBid))
        {
            _logger.LogInformation(
                "Kurs aus PGN: {Count} Chessable-Linien ohne Trainingsstart, aber ohne Kurs-Id — Linien behalten den bisherigen Start.",
                oids.Count);
            return pgn;
        }

        try
        {
            var cached = await _chessableProxy.GetCachedLinePgnsAsync(chessableBid, oids, ChessableTrainingStart.MarkerMode, ct);
            var colors = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (oid, block) in cached)
            {
                var color = ChessableTrainingStart.SolverColorOf(block);
                if (color is not null) colors[oid] = color;
            }
            if (colors.Count == 0) return pgn;
            _logger.LogInformation(
                "Kurs aus PGN: Trainingsstart für {Found} von {Asked} Chessable-Linien aus dem Linien-Cache ergänzt.",
                colors.Count, oids.Count);
            return ChessableTrainingStart.InsertColors(pgn, colors);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Kurs aus PGN: Trainingsstart für {Count} Chessable-Linien nicht abrufbar — Linien behalten den bisherigen Start.",
                oids.Count);
            return pgn;
        }
    }

    /// <summary>Die Merkmale, die ein Buch zum persönlichen Kurs EINES Users machen — an beiden
    /// Entstehungswegen (PGN-Upload und leerer Kurs) identisch.</summary>
    private static void ApplyPersonalCourseMetadata(Book book, int userId, string name, DateTime nowUtc)
    {
        book.OwnerUserId = userId;
        book.DisplayName = name;
        book.Kind = BookKind.Study;
        book.Tags = "personal";
        book.UpdatedAt = nowUtc;
    }

    /// <summary>Der Listeneintrag eines GERADE angelegten eigenen Kurses (noch nichts gelöst, kein Modus) —
    /// an beiden Entstehungswegen (PGN-Upload und leerer Kurs) identisch.</summary>
    internal static CourseListItemDto NewOwnedCourseItem(Book book, int puzzleCount) => new()
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

    /// <summary>Löscht einen eigenen (selbst hochgeladenen bzw. importierten) Kurs des Users samt
    /// Fortschritt/Puzzles. Nur der Besitzer darf löschen; fremde/globale Bücher → 404.</summary>
    public async Task DeletePersonalCourseAsync(int userId, int bookId)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == bookId);
        if (book is null || book.OwnerUserId != userId)
            throw new NotFoundException("Book not found.");
        await _bookAdmin.DeleteBookAsync(bookId);
    }
}
