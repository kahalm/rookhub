using System.ComponentModel.DataAnnotations;
using RookHub.Api.Models;

namespace RookHub.Api.DTOs;

/// <summary>
/// Effektives Trainingsziel eines Users — persönlicher Override, sonst Gruppen-Vorlage, sonst keins.
/// <see cref="DailyMinutes"/> = ein gemeinsames Tageszeit-Ziel (alle Quellen füttern denselben Topf),
/// <see cref="PlayGames"/> = Anzahl Rapid-/Classical-Partien pro ISO-Woche (jeweils 0 = nicht Teil des Ziels).
/// </summary>
public class TrainingGoalDto
{
    /// <summary>Tagesziel Trainingszeit in Minuten (gemeinsamer Topf aller Quellen).</summary>
    public int DailyMinutes { get; set; }
    /// <summary>Wochenziel: Anzahl Rapid-/Classical-Partien pro ISO-Woche.</summary>
    public int PlayGames { get; set; }
    public int WeeklyDaysTarget { get; set; }

    /// <summary>"personal" = eigener Override, "group" = aus Gruppen-Vorlage geerbt, "none" = noch keins gesetzt.</summary>
    public string Source { get; set; } = "none";
    /// <summary>Name der Gruppe, aus der die Vorlage stammt (nur bei <see cref="Source"/> = "group").</summary>
    public string? GroupName { get; set; }
}

/// <summary>Von der RepCheck-Extension gemeldetes Häppchen aktiver Chessable-Trainingszeit
/// (<c>POST /api/extension/training-activity</c>). Der Zeitstempel wird serverseitig gesetzt.</summary>
public class ChessableActivityInputDto
{
    /// <summary>Aktiv trainierte Zeit dieses Häppchens in Sekunden.</summary>
    [Range(1, 3600)] public int SecondsActive { get; set; }
    /// <summary>Anzahl in diesem Häppchen abgeschlossener (gewerteter) Züge — informativ.</summary>
    [Range(0, 10000)] public int MovesTrained { get; set; }
    /// <summary>Art des Chessable-Kurses (Opening/Middlegame/Endgame), ermittelt aus Repertoire-Zuordnung. Null = unbekannt.</summary>
    public RepertoireKind? CourseKind { get; set; }
    /// <summary>Chessable-Kurs-ID (numerisch als String), von der Extension aus der Seite aufgelöst. Null = unbekannt.</summary>
    [MaxLength(32)] public string? CourseId { get; set; }
    /// <summary>Lesbarer Kursname (nur Anzeige). Null = unbekannt.</summary>
    [MaxLength(200)] public string? CourseName { get; set; }
}

/// <summary>Ein in der Chessable-History gruppierter Kurs: aggregierte Trainingszeit + ermitteltes Thema
/// (manuelle Zuordnung &gt; Repertoire-Auto-Zuordnung &gt; unzugeordnet).</summary>
public class ChessableCourseSummaryDto
{
    /// <summary>Chessable-Kurs-ID (numerisch als String).</summary>
    public string CourseId { get; set; } = string.Empty;
    /// <summary>Lesbarer Kursname (jüngster bekannter), falls vorhanden.</summary>
    public string? CourseName { get; set; }
    /// <summary>Summe der aktiven Trainingssekunden über alle Häppchen dieses Kurses.</summary>
    public int TotalSeconds { get; set; }
    /// <summary>Summe der gewerteten Züge über alle Häppchen — informativ.</summary>
    public int TotalMoves { get; set; }
    /// <summary>Anzahl gemeldeter Häppchen.</summary>
    public int ActivityCount { get; set; }
    /// <summary>Zeitpunkt der jüngsten Aktivität (UTC, ISO-8601).</summary>
    public DateTime LastActivityAt { get; set; }
    /// <summary>Manuell zugeordnetes Thema ("opening"/"middlegame"/"endgame"/"tactics") oder null.</summary>
    public string? AssignedTheme { get; set; }
    /// <summary>Automatisch aus einem Repertoire abgeleitetes Thema (RepertoireKind als String) oder null.</summary>
    public string? AutoTheme { get; set; }
    /// <summary>true, wenn ein Thema feststeht (manuell ODER automatisch) — sonst „nicht zugeordnet".</summary>
    public bool IsAssigned { get; set; }
}

