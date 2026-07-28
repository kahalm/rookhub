namespace RookHub.Api.DTOs;

/// <summary>
/// Eine Stellung eines Kalkulationsbuchs in der Übersichtsliste — bewusst LEICHT (ohne FEN,
/// Kommentar und vor allem ohne <c>Moves</c>): im Kalkulations-Modus gibt es keine Lösung, also
/// verlässt die gespeicherte Zugfolge den Server gar nicht erst.
/// </summary>
public class CalcPositionListItemDto
{
    public int Id { get; set; }
    public string Round { get; set; } = string.Empty;
    public string? Title { get; set; }
    /// <summary>Kapitel der Linie (<c>null</c> = ohne Kapitel) — fürs Gruppieren in der Sprungliste.</summary>
    public string? Chapter { get; set; }
    /// <summary>Der Nutzer hat zu dieser Stellung schon einen Analysebaum gespeichert.</summary>
    public bool HasTree { get; set; }
}

/// <summary>Kopf + Stellungsliste eines Buchs für den Kalkulations-Modus.</summary>
public class CalcBookDto
{
    public int BookId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Ist das Buch als Kalkulationsbuch markiert (<c>Book.IsCalculation</c>)? Der Modus
    /// funktioniert auch ohne das Flag (Direkt-Link), die Kursübersicht bietet ihn aber nur dann an.</summary>
    public bool IsCalculation { get; set; }
    public List<CalcPositionListItemDto> Positions { get; set; } = new();
}

/// <summary>
/// Eine einzelne Stellung inkl. eigenem Analysebaum. Enthält NIE die Lösungszüge: geliefert wird
/// nur die Ausgangs-FEN und — bei Buchlinien mit Trainingsstart mitten in der Partie — die Züge
/// BIS zum Trainingsstart (<see cref="SetupMoves"/>), damit das Frontend die Ausgangsstellung
/// nachstellen kann. Alles danach (die eigentliche Lösung) bleibt auf dem Server.
/// </summary>
public class CalcPositionDto
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string Round { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Chapter { get; set; }
    /// <summary>FEN aus dem PGN-Header (Ausgangspunkt, ggf. noch vor den <see cref="SetupMoves"/>).</summary>
    public string Fen { get; set; } = string.Empty;
    /// <summary>Züge (UCI, leerzeichengetrennt) von <see cref="Fen"/> bis zur Aufgabenstellung; leer
    /// bei reinen Stellungs-Linien (Kalkulationsbuch) und bei Puzzles mit <c>StartPly &lt; 0</c>.</summary>
    public string SetupMoves { get; set; } = string.Empty;
    /// <summary>Optionaler Erklär-/Aufgabentext zur Stellung (<c>BookPuzzle.Comment</c>).</summary>
    public string? Comment { get; set; }
    /// <summary>Eigener Analysebaum als JSON; <c>null</c> = noch keiner gespeichert.</summary>
    public string? TreeJson { get; set; }
    public DateTime? TreeUpdatedAt { get; set; }
}

/// <summary>Eingabe: den eigenen Analysebaum zu einer Stellung speichern (Upsert).</summary>
public class SaveCalcTreeDto
{
    /// <summary>Serialisierter Baum. Muss gültiges JSON sein und darf
    /// <c>CalculationService.MaxTreeJsonLength</c> nicht überschreiten.</summary>
    public string TreeJson { get; set; } = string.Empty;
}

/// <summary>Antwort nach dem Speichern eines Analysebaums.</summary>
public class CalcTreeSavedDto
{
    public int BookPuzzleId { get; set; }
    public DateTime UpdatedAt { get; set; }
}
