namespace RookHub.Api.DTOs.Engines;

/// <summary>„Ohne Engine analysieren" (0.855.0): der eigene Zugbaum zu einer Partie samt Fortschritt der Hintergrundrechnung.</summary>
public class LineStudyDto
{
    public int Id { get; set; }
    public string GameKey { get; set; } = string.Empty;
    public string StartFen { get; set; } = string.Empty;
    public string? Title { get; set; }
    public AnalysisTreeDto Tree { get; set; } = new();
    public int Current { get; set; } = -1;
    /// <summary>Engine eingeschaltet? Erst dann gibt <c>GET …/evals</c> Zahlen heraus.</summary>
    public bool Revealed { get; set; }
    /// <summary>Gerechnete bzw. alle Stellungen des Baums (Ausgangsstellung eingeschlossen).</summary>
    public int Analyzed { get; set; }
    public int Total { get; set; }
}

/// <summary>Speichern (anlegen oder ersetzen). <see cref="GameKey"/> bildet der Client aus Ausgangsstellung + Partiezügen.</summary>
public class SaveLineStudyRequest
{
    public string? GameKey { get; set; }
    public string? StartFen { get; set; }
    public string? Title { get; set; }
    public AnalysisTreeDto? Tree { get; set; }
    public int Current { get; set; } = -1;
}

/// <summary>Bewertung der Stellung NACH dem Knoten <see cref="Index"/> des gespeicherten Baums (-1 = Ausgangsstellung), Weiß-Sicht
/// wie <c>GET …/evals</c> der Partie. Je Knotenindex statt je Stellungsschlüssel: Client und Server bilden den Schlüssel nicht
/// zeichengleich (en passant), die Reihenfolge des flachen Baums aber schon.</summary>
public class LineStudyEvalDto
{
    public int Index { get; set; }
    public int? Cp { get; set; }
    public int? Mate { get; set; }
    public string? BestUci { get; set; }
    public int Depth { get; set; }
}

/// <summary>Fortschritt; <see cref="Positions"/> nur nach dem Einschalten der Engine.</summary>
public class LineStudyEvalsDto
{
    public bool Revealed { get; set; }
    public int Analyzed { get; set; }
    public int Total { get; set; }
    public List<LineStudyEvalDto> Nodes { get; set; } = new();
}
