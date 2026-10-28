using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>Woher ein E-Book stammt — entscheidet, unter welchem Wurzelverzeichnis seine Dateien liegen.</summary>
public enum EbookSource
{
    /// <summary>Von einem Nutzer über die Oberfläche hochgeladen; liegt unter <c>Ebooks:Root</c>.</summary>
    Upload = 0,
    /// <summary>Aus der Bibliothek auf dem Server eingelesen (<c>Ebooks:LibraryRoot</c>, nur lesend) — die Datei
    /// bleibt an Ort und Stelle, RookHub merkt sich nur den Pfad.</summary>
    Library = 1,
}

/// <summary>Stand des Titelbilds (<c>&lt;Root&gt;/covers/&lt;Id&gt;.jpg</c>), das der <c>EbookCoverWorker</c> nachträglich rendert.</summary>
public enum EbookCoverState
{
    /// <summary>Noch nicht versucht — der Worker nimmt sich das Buch beim nächsten Durchlauf.</summary>
    Pending = 0,
    Done = 1,
    /// <summary>Werkzeug fehlt, Datei kaputt, Zeitüberschreitung — kein weiterer Versuch (sonst liefe der Worker im Kreis).</summary>
    Failed = 2,
    /// <summary>Format ohne Titelbild-Weg (mobi, azw3, cbr).</summary>
    Unsupported = 3,
}

/// <summary>
/// Ein E-BOOK der Bibliothek (<c>/ebooks</c>): ein Titel mit einer oder mehreren Dateien (dasselbe Buch als PDF
/// und EPUB), Metadaten und einer Gruppen-Freigabe je Buch (<see cref="EbookGroupAccess"/>) — dasselbe Muster
/// wie <see cref="BookGroupAccess"/> bei den Kursen, nur dass hier JEDER angemeldete Nutzer hochladen darf und die
/// Sichtbarkeit seines Buchs selbst steuert (seine eigenen Gruppen; Admins alle).
///
/// <para>Sichtbar ist ein Buch für den Besitzer, für Admins und für Mitglieder einer freigegebenen Gruppe
/// (die System-Gruppe „Everyone" öffnet es für alle Angemeldeten) — Regel in <c>Services/Ebooks/EbookAccess</c>.</para>
/// </summary>
public class Ebook
{
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Autor(en) als Anzeigetext („Jeremy Silman“, „Kotov, Yudovich“); leer = unbekannt.</summary>
    [MaxLength(300)]
    public string Author { get; set; } = string.Empty;

    public int? Year { get; set; }

