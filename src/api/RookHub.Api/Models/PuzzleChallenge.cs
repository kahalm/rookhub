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

/// <summary>Aus welcher Puzzle-Quelle die Challenge stammt — bestimmt, welche Tabelle die
/// <see cref="PuzzleChallenge.PuzzleId"/> referenziert und über welchen Deep-Link der Empfänger löst.</summary>
public enum PuzzleSource
{
    /// <summary>Standard-/Endless-Puzzle (Tabelle <c>Puzzles</c>, Deep-Link <c>/puzzles/:id</c>).</summary>
    Standard,
    /// <summary>Buch-/Kurs-/Tagespuzzle (Tabelle <c>BookPuzzles</c>, Deep-Link <c>/puzzles/book/:id</c>).</summary>
    Book
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

    /// <summary>ID des Puzzles — je nach <see cref="Source"/> eine <c>Puzzles.Id</c> (Standard/Endless)
    /// oder eine <c>BookPuzzles.Id</c> (Buch/Kurs/Tagespuzzle). Bewusst ohne harten FK, da polymorph.</summary>
    public int PuzzleId { get; set; }

    /// <summary>Quelle des Puzzles (Standard vs. Buch) — steuert Validierung, Metadaten-Lookup und Deep-Link.</summary>
    public PuzzleSource Source { get; set; } = PuzzleSource.Standard;

    public ChallengeStatus Status { get; set; } = ChallengeStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Wann der Empfänger das Puzzle versucht hat (null solange Pending).</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>Lösezeit des Empfängers in Sekunden (null solange Pending).</summary>
    public int? TimeSpentSeconds { get; set; }
}
