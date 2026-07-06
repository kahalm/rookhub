using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

/// <summary>Wochenpost in der Liste (ohne PGN-Inhalt).</summary>
public class WeeklyPostDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime ScheduledAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>Gesetzt, wenn der Wochenpost aus einem Buch-Kapitel stammt (statt hochgeladenem PGN) — Buch-Id.</summary>
    public int? SourceBookId { get; set; }
    /// <summary>Kapitelname der Buch-Quelle (null = „ohne Kapitel"); nur wenn <see cref="SourceBookId"/> gesetzt ist.</summary>
    public string? SourceChapter { get; set; }
}

/// <summary>Wochenpost-Detail inkl. PGN-Inhalt (für den Viewer).</summary>
public class WeeklyPostDetailDto : WeeklyPostDto
{
    public string PgnContent { get; set; } = string.Empty;
}

/// <summary>Editierbare Felder eines Wochenposts (Termin + Titel).</summary>
public class UpdateWeeklyPostDto
{
    [MaxLength(300)]
    public string? Title { get; set; }
    [MaxLength(500)]
    public string? Description { get; set; }
    public DateTime? ScheduledAt { get; set; }
}

/// <summary>Anlegen eines Wochenposts aus EINEM Kapitel eines Buchs (Kurs) statt aus hochgeladenem PGN.
/// <see cref="ChapterIndex"/> ist die 0-basierte Position aus <c>GET /api/courses/{bookId}/chapters</c>.</summary>
public class CreateWeeklyFromChapterDto
{
    public int BookId { get; set; }
    /// <summary>0-basierter Kapitel-Index (Lesereihenfolge, wie von der Kapitel-Liste geliefert).</summary>
    public int ChapterIndex { get; set; }
    public DateTime ScheduledAt { get; set; }
    [MaxLength(300)]
    public string? Title { get; set; }
    [MaxLength(500)]
    public string? Description { get; set; }
}

/// <summary>Wochenpost als Puzzle-Sequenz zum Durchspielen (PGN on-the-fly geparst).</summary>
public class WeeklyPlayDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<BookPuzzleDto> Puzzles { get; set; } = new();
}

/// <summary>Aufzeichnung eines gespielten Wochenpost-Puzzles (gelöst oder nicht).</summary>
public class RecordWeeklyAttemptDto
{
    [Range(0, 100000)] public int PuzzleIndex { get; set; }
    public bool Solved { get; set; }
    [Range(0, 86400)] public int TimeSeconds { get; set; }
    [Range(0, 3)] public int HintsUsed { get; set; }
    /// <summary>Anzahl Fehlzüge in diesem Puzzle (Abweichungen vom Lösungszug).</summary>
    [Range(0, 10000)] public int WrongAttempts { get; set; }
    /// <summary>Anzahl genutzter Mausrutscher in diesem Puzzle (0/1).</summary>
    [Range(0, 1000)] public int Mouseslips { get; set; }
}

/// <summary>Per-User-Fortschritt eines Wochenposts. „Erledigt" = alle Puzzles gespielt (Solved egal).</summary>
public class WeeklyPostProgressDto
{
    public int WeeklyPostId { get; set; }
    /// <summary>Anzahl Puzzles im Wochenpost.</summary>
    public int Total { get; set; }
    /// <summary>Anzahl gespielter (= aufgezeichneter) Puzzles.</summary>
    public int PlayedCount { get; set; }
    /// <summary>Davon gelöste Puzzles.</summary>
    public int SolvedCount { get; set; }
    /// <summary>True, wenn alle Puzzles gespielt wurden.</summary>
    public bool Completed { get; set; }
    /// <summary>Gesamtzeit des Users über alle gespielten Puzzles dieses Wochenposts in Sekunden.</summary>
    public int TotalSeconds { get; set; }
    /// <summary>Indizes der bereits gespielten Puzzles (für „zum ersten neuen Puzzle springen"); leer in der Übersicht.</summary>
    public List<int> PlayedIndices { get; set; } = new();
}

/// <summary>Aggregierte Wochenpost-Ergebnisse (für die Discord-Anzeige): wer wie weit ist.</summary>
public class WeeklyPostResultsDto
{
    public int WeeklyPostId { get; set; }
    /// <summary>Anzahl Puzzles im Wochenpost.</summary>
    public int Total { get; set; }
    /// <summary>Anzahl User, die alle Puzzles gespielt haben (= erledigt).</summary>
    public int CompletedCount { get; set; }
    public List<WeeklyPlayerResultDto> Players { get; set; } = new();
}

/// <summary>Stand eines Users bei einem Wochenpost (für die Discord-Anzeige).</summary>
public class WeeklyPlayerResultDto
{
    /// <summary>Interne User-Id — für die Admin-Detail-Aufschlüsselung (i). Für Nicht-Admin-Zwecke irrelevant.</summary>
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DiscordId { get; set; }
    public string? DiscordUsername { get; set; }
    public int PlayedCount { get; set; }
    public int SolvedCount { get; set; }
    /// <summary>Gesamtzeit über alle gespielten Puzzles in Sekunden.</summary>
    public int TotalSeconds { get; set; }
    /// <summary>Höchste in irgendeinem Puzzle dieses Posts genutzte Tipp-Stufe (0–3). &gt; 0 ⇒ mit Tipps gelöst (💡).</summary>
    public int HintsUsed { get; set; }
    /// <summary>True, wenn alle Puzzles gespielt wurden.</summary>
    public bool Completed { get; set; }
}

/// <summary>Admin-Detailaufschlüsselung eines Spielers bei einem Wochenpost: eine Zeile je gespieltem Puzzle.</summary>
public class WeeklyPlayerBreakdownDto
{
    public int WeeklyPostId { get; set; }
    public int UserId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    /// <summary>Anzahl Puzzles im Wochenpost.</summary>
    public int Total { get; set; }
    public List<WeeklyPuzzleBreakdownRowDto> Rows { get; set; } = new();
}

/// <summary>Eine Zeile der Admin-Aufschlüsselung: das Ergebnis des Spielers an einem einzelnen Puzzle.</summary>
public class WeeklyPuzzleBreakdownRowDto
{
    /// <summary>0-basierter Index des Puzzles in der Wochenpost-Sequenz.</summary>
    public int PuzzleIndex { get; set; }
    /// <summary>Titel/Bezeichnung des Puzzles (aus dem PGN), falls vorhanden.</summary>
    public string? Title { get; set; }
    public bool Solved { get; set; }
    public int TimeSeconds { get; set; }
    /// <summary>Höchste genutzte Tipp-Stufe (0–3).</summary>
    public int HintsUsed { get; set; }
    /// <summary>Anzahl Fehlzüge in diesem Puzzle.</summary>
    public int WrongAttempts { get; set; }
    /// <summary>Anzahl genutzter Mausrutscher in diesem Puzzle.</summary>
    public int Mouseslips { get; set; }
    public DateTime AttemptedAt { get; set; }
}
