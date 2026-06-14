using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

/// <summary>Eine Challenge an einen Freund anlegen: „schick dieses Puzzle an XY".</summary>
public class CreateChallengeDto
{
    [Required]
    public int ToUserId { get; set; }
    [Required]
    public int PuzzleId { get; set; }
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
    public int Rating { get; set; }
    public string? Themes { get; set; }
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
    public int Rating { get; set; }
    /// <summary>"Pending" | "Solved" | "Failed".</summary>
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int? TimeSpentSeconds { get; set; }
}
