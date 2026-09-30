using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

// ClubHub — Kartei der Kinder und Jugendlichen (Regeln: Services/Club/ClubService.cs).

public class ClubContactDto
{
    /// <summary><c>phone</c> oder <c>email</c>.</summary>
    [Required, MaxLength(8)]
    public string Kind { get; set; } = "phone";
    [Required, MaxLength(200)]
    public string Value { get; set; } = string.Empty;
    /// <summary>Wessen Kontakt das ist („Mutter Daniela").</summary>
    [MaxLength(80)]
    public string? Label { get; set; }
}

public class ClubGroupRefDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Ein Kind in der Liste — mit Kontakten, damit die Liste zugleich die Telefonliste ist.</summary>
public class ClubMemberListDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public int? BirthYear { get; set; }
    public string? Level { get; set; }
    public bool Archived { get; set; }
    public bool Linked { get; set; }
    public List<ClubGroupRefDto> Groups { get; set; } = new();
    public List<ClubContactDto> Contacts { get; set; } = new();
}

public class ClubNoteDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    /// <summary>Benutzername des Trainers; <c>null</c>, wenn es das Konto nicht mehr gibt.</summary>
    public string? Author { get; set; }
    /// <summary>Darf der Aufrufer sie löschen (eigene Notiz oder Leitung)?</summary>
    public bool CanDelete { get; set; }
}

public class ClubAttendanceEntryDto
{
    public int SessionId { get; set; }
    public int GroupId { get; set; }
    public string Group { get; set; } = string.Empty;
    /// <summary>yyyy-MM-dd.</summary>
    public string Date { get; set; } = string.Empty;
    public string? Topic { get; set; }
    /// <summary><c>present</c>, <c>excused</c> oder <c>absent</c>.</summary>
    public string Status { get; set; } = string.Empty;
}

public class ClubAttendanceSummaryDto
{
    public int Present { get; set; }
    public int Excused { get; set; }
    public int Absent { get; set; }
    /// <summary>Die jüngsten Einheiten, neueste zuerst.</summary>
    public List<ClubAttendanceEntryDto> Recent { get; set; } = new();
}

public class ClubMemberDto : ClubMemberListDto
{
    /// <summary>yyyy-MM-dd, wenn das ganze Datum bekannt ist.</summary>
    public string? BirthDate { get; set; }
    public string? FideId { get; set; }
    public string? NationalId { get; set; }
    public string? Notes { get; set; }
    public bool? PhotoConsent { get; set; }
    public string? LinkedUsername { get; set; }
    /// <summary>Offener Einmal-Code zum Verknüpfen (nicht abgelaufen), sonst <c>null</c>.</summary>
    public string? LinkCode { get; set; }
    public DateTime? LinkCodeExpires { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ClubNoteDto> NoteEntries { get; set; } = new();
    public ClubAttendanceSummaryDto Attendance { get; set; } = new();
    /// <summary>Löschen darf nur die Leitung.</summary>
    public bool CanDelete { get; set; }
}

/// <summary>Anlegen und Ändern in einer Form: das ganze Blatt, Kontakte und Gruppen eingeschlossen.</summary>
public class ClubMemberInputDto
{
    [Required, MaxLength(80)]
    public string FirstName { get; set; } = string.Empty;
    [Required, MaxLength(80)]
    public string LastName { get; set; } = string.Empty;
    /// <summary>yyyy-MM-dd; leer = unbekannt.</summary>
    [MaxLength(10)]
    public string? BirthDate { get; set; }
    [Range(1900, 2200)]
    public int? BirthYear { get; set; }
    [MaxLength(60)]
    public string? Level { get; set; }
    [MaxLength(16)]
    public string? FideId { get; set; }
    [MaxLength(16)]
    public string? NationalId { get; set; }
    [MaxLength(4000)]
    public string? Notes { get; set; }
    public bool? PhotoConsent { get; set; }
    public bool Archived { get; set; }
    [MaxLength(20)]
    public List<ClubContactDto> Contacts { get; set; } = new();
    [MaxLength(50)]
    public List<int> GroupIds { get; set; } = new();
}

public class ClubNoteInputDto
{
    [Required, MaxLength(2000)]
    public string Text { get; set; } = string.Empty;
}

public class ClubLinkCodeDto
{
    public string Code { get; set; } = string.Empty;
    public DateTime Expires { get; set; }
}

public class ClubLinkRedeemDto
{
    [Required, MaxLength(32)]
    public string Code { get; set; } = string.Empty;
}

/// <summary>Was das verknüpfte KONTO über seine Verknüpfung sieht — nur, dass es sie gibt.</summary>
public class ClubLinkStateDto
{
    public bool Linked { get; set; }
    public string? FirstName { get; set; }
}

public class ClubTrainerDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
}

