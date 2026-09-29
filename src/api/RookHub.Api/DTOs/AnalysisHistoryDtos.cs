namespace RookHub.Api.DTOs;

/// <summary>Stand des Analysebretts speichern. Ohne <see cref="Id"/> (oder mit einer fremden/verschwundenen) entsteht ein
/// neuer Eintrag — außer, dieselbe Stellung mit denselben Zügen steht schon im Verlauf, dann rückt DER nach oben.</summary>
public class SaveAnalysisHistoryRequest
{
    public int? Id { get; set; }
    public string? StartFen { get; set; }
    /// <summary>Zugfolge als UCI (Rochade als e1g1).</summary>
    public List<string>? Moves { get; set; }
    public int Ply { get; set; }
    public string? Title { get; set; }
    public List<int>? Starred { get; set; }
}

/// <param name="Preview">Die ersten Halbzüge nummeriert als SAN („1.e4 e5 2.Nf3 …") — für die Liste.</param>
public record AnalysisHistoryEntryDto(int Id, string StartFen, IReadOnlyList<string> Moves, int Ply, string? Title,
    IReadOnlyList<int> Starred, string Preview, int MoveCount, DateTime CreatedAt, DateTime UpdatedAt);
