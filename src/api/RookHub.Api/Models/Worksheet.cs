namespace RookHub.Api.Models;

/// <summary>
/// Ein AUFGABENBLATT: eine benannte, sortierte Sammlung von Stellungen, die man ausdruckt
/// (6/4/2 Diagramme je A4-Seite, Platz für die Lösung).
///
/// <para>Jeder Nutzer hat genau EIN Blatt mit <see cref="IsClipboard"/> — die „Zwischenablage":
/// der Sammelkorb, in dem alles landet, was man unterwegs an ein Aufgabenblatt schickt (Kapitel,
/// markierte Linien, das zuletzt gelöste Puzzle). Aus der Zwischenablage wird per „Als Aufgabenblatt
/// speichern" ein benanntes Blatt — die Stellungen WANDERN dabei mit, die Ablage ist danach wieder
/// leer und frei fürs nächste Blatt.</para>
/// </summary>
public class Worksheet
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>Name des Blatts; bei der Zwischenablage leer (die heißt in der UI übersetzt).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Die eine Zwischenablage des Nutzers (Sammelkorb) — nicht löschbar, nur leerbar.</summary>
    public bool IsClipboard { get; set; }

    /// <summary>Diagramme je A4-Seite: 6 (knapp, wie das alte Blatt), 4 oder 2 (Platz für Begleittexte).</summary>
    public int PerPage { get; set; } = 6;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<WorksheetItem> Items { get; set; } = new();
}

/// <summary>Woher eine Stellung auf dem Blatt stammt — nur Herkunftsvermerk (Rücksprung in den Kurs).</summary>
public enum WorksheetItemSource
{
    /// <summary>Von Hand eingegeben (FEN eingefügt).</summary>
    Manual = 0,
    /// <summary>Standard-/Endlos-Puzzle (<c>Puzzles.Id</c>).</summary>
    Standard = 1,
    /// <summary>Kurs-/Buch-Linie (<c>BookPuzzles.Id</c>).</summary>
    Book = 2,
}

/// <summary>
/// Eine Aufgabe auf dem Blatt. Die Stellung ist als FEN AUSGESCHRIEBEN und damit unabhängig von
/// ihrer Quelle: ein neu importierter Kurs, ein gelöschtes Puzzle oder eine umsortierte Linie
/// ändern ein einmal zusammengestelltes Blatt nicht mehr. <see cref="Source"/>/<see cref="SourceId"/>
/// sind reiner Herkunftsvermerk (kein FK, bewusst) — für „im Kurs nachsehen".
/// </summary>
public class WorksheetItem
{
    public int Id { get; set; }

    public int WorksheetId { get; set; }
    public Worksheet Worksheet { get; set; } = null!;

    /// <summary>Position auf dem Blatt (aufsteigend, Lücken erlaubt — die Reihenfolge zählt, nicht der Wert).</summary>
    public int SortOrder { get; set; }

    /// <summary>Die gefragte Stellung (Vorspielzüge sind eingerechnet).</summary>
    public string Fen { get; set; } = string.Empty;

    /// <summary>Brett-Ausrichtung beim Druck: <c>white</c> oder <c>black</c> (Sicht des Lösenden).</summary>
    public string Orientation { get; set; } = "white";

    /// <summary>Überschrift über dem Diagramm (vom Nutzer gesetzt; leer = keine).</summary>
    public string Heading { get; set; } = string.Empty;

    /// <summary>Begleittext unter dem Diagramm (vom Nutzer gesetzt; leer = keiner).</summary>
    public string Text { get; set; } = string.Empty;

    public WorksheetItemSource Source { get; set; } = WorksheetItemSource.Manual;

    /// <summary>ID in der zur <see cref="Source"/> passenden Tabelle; <c>null</c> bei Handeingabe.</summary>
    public int? SourceId { get; set; }

    /// <summary>Kurs/Buch der Herkunft (nur bei <see cref="WorksheetItemSource.Book"/>) — für den Rücksprung.</summary>
    public int? BookId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
