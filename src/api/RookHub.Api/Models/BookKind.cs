namespace RookHub.Api.Models;

/// <summary>
/// Art eines Buchs für das Trainingsziel-Routing: Kurszeit eines <see cref="BookKind.Puzzle"/>-Buchs
/// zählt in die Tagesziel-Kategorie „Puzzles", die eines <see cref="BookKind.Study"/>-Buchs (Theorie-/
/// Studienbuch) in die Kategorie „Buch/Kurs". Default ist <see cref="BookKind.Puzzle"/> (klassisches
/// Verhalten: importierte Bücher sind Puzzle-/Taktikbücher). Wird als int gespeichert.
/// </summary>
public enum BookKind
{
    Puzzle = 0,
    Study = 1,
}
