using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;

namespace RookHub.Api.Services;

/// <summary>
/// Kurs ⇄ Repertoire umwandeln — BEIDE Richtungen an EINER Stelle. Sie sind dieselbe Operation mit
/// vertauschten Rollen (Quell-PGN holen, Ziel daraus anlegen, Original entfernen), lagen aber in
/// verschiedenen Schichten: „Kurs → Repertoire" im <see cref="CourseService"/>, „Repertoire → Kurs"
/// ausgeschrieben im <see cref="Controllers.RepertoireController"/> — samt Besitzer-Prüfung,
/// Leer-Prüfung und Reihenfolge des Löschens. Eine Regel, die nur in einem Controller steht, ist für
/// jeden anderen Aufrufer unerreichbar und für einen Service-Test unsichtbar.
/// </summary>
/// <remarks>
/// <para><b>DI-Falle:</b> <see cref="CourseService"/> hängt bereits an <see cref="RepertoireService"/>.
/// Deshalb darf der Repertoire-Dienst NICHT umgekehrt den Kurs-Dienst bekommen (Zyklus, den der
/// Container erst beim ersten Auflösen meldet). Dieser Dienst hängt an beiden und wird nur von den
/// Controllern gerufen — er ist die einzige Stelle, an der sich die zwei Seiten begegnen.</para>
/// <para><b>Beides VERSCHIEBT.</b> Das Original wird erst NACH erfolgreichem Anlegen des Ziels
/// entfernt: scheitert die Umwandlung, steht der Nutzer wieder da, wo er war. Eine Ausnahme ist
/// deshalb hier kein Schönheitsfehler, sondern das, was die Quelle rettet.</para>
/// </remarks>
public class CourseRepertoireConversionService
{
    private readonly AppDbContext _db;
    private readonly CourseService _courses;
    private readonly RepertoireService _repertoires;
    private readonly BookAdminService _bookAdmin;

    public CourseRepertoireConversionService(AppDbContext db, CourseService courses, RepertoireService repertoires, BookAdminService bookAdmin)
    {
        _db = db;
        _courses = courses;
        _repertoires = repertoires;
        _bookAdmin = bookAdmin;
    }

    /// <summary>„Kurs → Repertoire umwandeln" (Verschieben): legt aus dem Kurs-PGN (inkl. Varianten/
    /// Kommentaren, wenn <see cref="Models.BookSource.SourcePgn"/> vorhanden) ein neues Repertoire des Users an und
    /// ENTFERNT den Original-Kurs, sofern es ein persönlicher (eigener) Kurs ist
    /// (<c>Book.OwnerUserId == userId</c>). Geteilte Gruppen-/Admin-Bücher werden NICHT gelöscht (gehören
    /// dem User nicht) — dann bleibt der Kurs bestehen. Zugriff wird geprüft (kein Zugriff → 404).</summary>
    public async Task<RepertoireDto> ConvertCourseToRepertoireAsync(int userId, int bookId, bool isAdmin)
    {
        var (pgn, fileName) = await _courses.GetBookPgnAsync(userId, bookId, isAdmin); // prüft Zugriff
        var book = await _db.Books.FirstAsync(b => b.Id == bookId);
        var repo = await _repertoires.CreateFromPgnAsync(userId, book.DisplayName ?? "Kurs", fileName, pgn);
        // Verschieben statt Kopieren: eigenen Kurs nach erfolgreicher Umwandlung entfernen.
        if (book.OwnerUserId == userId)
            await _bookAdmin.DeleteBookAsync(bookId);
        return repo;
    }

    /// <summary>„Repertoire → Kurs umwandeln" (Verschieben): legt aus dem kombinierten Repertoire-PGN
    /// einen persönlichen Kurs an und ENTFERNT anschließend das Original-Repertoire. Funktioniert nur mit
    /// Puzzle-PGN im Chessable-Stil (FEN + Round + Trainingsmarker je Zug); ein reines Eröffnungs-
    /// Repertoire ohne Puzzle-Marker wirft <see cref="InvalidOperationException"/> (→ 400) — dann bleibt
    /// das Repertoire erhalten. Ein noch LEERES Repertoire (nie eine PGN importiert) wirft eine
    /// <see cref="CourseConversionException"/> mit <c>Code = "repertoire_empty"</c>.</summary>
    /// <exception cref="KeyNotFoundException">Das Repertoire gehört dem Nutzer nicht (→ 404).</exception>
    public async Task<CourseListItemDto> ConvertRepertoireToCourseAsync(int userId, int repertoireId)
    {
        // Umwandeln VERSCHIEBT (löscht das Original) → nur der Besitzer, nicht ein Freigabe-Empfänger.
        if (!await _repertoires.IsOwnerAsync(repertoireId, userId))
            throw new KeyNotFoundException("Repertoire not found.");

        var detail = await _repertoires.GetByIdAsync(repertoireId, userId);
        var pgn = await _repertoires.GetCombinedPgnAsync(repertoireId, userId);

        // Ein LEERES Repertoire (angelegt, aber nie eine PGN importiert) ist ein anderer Fall als
        // „PGN vorhanden, aber ohne Puzzle-Linien". Ohne eigenen Code landen beide in derselben
        // 400-Meldung des Frontends („keine Puzzle-Linien"), die dem Nutzer das falsche Problem
        // nennt und den eigentlichen nächsten Schritt (PGN hochladen / Kurs über die Extension
        // holen) verschweigt. Siehe TODO.md, Fund aus den Prod-Logs vom 2026-09-05.
        if (string.IsNullOrWhiteSpace(pgn))
            throw new CourseConversionException("Repertoire is empty - import a PGN first.", "repertoire_empty");

        var course = await _courses.UploadPersonalCourseAsync(userId, detail.Name + ".pgn", pgn, detail.Name);
        // Verschieben statt Kopieren: das Original-Repertoire nach erfolgreicher Umwandlung entfernen.
        await _repertoires.DeleteAsync(repertoireId, userId);
        return course;
    }
}

/// <summary>
/// Eine gescheiterte Umwandlung, deren Grund das Frontend UNTERSCHEIDEN können muss — der Text
/// allein reicht dafür nicht (er ist übersetzbar und ändert sich). Erbt von
/// <see cref="InvalidOperationException"/>, damit der bestehende 400-Zweig der Controller sie ohne
/// Sonderbehandlung fängt; wer das <see cref="Code"/>-Feld ausliefern will, fängt sie VORHER.
/// </summary>
public class CourseConversionException : InvalidOperationException
{
    public CourseConversionException(string message, string code) : base(message) => Code = code;

    /// <summary>Maschinenlesbarer Grund (heute nur <c>repertoire_empty</c>) — Teil des HTTP-Vertrags.</summary>
    public string Code { get; }
}
