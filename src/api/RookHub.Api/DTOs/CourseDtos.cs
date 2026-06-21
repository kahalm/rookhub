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

/// <summary>
/// Statistik eines Kurs-Bereichs (ganzes Buch ODER ein Kapitel) für den aktuellen User:
/// Fortschritt (gelöst/gesamt), akkumulierte Zeit und Erst-Versuch-Trefferquote.
/// Zeit + Trefferquote zählen nur Versuche seit dem letzten Reset (<see cref="Models.CourseProgress.ResetAt"/>).
/// </summary>
public class CourseScopeStatsDto
{
    public int SolvedCount { get; set; }
    public int Total { get; set; }
    /// <summary>0–100, gerundet (Anteil gelöster Puzzles).</summary>
    public int ProgressPercent { get; set; }
    /// <summary>Akkumulierte Zeit über alle Versuche (seit letztem Reset), in Sekunden.</summary>
    public int TotalSeconds { get; set; }
    /// <summary>Anzahl Puzzles, an denen seit dem Reset mindestens ein Versuch gemacht wurde.</summary>
    public int AttemptedCount { get; set; }
    /// <summary>Davon beim ERSTEN Versuch (nach Reset) korrekt gelöst.</summary>
    public int FirstTryCorrect { get; set; }
    /// <summary>0–100, gerundet: <see cref="FirstTryCorrect"/> / <see cref="AttemptedCount"/>.</summary>
    public int AccuracyPercent { get; set; }
}

/// <summary>Nächstes zu lösendes Puzzle eines Kurses + aktueller Fortschritt.</summary>
public class CourseNextPuzzleDto
{
    /// <summary>Null, wenn der Kurs abgeschlossen ist (<see cref="Completed"/> = true).</summary>
    public BookPuzzleDto? Puzzle { get; set; }
    public int SolvedCount { get; set; }
    public int Total { get; set; }
    public bool Completed { get; set; }

    /// <summary>Statistik für das ganze Buch (immer gesetzt).</summary>
    public CourseScopeStatsDto? Book { get; set; }
    /// <summary>Statistik für das Kapitel des aktuellen Puzzles; <c>null</c>, wenn das Buch nur
    /// ein Kapitel hat (dann = Buch) oder kein aktuelles Puzzle existiert.</summary>
    public CourseScopeStatsDto? Chapter { get; set; }
    /// <summary>Name des aktuellen Kapitels (zu <see cref="Chapter"/>); <c>null</c> = „ohne Kapitel".</summary>
    public string? ChapterName { get; set; }
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
    /// <summary>Höchste in diesem Versuch angesehene Tipp-Stufe (0–3).</summary>
    public int HintsUsed { get; set; }
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

    /// <summary>Statistik für das ganze Buch (immer gesetzt).</summary>
    public CourseScopeStatsDto? Book { get; set; }
    /// <summary>Statistik für das Kapitel des zuletzt bearbeiteten Puzzles; <c>null</c>, wenn das Buch
    /// nur ein Kapitel hat oder der Kontext kein Kapitel kennt (z. B. nach Reset).</summary>
    public CourseScopeStatsDto? Chapter { get; set; }
    /// <summary>Name des aktuellen Kapitels (zu <see cref="Chapter"/>); <c>null</c> = „ohne Kapitel".</summary>
    public string? ChapterName { get; set; }
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