/// <summary>Eingabe zum manuellen Zuordnen eines Chessable-Kurses zu einem Thema.</summary>
public class ChessableCourseThemeInputDto
{
    /// <summary>Thema: Opening/Middlegame/Endgame/Tactics.</summary>
    [Required] public ChessableTheme Theme { get; set; }
}

/// <summary>Eingabe zum Setzen eines Ziels (persönlich oder als Gruppen-Vorlage).
/// <see cref="DailyMinutes"/> = Trainingsminuten/Tag, <see cref="PlayGames"/> = Partien/Woche.</summary>
public class TrainingGoalInputDto
{
    [Range(0, 600)] public int DailyMinutes { get; set; }
    [Range(0, 200)] public int PlayGames { get; set; }
    [Range(0, 7)] public int WeeklyDaysTarget { get; set; }
}

/// <summary>Eine manuell eingetragene Offline-Aktivität (selbst gemeldet, korrigierbar).</summary>
public class ManualActivityDto
{
    public int Id { get; set; }
    /// <summary>UTC-Datum als yyyy-MM-dd.</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>"OtbGame" | "OfflinePuzzle" | "OfflineStudy" | "Coaching".</summary>
    public ManualActivityKind Kind { get; set; }
    /// <summary>Bei OtbGame = Anzahl Partien; sonst = Minuten.</summary>
    public int Amount { get; set; }
    public string? Note { get; set; }
}

/// <summary>Eingabe zum Anlegen/Ändern einer manuellen Offline-Aktivität.
/// Amount = Partienzahl (OtbGame) bzw. Minuten (sonst); serverseitig je Art geklemmt.</summary>
public class ManualActivityInputDto
{
    /// <summary>UTC-Datum als yyyy-MM-dd (Standard: heute; nicht in der Zukunft).</summary>
    [Required] public string Date { get; set; } = string.Empty;
    public ManualActivityKind Kind { get; set; }
    [Range(1, 600)] public int Amount { get; set; }
    [MaxLength(200)] public string? Note { get; set; }
}

/// <summary>Aufschlüsselung von Trainingssekunden nach <b>Quelle</b> (woher die Zeit stammt).</summary>
public class SourceBreakdownDto
{
    /// <summary>Standard-/Endlos-/Tages-/Einzel-Puzzle + manuelles Offline-Puzzle.</summary>
    public int RandomPuzzleSeconds { get; set; }
    /// <summary>Kurse/Bücher (alle Kurs-Versuche) + manuelles Offline-Studium/Coaching.</summary>
    public int CourseBookSeconds { get; set; }
    /// <summary>Chessable-Training (von der RepCheck-Extension gemeldet).</summary>
    public int ChessableSeconds { get; set; }
}

/// <summary>Aufschlüsselung von Trainingssekunden nach <b>Thema</b> (Phase/Taktik, best-effort;
/// nicht klassifizierbare Zeit landet in <see cref="OtherSeconds"/>).</summary>
public class ThemeBreakdownDto
{
    public int OpeningSeconds { get; set; }
    public int MiddlegameSeconds { get; set; }
    public int EndgameSeconds { get; set; }
    public int TacticsSeconds { get; set; }
    /// <summary>Sonstiges/unzugeordnet (kein verlässliches Thema-Signal).</summary>
    public int OtherSeconds { get; set; }
}

