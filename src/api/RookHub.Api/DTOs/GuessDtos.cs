namespace RookHub.Api.DTOs;

public class CreateGuessSessionRequest
{
    public int GameAnalysisId { get; set; }
    /// <summary>Geratene Seite; Vorgabe Weiß.</summary>
    public bool? GuessWhite { get; set; }
    /// <summary>Erster zu ratender Halbzug; Vorgabe = nach der Eröffnung.</summary>
    public int? StartPly { get; set; }
}

public class GuessMoveRequest
{
    /// <summary>Der geratene Zug in UCI (<c>e2e4</c>, Umwandlung <c>e7e8q</c>).</summary>
    public string? Uci { get; set; }
    /// <summary>Seit der letzten Meldung verbrauchte Sekunden (der Server addiert).</summary>
    public int? AddSeconds { get; set; }
}

/// <summary>Zustand einer Sitzung — das, was der Client zum Weiterspielen braucht.
/// <b>Ohne</b> Partiezug und ohne Kandidatenliste: beides wäre die Lösung.</summary>
public class GuessSessionDto
{
    public int Id { get; set; }
    public int GameAnalysisId { get; set; }
    public string? Title { get; set; }
    public string? White { get; set; }
    public string? Black { get; set; }
    public bool GuessWhite { get; set; }
    public int StartPly { get; set; }
    /// <summary>running · done</summary>
    public string Status { get; set; } = "running";

    /// <summary>Punkte bisher und das bis hierhin Erreichbare (immer als „x von y").</summary>
    public int Points { get; set; }
    public int MaxPoints { get; set; }
    public int MovesPlayed { get; set; }
    /// <summary>Wie oft der Partiezug exakt getroffen wurde.</summary>
    public int GameMoveHits { get; set; }
    public int SecondsSpent { get; set; }

    /// <summary>Die zu ratende Stellung — <c>null</c>, wenn die Sitzung durch ist.</summary>
    public GuessPositionDto? Position { get; set; }
    /// <summary>Wie viele Halbzüge der geratenen Seite insgesamt anstehen (für den Fortschritt).</summary>
    public int TotalGuesses { get; set; }
}

public class GuessPositionDto
{
    public int Ply { get; set; }
    public int MoveNumber { get; set; }
    public bool WhiteToMove { get; set; }
    public string Fen { get; set; } = string.Empty;
    /// <summary>Der Zug DAVOR (zum Hervorheben auf dem Brett) — nicht der zu ratende.</summary>
    public string? LastMoveUci { get; set; }
}

/// <summary>Antwort auf einen Rateversuch: Bewertung + was tatsächlich gespielt wurde.</summary>
public class GuessResultDto
{
    /// <summary>Stufe als camelCase-Name (zugleich i18n-Schlüssel); <c>null</c> = übersprungen
    /// oder Stellung nicht wertbar.</summary>
    public string? Grade { get; set; }
    public int Points { get; set; }
    /// <summary>Der eigene Zug in der Schreibweise des Bretts.</summary>
    public string? PlayedSan { get; set; }
    /// <summary>Der Zug, der in der Partie folgte.</summary>
    public string GameMoveSan { get; set; } = string.Empty;
    public string GameMoveUci { get; set; } = string.Empty;
    /// <summary>Die Antwort des Gegners (automatisch nachgespielt), falls es eine gab.</summary>
    public string? ReplySan { get; set; }
    public string? ReplyUci { get; set; }
    /// <summary>Unterschied zum Partiezug in Centipawns (Anzeige „warum diese Punkte?").</summary>
    public int? DiffCp { get; set; }
    /// <summary>Bewertung der Stellung nach dem Partiezug, aus Sicht der geratenen Seite.</summary>
    public string? EvalText { get; set; }
    /// <summary>Der neue Zustand — inklusive nächster Stellung.</summary>
    public GuessSessionDto Session { get; set; } = new();
}

/// <summary>Eine Zeile des Rückblicks nach dem Ende.</summary>
public class GuessReviewMoveDto
{
    public int Ply { get; set; }
    public int MoveNumber { get; set; }
    public bool White { get; set; }
    public string GameSan { get; set; } = string.Empty;
    public string? PlayedSan { get; set; }
    public string? Grade { get; set; }
    public int Points { get; set; }
    public int? DiffCp { get; set; }
    public int SecondsSpent { get; set; }
}