public class ClubGroupListDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Schedule { get; set; }
    /// <summary>Trainingstag, 1 = Montag … 7 = Sonntag; <c>null</c> = kein fester Tag.</summary>
    public int? Weekday { get; set; }
    public bool Archived { get; set; }
    public int MemberCount { get; set; }
    public int SessionCount { get; set; }
    /// <summary>yyyy-MM-dd der jüngsten Einheit.</summary>
    public string? LastSession { get; set; }
    public List<ClubTrainerDto> Trainers { get; set; } = new();
}

public class ClubSessionDto
{
    public int Id { get; set; }
    public string Date { get; set; } = string.Empty;
    public string? Topic { get; set; }
    public string? Notes { get; set; }
    public int Present { get; set; }
    public int Excused { get; set; }
    public int Absent { get; set; }
}

public class ClubGroupMemberRowDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public int? BirthYear { get; set; }
    public string? Level { get; set; }
    /// <summary>Status je Einheit in der Reihenfolge von <see cref="ClubGroupDto.Sessions"/>; <c>null</c> = nicht erfasst.</summary>
    public List<string?> Statuses { get; set; } = new();
    public int Present { get; set; }
    /// <summary>Einheiten, in denen das Kind erfasst wurde.</summary>
    public int Recorded { get; set; }
}

public class ClubGroupDto : ClubGroupListDto
{
    /// <summary>Die jüngsten Einheiten, ÄLTESTE zuerst (so liest sich die Anwesenheitstabelle von links nach rechts).</summary>
    public List<ClubSessionDto> Sessions { get; set; } = new();
    public List<ClubGroupMemberRowDto> Members { get; set; } = new();
    /// <summary>Gruppe ändern, Trainer zuteilen, löschen: nur die Leitung.</summary>
    public bool CanManage { get; set; }
}

public class ClubGroupInputDto
{
    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;
    [MaxLength(200)]
    public string? Schedule { get; set; }
    /// <summary>Trainingstag, 1 = Montag … 7 = Sonntag; leer = kein fester Tag.</summary>
    [Range(1, 7)]
    public int? Weekday { get; set; }
    public bool Archived { get; set; }
}

public class ClubTrainerInputDto
{
    [Required, MaxLength(100)]
    public string Username { get; set; } = string.Empty;
}

public class ClubAttendanceInputDto
{
    public int MemberId { get; set; }
    /// <summary><c>present</c>, <c>excused</c>, <c>absent</c> — oder leer = nicht erfasst.</summary>
    [MaxLength(8)]
    public string? Status { get; set; }
}

/// <summary>Eine Einheit samt Anwesenheit speichern — je Gruppe und Tag gibt es EINE, ein zweites Speichern ersetzt sie.</summary>
public class ClubSessionInputDto
{
    /// <summary>yyyy-MM-dd.</summary>
    [Required, MaxLength(10)]
    public string Date { get; set; } = string.Empty;
    [MaxLength(200)]
    public string? Topic { get; set; }
    [MaxLength(2000)]
    public string? Notes { get; set; }
    [MaxLength(500)]
    public List<ClubAttendanceInputDto> Attendance { get; set; } = new();
}

public class ClubSessionDetailDto : ClubSessionDto
{
    public int GroupId { get; set; }
    public List<ClubAttendanceInputDto> Attendance { get; set; } = new();
}

/// <summary>Was RookHub und KidHub über das verknüpfte Konto wissen — nur Summen, keine Einzelversuche.</summary>
public class ClubProgressDto
{
    public string Username { get; set; } = string.Empty;
    public int PuzzleAttempts { get; set; }
    public int PuzzlesSolved { get; set; }
    public double PuzzleAccuracy { get; set; }
    public int PuzzleElo { get; set; }
    public int BestStreak { get; set; }
    /// <summary>Trainingsminuten der letzten 28 Tage (alle Quellen).</summary>
    public int Minutes28 { get; set; }
    /// <summary>Tage mit Training in den letzten 28 Tagen.</summary>
    public int ActiveDays28 { get; set; }
    /// <summary>yyyy-MM-dd des letzten Trainingstags im Fenster.</summary>
    public string? LastActive { get; set; }
    /// <summary>KidHub: Stufen mit mindestens einem Stern.</summary>
    public int KidsLevelsDone { get; set; }
    public int KidsStars { get; set; }
    /// <summary>KidHub: gelöste Kurs-Aufgaben.</summary>
    public int KidsCourseLines { get; set; }
}