/// <summary>Ein Tag im Ziele-Tracker: gesamte Trainingssekunden + Aufschlüsselung nach Quelle/Thema
/// + gespielte Partien an dem Tag (informativ — Spielen ist ein Wochenziel) + Tagesstatus.</summary>
public class TrackerDayDto
{
    /// <summary>UTC-Datum als yyyy-MM-dd.</summary>
    public string Date { get; set; } = string.Empty;
    /// <summary>Gesamte (gemeinsam getopfte) Trainingssekunden des Tages.</summary>
    public int TotalSeconds { get; set; }
    public SourceBreakdownDto BySource { get; set; } = new();
    public ThemeBreakdownDto ByTheme { get; set; } = new();
    /// <summary>Rapid-/Classical-Partien an diesem Tag (informativ; Tagesstatus nutzt nur die Trainingszeit).</summary>
    public int PlayGames { get; set; }
    /// <summary>"none" | "partial" | "full" gegenüber dem effektiven Tageszeit-Ziel.</summary>
    public string Status { get; set; } = "none";
    /// <summary>Enthält dieser Tag mindestens eine manuell (selbst) eingetragene Offline-Aktivität?</summary>
    public bool HasManual { get; set; }
}

/// <summary>Effektives Ziel + Tagesreihe (nur Tage mit Aktivität) + Perioden-Aufschlüsselung
/// (Summen über das gesamte Tracker-Fenster, nach Quelle und Thema).</summary>
public class TrackerResponseDto
{
    public TrainingGoalDto Goal { get; set; } = new();
    public List<TrackerDayDto> Days { get; set; } = new();
    /// <summary>Summe der Trainingssekunden über das Fenster, aufgeschlüsselt nach Quelle.</summary>
    public SourceBreakdownDto BreakdownBySource { get; set; } = new();
    /// <summary>Summe der Trainingssekunden über das Fenster, aufgeschlüsselt nach Thema.</summary>
    public ThemeBreakdownDto BreakdownByTheme { get; set; } = new();
}

/// <summary>Fortschritt der zeitbasierten Tages-Trainingszeit (gemeinsamer Topf).</summary>
public class CategoryProgressDto
{
    public int TargetMinutes { get; set; }
    public int DoneSeconds { get; set; }
    public bool Met { get; set; }
}

/// <summary>Fortschritt des wöchentlichen Spielen-Ziels (Anzahl Rapid-/Classical-Partien in der laufenden ISO-Woche).</summary>
public class PlayProgressDto
{
    public int TargetGames { get; set; }
    public int DoneGames { get; set; }
    public bool Met { get; set; }
}

/// <summary>Heutiger Fortschritt: gemeinsames Tageszeit-Ziel + Aufschlüsselung von heute (Quelle/Thema)
/// + Wochenstand (Spielen-Partien + voll erfüllte Tage).</summary>
public class TodayProgressDto
{
    public TrainingGoalDto Goal { get; set; } = new();
    /// <summary>Tageszeit-Ziel: heute trainierte Zeit (alle Quellen) vs. Zielminuten.</summary>
    public CategoryProgressDto Daily { get; set; } = new();
    /// <summary>Heutige Trainingssekunden aufgeschlüsselt nach Quelle.</summary>
    public SourceBreakdownDto BySource { get; set; } = new();
    /// <summary>Heutige Trainingssekunden aufgeschlüsselt nach Thema.</summary>
    public ThemeBreakdownDto ByTheme { get; set; } = new();
    /// <summary>Wöchentliches Spielen-Ziel: Partien in der laufenden ISO-Woche (Mo–So) vs. Zielanzahl.</summary>
    public PlayProgressDto Play { get; set; } = new();
    /// <summary>"none" | "partial" | "full" für heute (nur das Tageszeit-Ziel).</summary>
    public string Status { get; set; } = "none";
    /// <summary>Voll erfüllte Tage in der laufenden ISO-Woche (Mo–So, inkl. heute).</summary>
    public int WeekDaysMet { get; set; }
    public int WeeklyDaysTarget { get; set; }
}