    /// <summary>Schlagworte als CSV (<c>Endspiel,Strategie</c>) — die Filterzeile der Übersicht. Max. 20 à 40 Zeichen.</summary>
    [MaxLength(600)]
    public string Tags { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>Ab welcher Spielstärke das Buch gedacht ist (aus den Beiblättern der Bibliothek); <c>null</c> = keine Angabe.</summary>
    public int? TargetMinElo { get; set; }

    /// <summary>Mit welchem Modus das Brett neben dem Buch startet (0.781.0): view/free/calc/solve/maia;
    /// <c>null</c> = <see cref="EbookFormats.DefaultBoardMode"/> („solve“).</summary>
    [MaxLength(10)]
    public string? DefaultBoardMode { get; set; }

    /// <summary>Wer es hochgeladen bzw. eingelesen hat — darf es bearbeiten und löschen. <c>null</c>, wenn das Konto
    /// gelöscht wurde (dann nur noch Admins).</summary>
    public int? OwnerUserId { get; set; }
    public AppUser? Owner { get; set; }

    /// <summary>Hochgeladen OHNE Anmeldung (0.778.0): die anonyme Kennung des Besuchers (<c>X-Visitor-Id</c>);
    /// <see cref="OwnerUserId"/> ist dann leer. Beim Anmelden wandert das Buch ins Konto (Kennung wird geleert);
    /// nie übernommene Bücher räumt der <c>EbookAnonymousCleanupWorker</c> nach 30 Tagen weg.</summary>
    [MaxLength(36)]
    public string? AnonymousSessionId { get; set; }

    public EbookSource Source { get; set; } = EbookSource.Upload;

    /// <summary>Seitenzahl der PDF-Datei (vom Cover-Worker ermittelt); <c>null</c> = unbekannt/kein PDF.</summary>
    public int? PageCount { get; set; }

    public EbookCoverState CoverState { get; set; } = EbookCoverState.Pending;

    /// <summary>Stand der Diagramm-Erkennung (<see cref="EbookDiagram"/>), die der <c>EbookDiagramWorker</c> nachträglich macht.</summary>
    public EbookDiagramState DiagramState { get; set; } = EbookDiagramState.Pending;

    /// <summary>Erkannte Diagramme (nur gültig bei <see cref="EbookDiagramState.Done"/>).</summary>
    public int DiagramCount { get; set; }

    /// <summary>Diagramme von Hand bzw. per Vision-Modell nachgetragen (0.782.0, z. B. Pongó per Opus, <c>FontFamily = "opus"</c>):
    /// der <c>EbookDiagramWorker</c> ersetzt sie NIE — ein Neuerkennen (Rescan, Recheck-Migration, DJVU-Umwandlung) setzt nur
    /// den Stand zurück auf <see cref="EbookDiagramState.Done"/>. Zurücksetzen nur von Hand in der DB.</summary>
    public bool DiagramsCurated { get; set; }

    /// <summary>Weitere Diagramme werden noch außerhalb des Workers ergänzt (0.806.1, Lückenfüller mit Opus-Lesungen): die
    /// Bibliothek zeigt dafür „Wird verarbeitet“, ohne den <see cref="DiagramState"/> anzufassen (Pending würde eine
    /// Neuerkennung auslösen). Setzt und löscht nur das Werkzeug, das die Diagramme nachträgt.</summary>
    public bool DiagramsBacklog { get; set; }

    /// <summary>„Ergänzen statt ersetzen“ (0.811.0, <c>POST /api/admin/ebooks/{id}/diagrams/supplement</c>): der nächste Lauf des
    /// <c>EbookDiagramWorker</c> erkennt das Buch neu — auch wenn es <see cref="DiagramsCurated"/> ist —, lässt aber alle
    /// vorhandenen Diagramme stehen und fügt nur die hinzu, die an einer noch freien Stelle liegen. Unplausible Lesungen werden
    /// dabei als <c>FontFamily = "unread"</c> aufgehoben (für Nutzer unsichtbar). Der Worker löscht das Feld danach.</summary>
    public bool DiagramSupplement { get; set; }

    /// <summary>Vorrang in der Warteschlange der Diagramm-Erkennung (0.870.0): ein Admin hat das Buch gerade neu eingereiht
    /// (<c>POST /api/admin/ebooks/{id}/diagrams/supplement</c>) und will das Ergebnis jetzt sehen — es läuft vor allen anderen,
    /// auch vor Uploads. Wird nach dem Lauf gelöscht.</summary>
    public bool DiagramPriority { get; set; }

    /// <summary>Wer das Buch gerade erkennt (0.870.0, <c>EbookDiagramLease</c>): der eigene Worker (<c>server</c>) oder ein
    /// externer Diagramm-Worker (<c>pat:&lt;Token-Id&gt;</c>, <c>/api/diagram-worker</c>). Nur gültig bis
    /// <see cref="DiagramLeaseUntil"/>; danach ist das Buch wieder frei (bleibt die ganze Zeit <see cref="EbookDiagramState.Pending"/>).</summary>
    public Guid? DiagramLeaseId { get; set; }

    [MaxLength(100)]
    public string? DiagramLeaseOwner { get; set; }

    public DateTime? DiagramLeaseUntil { get; set; }

    /// <summary>Ein externer Diagramm-Worker hat das Buch mit Fehler zurückgegeben (0.870.0): externe Worker nehmen es nicht
    /// wieder, der eigene Worker erkennt es wie bisher. Wird nach dem Lauf gelöscht.</summary>
    public bool DiagramExternalFailed { get; set; }

    /// <summary>Wann der Hochladende benachrichtigt wurde, dass die Verarbeitung (Titelbild, Umwandlung, Diagramme, Prüfung)
    /// fertig ist (0.773.0); <c>null</c> = noch nicht. Nur für Uploads — eingelesene Bibliotheksbücher melden sich nicht.</summary>
    public DateTime? ReadyNotifiedAt { get; set; }

    /// <summary>Stand der Stockfish-Prüfung der Diagramm-Lösungen (0.772.0); läuft nach der Erkennung.</summary>
    public EbookDiagramState SolutionCheckState { get; set; } = EbookDiagramState.Pending;

    /// <summary>Wann das Buch (zuletzt) in die Stockfish-Prüfung eingereiht wurde (0.801.0). Die Prüfung arbeitet als STAPEL —
    /// zuletzt eingereiht zuerst (Wunsch 15.10.2026: ein Buch, das man gerade liest, soll nicht hinter Tausenden Diagrammen
    /// alter Bücher warten). <c>null</c> = vor 0.801.0 oder per SQL eingereiht → zählt <see cref="CreatedAt"/>.</summary>
    public DateTime? SolutionCheckQueuedAt { get; set; }

    /// <summary>Umwandlung DJVU → PDF (0.770.0): Bücher, die NUR als DJVU vorliegen, bekommen eine lesbare PDF-Fassung
    /// (<see cref="EbookFile.Derived"/>), die der Browser-Leser zeigen kann.</summary>
    public EbookConversionState ConversionState { get; set; } = EbookConversionState.NotNeeded;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Sammlung (0.787.0, z. B. alle Hefte einer Zeitschrift): in der Liste erscheint die Sammlung als EIN Ordner,
    /// ein Klick zeigt die einzelnen Bücher. <c>null</c> = steht für sich.</summary>
    public int? CollectionId { get; set; }
    public EbookCollection? Collection { get; set; }

    /// <summary>Weitere Sammlungen (0.861.0): das Buch steht in der Liste unter <see cref="CollectionId"/> (Hauptsammlung),
    /// erscheint aber auch beim Öffnen dieser Sammlungen (z. B. ein Kongressbuch zusätzlich unter „Turnierbücher“).
    /// Nie gleich der Hauptsammlung.</summary>
    public List<EbookCollectionLink> ExtraCollections { get; set; } = new();

    public List<EbookFile> Files { get; set; } = new();
    public List<EbookGroupAccess> GroupAccess { get; set; } = new();
}

/// <summary>Eine Datei eines E-Books (ein Format). Ein Buch hat mindestens eine; die Bibliothek bringt oft
/// PDF + DJVU + EPUB desselben Titels mit.</summary>
public class EbookFile
{
    public int Id { get; set; }

