using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// ClubHub — Kartei der Kinder und Jugendlichen des Vereins (Wunsch 2026-09-30). Ein Eintrag ist ein KARTEIBLATT, kein
/// Konto: das Kind meldet sich nirgends an. Hat es ein RookHub-/KidHub-Konto, wird es über einen Einmal-Code verknüpft
/// (<see cref="LinkedUserId"/>) — den Code löst das KONTO ein, nicht der Trainer, damit niemand fremde Konten an ein Blatt
/// hängt und deren Fortschritt liest.
///
/// <para>Daten von Minderjährigen: nur mit <see cref="Permissions.ClubManage"/> bzw. als Trainer einer Gruppe des Kindes
/// (<see cref="Permissions.ClubTrainer"/>) lesbar, nie öffentlich, nie im Log. Löschen entfernt das Blatt mit Kontakten,
/// Notizen und Anwesenheit.</para>
/// </summary>
public class ClubMember
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Leer = nicht bekannt (Wunsch 2026-09-30: „Nachname optional, ich weiß den oft nicht"). Die Spalte bleibt
    /// NOT NULL — ein leerer Text, kein <c>null</c>; sortiert wird dann nach dem Vornamen.</summary>
    [Required(AllowEmptyStrings = true), MaxLength(80)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>Ganzes Geburtsdatum, wenn bekannt — sonst nur <see cref="BirthYear"/>.</summary>
    public DateOnly? BirthDate { get; set; }

    /// <summary>Jahrgang (für die Altersklasse U8 … U18). Bei gesetztem <see cref="BirthDate"/> dessen Jahr.</summary>
    public int? BirthYear { get; set; }

    /// <summary>Stufe/Diplom als freier Text („Bauerndiplom", „Stufe 2").</summary>
    [MaxLength(60)]
    public string? Level { get; set; }

    // FIDE-ID, ÖSB-Nummer, Notiz und Foto-Einwilligung gab es in 0.613.0–0.617.1; der User wollte sie nicht
    // („weitere Angaben entfernen", 2026-09-30) — Migration ClubMemberWithoutExtras hat die Spalten entfernt.

    /// <summary>Nicht mehr im Training (ausgetreten, pausiert) — bleibt in der Kartei, fällt aus den Listen.</summary>
    public bool Archived { get; set; }

    /// <summary>
    /// Ein TRAINER statt eines Kindes (Wunsch 2026-09-30: „liste unter den Kindern auch die Trainer auf bei der
    /// Anwesenheit"). Dieselbe Kartei (Kontakte, Archiv), aber: in JEDER Abhak-Liste unter den Kindern, ohne
    /// Gruppenzugehörigkeit, für jeden mit Club-Recht sichtbar, ohne Lernstand. Kein Konto — wer ClubHub BEDIENEN darf,
    /// regelt weiter <see cref="ClubGroupTrainer"/> (Konto je Gruppe).
    /// </summary>
    public bool IsTrainer { get; set; }

    /// <summary>Verknüpftes Konto (FK SetNull). Höchstens EIN Blatt je Konto.</summary>
    public int? LinkedUserId { get; set; }
    public AppUser? LinkedUser { get; set; }

    /// <summary>Offener Einmal-Code zum Verknüpfen; mit dem Einlösen oder Ablaufen geleert.</summary>
    [MaxLength(16)]
    public string? LinkCode { get; set; }
    public DateTime? LinkCodeExpires { get; set; }

    public DateTime CreatedAt { get; set; }
    /// <summary>Kein FK — das Blatt überlebt das Konto dessen, der es angelegt hat.</summary>
    public int? CreatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<ClubContact> Contacts { get; set; } = new();
    public List<ClubGroupMember> Groups { get; set; } = new();
    public List<ClubNote> NoteEntries { get; set; } = new();
}

/// <summary>
/// Ein Kontakt zu einem Kind: Telefonnummer oder E-Mail, beliebig viele je Kind, jeder mit einem Hinweis, WESSEN er ist
/// („Mutter Daniela", „Vater Franz").
/// </summary>
public class ClubContact
{
    public const string Phone = "phone";
    public const string Email = "email";

    public int Id { get; set; }
    public int MemberId { get; set; }
    public ClubMember? Member { get; set; }

    /// <summary><see cref="Phone"/> oder <see cref="Email"/>.</summary>
    [Required, MaxLength(8)]
    public string Kind { get; set; } = Phone;

    [Required, MaxLength(200)]
    public string Value { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? Label { get; set; }

    /// <summary>Reihenfolge, wie eingegeben — der erste Kontakt steht in der Liste.</summary>
    public int Position { get; set; }
}

/// <summary>Eine Trainingsgruppe („Anfänger Freitag").</summary>
public class ClubGroup
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Wann und wo, als freier Text („Fr 17:00–18:30, Vereinsheim").</summary>
    [MaxLength(200)]
    public string? Schedule { get; set; }

    /// <summary>
    /// Trainingstag, 1 = Montag … 7 = Sonntag (ISO); <c>null</c> = kein fester Tag. Damit schlägt die Oberfläche am
    /// Trainingstag von selbst die Anwesenheitsliste vor („am Freitag abhaken, wer da ist").
    /// </summary>
    public int? Weekday { get; set; }

    public bool Archived { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<ClubGroupMember> Members { get; set; } = new();
    public List<ClubGroupTrainer> Trainers { get; set; } = new();
    public List<ClubSession> Sessions { get; set; } = new();
}

public class ClubGroupMember
{
    public int GroupId { get; set; }
    public ClubGroup? Group { get; set; }
    public int MemberId { get; set; }
    public ClubMember? Member { get; set; }
}

/// <summary>Ein Konto als Trainer einer Gruppe — es sieht deren Kinder (mit <see cref="Permissions.ClubTrainer"/>).</summary>
public class ClubGroupTrainer
{
    public int GroupId { get; set; }
    public ClubGroup? Group { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
}

/// <summary>Eine Trainingseinheit einer Gruppe — höchstens eine je Tag.</summary>
public class ClubSession
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public ClubGroup? Group { get; set; }
    public DateOnly Date { get; set; }

    [MaxLength(200)]
    public string? Topic { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<ClubAttendance> Attendance { get; set; } = new();
    public List<ClubSessionPhoto> Photos { get; set; } = new();
}

/// <summary>
/// Ein Foto zu einer Trainingseinheit (Wunsch 2026-09-30: „zu Trainings mehrere Fotos hochladen"). Liegt wie die
/// Formular-Fotos in der Datenbank — aufrecht gedreht und verkleinert (<see cref="ClubService.PhotoMaxEdge"/>), dazu ein
/// kleines Vorschaubild für die Übersichten. Fotos von Kindern: nur angemeldet und nur mit Zugriff auf die Gruppe.
/// </summary>
public class ClubSessionPhoto
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public ClubSession? Session { get; set; }

    /// <summary>JPEG, längste Seite höchstens <see cref="ClubService.PhotoMaxEdge"/>.</summary>
    public byte[] Image { get; set; } = [];
    /// <summary>JPEG, längste Seite höchstens <see cref="ClubService.ThumbMaxEdge"/>.</summary>
    public byte[] Thumb { get; set; } = [];
    public int Width { get; set; }
    public int Height { get; set; }

    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Da oder nicht da. Den Wert 2 („entschuldigt") gab es nur in 0.613.0 — er wird nicht mehr vergeben
/// (Wunsch 2026-09-30: „interessiert mich nicht"); die Zahlen bleiben, wie sie in der Datenbank stehen.</summary>
public enum ClubAttendanceStatus
{
    Present = 1,
    Absent = 3,
}

public class ClubAttendance
{
    public int SessionId { get; set; }
    public ClubSession? Session { get; set; }
    public int MemberId { get; set; }
    public ClubMember? Member { get; set; }
    public ClubAttendanceStatus Status { get; set; }
}

/// <summary>Eine datierte Trainer-Notiz zum Lernstand eines Kindes.</summary>
public class ClubNote
{
    public int Id { get; set; }
    public int MemberId { get; set; }
    public ClubMember? Member { get; set; }
    /// <summary>Kein FK — die Notiz bleibt, wenn das Konto des Trainers geht.</summary>
    public int? AuthorUserId { get; set; }
    public DateTime CreatedAt { get; set; }

    [Required, MaxLength(2000)]
    public string Text { get; set; } = string.Empty;
}
