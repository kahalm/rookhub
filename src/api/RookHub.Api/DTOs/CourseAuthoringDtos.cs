using RookHub.Api.Models;

namespace RookHub.Api.DTOs;

/// <summary>Vollbild der Kurs-Detailseite: Metadaten, eigener Fortschritt, Kapitel-Verwaltungssicht.</summary>
public class CourseDetailDto
{
    public int BookId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Difficulty { get; set; }
    public int? Rating { get; set; }
    public int? MinElo { get; set; }
    public int? MaxElo { get; set; }
    public string? Tags { get; set; }
    public List<string> Themes { get; set; } = new();
    public BookKind Kind { get; set; }
    /// <summary>Kalkulationsbuch (Stellungen ohne Lösung) — Fortschritt = bearbeitete Stellungen.</summary>
    public bool IsCalculation { get; set; }
    public bool IsPublic { get; set; }
    public string? PublicSlug { get; set; }

    /// <summary>Eigener Kurs (Besitzer) bzw. von jemandem geteilt — wie in der Übersicht.</summary>
    public bool IsOwned { get; set; }
    public bool IsShared { get; set; }
    public string? SharedByUsername { get; set; }
    public bool IsPinned { get; set; }
    /// <summary>Darf der Aufrufer Inhalte bearbeiten (Kapitel/Linien)? Besitzer oder Admin.</summary>
    public bool CanManage { get; set; }

    /// <summary>Zählbasis wie in der Kursübersicht: Kalkulationsbuch = ALLE Stellungen, sonst Quiz-Linien.</summary>
    public int PuzzleCount { get; set; }
    public int SolvedCount { get; set; }
    public int ProgressPercent { get; set; }
    /// <summary>Alle Linien inkl. Info-/Stellungs-Linien.</summary>
    public int TotalLines { get; set; }
    /// <summary>Davon Info-/Stellungs-Linien (ohne abgefragte Lösung).</summary>
    public int InfoLineCount { get; set; }

    public string? LastMode { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public int? LinkedBookId { get; set; }
    public string? LinkedDisplayName { get; set; }

    public List<CourseManageChapterDto> Chapters { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Kapitel in der VERWALTUNGSSICHT der Detailseite. Anders als <see cref="CourseChapterDto"/>
/// (Solver-Kapitelliste, nur Quiz-Linien) sind hier ALLE Kapitel enthalten — auch solche, die
/// ausschließlich aus Stellungs-/Info-Linien bestehen (genau die entstehen im Kalkulations-Modus).
/// Adressiert wird über den <see cref="Name"/>, nicht über einen Index: der Index verschiebt sich,
/// sobald Kapitel hinzukommen.
/// </summary>
public class CourseManageChapterDto
{
    /// <summary><c>null</c> = Sammelgruppe „ohne Kapitel".</summary>
    public string? Name { get; set; }
    /// <summary>Alle Linien des Kapitels.</summary>
    public int LineCount { get; set; }
    /// <summary>Davon abgefragte Quiz-Linien (mit Lösung).</summary>
    public int QuizCount { get; set; }
    public int SolvedCount { get; set; }
    public int ProgressPercent { get; set; }
    /// <summary>Index in der Solver-Kapitelliste (<c>?chapterIndex=</c>); <c>null</c>, wenn das Kapitel
    /// keine Quiz-Linien hat und daher im Solver nicht startbar ist.</summary>
    public int? SolverIndex { get; set; }
    /// <summary>Id der ERSTEN Linie des Kapitels — Einstiegspunkt für den Kalkulations-Modus.</summary>
    public int? FirstLineId { get; set; }
}

/// <summary>Eingabe: Stellungen als Text einfügen (eine je Zeile, optional nummeriert + Kommentar).</summary>
public class AddCourseLinesDto
{
    /// <summary>Zielkapitel; <c>null</c>/leer = „ohne Kapitel". Existiert es nicht, entsteht es dadurch.</summary>
    public string? Chapter { get; set; }
    public string Text { get; set; } = string.Empty;
}

/// <summary>Eine Zeile, die nicht übernommen wurde (Grund-Schlüssel: invalid_fen/duplicate/too_many).</summary>
public class CourseLineIssueDto
{
    public int LineNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Ergebnis des Einfügens.</summary>
public class AddCourseLinesResultDto
{
    public int Added { get; set; }
    public string? Chapter { get; set; }
    public List<CourseLineIssueDto> Issues { get; set; } = new();
    /// <summary>Linien des Buchs nach dem Einfügen (für die Anzeige ohne Nachladen).</summary>
    public int TotalLines { get; set; }
}

/// <summary>Eingabe: Kapitel umbenennen (<c>Chapter</c> = bisheriger Name, null = „ohne Kapitel").</summary>
public class RenameCourseChapterDto
{
    public string? Chapter { get; set; }
    public string? NewName { get; set; }
}

/// <summary>Eingabe: ein Kapitel adressieren (null/leer = „ohne Kapitel").</summary>
public class CourseChapterRefDto
{
    public string? Chapter { get; set; }
}

/// <summary>Eingabe: Kalkulations-Modus des Kurses ein-/ausschalten (<c>Book.IsCalculation</c>).</summary>
public class SetCourseCalculationDto
{
    public bool IsCalculation { get; set; }
}

/// <summary>Eine einzelne Linie in der Verwaltungssicht eines Kapitels.</summary>
public class CourseLineDto
{
    public int Id { get; set; }
    public string LineId { get; set; } = string.Empty;
    public string Round { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Chapter { get; set; }
    public string Fen { get; set; } = string.Empty;
    public string? Comment { get; set; }
    /// <summary>Stellungs-/Info-Linie (keine abgefragte Lösung).</summary>
    public bool IsInfoOnly { get; set; }
    /// <summary>Anzahl Halbzüge der gespeicherten Linie (0 = reine Stellung). Die Züge selbst werden
    /// hier NICHT ausgeliefert — die Detailseite soll keine Lösung verraten.</summary>
    public int MoveCount { get; set; }
}
