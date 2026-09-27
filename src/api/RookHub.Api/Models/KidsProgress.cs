namespace RookHub.Api.Models;

/// <summary>
/// Fortschritt eines angemeldeten Kindes auf KidHub in EINER Stufe der Leiter: die beste Sternzahl und
/// der laufende Durchgang. Ohne Konto liegt dasselbe nur im Browser; angemeldet gleicht KidHub beide
/// Stände ab (<see cref="Services.KidsProgressMerge"/>).
/// </summary>
public class KidsLevelProgress
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Stufe, 1-basiert (<see cref="KidsPuzzle.Level"/>).</summary>
    public int Level { get; set; }

    /// <summary>Beste Sternzahl eines geschafften Durchgangs, 0–3 (0 = noch nie geschafft).</summary>
    public int Stars { get; set; }

    /// <summary>Laufender Durchgang: naechste Aufgabe (0-basiert) und bisherige Fehler.</summary>
    public int RunIndex { get; set; }
    public int RunMistakes { get; set; }

    /// <summary>Wann sich der laufende Durchgang zuletzt geaendert hat — laut dem GERAET, das ihn
    /// spielte. Beim Abgleich gewinnt der juengere Durchgang; Sterne gewinnen immer nach Hoehe.</summary>
    public DateTime RunAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Stand eines Kinderkurses (<see cref="Book.ForKids"/>) fuer ein angemeldetes Kind: nur, wann er
/// zuletzt von vorn begonnen wurde. Die geloesten Linien stehen in <see cref="KidsCourseLine"/>.
/// </summary>
public class KidsCourseProgress
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public int BookId { get; set; }
    public Book? Book { get; set; }

    /// <summary>„Von vorn" — Linien, die davor geloest wurden, zaehlen nicht mehr. Ohne diese Marke
    /// braechte der Abgleich mit einem anderen Geraet die gerade verworfenen Linien zurueck.</summary>
    public DateTime? ResetAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>Eine geloeste Linie eines Kinderkurses.</summary>
public class KidsCourseLine
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public int BookId { get; set; }
    public Book? Book { get; set; }

    /// <summary>Die Linie (<see cref="BookPuzzle"/>) — bewusst OHNE Fremdschluessel: eine geloeschte
    /// Linie laesst hier nur eine Zahl zurueck, die keine Aufgabe mehr trifft, und haelt kein
    /// Loeschen auf.</summary>
    public int BookPuzzleId { get; set; }

    public DateTime SolvedAt { get; set; }
}
