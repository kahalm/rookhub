namespace RookHub.Api.Services;

/// <summary>
/// Version der Import-/Aufbereitungs-Pipeline (Roh-PGN → gespeicherte <see cref="Models.BookPuzzle"/>
/// bzw. abgeleitete Repertoire-Daten). Wird beim Import in <c>Book.ImportVersion</c> /
/// <c>Repertoire.ImportVersion</c> geschrieben.
///
/// <para>Erhöhe <see cref="CurrentVersion"/> um 1, wenn sich die Transformation so ändert, dass
/// BEREITS importierte Daten unvollständig/veraltet werden. Datensätze mit kleinerer Version gelten
/// dann als „veraltet" und können über den Reprocess-Knopf neu aufbereitet werden — in-place
/// (Match per LineId, Fortschritt/Statistik bleiben erhalten).</para>
///
/// <para>Versionshistorie:</para>
/// <list type="bullet">
/// <item><c>1</c> — Pro-Zug-Kommentare der Hauptlinie (<c>BookPuzzle.MoveComments</c>) +
///   Speichern des Roh-PGN je Buch (<c>Book.SourcePgn</c>), damit Bücher offline neu aufbereitet
///   werden können. Alles davor importierte gilt als Version 0 = veraltet.</item>
/// <item><c>2</c> — Kapitel-Spoiler-Entschärfung für <see cref="Models.BookKind.Puzzle"/>-Bücher:
///   beim Import wird der motivverratende Teil nach „Chapter N:"/„Kapitel N:" aus
///   <c>BookPuzzle.Chapter</c> entfernt (→ nur noch „Chapter N"). Study-Bücher behalten ihre
///   Kapitelnamen. Bestehende Puzzle-Bücher werden über den Reprocess-Knopf entschärft.</item>
/// <item><c>3</c> — Geduldete Alternativzüge (Chessable <c>softFail</c>) werden vom piratechess-Export
///   jetzt als <c>{[%alt …]}</c> ins PGN geschrieben (Grundlage für den Repertoire-Trainer: e5/c5/…
///   als „geduldet" statt „falsch"). Diese Daten stehen NICHT im lokal gespeicherten PGN, sie kommen
///   nur aus einem frischen Chessable-Abruf → für Chessable-Repertoires ist der Reprocess ein
///   Re-Fetch (in-place ins bestehende Repertoire, Trainings-Fortschritt bleibt). Nicht-Chessable-
///   bzw. lokal aus <c>Book.SourcePgn</c> aufbereitbare Datensätze ändern sich an dieser Version
///   inhaltlich nicht (reiner Versions-Mark bzw. idempotenter Re-Import).</item>
/// <item><c>4</c> — Chessable-Info-/Erklärlinien (<c>IsInfo=1</c>) werden vom piratechess-Export jetzt
///   mit <c>[%info]</c> markiert; der Import setzt daraus <c>BookPuzzle.IsInfoOnly</c>. Solche Linien
///   werden nicht mehr als Quiz abgefragt (aus Random-/Tagespuzzle-Töpfen ausgeblendet, zählen nicht
///   zum Kurs-Fortschritt, sequenziell nur zum Durchklicken). Der Marker steht NICHT im lokal
///   gespeicherten Alt-PGN → für Chessable-Bücher/-Repertoires ist der Reprocess ein Re-Fetch.</item>
/// </list>
/// </summary>
public static class ImportPipeline
{
    /// <summary>Aktuelle Pipeline-Version. Beim Bump: Eintrag in der Versionshistorie oben ergänzen.</summary>
    public const int CurrentVersion = 4;
}