    public int EbookId { get; set; }
    public Ebook Ebook { get; set; } = null!;

    /// <summary>Kleingeschriebene Endung ohne Punkt (<c>pdf</c>, <c>epub</c>, <c>djvu</c>, <c>cbz</c> …).</summary>
    [Required, MaxLength(10)]
    public string Format { get; set; } = string.Empty;

    /// <summary>Pfad RELATIV zum Wurzelverzeichnis der Quelle (<see cref="Ebook.Source"/>), immer mit „/“. Nie absolut,
    /// nie mit „..“ — <c>EbookStorage</c> prüft das beim Auflösen.</summary>
    [Required, MaxLength(1000)]
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>Ursprünglicher Dateiname (für den Download); bei Bibliotheksdateien der Name auf der Platte.</summary>
    [Required, MaxLength(300)]
    public string FileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>SHA-256 (Hex, klein) — nur bei Uploads berechnet; erkennt eine doppelt hochgeladene Datei.</summary>
    [MaxLength(64)]
    public string? Sha256 { get; set; }

    /// <summary>Von RookHub ERZEUGT (z. B. PDF aus DJVU, 0.770.0): liegt immer unter der beschreibbaren Wurzel
    /// (<c>Ebooks:Root</c>, <c>converted/</c>), auch wenn das Buch aus der Bibliothek stammt, und wird mit dem Buch gelöscht.</summary>
    public bool Derived { get; set; }

