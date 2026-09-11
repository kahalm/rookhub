using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>Wo eine Bibliothekspartie in der Vorsortierung steht.</summary>
public enum LibraryGameStatus
{
    /// <summary>Eingelesen, noch von niemandem angesehen.</summary>
    New = 0,
    /// <summary>Kommt in die engere Wahl — der Vorrat, aus dem analysiert wird.</summary>
    Shortlisted = 1,
    /// <summary>Aussortiert (mit <see cref="LibraryGame.Note"/> als Begruendung).</summary>
    Rejected = 2,
    /// <summary>Ist als <see cref="GameAnalysis"/> uebernommen — <see cref="LibraryGame.GameAnalysisId"/> zeigt darauf.</summary>
    Imported = 3,
    /// <summary>Dieselben Zuege stehen schon unter einer anderen Id (<see cref="LibraryGame.DuplicateOfId"/>).</summary>
    Duplicate = 4,
}

/// <summary>
/// Eine Partie im ROHBESTAND: eingelesen aus einer PGN-Sammlung, noch nicht ausgewaehlt und noch
/// nicht gerechnet. Die Ablage zwischen „liegt als Datei da" und „ist eine Partie-Analyse".
///
/// <para><b>Warum eine eigene Tabelle und nicht gleich <see cref="GameAnalysis"/>:</b> eine
/// Analyse ist ein VERSPRECHEN an die Engine — je Halbzug ein Auftrag, bei Tiefe 20 grob zwanzig
/// Sekunden je Stellung, also eine halbe Stunde je Partie. Bei 130 679 kommentierten Meisterpartien
/// aus dem ChessBase-Magazin waeren das Jahre Rechenzeit. Die Auswahl ist damit die eigentliche
/// Arbeit, und sie braucht einen Ort, an dem die Partien liegen, waehrend sie getroffen wird.</para>
///
/// <para><b>Die Zahlenspalten sind der Sinn der Sache.</b> Sie stehen hier, damit sich der Bestand
/// SORTIEREN laesst, ohne 338 MB PGN erneut zu lesen: wie viele Halbzuege traegt ein Kommentar,
/// wie viel Text ist es, wie viele Nebenvarianten. Sie werden nach und nach befuellt — beim
/// Einlesen, was ohne Aufwand mitfaellt, spaeter der Rest (Sprache, Eignungsnote). Ein Feld, das
/// noch niemand befuellt hat, ist deshalb <c>null</c> und nicht 0: „nicht gezaehlt" und „keine
/// Kommentare" sind verschiedene Aussagen, und die Vorsortierung haengt genau daran.</para>
/// </summary>
public class LibraryGame
{
    public int Id { get; set; }

    // ===== Herkunft ==========================================================

    /// <summary>Datei, aus der die Partie eingelesen wurde (Dateiname, kein Pfad — der aendert sich).</summary>
    [MaxLength(200)] public string? SourceFile { get; set; }

    /// <summary>PGN-Tag <c>[SourceTitle]</c>, bei ChessBase-Ausgaben etwa „CBM 104 Extra". Die
    /// eigentliche Herkunft der Partie, unabhaengig davon, ueber welche Datei sie herkam.</summary>
    [MaxLength(200)] public string? SourceTitle { get; set; }

    /// <summary>PGN-Tag <c>[Source]</c> („ChessBase", „TWIC", …).</summary>
    [MaxLength(100)] public string? SourceRef { get; set; }

    /// <summary>PGN-Tag <c>[GameId]</c> der Quelle — erlaubt spaeter den Abgleich mit derselben
    /// Sammlung, ohne die Zuege zu vergleichen. Nicht eindeutig ueber Quellen hinweg.</summary>
    [MaxLength(40)] public string? ExternalGameId { get; set; }

