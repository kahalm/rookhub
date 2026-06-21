namespace RookHub.Api.DTOs;

/// <summary>Ein Buch als Kurs in der Übersicht inkl. nutzerbezogenem Fortschritt.</summary>
public class CourseListItemDto
{
    public int BookId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Difficulty { get; set; }
    public int? Rating { get; set; }
    public string? Tags { get; set; }
    public string? Description { get; set; }
    public int PuzzleCount { get; set; }
    public int SolvedCount { get; set; }
    /// <summary>0–100, gerundet. 0 bei leerem Buch.</summary>
    public int ProgressPercent { get; set; }
    public string? LastMode { get; set; }

    /// <summary>
    /// Zeitpunkt der letzten Verwendung dieses Kurses durch den User (= <see cref="Models.CourseProgress.UpdatedAt"/>,
    /// upserted bei jedem Versuch/Reset). <c>null</c> = noch nie angefangen. Die Übersicht sortiert angefangene
    /// Kurse nach diesem Wert absteigend nach vorn.
    /// </summary>
    public DateTime? LastActivityAt { get; set; }

    /// <summary>
    /// <c>true</c> = persönlicher Kurs des Users (z. B. selbst importierter Chessable-Kurs,
    /// <c>Book.OwnerUserId == userId</c>). <c>false</c> = öffentlicher Kurs, über eine Gruppe
    /// freigegeben (bzw. globales Admin-Buch). Steuert die Aufteilung in der Übersicht.
    /// </summary>
    public bool IsOwned { get; set; }
}

/// <summary>
/// Ein Kapitel eines Buchs (= eindeutiger <see cref="Models.BookPuzzle.Chapter"/>-Wert, in
/// Lesereihenfolge) inkl. nutzerbezogenem Fortschritt. <see cref="Index"/> ist die 0-basierte
/// Position in der Lesereihenfolge und dient als stabiler Selektor für die Kapitel-Navigation.
/// </summary>
public class CourseChapterDto
{
    public int Index { get; set; }
    /// <summary>Kapitelname; <c>null</c> für Puzzles ohne Kapitelangabe (Sammel-„ohne Kapitel").</summary>
    public string? Name { get; set; }
    public int PuzzleCount { get; set; }
    public int SolvedCount { get; set; }
    /// <summary>0–100, gerundet.</summary>
    public int ProgressPercent { get; set; }
}

/// <summary>Nächstes zu lösendes Puzzle eines Kurses + aktueller Fortschritt.</summary>
public class CourseNextPuzzleDto
{
    /// <summary>Null, wenn der Kurs abgeschlossen ist (<see cref="Completed"/> = true).</summary>
    public BookPuzzleDto? Puzzle { get; set; }
    public int SolvedCount { get; set; }
    public int Total { get; set; }
    public bool Completed { get; set; }
}

/// <summary>Aufzeichnung eines Lösungsversuchs im Kurs.</summary>
public class RecordCourseResultDto
{
    public int BookPuzzleId { get; set; }
    public bool Solved { get; set; }
    /// <summary>Optional: "sequential" oder "random" — aktualisiert den zuletzt genutzten Modus.</summary>
    public string? Mode { get; set; }
    /// <summary>Optional: am Puzzle verbrachte Zeit in Sekunden (nur fürs Logging der Startzeit).</summary>
    public int TimeSeconds { get; set; }
    /// <summary>Optional: 0-basierter Kapitel-Index. Gesetzt → der zurückgegebene Fortschritt
    /// wird auf dieses Kapitel beschränkt (Kapitel-Modus); sonst buchweit.</summary>
    public int? ChapterIndex { get; set; }
}

/// <summary>Fortschritt eines Kurses (Buch) für den aktuellen User.</summary>
public class CourseProgressDto
{
    public int BookId { get; set; }
    public int SolvedCount { get; set; }
    public int Total { get; set; }
    public int ProgressPercent { get; set; }
    public bool Completed { get; set; }
    public string? LastMode { get; set; }
}

/// <summary>
/// Aggregierte Kurs-Puzzle-Statistik des Users (Pendant zu <see cref="PuzzleStatsDto"/> für Standard-Puzzles,
/// aber ohne Elo — Buch-/Kurs-Puzzles haben kein User-Elo). Quelle: <see cref="Models.CourseAttempt"/>.
/// </summary>
public class CourseStatsDto
{
    public int TotalAttempts { get; set; }
    public int Solved { get; set; }
    public double Accuracy { get; set; }
    public int CurrentStreak { get; set; }
    public int BestStreak { get; set; }
}

/// <summary>
/// Ein einzelner Kurs-Lösungsversuch für die History-Tabelle (Pendant zu <see cref="PuzzleAttemptDto"/>,
/// ohne Elo). <see cref="BookPuzzleId"/> dient dem „Öffnen"-Link (<c>/puzzles/book/:id</c>).
/// </summary>
public class CourseAttemptDto
{
    public int BookPuzzleId { get; set; }
    public string LineId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string BookFileName { get; set; } = string.Empty;
    public int? BookRating { get; set; }
    public string? Difficulty { get; set; }
    public bool Solved { get; set; }
    public int TimeSeconds { get; set; }
    public DateTime AttemptedAt { get; set; }
}
