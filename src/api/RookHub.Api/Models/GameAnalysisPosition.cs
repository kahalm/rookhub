using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// EINE Stellung einer <see cref="GameAnalysis"/>: die Stellung VOR dem Halbzug, der Zug, der in der
/// Partie folgte, und — sobald die Engine fertig ist — die Kandidatenliste.
///
/// <para><see cref="CandidatesJson"/> ist das, was die Wertung braucht (<see cref="Services.GuessScoring"/>):
/// <c>[{"uci":"e2e4","cp":30},{"uci":"d2d4","mate":3}]</c>, Bewertung <b>aus Sicht der Seite am Zug</b>.
/// Der Broker liefert sie aus Weiß-Sicht — die Umrechnung passiert beim Einlesen, damit später nicht
/// jede Auswertung erneut daran denken muss.</para>
/// </summary>
public class GameAnalysisPosition
{
    public int Id { get; set; }

    public int GameAnalysisId { get; set; }
    public GameAnalysis? GameAnalysis { get; set; }

    /// <summary>0-basierter Halbzug: 0 = vor dem ersten Zug von Weiß.</summary>
    public int Ply { get; set; }

    [Required, MaxLength(120)] public string Fen { get; set; } = string.Empty;

    /// <summary>Der in der Partie gespielte Zug (Standard-UCI, Rochade als <c>e1g1</c>).</summary>
    [Required, MaxLength(10)] public string GameMoveUci { get; set; } = string.Empty;

    [Required, MaxLength(16)] public string GameMoveSan { get; set; } = string.Empty;

    /// <summary>Laufender Analyse-Auftrag (nur solange die Stellung noch rechnet). Ohne FK: der
    /// Auftrag darf weggeräumt werden, sobald sein Ergebnis hier kopiert ist.</summary>
    public int? AnalysisJobId { get; set; }

    /// <summary>Kandidatenliste, siehe Klassenkommentar. <c>null</c> = noch nicht analysiert.</summary>
    public string? CandidatesJson { get; set; }

    /// <summary>Erreichte Suchtiefe der übernommenen Zeile.</summary>
    public int Depth { get; set; }

    /// <summary>Bewertung der Hauptvariante als Text (<c>+0.34</c> / <c>#3</c>) — damit Listen die
    /// (großen) Kandidatenlisten nicht laden müssen. Aus Sicht der Seite am Zug.</summary>
    [MaxLength(16)] public string? EvalText { get; set; }

    public DateTime? AnalyzedAt { get; set; }
}
