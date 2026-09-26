namespace RookHub.Api.Services;

/// <summary>
/// Welche Halbzug-Nummer (<see cref="Models.CommentText.Ply"/>) in einem KURS-Satz welches Feld der
/// Linie meint. Die Zug-Kommentare zaehlen wie <see cref="Models.BookPuzzle.MoveComments"/> (<c>-1</c> =
/// Einleitung vor dem ersten Zug, <c>0..n</c> = nach dem Halbzug); die uebrigen Texte der Linie liegen
/// DARUNTER, damit ein Satz alles traegt, was die Linie an Text hat, und eine Abfrage reicht.
///
/// <para>Hier und NUR hier stehen die Zahlen — keine magischen <c>-2</c>/<c>-3</c>/<c>-4</c> im Code verteilen.</para>
/// </summary>
public static class CourseTextSlots
{
    /// <summary>Einleitung vor dem ersten Zug (<c>MoveComments[-1]</c>).</summary>
    public const int Intro = -1;

    /// <summary><see cref="Models.BookPuzzle.Comment"/> — Einleitungs-/Erklaertext der Linie. Steht oft
    /// woertlich auch in <see cref="Intro"/> (Prod 2026-09-26: 51 549 von 87 035 Linien) und geht dann
    /// nur EINMAL ans Modell.</summary>
    public const int Comment = -2;

    /// <summary><see cref="Models.BookPuzzle.Title"/> — Ueberschrift.</summary>
    public const int Title = -3;

    /// <summary><see cref="Models.BookPuzzle.Chapter"/> — Ueberschrift, und im Frontend zugleich ein
    /// SCHLUESSEL: die Uebersetzung ersetzt das Feld deshalb nie, sie kommt als eigenes Label.</summary>
    public const int Chapter = -4;

    /// <summary>Prosa (Kommentare, Einleitung) — nur sie zaehlt fuer die Laengen- und Sprachpruefung einer
    /// Uebersetzung; Titel und Kapitelnamen sind zu kurz, um daran etwas abzulesen.</summary>
    public static bool IsProse(int slot) => slot >= Comment;

    /// <summary>Ueberschriften (Titel, Kapitel).</summary>
    public static bool IsHeading(int slot) => slot is Title or Chapter;
}
