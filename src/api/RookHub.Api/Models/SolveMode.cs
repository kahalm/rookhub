namespace RookHub.Api.Models;

/// <summary>
/// Spielweise eines Lösungsversuchs — die EINE Wahrheit für alle Solver-Bereiche (Standard-Puzzles,
/// Kurs-/Buchlinien, Buch-/Tagespuzzle, Endless, Wochenpost). Die Zeichenketten sind Vertrag zum
/// Client UND Spaltenwert in der DB (<c>varchar(10)</c>, DB-Default <see cref="Training"/>) —
/// nicht umbenennen ohne Migration.
/// </summary>
/// <remarks>
/// <see cref="Training"/> ist bewusst der Rückfallwert: so verhält sich der Bestand seit jeher
/// (Brett eingefroren bzw. eine höhere Visualisierungsstufe), und Zeilen ohne Modus (Altbestand)
/// bleiben damit mit neuen Zeilen vergleichbar.
/// </remarks>
public static class SolveMode
{
    /// <summary>Modus „Training": Brett eingefroren bzw. höhere Visualisierungsstufe (Default/Altbestand).</summary>
    public const string Training = "training";

    /// <summary>Modus „Einfach": Figuren normal ziehbar.</summary>
    public const string Easy = "easy";

    /// <summary>
    /// Normalisiert einen vom Client gelieferten Modus. Fehlt er oder ist er unbekannt, gilt
    /// <see cref="Training"/> — ein unbekannter Wert darf den Versuch NIE mit 400 abweisen.
    /// </summary>
    public static string Normalize(string? mode) =>
        string.Equals(mode?.Trim(), Easy, StringComparison.OrdinalIgnoreCase) ? Easy : Training;

    /// <summary>True, wenn der (bereits normalisierte oder rohe) Wert „easy" bedeutet.</summary>
    public static bool IsEasy(string? mode) => Normalize(mode) == Easy;

    /// <summary>
    /// Die EINE Zählregel für alle Statistiken: aus Gesamtmenge und der Zahl der ausdrücklich als
    /// „easy" markierten Zeilen die beiden Zähler bilden. Alles, was nicht „easy" ist, zählt als
    /// „training" — damit fällt Altbestand (Zeilen ohne Modus) automatisch auf Training.
    /// </summary>
    public static (int Training, int Easy) Split(int total, int easyCount)
    {
        var easy = Math.Clamp(easyCount, 0, Math.Max(0, total));
        return (Math.Max(0, total - easy), easy);
    }
}
