namespace RookHub.Api.Models;

/// <summary>
/// Ein Abschnitt der Trainingsstunde: Zeitspanne im Video, die gezeigte Stellung und der Trainerkommentar.
/// <see cref="BoardFen"/> ist das Figurenfeld der FEN allein (erstes Feld) und indiziert — darüber läuft die
/// Stellungssuche „im Training besprochen?“ und die Zuordnung zu den Zügen einer RookHub-Partie.
/// </summary>
public class TrainingSegment
{
    public int Id { get; set; }

    public int LessonId { get; set; }
    public TrainingLesson? Lesson { get; set; }

    /// <summary>Reihenfolge in der Datei.</summary>
    public int Position { get; set; }

    public int StartSec { get; set; }
    public int? EndSec { get; set; }

    /// <summary>Verweis auf <see cref="TrainingLessonGame.Key"/> derselben Stunde.</summary>
    public string? GameKey { get; set; }

    /// <summary>Volle FEN, wie geliefert (normalisiert auf sechs Felder).</summary>
    public string? Fen { get; set; }

    /// <summary>Nur das Figurenfeld der FEN (<c>rnbqkbnr/pppppppp/8/…</c>) — Suchschlüssel.</summary>
    public string? BoardFen { get; set; }

    /// <summary>Der besprochene Zug in SAN (<c>Bb5+</c>), falls einer genannt ist.</summary>
    public string? Move { get; set; }

    /// <summary><c>played</c> (der Partiezug) | <c>analysis</c> (ein Zug der Besprechung, gehört als Variante an die
    /// Stellung, nie als Partiezug) | <c>null</c> (nicht angegeben).</summary>
    public string? MoveKind { get; set; }

    /// <summary>Wo das Zitat im Video steht, falls abweichend von <see cref="StartSec"/>.</summary>
    public int? QuoteStartSec { get; set; }
    public int? QuoteEndSec { get; set; }

    public string? Comment { get; set; }
    public string? Quote { get; set; }
    public string? Variations { get; set; }

    /// <summary>Schlagworte, kommagetrennt (<c>question,key,mistake,plan,opening,endgame,tactic</c>).</summary>
    public string? Tags { get; set; }

    /// <summary>Kommentar + Zitat in einer Spalte für den Volltext-Index (MariaDB FULLTEXT).</summary>
    public string? SearchText { get; set; }

    public TrainingQuestion? Question { get; set; }
}
