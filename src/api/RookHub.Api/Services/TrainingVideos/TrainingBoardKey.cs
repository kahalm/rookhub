namespace RookHub.Api.Services.TrainingVideos;

/// <summary>
/// Suchschlüssel einer Stellung für die Trainingsvideos: nur das Figurenfeld der FEN (erstes Feld). Zugrecht, Rochade
/// und Zähler zählen bewusst nicht — die aufbereiteten Dateien raten sie teils, und „wurde diese Stellung besprochen?“
/// meint das Brett.
/// </summary>
public static class TrainingBoardKey
{
    /// <summary>Figurenfeld aus einer vollen FEN oder einem reinen Figurenfeld; <c>null</c>, wenn es kein gültiges Brett ist.</summary>
    public static string? Of(string? fen)
    {
        if (string.IsNullOrWhiteSpace(fen)) return null;
        var board = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return FenListParser.NormalizeFen(board + " w") is null ? null : board;
    }

    /// <summary>Zugrecht aus der FEN: <c>'w'</c>/<c>'b'</c>, <c>null</c> ohne Zugfeld.</summary>
    public static char? Side(string? fen)
    {
        if (string.IsNullOrWhiteSpace(fen)) return null;
        var parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        return FenFields.WhiteToMove(fen) ? 'w' : 'b';
    }
}
