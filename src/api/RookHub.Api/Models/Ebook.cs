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

    /// <summary>Wann der Hochladende benachrichtigt wurde, dass die Verarbeitung (Titelbild, Umwandlung, Diagramme, Prüfung)
    /// fertig ist (0.773.0); <c>null</c> = noch nicht. Nur für Uploads — eingelesene Bibliotheksbücher melden sich nicht.</summary>
    public DateTime? ReadyNotifiedAt { get; set; }

    /// <summary>Stand der Stockfish-Prüfung der Diagramm-Lösungen (0.772.0); läuft nach der Erkennung.</summary>
    public EbookDiagramState SolutionCheckState { get; set; } = EbookDiagramState.Pending;

    /// <summary>Umwandlung DJVU → PDF (0.770.0): Bücher, die NUR als DJVU vorliegen, bekommen eine lesbare PDF-Fassung
    /// (<see cref="EbookFile.Derived"/>), die der Browser-Leser zeigen kann.</summary>
    public EbookConversionState ConversionState { get; set; } = EbookConversionState.NotNeeded;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

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
    /// die Lage (<see cref="X"/> …) bleibt 0. <c>null</c> bei PDF-Diagrammen.</summary>
    [MaxLength(300)]
    public string? ImageRef { get; set; }

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
