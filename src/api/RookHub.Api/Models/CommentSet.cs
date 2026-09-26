namespace RookHub.Api.Models;

/// <summary>
/// EIN Satz Zug-Kommentare in EINER Sprache zu EINER Partie.
///
/// <para><b>Warum getrennt vom PGN.</b> Das PGN ist die QUELLE — es traegt Herkunft und Ausgabe
/// (<c>SourceTitle</c>, <c>SourceVersion</c>) einer gekauften Sammlung. Wer eine Uebersetzung
/// hineinschreibt, kann Quelle und Zutat nie wieder auseinanderhalten, und ein erneutes Einlesen
/// der Datei wuerde sie verwerfen. Hier daneben abgelegt bleibt beides fuer sich.</para>
///
/// <para><b>Warum ein SATZ und nicht Zeilen mit Sprachspalte.</b> Die Herkunft (aus der Quelle
/// gelesen? maschinell uebersetzt? mit welchem Modell?) gehoert EINMAL je Sprache hin und nicht an
/// jede Zeile; ein Uebersetzungslauf schreibt einen Satz am Stueck; und „Sprache umschalten" ist
/// dann eine Abfrage statt eines Filters ueber Zeilen verschiedener Herkunft.</para>
///
/// <para><b>Der Anker ist die BIBLIOTHEKSZEILE</b>, wo es eine gibt: fordern zwei Leute dieselbe
/// Partie an, entstehen zwei Analysen — eine Uebersetzung je Analyse waere dieselbe Arbeit zweimal
/// bezahlt. Eine selbst eingeworfene Partie hat keine Bibliothekszeile und haengt deshalb an der
/// Analyse.</para>
///
/// <para><b>Die dritte Art ist die LINIE EINES KURSES</b> (<see cref="BookPuzzleId"/>, seit 0.547.0):
/// Zug-Kommentare, Einleitung, Linien-Titel und Kapitelname in einer weiteren Sprache. Fuer Kurse gibt
/// es KEINEN Quell-Satz — die Quelle bleibt <see cref="BookPuzzle.Comment"/>/<see cref="BookPuzzle.MoveComments"/>/
/// <see cref="BookPuzzle.Title"/>/<see cref="BookPuzzle.Chapter"/>, denn genau diese Felder ueberschreiben
/// Aufbereitung und naechtliches Aktualisieren; ein zweiter Quell-Satz muesste staendig nachgezogen werden.
/// Jeder Text eines Kurs-Satzes traegt deshalb den Fingerabdruck seiner Vorlage
/// (<see cref="CommentText.SourceHash"/>), die Belegung der Halbzug-Nummern steht in
/// <c>Services.CourseTextSlots</c>.</para>
///
/// <para>Genau EINER der drei Anker ist gesetzt.</para>
/// </summary>
public class CommentSet
{
    public int Id { get; set; }

    /// <summary>Die Zeile des Rohbestands — geteilt ueber alle Analysen derselben Partie.</summary>
    public int? LibraryGameId { get; set; }
    public LibraryGame? LibraryGame { get; set; }

    /// <summary>Eine selbst eingeworfene Partie ohne Bibliothekszeile.</summary>
    public int? GameAnalysisId { get; set; }
    public GameAnalysis? GameAnalysis { get; set; }

    /// <summary>Eine Linie eines Kurses (Uebersetzung, nie Quelle). Faellt die Linie weg, faellt der
    /// Satz mit (Cascade; die Loeschpfade raeumen ihn zusaetzlich AUSDRUECKLICH ab — InMemory
    /// kaskadiert nicht).</summary>
    public int? BookPuzzleId { get; set; }
    public BookPuzzle? BookPuzzle { get; set; }

    /// <summary>ISO-Kuerzel, wie <see cref="LibraryGame.Languages"/> sie fuehrt („de", „en").</summary>
    public string Language { get; set; } = string.Empty;

    public CommentOrigin Origin { get; set; } = CommentOrigin.Source;

    /// <summary>Die Sprache der Vorlage — nur bei einer Uebersetzung gesetzt.</summary>
    public string? TranslatedFrom { get; set; }

    /// <summary>Womit uebersetzt wurde. Ohne diese Angabe laesst sich spaeter nicht entscheiden,
    /// welche Uebersetzungen ein besseres Modell neu machen sollte.</summary>
    public string? Model { get; set; }

    public CommentSetStatus Status { get; set; } = CommentSetStatus.Ready;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<CommentText> Texts { get; set; } = [];
}

/// <summary>Woher der Text stammt. Die Unterscheidung ist keine Buchhaltung: eine maschinelle
/// Uebersetzung, die sich als die Anmerkung des Grossmeisters ausgibt, ist eine Falschaussage
/// ueber die Quelle — das Brett kennzeichnet sie deshalb.</summary>
public enum CommentOrigin
{
    /// <summary>So aus dem PGN gelesen (ggf. aus einem zweisprachigen Block herausgetrennt).</summary>
    Source = 0,
    Machine = 1,
    Human = 2,
}

public enum CommentSetStatus
{
    /// <summary>Angelegt, aber noch nicht vollstaendig — ein laufender Uebersetzungsdurchgang.</summary>
    Draft = 0,
    Ready = 1,
}

/// <summary>Ein Kommentar innerhalb eines <see cref="CommentSet"/> — je Halbzug einer.</summary>
public class CommentText
{
    public int Id { get; set; }

    public int CommentSetId { get; set; }
    public CommentSet? CommentSet { get; set; }

    /// <summary>Zaehlt wie <see cref="GameAnalysisPosition.Ply"/>; <c>-1</c> = der Text vor dem
    /// ersten Zug (die Einleitung). Bei Kurs-Saetzen zusaetzlich <c>-2</c> = <see cref="BookPuzzle.Comment"/>,
    /// <c>-3</c> = Linien-Titel, <c>-4</c> = Kapitelname (<c>Services.CourseTextSlots</c>).</summary>
    public int Ply { get; set; }

    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Fingerabdruck der VORLAGE, aus der dieser Text entstand: 16 Hex-Zeichen, Praefix von SHA-256
    /// ueber den normalisierten Quelltext (<c>Services.CourseTextHash</c>). Pflicht bei Kurs-Saetzen,
    /// <c>null</c> bei Partien.
    ///
    /// <para>Zwei Aufgaben: (1) <b>Veraltet?</b> Aendert die Aufbereitung den Kommentar einer Linie, passt
    /// der Fingerabdruck nicht mehr — ausgeliefert wird dann das Original, und der naechste Lauf
    /// uebersetzt nur diesen Text neu. (2) <b>Schon einmal uebersetzt?</b> Derselbe Text steht oft in
    /// mehreren Kursen (mehrfach importierte Chessable-Kurse, <c>_firstkey</c>-Kopien, gleiche
    /// Kapitelnamen) — dann wird kopiert statt noch einmal bezahlt.</para>
    /// </summary>
    public string? SourceHash { get; set; }
}
