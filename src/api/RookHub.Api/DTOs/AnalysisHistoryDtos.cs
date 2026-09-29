namespace RookHub.Api.DTOs;

/// <summary>
/// Ein Knoten des Zugbaums (0.604.0) in FLACHER Form: <see cref="P"/> ist der Index des Elternknotens in derselben Liste
/// (-1 = Ausgangsstellung) und liegt immer VOR dem Knoten; die Reihenfolge der Geschwister ist die Reihenfolge in der
/// Liste, das erste Kind ist die Fortsetzung. Flach, weil ein verschachtelter Baum je Halbzug zwei JSON-Ebenen braucht —
/// bei 600 Halbzügen weit über der Schachtelungsgrenze des Serialisierers (64).
/// </summary>
public class AnalysisTreeNodeDto
{
    public int P { get; set; } = -1;
    /// <summary>Zug als UCI (Rochade auch König-schlägt-Turm, gespeichert als e1g1).</summary>
    public string U { get; set; } = string.Empty;
    /// <summary>Mit Stern markiert.</summary>
    public bool? S { get; set; }
    /// <summary>Zuletzt gesehene Bewertung der Stellung NACH dem Zug („+0.25", „#-3"), Weiß-Sicht.</summary>
    public string? E { get; set; }
}

public class AnalysisTreeDto
{
    /// <summary>Ausgangsstellung mit Stern markiert.</summary>
    public bool? S { get; set; }
    public List<AnalysisTreeNodeDto>? N { get; set; }
}

public class SaveAnalysisHistoryRequest
{
    public int? Id { get; set; }
    public string? StartFen { get; set; }
    public string? Title { get; set; }
    public AnalysisTreeDto? Tree { get; set; }
    /// <summary>Index des Knotens, an dem man stand (-1 = Ausgangsstellung).</summary>
    public int Current { get; set; } = -1;
}

/// <param name="Moves">Die HAUPTLINIE als UCI.</param>
/// <param name="Ply">Tiefe des Knotens, an dem man stand.</param>
/// <param name="Preview">Die ersten Halbzüge der Hauptlinie nummeriert als SAN („1.e4 e5 2.Nf3 …") — für die Liste.</param>
/// <param name="NodeCount">Alle Züge des Baums, Varianten eingeschlossen.</param>
/// <param name="Tree">Der ganze Baum — nur beim Abruf EINES Eintrags und nach dem Speichern, nicht in der Liste.</param>
public record AnalysisHistoryEntryDto(int Id, string StartFen, IReadOnlyList<string> Moves, int Ply, string? Title,
    string Preview, int MoveCount, int NodeCount, int StarCount, AnalysisTreeDto? Tree, int Current,
    DateTime CreatedAt, DateTime UpdatedAt);
