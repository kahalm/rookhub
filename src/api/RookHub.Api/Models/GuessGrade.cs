namespace RookHub.Api.Models;

/// <summary>
/// Bewertung EINES geratenen Zuges in der Punktepartie — eine benannte STUFE, keine freie Zahl.
/// Gemessen wird immer gegen den TATSÄCHLICH GESPIELTEN Partiezug, nicht gegen den Engine-Zug:
/// die Übung heißt „errate den Zug", die Engine urteilt nur über die Alternativen.
///
/// <para>Gespeichert wird die Stufe, die Punkte entstehen ausschließlich in
/// <see cref="GuessGrades.PointsFor"/> — dieselbe Trennung wie bei <see cref="CalculationGrade"/>:
/// eine spätere Neugewichtung passiert an einer Stelle und schreibt die Vergangenheit nicht um.
/// Der Name der Stufe (camelCase) ist zugleich der i18n-Schlüssel im Frontend.</para>
///
/// <para>Die Reihenfolge ist aufsteigend nach Güte, damit sich Läufe vergleichen lassen; die
/// Punkte sind bewusst NICHT linear (siehe <see cref="GuessGrades.PointsFor"/>).</para>
/// </summary>
public enum GuessGrade
{
    /// <summary>Deutlich schlechter als der Partiezug (Vorgabe: mehr als
    /// <see cref="GuessScoring.MuchWorsePawns"/> Bauerneinheiten).</summary>
    MuchWorse = 0,

    /// <summary>Schlechter als der Partiezug, aber kein Patzer — die Lücke zwischen „ähnlich"
    /// und „deutlich schlechter".</summary>
    Worse = 1,

    /// <summary>Ein ANDERER Zug, aber praktisch gleich gut (Vorgabe: höchstens
    /// <see cref="GuessScoring.SimilarPawns"/> Unterschied).</summary>
    Similar = 2,

    /// <summary>Genau der Partiezug.</summary>
    GameMove = 3,

    /// <summary>Genau der Partiezug — UND jede Alternative ist mindestens
    /// <see cref="GuessScoring.OnlyMoveGapPawns"/> schlechter. Der Zug war also der einzige, der
    /// die Stellung hält; ihn zu finden ist mehr wert als einen von mehreren guten zu treffen.</summary>
    OnlyMove = 4,

    /// <summary>Besser als der Partiezug.</summary>
    Better = 5,

    /// <summary>Eindeutig besser als der Partiezug (Vorgabe: mindestens
    /// <see cref="GuessScoring.ClearlyBetterPawns"/> Bauerneinheiten).</summary>
    ClearlyBetter = 6,
}

/// <summary>
/// Die EINE Stelle, an der aus einer <see cref="GuessGrade"/> Punkte werden.
/// </summary>
public static class GuessGrades
{
    public const int Min = (int)GuessGrade.MuchWorse;
    public const int Max = (int)GuessGrade.ClearlyBetter;

    /// <summary>Punkte je Stufe (Vorgabe des Betreibers, 2026-09-05).</summary>
    public static int PointsFor(GuessGrade grade) => grade switch
    {
        GuessGrade.MuchWorse     => -2,
        GuessGrade.Worse         => 0,
        GuessGrade.Similar       => 2,
        GuessGrade.GameMove      => 5,
        GuessGrade.OnlyMove      => 8,
        GuessGrade.Better        => 8,
        GuessGrade.ClearlyBetter => 10,
        _ => 0,
    };

    /// <summary>Höchstpunktzahl je Stellung — Bezugsgröße für „x von y" und den Par-Vergleich.</summary>
    public const int MaxPointsPerMove = 10;

    public static bool IsValid(int grade) => grade >= Min && grade <= Max;
}