    /// <summary>ISBN-13 der Datei, kommagetrennt (0.863.0, <c>EbookFingerprint</c>): aus den Metadaten (EPUB-OPF, PDF-XMP/Info)
    /// und dem Text der ersten Seiten, ISBN-10 umgerechnet, Prüfziffer geprüft. <c>null</c> = keine gefunden.</summary>
    [MaxLength(200)]
    public string? Isbns { get; set; }

    /// <summary>MinHash-Fingerabdruck des Texts (64 × uint32, Base64; 0.863.0) — erkennt dasselbe Buch in einer anderen
    /// Datei (anderer PDF-Export, EPUB statt PDF). <c>null</c> = (fast) kein Text (Scan) oder Format ohne Text.</summary>
    [MaxLength(400)]
    public string? TextFingerprint { get; set; }

    /// <summary>Wann <see cref="Isbns"/>/<see cref="TextFingerprint"/> bestimmt wurden; <c>null</c> = noch offen
    /// (der <c>EbookFingerprintWorker</c> holt das nach).</summary>
    public DateTime? FingerprintedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Gruppe, die ein E-Book sehen darf (n:m, zusammengesetzter Schlüssel) — wie <see cref="BookGroupAccess"/>.</summary>
public class EbookGroupAccess
{
    public int EbookId { get; set; }
    public Ebook? Ebook { get; set; }

