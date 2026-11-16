namespace RookHub.Api.Models;

/// <summary>Eine in der Trainingsstunde besprochene Partie (eigene Partie, Lehrbeispiel oder Übung).</summary>
public class TrainingLessonGame
{
    public int Id { get; set; }

    public int LessonId { get; set; }
    public TrainingLesson? Lesson { get; set; }

    /// <summary>Kennung innerhalb der Datei (<c>g1</c>); Segmente und Notizen verweisen darauf.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary><c>own</c> | <c>lesson</c> | <c>exercise</c>.</summary>
    public string Kind { get; set; } = "lesson";

    public string? White { get; set; }
    public string? Black { get; set; }
    public string? Event { get; set; }
    public int? Year { get; set; }
    public string? Eco { get; set; }
    public string? Result { get; set; }

    /// <summary>Abschnitt im Video in Sekunden.</summary>
    public int? FromSec { get; set; }
    public int? ToSec { get; set; }

    public string? Pgn { get; set; }

    /// <summary>Der volle Analysebaum (ChessBase-PGN mit Varianten), falls geliefert.</summary>
    public string? AnalysisPgn { get; set; }

    /// <summary>Die zugehörige RookHub-Partie (<see cref="SavedGame"/>), falls zugeordnet — bewusst ohne Fremdschlüssel:
    /// die Datei stammt von außen, die Partie kann fehlen oder gelöscht werden.</summary>
    public int? RookhubGameId { get; set; }

    /// <summary><c>high</c> | <c>low</c> | <c>none</c>.</summary>
    public string? MatchConfidence { get; set; }

    public int Position { get; set; }
}
