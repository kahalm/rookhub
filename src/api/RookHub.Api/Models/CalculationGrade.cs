namespace RookHub.Api.Models;

/// <summary>
/// Selbstbewertung EINER durchgerechneten Stellung im Kalkulations-Modus — eine benannte STUFE,
/// keine freie Zahl. Der Nutzer vergibt sie, NACHDEM er die Lösung anderswo geprüft hat.
/// <para>Bewusst benannte Stufen statt „0 bis 10": eine Stufe ist reproduzierbar, „7 von 10" bedeutet
/// nächste Woche etwas anderes als heute. Der Name der Stufe (camelCase, also <c>notSolved</c>,
/// <c>someIdeas</c>, <c>moveNoMainLine</c>, <c>moveNoSideLines</c>, <c>solved</c>) ist zugleich der
/// i18n-Schlüssel im Frontend.</para>
/// <para>Die Reihenfolge ist Absicht: „Hauptfolge nicht gesehen" wiegt SCHWERER als „Nebenfolgen nicht
/// gesehen" — die Hauptfortsetzung ist der Kern der Rechnung, Nebenvarianten sind Kür.</para>
/// <para><b>Gespeichert wird die Stufe</b> (<c>CalculationTree.Grade</c>), nicht die Punktzahl: die
/// Punkte sind eine Ableitung (<see cref="CalculationGrades.PointsFor(int)"/>) und dürfen sich
/// ändern, ohne dass bereits gespeicherte Bewertungen ihre Bedeutung verlieren. Wäre die Punktzahl
/// das Gespeicherte, würde jede spätere Neugewichtung die Vergangenheit umschreiben.</para>
/// </summary>
public enum CalculationGrade
{
    /// <summary>Nicht gelöst.</summary>
    NotSolved = 0,
    /// <summary>Manche Ideen gesehen.</summary>
    SomeIdeas = 1,
    /// <summary>Richtiger Zug, aber die Hauptfolge nicht gesehen.</summary>
    MoveNoMainLine = 2,
    /// <summary>Richtiger Zug, aber die Nebenfolgen nicht gesehen.</summary>
    MoveNoSideLines = 3,
    /// <summary>Gelöst.</summary>
    Solved = 4,
}

/// <summary>
/// Die EINE Stelle, an der aus einer <see cref="CalculationGrade"/> Punkte werden. Heute linear
/// 0..4 — eine spätere Neugewichtung (z. B. „gelöst" doppelt zählen) passiert ausschließlich hier
/// und wirkt rückwirkend auf alle gespeicherten Stufen, weil in der DB die Stufe steht.
/// </summary>
public static class CalculationGrades
{
    /// <summary>Niedrigste gültige Stufe (<see cref="CalculationGrade.NotSolved"/>).</summary>
    public const int Min = (int)CalculationGrade.NotSolved;

    /// <summary>Höchste gültige Stufe (<see cref="CalculationGrade.Solved"/>).</summary>
    public const int Max = (int)CalculationGrade.Solved;

    /// <summary>Ist <paramref name="grade"/> eine bekannte Stufe? Alles andere ist ein Client-Fehler
    /// (400) und ausdrücklich NICHT „still auf 0 setzen" — eine unbekannte Stufe bedeutet nicht
    /// „nicht gelöst".</summary>
    public static bool IsValid(int grade) => grade >= Min && grade <= Max;

    /// <summary>
    /// Punkte einer Stufe — die einzige Punkte-Ableitung im ganzen Backend. Heute linear
    /// (Stufe 0..4 ⇒ 0..4 Punkte).
    /// <para>Werte außerhalb 0..4 werden hier geklemmt statt zu werfen: hierher kommen nur
    /// bereits gespeicherte Stufen, und eine von Hand verbogene DB-Zeile soll eine Kapitelsumme
    /// nicht in einen 500er verwandeln. Eingaben werden vorher hart geprüft
    /// (<see cref="IsValid"/> → 400).</para>
    /// </summary>
    public static int PointsFor(int grade) => Math.Clamp(grade, Min, Max);

    /// <inheritdoc cref="PointsFor(int)"/>
    public static int PointsFor(CalculationGrade grade) => PointsFor((int)grade);

    /// <summary>Punkte einer bestmöglich bewerteten Stellung — Basis jedes „x / y".</summary>
    public static int MaxPointsPerPosition => PointsFor(CalculationGrade.Solved);

    /// <summary>Maximalpunktzahl von <paramref name="positionCount"/> Stellungen (Kapitel/Kurs).
    /// Summen werden IMMER mit ihrem Maximum ausgeliefert — eine nackte Summe ist ohne die Zahl
    /// der Stellungen nicht lesbar („14" sagt nichts, „14 / 24" schon).</summary>
    public static int MaxPointsFor(int positionCount) => positionCount * MaxPointsPerPosition;
}
