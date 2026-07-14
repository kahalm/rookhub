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
/// <item><c>5</c> — Zug-lose Erklär-/Intro-Seiten (Kommentar, keine Züge) werden beim Buch-/Kurs-Import
///   nicht mehr verworfen, sondern als Info-Linie behalten (synthetischer Fake-Zug e4 ab Grundstellung,
///   <c>IsInfoOnly</c>) → erscheinen beim sequenziellen Durcharbeiten als Durchklick-Text. Diese Seiten
///   stehen bereits im gespeicherten <c>Book.SourcePgn</c> (Kommentar-only-Spiel) → der Reprocess ist
///   hier ein reiner LOKALER Re-Import aus der Quelle, KEIN Chessable-Re-Fetch nötig.</item>
/// <item><c>6</c> — Kommentar-Kappung von 5000 auf 100.000 Zeichen angehoben (<c>BookPuzzle.Comment</c>
///   jetzt LONGTEXT statt varchar(5000)): lange Chessable-Erklär-/Intro-Texte (z. B. „Introduction …
///   #2" in „100 Tactical Patterns") wurden bei 5000 Zeichen abgeschnitten. Der volle Text steht im
///   gespeicherten <c>Book.SourcePgn</c> → lokal aufbereitbare Bücher brauchen nur einen LOKALEN
///   Re-Import; Chessable-Bücher laufen (wie bisher) über einen Re-Fetch.</item>
/// <item><c>7</c> — Chessable-Kapitel-Einleitungen als NULL-Zug (<c>{[%info]} 1. -- {Text}</c>) wurden
///   fälschlich verworfen: der NULL-Zug <c>--</c> ergibt keine UCI-Züge, und die Info-Behalten-Logik
///   verlangte einen nicht-leeren ERSTEN Kommentar — hier ist der erste Kommentar aber nur der leere
///   <c>[%info]</c>-Marker, der Erklärtext folgt erst im Zug-Kommentar. Jetzt werden <c>[%info]</c>-
///   Linien auch bei NULL-Zug als Info-Linie behalten (Text = erster NICHT-leerer Kommentar). Diese
///   Linien stehen bereits im gespeicherten <c>Book.SourcePgn</c> → lokal aufbereitbar; Chessable-Bücher
///   laufen (wie bisher) über einen Re-Fetch.</item>
/// <item><c>8</c> — Pro-Zug-Board-Annotationen (Chessable <c>[%cal]</c>-Pfeile / <c>[%csl]</c>-Feld-
///   Markierungen) werden beim Import je Halbzug extrahiert und in <c>BookPuzzle.MoveShapes</c> (JSON)
///   gespeichert, statt beim Kommentar-Cleanup verworfen zu werden → das Frontend zeichnet sie im
///   Review aufs Brett. Die Annotationen stehen bereits im <c>Book.SourcePgn</c> → lokal aufbereitbar;
///   Chessable-Bücher laufen (wie bisher) über einen Re-Fetch.</item>
/// <item><c>9</c> — Fortsetzungs-Varianten in Zug-Kommentaren werden nicht mehr verworfen: endet ein
///   Hauptlinien-Kommentar (Chessable-Stil) mit einem Verweis auf eine Fortsetzung („…the continuation
///   would have been", „better was …") und folgt ihm direkt eine Varianten-Klammer <c>(…)</c>, so wird
///   deren Inhalt (Züge + Zug-Kommentare) kompakt in den Kommentar gefaltet, statt ihn mitten im Satz
///   enden zu lassen (<c>PgnParser.ExtractMoveComments</c>). Der Varianten-Text steht bereits im
///   <c>Book.SourcePgn</c> → lokal aufbereitbar; Chessable-Bücher laufen (wie bisher) über einen
///   Re-Fetch.</item>
/// <item><c>10</c> — Von Chessable geduldete Alternativzüge (softFail → <c>[%alt …]</c>) werden beim
///   Import je Halbzug nach <c>BookPuzzle.AltMoves</c> (JSON <c>{ply:[uci]}</c>) extrahiert (SAN→UCI aus
///   der Stellung vor dem Hauptzug), statt beim Kommentar-Cleanup verworfen zu werden. Der Kurs-/Puzzle-
///   Solver erkennt einen solchen Zug jetzt als gleichwertige Alternative (zeigt „auch eine Alternative",
///   nimmt ihn zurück, wartet weiter auf den Hauptzug) statt ihn als Fehler zu werten — analog zum
///   Repertoire-Trainer, der <c>[%alt]</c> aus dem PGN-Baum bereits akzeptiert. Die Marker stehen bereits
///   im <c>Book.SourcePgn</c> → lokal aufbereitbar; Chessable-Bücher laufen (wie bisher) über einen
///   Re-Fetch.</item>
/// <item><c>11</c> — Info-/Erklärlinien behalten jetzt die ECHTE Stellung aus dem <c>[FEN]</c>-Header
///   (Züge leer) statt einer synthetischen Grundstellung + Fake-Zug <c>e2e4</c>. Chessable-„Introduction"-
///   /„Evaluate …"-Seiten (z. B. <c>⏲Exercise #N - Introduction</c>) zeigten sonst die Grundstellung
///   statt der besprochenen Position. Die richtige FEN steht bereits im <c>Book.SourcePgn</c> → rein
///   lokal aufbereitbar (kein Chessable-Re-Fetch nötig).</item>
/// <item><c>12</c> — Spiel-Splitting robuster (<c>PgnParser.SplitGames</c>): (a) umbruch-bedingte
///   Kommentar-Fortsetzungszeilen, die mit <c>[</c> beginnen (<c>[%cal …]</c>/<c>[%tqu …]</c> am
///   Zeilenanfang), wurden als „Tag-Zeile" verworfen — samt ggf. schließendem <c>}</c>, wodurch
///   nachfolgende echte Züge als Kommentartext gefressen wurden (fehlende/verschobene Züge und
///   Kommentare, ohne Fehler). Jetzt zählt eine Klammertiefe über Zeilen hinweg: Inhalt offener
///   <c>{…}</c>-Kommentare bleibt erhalten, auch header-artige Zeilen darin splitten kein Spiel mehr.
///   (b) Header-only-Spiele (Header ohne Movetext) mischten ihre Header (z. B. die FEN) still in das
///   NÄCHSTE Spiel; sie werden jetzt separat geflusht. Der Quelltext steht im <c>Book.SourcePgn</c>
///   → lokal aufbereitbar; Chessable-Bücher laufen (wie bisher) über einen Re-Fetch.</item>
/// <item><b>13:</b> Chessable-<c>oid</c> je Linie wird beim Import gespeichert (<c>BookPuzzle.ChessableOid</c>,
///   aus dem PGN-Header <c>[ChessableOid]</c>, den piratechess seit v1.29.0 mitgibt) — Grundlage für die
///   linien-genauen Kurs-Fortschritts-Overlays in der RepCheck-Extension. Alt-Bücher haben die oid erst
///   nach einem Chessable-Re-Fetch (der lokale Reprocess aus <c>SourcePgn</c> füllt sie nur, wenn das
///   gespeicherte PGN bereits <c>[ChessableOid]</c> enthält).</item>
/// <item><b>14:</b> Reiner Re-Fetch-Trigger (keine Transformationsänderung): erzwingt das Neu-Holen aller
///   Chessable-Kurse/-Repertoires, die im Deploy-Fenster auf v13 gehoben wurden, WÄHREND piratechess noch
///   die oid-lose Vorversion (&lt; v1.0.39) lieferte. Solche Importe stehen auf v13 (= „aktuell"), tragen
///   aber KEINE <c>[ChessableOid]</c> → das Fortschritts-Overlay zeigt 0. Der Bump macht sie wieder
///   „veraltet", sodass ein „Aktualisieren" sie über das jetzt oid-liefernde piratechess (≥ v1.0.39,
///   auch aus dem Cache) neu holt und die oids nachträgt. Voll-gecachte Kurse laufen dabei netzfrei
///   über die Fast-Lane.</item>
/// <item><b>15:</b> Fix zur v14-Runde: die Reprocess-Klassifikation „modern" (= Quelle lokal aufbereitbar,
///   kein Re-Fetch) hing an <c>[%alt]</c>/<c>[%info]</c> — Marker, die piratechess schon VOR der oid-Ära
///   schrieb. Ein Chessable-Kurs/-Repertoire mit <c>[%alt]</c>, aber OHNE <c>[ChessableOid]</c> galt daher
///   fälschlich als modern → wurde beim „Aktualisieren" nur versions-markiert statt re-gefetcht → bekam nie
///   oids und wurde als „aktuell" (v14) nicht mehr angeboten. „Modern" verlangt jetzt <c>[ChessableOid]</c>;
///   der Bump macht die auf v14 fälschlich verbrannten Datensätze wieder veraltet, sodass sie über das
///   oid-liefernde piratechess (≥ v1.0.39, auch aus dem Cache) neu geholt werden.</item>
/// </list>
/// </summary>
public static class ImportPipeline
{
    /// <summary>Aktuelle Pipeline-Version. Beim Bump: Eintrag in der Versionshistorie oben ergänzen.</summary>
    public const int CurrentVersion = 15;
}
