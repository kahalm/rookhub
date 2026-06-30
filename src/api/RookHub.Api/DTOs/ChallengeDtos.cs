using System.ComponentModel.DataAnnotations;
using RookHub.Api.Models;

namespace RookHub.Api.DTOs;

/// <summary>Eine Challenge an einen oder mehrere Freunde anlegen: „schick dieses Puzzle an XY (und Z…)".
/// Quelle bestimmt, ob <see cref="PuzzleId"/> ein Standard- oder ein Buch-Puzzle referenziert.</summary>
public class CreateChallengeBatchDto
{
    // Obergrenze gegen Missbrauch: pro Challenge-Batch eine begrenzte Empfängerliste (der Service
    // iteriert je Empfänger + legt Notifications an). 50 ist weit über jeder realen Freundeszahl im Menü.
    [Required]
    [MinLength(1)]
    [MaxLength(50)]
    public List<int> ToUserIds { get; set; } = new();

    [Required]
    public int PuzzleId { get; set; }

    /// <summary>Standard (Puzzles) oder Book (BookPuzzles). Default Standard.</summary>
    public PuzzleSource Source { get; set; } = PuzzleSource.Standard;
}

/// <summary>Ergebnis eines Batch-Versands: wie viele Challenges angelegt wurden + wer warum übersprungen wurde.</summary>
public class ChallengeBatchResultDto
{
    public int Sent { get; set; }
    public List<ChallengeSkipDto> Skipped { get; set; } = new();
}

/// <summary>Ein übersprungener Empfänger inkl. Grund: "self" | "not_friends" | "duplicate".</summary>
public class ChallengeSkipDto
{
    public int ToUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Ergebnis-Rückmeldung des Empfängers nach dem Lösen einer Challenge.</summary>
public class ResolveChallengeDto
{
    public bool Solved { get; set; }

    [Range(0, 3600)]
    public int TimeSpentSeconds { get; set; }
}

/// <summary>Eingehende, noch offene Challenge (Posteingang des Empfängers).</summary>
public class IncomingChallengeDto
{
    public int Id { get; set; }
    public int FromUserId { get; set; }
    public string FromUsername { get; set; } = string.Empty;
    public string? FromDisplayName { get; set; }
    public int PuzzleId { get; set; }
    /// <summary>"Standard" | "Book".</summary>
    public string Source { get; set; } = nameof(PuzzleSource.Standard);
    public int Rating { get; set; }
    public string? Themes { get; set; }
    /// <summary>Nur bei Buch-Puzzles gesetzt (Puzzle-/Kapitel-Titel).</summary>
    public string? Title { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Gesendete Challenge inkl. Ergebnis-Status des Empfängers.</summary>
public class OutgoingChallengeDto
{
    public int Id { get; set; }
    public int ToUserId { get; set; }
    public string ToUsername { get; set; } = string.Empty;
    public string? ToDisplayName { get; set; }
    public int PuzzleId { get; set; }
    /// <summary>"Standard" | "Book".</summary>
    public string Source { get; set; } = nameof(PuzzleSource.Standard);
    public int Rating { get; set; }
    public string? Title { get; set; }
    /// <summary>"Pending" | "Solved" | "Failed".</summary>
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int? TimeSpentSeconds { get; set; }
}