    /// <summary>
    /// SHA-256 ueber die normalisierte Zugfolge — der Griff, an dem Dubletten haengen.
    ///
    /// <para>Eine Sammlung aus Zeitschriften-Ausgaben enthaelt dieselbe Partie mehrfach, jedes Mal
    /// von jemand anderem kommentiert. Ueber Namen und Datum zu vergleichen scheitert an
    /// Schreibweisen; die Zuege sind das, was wirklich gleich ist. Der Hash steht in einer eigenen
    /// Spalte mit Index, weil die Frage „kenne ich die schon" bei jedem Einlesen 130 000-mal
    /// gestellt wird.</para>
    /// </summary>
    [MaxLength(64)] public string? MovesHash { get; set; }

    /// <summary>Zeigt auf die zuerst eingelesene Fassung, wenn diese hier eine Dublette ist.
    /// Bewusst behalten statt geloescht: die zweite Fassung traegt die Kommentare eines ANDEREN
    /// Kommentators, und welche die bessere ist, entscheidet die Vorsortierung.</summary>
    public int? DuplicateOfId { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ===== Kopfdaten der Partie =============================================

    [MaxLength(120)] public string? White { get; set; }
    [MaxLength(120)] public string? Black { get; set; }
    public int? WhiteElo { get; set; }
    public int? BlackElo { get; set; }
    [MaxLength(16)]  public string? Result { get; set; }
    [MaxLength(200)] public string? Event { get; set; }
    [MaxLength(120)] public string? Site { get; set; }
    [MaxLength(20)]  public string? Round { get; set; }

    /// <summary>Partiedatum, soweit das PGN eines nennt. Als Datum und nicht als Text, damit sich
    /// nach Epoche sortieren laesst — „Meisterpartien" heisst bei Steinitz etwas anderes als heute.
    /// Unvollstaendige PGN-Daten („1904.??.??") landen als <c>null</c>.</summary>
    public DateOnly? PlayedOn { get; set; }

    [MaxLength(8)] public string? Eco { get; set; }

    /// <summary>Startstellung, falls das PGN einen <c>[FEN]</c>-Tag traegt; <c>null</c> = Grundstellung.</summary>
    [MaxLength(120)] public string? StartFen { get; set; }

    /// <summary>Laenge der Partie in Halbzuegen. Der erste grobe Filter: unter etwa dreissig
    /// Halbzuegen gibt eine Punktepartie nichts her, ueber zweihundert wird sie zur Sitzung.</summary>
    public int? PlyCount { get; set; }

    // ===== Kommentar-Merkmale (die Auswahlkriterien) =========================

    /// <summary>PGN-Tag <c>[Annotator]</c> — wer die Partie kommentiert hat.</summary>
    [MaxLength(200)] public string? Annotator { get; set; }

    /// <summary>Anzahl der Kommentare in geschweiften Klammern.</summary>
    public int? CommentCount { get; set; }

    /// <summary>Wie viele HALBZUEGE einen Kommentar tragen. Das ist die Zahl, die zaehlt: die
    /// Punktepartie haelt an kommentierten Zuegen an, eine Partie mit drei langen Kommentaren am
    /// Ende taugt dafuer weniger als eine mit dreissig kurzen entlang der Partie.</summary>
    public int? CommentedPlies { get; set; }

    /// <summary>Zeichen Kommentartext insgesamt. Trennt „gute Partie!" von echter Prosa.</summary>
    public int? CommentChars { get; set; }

    /// <summary>Anzahl der NAG-Symbole (<c>$1</c>, <c>$16</c>, …). Eine Partie ganz ohne Klammern
    /// kann trotzdem bewertet sein — nur eben ohne Worte.</summary>
    public int? NagCount { get; set; }

    /// <summary>Anzahl der Nebenvarianten. Viel Analyse ist nicht dasselbe wie viel Erklaerung;
    /// fuer die Punktepartie ist eine erklaerte Partie mehr wert als eine durchgerechnete.</summary>
    public int? VariationCount { get; set; }

    /// <summary>Der ERSTE kommentierte Halbzug der Hauptvariante (1-basiert); <c>null</c> = keiner.
    /// Der Punkt, an dem der Kommentator die Partie fuer erklaerungsbeduerftig hielt — einer der
    /// beiden Hinweise darauf, wo eine Punktepartie sinnvoll anfaengt.</summary>
    public int? FirstCommentedPly { get; set; }

    /// <summary>
    /// Die ersten <see cref="LibraryGameReader.OpeningPlies"/> Halbzuege normalisiert und durch
    /// Leerzeichen getrennt („e4 e5 Nf3 Nc6 …").
    ///
    /// <para>Damit wird der Bestand zur Eroeffnungsstatistik: „wie viele dieser 130 000 Partien
    /// spielen dieselben ersten k Zuege" ist eine Praefix-Suche auf einer indizierten Spalte
    /// (<c>LIKE 'e4 e5 Nf3%'</c>) und keine Volltextsuche ueber 338 MB. Der erste Halbzug, an dem
    /// diese Zahl klein wird, ist der Zug, an dem die Partie das Buch verlaesst.</para>
    /// </summary>
    [MaxLength(200)] public string? OpeningLine { get; set; }

    /// <summary>
    /// Spieler, Turnier und Kommentator in EINER kleingeschriebenen Zeichenkette — das Feld, ueber
    /// das die Bestandssuche laeuft. Darauf liegt ein VOLLTEXT-Index.
    ///
    /// <para><b>Warum nicht einfach ueber die vier Spalten suchen:</b> gemessen am echten Bestand
    /// (2026-09-11, 130 572 Zeilen) kostete ein <c>LIKE '%Capablanca%'</c> ueber eine einzige dieser
    /// Spalten 4,5 Sekunden — die Tabelle traegt 338 MB Partietext, und jede Teilzeichenketten-Suche
    /// liest sie ganz. Ueber eine schmale indizierte Spalte wurde daraus knapp eine Sekunde, mit
    /// Sortierung nach der Eignungsnote aber fuenfzig (der Optimierer nahm den Score-Index und
    /// suchte sich zeilenweise durch). Mit dem Volltext-Index sind es 13 ms.</para>
    ///
    /// <para>Volltext heisst WORTANFANG, nicht Teilzeichenkette: „Capa" findet „Capablanca",
    /// „blanca" nicht. Fuer Namen ist das genau die Suche, die Leute tippen.</para>
    /// </summary>
    [MaxLength(190)] public string? SearchText { get; set; }

    /// <summary>Sprache(n) der Kommentare als CSV von ISO-Kuerzeln („de", „en,de"). Wird erst
    /// spaeter befuellt (Erkennung ueber den Kommentartext), deshalb nullbar.</summary>
    [MaxLength(40)] public string? Languages { get; set; }

    // ===== Vorsortierung =====================================================

    /// <summary>Eignungsnote fuer die Punktepartie, aus den Zahlen oben errechnet. Eine eigene
    /// Spalte und keine Formel bei der Abfrage: die Gewichtung wird sich aendern, und dann soll man
    /// den Bestand einmal neu bewerten und danach danach sortieren koennen.</summary>
    public int? Score { get; set; }

    public LibraryGameStatus Status { get; set; } = LibraryGameStatus.New;

    /// <summary>Gesetzt, sobald die Partie als Analyse uebernommen wurde.</summary>
    public int? GameAnalysisId { get; set; }

    /// <summary>Notiz aus der Durchsicht („Kommentare nur auf Russisch", „Endspiel zu kurz").</summary>
    [MaxLength(500)] public string? Note { get; set; }

    // ===== Die Partie selbst =================================================

    /// <summary>
    /// Das vollstaendige PGN dieser EINEN Partie, Kopfzeilen inklusive.
    ///
    /// <para>Bewusst hier und nicht als Verweis auf Datei und Byte-Position: die Quelldatei ist ein
    /// Fund im Ablage-Ordner und keine Zusage. Haengt der Bestand an ihr, ist er weg, sobald jemand
    /// aufraeumt — und dann steht eine Tabelle mit 130 000 Zeilen Statistik da, zu der es keine
    /// Zuege mehr gibt. Der Preis sind grob 350 MB in der Datenbank.</para>
    /// </summary>
    public string Pgn { get; set; } = string.Empty;
}
