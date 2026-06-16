namespace RookHub.Api.DTOs;

/// <summary>Speichert/aktualisiert den Chessable-Bearer eines Users.</summary>
public record SaveChessableBearerRequest(string Bearer);

/// <summary>ADMIN: Kurs im Namen eines Users holen. <paramref name="Target"/> = "repertoire" oder "book"
/// (Default "repertoire", falls leer); optional ein Name. Besitzer des Ergebnisses ist immer der Admin.</summary>
public record AdminChessableImportRequest(string? Name, string? Target = null);

/// <summary>ADMIN: Ein User mit hinterlegtem Chessable-Bearer (Auswahl für „Kurse holen").</summary>
public record ChessableCredentialedUserDto(int UserId, string Username, System.DateTime? CoursesCachedAt);

/// <summary>Ob der User den Chessable-Haftungsausschluss bestätigt hat.</summary>
public record ChessableDisclaimerDto(bool Accepted);

/// <summary>Antwort der credentials-Endpoints. Bearer wird nur maskiert zurueckgegeben.</summary>
public record ChessableCredentialResponse(bool HasCredentials, string? MaskedBearer);

/// <summary>Ein Chessable-Kurs (vom piratechess-Backend durchgereicht).</summary>
public record ChessableCourseDto(string Bid, string Name)
{
    /// <summary>Dieser Kurs wurde vom User bereits als Repertoire importiert.</summary>
    public bool ImportedRepertoire { get; init; }
    /// <summary>Dieser Kurs wurde vom User bereits als Buch importiert.</summary>
    public bool ImportedBook { get; init; }
    /// <summary>Rohdaten liegen in der piratechess-DB (gecacht) → Import quasi sofort (kein Chessable-Abruf).</summary>
    public bool Cached { get; init; }
}

/// <summary>Kursliste + Zeitpunkt des Abrufs (aus dem DB-Cache oder frisch geholt).</summary>
public record ChessableCoursesDto(List<ChessableCourseDto> Courses, DateTime? CachedAt);

/// <summary>Test-Ergebnis: gibt die uid des Bearers und die Anzahl der Kurse zurueck.</summary>
public record ChessableTestResultDto(string Uid, int CourseCount);

/// <summary>Antwort des piratechess /api/chessable/direct/course-Endpoints (tiefer Kurs-Abruf, synchron).</summary>
public record ChessableCourseDataDto(string Bid, string Name, string Mode, int ChapterCount, int LineCount, string Pgn);

/// <summary>Antwort von piratechess /direct/course/start (async).</summary>
public record ChessableCourseStartDto(string JobId);

/// <summary>Fortschritt/Ergebnis eines piratechess-Kurs-Abruf-Jobs (/direct/course/{jobId}).</summary>
public record ChessableCourseProgressDto(
    string Status, int ChaptersDone, int ChaptersTotal, int LinesDone,
    int ChapterCount, int LineCount, string? CourseName, string? Pgn, string? Error);

/// <summary>Startet einen Kurs-Import. Target: "repertoire" oder "book". Name optional (Anzeigename).</summary>
public record StartChessableImportRequest(string Target, string? Name);

/// <summary>Status/Fortschritt eines Chessable-Kurs-Imports (Polling).</summary>
public record ChessableImportDto(
    int Id,
    string Bid,
    string CourseName,
    string Target,
    string Status,
    string Phase,
    string? Error,
    int? ResultId,
    int Imported,
    int Skipped,
    int Invalid,
    int ChaptersDone,
    int ChaptersTotal,
    int LinesDone,
    int QueuedAhead,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt);

/// <summary>Import-Satz für die Admin-Ansicht: wie <see cref="ChessableImportDto"/>, zusätzlich
/// Besitzer (UserId/Username) und Zeitstempel (für „alle Jobs + Verlauf" und das Dashboard-Widget).</summary>
public record ChessableAdminImportDto(
    int Id,
    int UserId,
    string Username,
    string Bid,
    string CourseName,
    string Target,
    string Status,
    string Phase,
    string? Error,
    int? ResultId,
    int Imported,
    int Skipped,
    int Invalid,
    int ChaptersDone,
    int ChaptersTotal,
    int LinesDone,
    int QueuedAhead,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt);
