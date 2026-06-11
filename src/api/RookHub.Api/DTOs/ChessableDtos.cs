namespace RookHub.Api.DTOs;

/// <summary>Speichert/aktualisiert den Chessable-Bearer eines Users.</summary>
public record SaveChessableBearerRequest(string Bearer);

/// <summary>Antwort der credentials-Endpoints. Bearer wird nur maskiert zurueckgegeben.</summary>
public record ChessableCredentialResponse(bool HasCredentials, string? MaskedBearer);

/// <summary>Ein Chessable-Kurs (vom piratechess-Backend durchgereicht).</summary>
public record ChessableCourseDto(string Bid, string Name);

/// <summary>Test-Ergebnis: gibt die uid des Bearers und die Anzahl der Kurse zurueck.</summary>
public record ChessableTestResultDto(string Uid, int CourseCount);
