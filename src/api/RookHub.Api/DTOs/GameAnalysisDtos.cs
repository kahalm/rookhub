namespace RookHub.Api.DTOs;

/// <summary>Anlegen einer Partie-Analyse — PGN plus optionale Abweichungen von den Vorgaben.</summary>
public class CreateGameAnalysisRequest
{
    public string? Pgn { get; set; }
    public string? Title { get; set; }
    /// <summary>Vorgabe 30 (siehe <c>GameAnalysisDefaults</c>); Tiefe 40 kostet grob das Zehnfache.</summary>
    public int? TargetDepth { get; set; }
    /// <summary>Vorgabe 5 = Protokoll-Maximum.</summary>
    public int? MultiPv { get; set; }
    /// <summary>Leer = Hintergrund-Engine aus dem Profil.</summary>
    public string? EngineId { get; set; }
}

public class GameAnalysisDto
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? White { get; set; }
    public string? Black { get; set; }
    public string? Result { get; set; }
    public string? Event { get; set; }
    public int TargetDepth { get; set; }
    public int MultiPv { get; set; }
    public string? EngineId { get; set; }
    /// <summary>pending · running · done · failed</summary>
    public string Status { get; set; } = "pending";
    public int PlyCount { get; set; }
    /// <summary>Wie viele Stellungen bereits ihre Kandidatenliste haben (Fortschritt).</summary>
    public int AnalyzedPlies { get; set; }
    public string? LastError { get; set; }
    /// <summary>Kuratierter Bestand: als Punktepartie fuer jeden spielbar, auch ohne Anmeldung.</summary>
    public bool IsPublic { get; set; }
    /// <summary>Traegt das Quell-PGN Kommentare? Grundlage des Filters „alle / nur kommentierte" in
    /// der Punktepartie-Auswahl. Ermittelt in SQL (<c>Pgn LIKE '%{%'</c>) statt aus einer eigenen
    /// Spalte: jede geschweifte Klammer in einem PGN IST ein Kommentar, und so bleibt das
    /// LONGTEXT-Feld ausserhalb der Antwort — geladen wuerde es sonst je Zeile.</summary>
    public bool Annotated { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    /// <summary>Nur im Detail-Abruf gefüllt.</summary>
    public List<GameAnalysisPositionDto>? Positions { get; set; }
}

/// <summary>Eine Stellung der Partie. BEWUSST ohne Kandidatenliste: die ist die Grundlage der
/// späteren Punktepartie und bleibt serverseitig — wer sie ausliefert, liefert die Lösung mit.</summary>
public class GameAnalysisPositionDto
{
    public int Ply { get; set; }
    public int MoveNumber { get; set; }
    public bool White { get; set; }
    public string San { get; set; } = string.Empty;
    public string Uci { get; set; } = string.Empty;
    public string Fen { get; set; } = string.Empty;
    public string? EvalText { get; set; }
    public int Depth { get; set; }
    public bool Analyzed { get; set; }
}

/// <summary>Kuratierten Bestand einer Partie-Analyse schalten (Besitzer/Admin).</summary>
public class SetGameAnalysisPublicRequest
{
    public bool IsPublic { get; set; }
}

/// <summary>Eine auf der PUNKTEPARTIE-Seite eingeworfene Partie. Bewusst NUR PGN und Titel: Tiefe,
/// Linienzahl und Engine setzt der Server (<c>GameAnalysisDefaults.GuessTargetDepth</c>). Ein Feld,
/// das der Server ohnehin ueberschreibt, waere ein Versprechen, das die Antwort bricht — und auf
/// fremder Rechenzeit hat der Einwerfer die Tiefe nicht zu bestimmen.</summary>
public class CreateGuessGameRequest
{
    public string? Pgn { get; set; }
    public string? Title { get; set; }
}

/// <summary>Warum ein Einwurf abgelehnt wurde. Ein GRUND und kein Satz: die Seite formuliert ihn in
/// der Sprache des Nutzers, der Server kennt sie nicht.</summary>
public static class GuessUploadReason
{
    /// <summary>Der Nutzer hat schon <c>MaxOpenGuessGamesPerUser</c> Partien in der Rechnung.</summary>
    public const string TooManyOpen = "too-many-open";
    /// <summary>Weder eigene Hintergrund-Engine noch freigegebene Haus-Engine.</summary>
    public const string NoEngine = "no-engine";
    /// <summary>Im Text steckt keine spielbare Partie.</summary>
    public const string InvalidPgn = "invalid-pgn";
}

/// <summary>Ergebnis eines Einwurfs: entweder die angelegte Analyse oder ein Grund.</summary>
public record GuessUploadResult(GameAnalysisDto? Analysis, string? Reason);

/// <summary>Ob und wie oft der Nutzer noch einwerfen darf.</summary>
public class GuessUploadStatusDto
{
    /// <summary>Ueberhaupt eine Engine da (eigene oder Haus)? Ohne sie zeigt die Seite das Feld gar nicht erst.</summary>
    public bool EngineAvailable { get; set; }
    /// <summary>Rechnet die eigene Maschine? Nur fuer den Hinweistext — die Tiefe bleibt so oder so fest.</summary>
    public bool OwnEngine { get; set; }
    public int OpenGames { get; set; }
    public int MaxGames { get; set; }
}
