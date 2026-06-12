namespace RookHub.Api.DTOs;

/// <summary>Speichert/aktualisiert den Chessable-Bearer eines Users.</summary>
public record SaveChessableBearerRequest(string Bearer);

/// <summary>Antwort der credentials-Endpoints. Bearer wird nur maskiert zurueckgegeben.</summary>
public record ChessableCredentialResponse(bool HasCredentials, string? MaskedBearer);

/// <summary>Ein Chessable-Kurs (vom piratechess-Backend durchgereicht).</summary>
public record ChessableCourseDto(string Bid, string Name);

/// <summary>Test-Ergebnis: gibt die uid des Bearers und die Anzahl der Kurse zurueck.</summary>
public record ChessableTestResultDto(string Uid, int CourseCount);

/// <summary>Antwort des piratechess /api/chessable/direct/course-Endpoints (tiefer Kurs-Abruf).</summary>
public record ChessableCourseDataDto(string Bid, string Name, string Mode, int ChapterCount, int LineCount, string Pgn);

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
    int Invalid);
