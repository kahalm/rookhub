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
/// Analyse. Genau EINES von beiden ist gesetzt.</para>
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
    /// ersten Zug (die Einleitung).</summary>
    public int Ply { get; set; }

    public string Text { get; set; } = string.Empty;
}