    public int GroupId { get; set; }
    public Group? Group { get; set; }
}

/// <summary>Lesestand eines Nutzers in einem E-Book: die zuletzt gesehene Seite (PDF) — „Weiterlesen ab Seite 23“.</summary>
public class EbookProgress
{
    public int EbookId { get; set; }
    public Ebook? Ebook { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>1-basierte Seite.</summary>
    public int Page { get; set; }

    /// <summary>Genaue Stelle für Formate ohne feste Seiten (EPUB: CFI), 0.770.0; bei PDF leer.</summary>
    [MaxLength(300)]
    public string? Locator { get; set; }

    /// <summary>Seitenzahl, wie der Leser sie beim Speichern kannte (für den Prozentbalken, auch wenn der Server keine hat).</summary>
    public int? PageCount { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Lesestand eines Besuchers OHNE Anmeldung (0.778.0) — eigene Tabelle, weil <see cref="EbookProgress"/> am Konto
/// hängt; beim Anmelden übernommen (der jüngere Stand gewinnt) und gelöscht.</summary>
public class EbookAnonymousProgress
{
    public int EbookId { get; set; }
    public Ebook? Ebook { get; set; }

    [MaxLength(36)]
    public string SessionId { get; set; } = string.Empty;

    public int Page { get; set; }

    [MaxLength(300)]
    public string? Locator { get; set; }

    public int? PageCount { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Stand der Diagramm-Erkennung eines Buchs.</summary>
public enum EbookDiagramState
{
    /// <summary>Noch nicht versucht — der Worker nimmt sich das Buch beim nächsten Durchlauf.</summary>
    Pending = 0,
    Done = 1,
    /// <summary>Datei kaputt, Zeitüberschreitung — kein weiterer Versuch.</summary>
    Failed = 2,
    /// <summary>Kein PDF oder kein bekannter Schach-Font darin (reiner Scan): nichts zu erkennen (Stufe 1).</summary>
    Unsupported = 3,
}

/// <summary>
/// Ein im PDF erkanntes SCHACHDIAGRAMM (0.766.0): digital gesetzte Bücher zeichnen Diagramme als Text in einem
/// Schach-Font (Chess Merida, ChessBase DiagramTT, Hastings …) — jedes Feld ein Buchstabe. Die Erkennung liest die
/// Buchstaben samt Lage und baut daraus Brett und FEN, exakt und ohne Bildverfahren
/// (<c>Services/Ebooks/EbookDiagramExtractor</c>). Die Lage ist als ANTEIL der Seite gespeichert (0–1, Ursprung links
/// oben), damit der Leser sie bei jedem Zoom ohne PDF-Wissen über die Leinwand legen kann.
/// </summary>
public class EbookDiagram
{
    public int Id { get; set; }

    public int EbookId { get; set; }
    public Ebook Ebook { get; set; } = null!;

    /// <summary>1-basierte Seite der PDF-Datei.</summary>
    public int Page { get; set; }

    /// <summary>Lage auf der Seite als Anteil (0–1), Ursprung links oben.</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }

    /// <summary>Vollständige FEN: Brett, Zugrecht, Rochade geschätzt (König + Turm auf Ausgangsfeldern), En-passant „-“.</summary>
    [Required, MaxLength(100)]
    public string Fen { get; set; } = string.Empty;

    /// <summary>Nummer/Beschriftung neben dem Diagramm (Aufgabennummer), wenn gefunden.</summary>
    [MaxLength(40)]
    public string? Label { get; set; }

    /// <summary>Textzeile direkt über dem Diagramm (Spieler, Ort, Jahr), wenn vorhanden.</summary>
    [MaxLength(200)]
    public string? Caption { get; set; }

    /// <summary>Ob das Zugrecht aus einem Marker/Text kam (<c>true</c>) oder nur die Vorgabe Weiß ist.</summary>
    public bool SideKnown { get; set; }

    /// <summary>EPUB (0.775.0): Pfad des Diagrammbilds im Buch; <see cref="Page"/> ist dann das Kapitel (Spine, 1-basiert) und
    /// die Lage (<see cref="X"/> …) bleibt 0. Bei PDF-Diagrammen <c>null</c> — außer sie sind mit dem EPUB desselben Buchs
    /// verknüpft (0.793.0), dann steht das Kapitel in <see cref="EpubChapter"/>.</summary>
    [MaxLength(300)]
    public string? ImageRef { get; set; }

    /// <summary>Sicherheit der Bilderkennung (0.794.0, Faltungsnetz): Wahrscheinlichkeit des UNSICHERSTEN Felds (0–1); <c>null</c>
    /// bei Font-Diagrammen und den alten Modellen. Unter ~0,9 lohnt ein zweiter Blick (Opus).</summary>
    public float? Confidence { get; set; }

    /// <summary>EPUB-Kapitel (Spine, 1-basiert) eines aus dem PDF erkannten Diagramms, das zusätzlich mit einem Bild im
    /// EPUB desselben Buchs verknüpft ist (0.793.0, <c>EbookFormatLinker</c>). Bei reinen EPUB-Diagrammen steht das Kapitel
    /// weiter in <see cref="Page"/>, hier <c>null</c>.</summary>
    public int? EpubChapter { get; set; }

    /// <summary>Welche Font-Familie das Diagramm gesetzt hat (zum Nachvollziehen von Lesefehlern).</summary>
    [MaxLength(60)]
    public string? FontFamily { get; set; }

    /// <summary>Lösung aus dem Buch (0.771.0): Hauptlinie als UCI (leerzeichengetrennt) — nur, wenn sie sich ab
    /// <see cref="Fen"/> regelgerecht nachspielen ließ (<c>EbookSolutionFinder</c>).</summary>
    [MaxLength(1200)]
    public string? SolutionUci { get; set; }

    /// <summary>Dieselbe Hauptlinie als SAN (leerzeichengetrennt).</summary>
    [MaxLength(1500)]
    public string? SolutionSan { get; set; }

    /// <summary>Der Lösungsabsatz aus dem Buch (Figurinen als Buchstaben), für „Lösung zeigen“.</summary>
    [MaxLength(3000)]
    public string? SolutionText { get; set; }

    /// <summary>Seite des Lösungsabsatzes (Sprung im Leser).</summary>
    public int? SolutionPage { get; set; }

    /// <summary>Woher die Lösung stammt: <c>book</c> oder <c>engine</c> (Stockfish, wenn das Buch keine hat), 0.772.0.</summary>
    [MaxLength(10)]
    public string? SolutionSource { get; set; }

    /// <summary>Ergebnis der Stockfish-Prüfung (<c>EbookSolutionChecker</c>).</summary>
    public EbookSolutionCheck SolutionCheck { get; set; } = EbookSolutionCheck.Unchecked;

    /// <summary>Engine-Bewertung der Diagrammstellung für die Seite am Zug in Centipawns (Matt in n = ±(10000 − n)).</summary>
    public int? EngineEval { get; set; }

    /// <summary>Partiensammlungen (0.831.0): die Partie, aus der das Diagramm stammt — der Leser spielt sie im Modus „Partie“
    /// nach. <c>null</c> = keine gefunden. Wird beim Löschen der Partie geleert.</summary>
    public int? GameId { get; set; }
    public EbookGame? Game { get; set; }

    /// <summary>Halbzüge der Partie bis zur Diagrammstellung (0 = Grundstellung); nur mit <see cref="GameId"/>.</summary>
    public int? GamePly { get; set; }
}

/// <summary>
/// Eine Partie zu einem Buch (0.831.0, Partiensammlungen): Kopf + Hauptlinie als SAN. Mehrere Diagramme derselben Partie
/// zeigen per <see cref="EbookDiagram.GameId"/> darauf. Gefüllt von außen (Abgleich der Diagrammstellungen mit der
/// Partiedatenbank bzw. der PGN des Buchs, <c>PUT /api/admin/ebooks/{id}/games</c>).
/// </summary>
public class EbookGame
{
    public int Id { get; set; }
    public int EbookId { get; set; }
    public Ebook Ebook { get; set; } = null!;

    [MaxLength(120)] public string? White { get; set; }
    [MaxLength(120)] public string? Black { get; set; }
    [MaxLength(200)] public string? Event { get; set; }
    /// <summary>Datum wie in der Quelle („1960.02.03“, „1960“).</summary>
    [MaxLength(20)] public string? Date { get; set; }
    [MaxLength(10)] public string? Result { get; set; }

    /// <summary>Hauptlinie ab der Grundstellung als SAN, leerzeichengetrennt.</summary>
    [Required] public string Moves { get; set; } = string.Empty;

    /// <summary>Herkunft (<c>prep:&lt;PrepGameId&gt;</c>, <c>pgn</c> …) — zum Nachvollziehen.</summary>
    [MaxLength(40)] public string? Source { get; set; }
}

/// <summary>
/// Ein Nutzer meldet ein erkanntes Diagramm als FALSCH (0.768.0, Knopf „Diagramm falsch" unter dem Brett). Die Stellung wird
/// mitgeschrieben (<see cref="Page"/>, <see cref="Fen"/>), weil eine Neuerkennung die Diagramm-Zeilen ersetzt — die Meldung
/// bleibt so auswertbar (welcher Font, welche Seite) und hängt nur lose an <see cref="DiagramId"/> (kein FK).
/// </summary>
public class EbookDiagramReport
{
    public int Id { get; set; }

    public int EbookId { get; set; }
    public Ebook Ebook { get; set; } = null!;

    /// <summary>Id des gemeldeten <see cref="EbookDiagram"/> zum Zeitpunkt der Meldung (nach einem Rescan evtl. verwaist).</summary>
    public int DiagramId { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public int Page { get; set; }

    [Required, MaxLength(100)]
    public string Fen { get; set; } = string.Empty;

    /// <summary>Optionaler Hinweis des Nutzers („Dame auf d1 fehlt").</summary>
    [MaxLength(300)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Stand der DJVU→PDF-Umwandlung eines Buchs.</summary>
public enum EbookConversionState
{
    /// <summary>Nichts zu tun (es gibt schon ein PDF oder kein DJVU).</summary>
    NotNeeded = 0,
    Pending = 1,
    Done = 2,
    Failed = 3,
}

/// <summary>Ergebnis der Stockfish-Prüfung einer Diagramm-Lösung.</summary>
public enum EbookSolutionCheck
{
    Unchecked = 0,
    /// <summary>Buchlösung bestätigt (erster Zug bester oder gleichwertig gewinnend).</summary>
    Confirmed = 1,
    /// <summary>Buchlösung behalten, aber die Engine bewertet den ersten Zug deutlich schlechter — im Leser als fraglich markiert.</summary>
    Doubtful = 2,
    /// <summary>Der zuerst gelesene Zug war eine Nebenvariante; ein anderer Kandidat aus demselben Absatz wurde genommen.</summary>
    Replaced = 3,
    /// <summary>Keine Buchlösung; Stockfish hat eine (entscheidende Stellung, Matt oder klarer Vorteil).</summary>
    Engine = 4,
    /// <summary>Keine Buchlösung und keine entscheidende Fortsetzung — keine Aufgabe (z. B. Eröffnungs- oder Ausgleichsstellung).</summary>
    NoPuzzle = 5,
}

/// <summary>Sammlung von E-Books (0.787.0): mehrere Bücher, die in der Liste als ein Ordner erscheinen (Zeitschriften-
/// Jahrgänge, mehrbändige Werke). Sichtbar ist sie, sobald eines ihrer Bücher sichtbar ist — eine eigene Freigabe hat sie nicht.</summary>
public class EbookCollection
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Buch in einer weiteren Sammlung neben seiner Hauptsammlung (0.861.0, <see cref="Ebook.ExtraCollections"/>).</summary>
public class EbookCollectionLink
{
    public int EbookId { get; set; }
    public Ebook? Ebook { get; set; }
    public int CollectionId { get; set; }
    public EbookCollection? Collection { get; set; }
}

/// <summary>
/// Doppelklick auf eine Buchstelle OHNE erkanntes Diagramm (0.788.0): der Leser fragt „Ist hier ein Diagramm, das nicht
/// erkannt wurde?“ und speichert die Antwort samt Stelle (Seite, Klickpunkt als Seitenanteil) — Material, um die
/// Erkennung später gezielt zu verbessern (welche Bücher, welche Seiten). Schließen ohne Antwort speichert nichts.
/// </summary>
public class EbookMissedDiagram
{
    public int Id { get; set; }
    public int EbookId { get; set; }
    public Ebook Ebook { get; set; } = null!;
    /// <summary>PDF-Seite (1-basiert); bei EPUB-Meldungen 0 — dann steht die Stelle in <see cref="EpubRef"/>/<see cref="EpubChapter"/>.</summary>
    public int Page { get; set; }
    /// <summary>Klickpunkt als Anteil der Seite (0–1, links oben).</summary>
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>EPUB (0.798.0): Pfad des doppelgeklickten Bilds im Buch (wie <see cref="EbookDiagram.ImageRef"/>); <c>null</c> bei PDF
    /// oder wenn auf Text geklickt wurde.</summary>
    [MaxLength(500)]
    public string? EpubRef { get; set; }
    /// <summary>EPUB (0.798.0): Kapitel (Spine-href) der Stelle.</summary>
    [MaxLength(500)]
    public string? EpubChapter { get; set; }
    /// <summary>Antwort: true = „ja, hier ist ein Diagramm“, false = „nein“.</summary>
    public bool IsDiagram { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Von Hand korrigierte Diagrammstellung (0.798.0, „Stellung korrigieren“ im ⋮ des Bretts). Jede Korrektur wird hier
/// mitgeschrieben — alte und neue FEN samt Font und Sicherheit der Erkennung zu diesem Zeitpunkt —, damit sich die
/// Erkennung später an diesen Fällen verbessern lässt. Hängt nur lose an <see cref="DiagramId"/> (kein FK, wie die Meldungen).
/// </summary>
public class EbookDiagramCorrection
{
    public int Id { get; set; }
    public int EbookId { get; set; }
    public Ebook Ebook { get; set; } = null!;
    public int DiagramId { get; set; }
    public int UserId { get; set; }
    public int Page { get; set; }
    [Required, MaxLength(100)]
    public string OldFen { get; set; } = string.Empty;
    [Required, MaxLength(100)]
    public string NewFen { get; set; } = string.Empty;
    [MaxLength(60)]
    public string? FontFamily { get; set; }
    public float? Confidence { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

