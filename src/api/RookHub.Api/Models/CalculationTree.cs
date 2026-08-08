using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Der selbst eingeklickte Analysebaum EINES Users zu EINER Stellung eines Kalkulationsbuchs
/// (<see cref="Book.IsCalculation"/>). Im Kalkulations-Modus gibt es keine Lösung: der Nutzer
/// rechnet die Stellung am eingefrorenen Brett durch und legt seine Varianten (für beide Seiten)
/// samt Kommentaren und Bewertungssymbolen selbst an.
/// <para>Der Baum wird als opakes JSON gehalten (<see cref="TreeJson"/>, Schema-Version im Dokument),
/// weil nur das Frontend ihn interpretiert — so kann sich die Baum-Struktur weiterentwickeln, ohne
/// dass jede Änderung eine Migration braucht. Unique über (UserId, BookPuzzleId) ⇒ ein Baum je
/// Nutzer und Stellung; <see cref="BookId"/> ist denormalisiert (= BookPuzzle.BookId) für die
/// „bearbeitet"-Zähler der Kursübersicht ohne Join.</para>
/// </summary>
public class CalculationTree
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public int BookId { get; set; }
    public Book? Book { get; set; }

    public int BookPuzzleId { get; set; }
    public BookPuzzle? BookPuzzle { get; set; }

    /// <summary>Serialisierter Analysebaum (LONGTEXT). Vom Server nicht interpretiert, nur auf
    /// gültiges JSON und Maximalgröße geprüft (siehe <c>CalculationService.MaxTreeJsonLength</c>).
    /// <para><b>Leer</b> ist erlaubt: die Zeile trägt auch dann Sinn, wenn nur Rechenzeit/Festlegung/
    /// Bewertung gesetzt sind, aber (noch) kein Baum gespeichert wurde. „Hat einen Baum" ist deshalb
    /// überall <c>TreeJson != ""</c>, nicht „Zeile existiert".</para></summary>
    public string TreeJson { get; set; } = string.Empty;

    /// <summary>
    /// Zug, auf den sich der Nutzer festgelegt hat, in SAN (z. B. „Nd5") — immer ein ERSTER Zug
    /// (Kind der Baumwurzel), höchstens einer je Stellung. <c>null</c> = keine Festlegung.
    /// <para>Bewusst eine eigene Spalte statt im opaken <see cref="TreeJson"/> vergraben: nur so
    /// lassen sich Festlegungen kapitel-/kursweit zählen und auswerten.</para>
    /// </summary>
    [MaxLength(20)]
    public string? ChosenSan { get; set; }

    /// <summary>Derselbe Zug in UCI (z. B. „c3d5") — damit das Frontend ihn ohne SAN-Parsing im
    /// Baum wiederfindet. Wird immer zusammen mit <see cref="ChosenSan"/> gesetzt/gelöscht.</summary>
    [MaxLength(10)]
    public string? ChosenUci { get; set; }

    /// <summary>Aktive Rechenzeit an dieser Stellung in Sekunden, über alle Besuche AUFSUMMIERT
    /// (der Client schickt Deltas, siehe <c>CalculationService.MaxSecondsPerFlush</c>).</summary>
    public int SecondsSpent { get; set; }

    /// <summary>
    /// IDEMPOTENZ-MARKE des zuletzt verbuchten Zeit-Deltas (vom Client vergeben, für den Server
    /// opak). <c>null</c> = es wurde noch nie Zeit mit Marke gebucht.
    /// <para>Ohne sie wäre <see cref="SecondsSpent"/> nicht wiederholungsfest: Zeit kommt als DELTA
    /// und wird ADDIERT, und ein Client, dessen Anfrage ankam, dessen ANTWORT aber verloren ging
    /// (Timeout, Verbindungsabbruch, 502), wiederholt sie — die Zeit würde still ein zweites Mal
    /// gezählt und wäre nicht mehr korrigierbar. Ein Patch mit bereits verbuchter Marke lässt
    /// deshalb nur noch den ZEIT-Anteil aus; Stufe und Festlegung sind idempotent (sie SETZEN) und
    /// laufen weiter durch.</para>
    /// </summary>
    [MaxLength(64)]
    public string? SecondsToken { get; set; }

    /// <summary>
    /// Wie viele Sekunden unter <see cref="SecondsToken"/> bereits verbucht wurden. Nötig, weil der
    /// Client einen noch nicht bestätigten Patch beim Wiedereinreihen um neu gemessene Zeit WACHSEN
    /// lässt (er behält dabei die Marke): angerechnet wird dann nur die Differenz. Ohne diesen Wert
    /// müsste der Server den gewachsenen Patch komplett verwerfen (gemessene Zeit ginge verloren)
    /// oder komplett anrechnen (Doppelzählung).
    /// </summary>
    public int SecondsTokenApplied { get; set; }

    /// <summary>
    /// Selbstbewertung als benannte STUFE (<see cref="CalculationGrade"/>, 0..4), vergeben NACHDEM
    /// der Nutzer die Lösung anderswo geprüft hat. Gespeichert wird die Stufe, nicht die Punktzahl:
    /// die Punkte sind eine Ableitung (<see cref="CalculationGrades.PointsFor(int)"/>) und dürfen
    /// neu gewichtet werden, ohne die Bedeutung alter Bewertungen zu ändern.
    /// <para><c>null</c> = noch nicht bewertet und ausdrücklich etwas anderes als Stufe 0
    /// („nicht gelöst" = geprüft und danebengelegen). Der Server liefert weiterhin keine Lösung
    /// aus — die Stufe ist reine Selbsteinschätzung.</para>
    /// </summary>
    public int? Grade { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
