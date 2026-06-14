namespace RookHub.Api.Models;

public enum ChallengeStatus
{
    /// <summary>Vom Empfänger noch nicht gelöst.</summary>
    Pending,
    /// <summary>Empfänger hat das Puzzle gelöst.</summary>
    Solved,
    /// <summary>Empfänger ist gescheitert / hat aufgegeben.</summary>
    Failed
}

/// <summary>
/// „Schick dieses Puzzle an einen Freund": Nach dem Lösen kann ein User ein konkretes Puzzle
/// an einen Freund als Challenge schicken. Der Empfänger sieht sie im Posteingang, löst sie,
/// und das Ergebnis (gelöst/gescheitert + Zeit) wird an den Absender zurückgemeldet.
/// </summary>
public class PuzzleChallenge
{
    public int Id { get; set; }

    public int FromUserId { get; set; }
    public AppUser FromUser { get; set; } = null!;

    public int ToUserId { get; set; }
    public AppUser ToUser { get; set; } = null!;

    public int PuzzleId { get; set; }
    public Puzzle Puzzle { get; set; } = null!;

    public ChallengeStatus Status { get; set; } = ChallengeStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Wann der Empfänger das Puzzle versucht hat (null solange Pending).</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>Lösezeit des Empfängers in Sekunden (null solange Pending).</summary>
    public int? TimeSpentSeconds { get; set; }
}
