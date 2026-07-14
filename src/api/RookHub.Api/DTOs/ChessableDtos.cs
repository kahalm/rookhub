namespace RookHub.Api.DTOs;

/// <summary>Speichert/aktualisiert den Chessable-Bearer eines Users.</summary>
public record SaveChessableBearerRequest(string Bearer);

/// <summary>ADMIN: Kurs im Namen eines Users holen. <paramref name="Target"/> = "repertoire" oder "book"
/// (Default "repertoire", falls leer); optional ein Name. Besitzer des Ergebnisses ist immer der Admin.</summary>
public record AdminChessableImportRequest(string? Name, string? Target = null);

/// <summary>ADMIN: Ein User mit hinterlegtem Chessable-Bearer (Auswahl für „Kurse holen").
/// <paramref name="Blocked"/>=true ⇒ Circuit-Breaker offen (Bearer abgewiesen, bis „Testen" bestätigt).</summary>
public record ChessableCredentialedUserDto(
    int UserId, string Username, System.DateTime? CoursesCachedAt, bool Blocked, string? BlockedReason);

/// <summary>Ob der User den Chessable-Haftungsausschluss bestätigt hat.</summary>
public record ChessableDisclaimerDto(bool Accepted);

/// <summary>Antwort der credentials-Endpoints. Bearer wird nur maskiert zurueckgegeben.
/// <paramref name="Blocked"/>=true ⇒ der Circuit-Breaker ist offen (Bearer wurde von Chessable als
/// gesperrt/gelöscht bzw. tot abgewiesen) — es laufen keine Anfragen mehr, bis „Testen" die
/// Gültigkeit bestätigt; <paramref name="BlockedReason"/> trägt die auslösende Meldung.</summary>
public record ChessableCredentialResponse(
    bool HasCredentials, string? MaskedBearer, bool Blocked = false, string? BlockedReason = null);

/// <summary>Ein Chessable-Kurs (vom piratechess-Backend durchgereicht).</summary>
public record ChessableCourseDto(string Bid, string Name)
{
    /// <summary>Dieser Kurs wurde vom User bereits als Repertoire importiert.</summary>
    public bool ImportedRepertoire { get; init; }
    /// <summary>Dieser Kurs wurde vom User bereits als Buch importiert.</summary>
    public bool ImportedBook { get; init; }
    /// <summary>Rohdaten liegen in der piratechess-DB (gecacht) → Import quasi sofort (kein Chessable-Abruf).</summary>
    public bool Cached { get; init; }
    /// <summary>Für diesen Kurs läuft bereits ein Import (Status "running": wartend oder in Arbeit) →
    /// nicht erneut einreihen.</summary>
    public bool Queued { get; init; }
}

/// <summary>Kursliste + Zeitpunkt des Abrufs (aus dem DB-Cache oder frisch geholt).</summary>
public record ChessableCoursesDto(List<ChessableCourseDto> Courses, DateTime? CachedAt);

/// <summary>Test-Ergebnis: gibt die uid des Bearers und die Anzahl der Kurse zurueck.</summary>
public record ChessableTestResultDto(string Uid, int CourseCount);

/// <summary>Antwort des piratechess /api/chessable/direct/course-Endpoints (tiefer Kurs-Abruf, synchron).
/// Dieselbe Form liefert auch der fetch-freie /course/parse-Endpoint (Browser-Import).</summary>
public record ChessableCourseDataDto(string Bid, string Name, string Mode, int ChapterCount, int LineCount, string Pgn);

/// <summary>Browser-Import („Über meinen Browser holen"): die RepCheck-Extension hat die rohen Chessable-
/// Antworten als eigene eingeloggte Session geholt (V2 aktiv) bzw. beim Training mitgeschnitten (V1 passiv)
/// und schickt sie je Kapitel — die getList-Antwort (<see cref="ChessableIngestChapter.ChapterJson"/>) plus
/// die getGame-Antworten (<see cref="ChessableIngestChapter.Lines"/>) in getList-Reihenfolge. RookHub reicht
/// sie an den fetch-freien piratechess-Parser durch und importiert das PGN als Repertoire (<c>Target</c>
/// "repertoire", Default) bzw. Buch/Kurs ("book"). Kein serverseitiger Chessable-Abruf/VPN.</summary>
public record ChessableIngestRequest(string Bid, string? Target, string? CourseName, List<ChessableIngestChapter>? Chapters);
public record ChessableIngestChapter(string? ChapterJson, List<string>? Lines);

/// <summary>Ergebnis eines Browser-Imports (analog zum Server-Import, aber synchron).</summary>
public record ChessableIngestResultDto(
    int ImportId, string Target, int? ResultId, string CourseName, int Imported, int Skipped, int Invalid, int LineCount);

/// <summary>Ein Kapitel-Chunk des kapitelweisen Browser-Imports. Die Extension streamt einen großen Kurs
/// Kapitel für Kapitel (bounded pro Request); der Server sammelt sie je <c>SessionId</c> und importiert
/// erst beim Chunk mit <c>Final=true</c> den GANZEN Kurs (korrekte Kapitel-/Round-Reihenfolge).
/// <c>Bid</c>/<c>Target</c>/<c>CourseName</c> werden vom ersten Chunk übernommen. <c>SessionId</c> ist
/// eine clientseitige GUID; Sessions sind pro (User, SessionId) isoliert.</summary>
public record ChessableIngestChunkRequest(
    string SessionId, string Bid, string? Target, string? CourseName, ChessableIngestChapter? Chapter, bool Final);

/// <summary>Antwort auf einen NICHT-finalen Chunk: bisher gepufferte Kapitel/Linien.</summary>
public record ChessableIngestChunkAck(bool Done, int Chapters, int Lines);

/// <summary>Antwort von piratechess /direct/course/start (async).</summary>
public record ChessableCourseStartDto(string JobId);

/// <summary>Vorab-Schätzung der Gesamt-Linienzahl eines Kurses (für die Admin-Kursliste).
/// <c>Cached</c>=true → sofort verfügbar (kein Chessable-Abruf nötig).</summary>
public record ChessableCourseInfoDto(string Bid, int TotalLines, bool Cached);

/// <summary>Fortschritt/Ergebnis eines piratechess-Kurs-Abruf-Jobs (/direct/course/{jobId}).</summary>
public record ChessableCourseProgressDto(
    string Status, int ChaptersDone, int ChaptersTotal, int LinesDone, int LinesTotal,
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
    int LinesTotal,
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
    int LinesTotal,
    int QueuedAhead,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt);
