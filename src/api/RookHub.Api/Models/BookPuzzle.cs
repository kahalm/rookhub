using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

public class BookPuzzle
{
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string LineId { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string BookFileName { get; set; } = string.Empty;

    /// <summary>FK auf <see cref="Models.Book"/>. Nullable für Altbestand (Backfill via Migration).</summary>
    public int? BookId { get; set; }
    public Book? Book { get; set; }

    [Required, MaxLength(20)]
    public string Round { get; set; } = string.Empty;

    [Required]
    public string Fen { get; set; } = string.Empty;

    [Required]
    public string Moves { get; set; } = string.Empty;

    /// <summary>
    /// Halbzug-Index, ab dem das Training startet (der per ChessBase-[%tqu] markierte Zug).
    /// <see cref="Fen"/> + <see cref="Moves"/> enthalten die KOMPLETTE Partie; beim Lösen wird
    /// bis <c>moves[StartPly]</c> (Setup) vorgespult, gelöst wird ab <c>moves[StartPly+1]</c>.
    /// 0 = kein Trainingsmarker (klassisch: moves[0] Setup, lösen ab moves[1]).
    /// </summary>
    public int StartPly { get; set; }

    [MaxLength(300)]
    public string? Title { get; set; }

    [MaxLength(200)]
    public string? Chapter { get; set; }

    /// <summary>Einleitungs-/Erklärkommentar der Linie. LONGTEXT (keine Längenbegrenzung mehr — Chessable-
    /// Intro-/Erklärlinien können mehrere Tausend Zeichen lang sein; früher varchar(5000) → abgeschnitten).</summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Pro-Zug-Kommentare der Hauptlinie als JSON-Objekt <c>{ "plyIndex": "text" }</c>. Der Schlüssel
    /// ist der 0-basierte Halbzug-Index in <see cref="Moves"/>, NACH dessen Zug der Kommentar in der
    /// PGN steht; <c>-1</c> = Einleitungskommentar vor dem ersten Zug. Null/leer, wenn das Buch keine
    /// Zug-Kommentare hat. Wird beim Durchspielen/Review im Frontend Schritt für Schritt angezeigt.
    /// (LONGTEXT — keine Längenbegrenzung, ganze Bücher können sehr ausführlich kommentiert sein.)
    /// </summary>
    public string? MoveComments { get; set; }

    [MaxLength(50)]
    public string? Difficulty { get; set; }

    public int? BookRating { get; set; }

    [MaxLength(200)]
    public string? Tags { get; set; }

    /// <summary>
    /// Vorberechnete, gestufte Lösungstipps (1=Motiv, 2=Figur/Bereich, 3=erster Zug) als JSON-Objekt,
    /// sprach-keyed: <c>{ "de": [h1,h2,h3], "en": [...], "hr": [...] }</c>. Per LLM beim Import/Reprocess
    /// erzeugt (siehe <see cref="HintGenerationService"/>), null wenn noch nicht generiert. LONGTEXT.
    /// </summary>
    public string? HintsJson { get; set; }

    /// <summary>Version des Tipp-Generators, mit dem <see cref="HintsJson"/> erzeugt wurde
    /// (0 = noch keine Tipps). Erlaubt späteres Neu-Generieren unabhängig von <c>Book.ImportVersion</c>.</summary>
    public int HintsVersion { get; set; }

    /// <summary>Vom Admin als „dumme/schlechte Tipps" markiert (Review-Flag fürs gezielte Neu-Generieren).
    /// Per Button im Solver gesetzt; rein redaktionell, beeinflusst die Anzeige der Tipps nicht.</summary>
    public bool HintsFlagged { get; set; }

    /// <summary>
    /// „Ausgemustert": Wird nicht mehr in den Zufalls-Pools (Daily/Random/Blind) gezogen.
    /// Gesetzt z. B. wenn ein Admin das Tagespuzzle für ein Datum neu generiert — das bis dahin
    /// gezogene Puzzle soll danach nie wieder als Tages-/Zufallspuzzle erscheinen. Direkter Aufruf
    /// per Id, Buch-Navigation (next/random im Buch) und persistierte Vergangenheits-Zuordnungen
    /// bleiben unberührt.
    /// </summary>
    public bool Retired { get; set; }

    /// <summary>
    /// „Info-/Erklärlinie": eine aus Chessable importierte Variante, die nur der Erklärung dient
    /// (Chessable <c>IsInfo=1</c> → piratechess emittiert den <c>[%info]</c>-Marker im PGN). Solche
    /// Linien werden <b>nicht als Quiz abgefragt</b>: sie erscheinen in keinem Zufalls-/Tagespuzzle-Topf,
    /// zählen nicht zum Kurs-Fortschritt (Total/„X gelöst"/100 %) und sind im sequenziellen Kurs-Modus
    /// nur zum Durchklicken da. Wird beim Import aus dem <c>[%info]</c>-Marker gesetzt.
    /// </summary>
    public bool IsInfoOnly { get; set; }
}
