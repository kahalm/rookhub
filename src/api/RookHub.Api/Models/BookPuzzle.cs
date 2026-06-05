using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

public class BookPuzzle
{
    public int Id { get; set; }

    [Required, MaxLength(300)]
    public string LineId { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string BookFileName { get; set; } = string.Empty;

    /// <summary>FK auf <see cref="Models.Book"/>. Nullable für Altbestand (Backfill via Migration).</summary>
    public int? BookId { get; set; }
    public Book? Book { get; set; }

    [Required, MaxLength(20)]
    public string Round { get; set; } = string.Empty;

    [Required]
    public string Fen { get; set; } = string.Empty;

    [Required]
    public string Moves { get; set; } = string.Empty;

    /// <summary>
    /// Halbzug-Index, ab dem das Training startet (der per ChessBase-[%tqu] markierte Zug).
    /// <see cref="Fen"/> + <see cref="Moves"/> enthalten die KOMPLETTE Partie; beim Lösen wird
    /// bis <c>moves[StartPly]</c> (Setup) vorgespult, gelöst wird ab <c>moves[StartPly+1]</c>.
    /// 0 = kein Trainingsmarker (klassisch: moves[0] Setup, lösen ab moves[1]).
    /// </summary>
    public int StartPly { get; set; }

    [MaxLength(300)]
    public string? Title { get; set; }

    [MaxLength(200)]
    public string? Chapter { get; set; }

    [MaxLength(5000)]
    public string? Comment { get; set; }

    [MaxLength(50)]
    public string? Difficulty { get; set; }

    public int? BookRating { get; set; }

    [MaxLength(200)]
    public string? Tags { get; set; }

    /// <summary>
    /// „Ausgemustert": Wird nicht mehr in den Zufalls-Pools (Daily/Random/Blind) gezogen.
    /// Gesetzt z. B. wenn ein Admin das Tagespuzzle für ein Datum neu generiert — das bis dahin
    /// gezogene Puzzle soll danach nie wieder als Tages-/Zufallspuzzle erscheinen. Direkter Aufruf
    /// per Id, Buch-Navigation (next/random im Buch) und persistierte Vergangenheits-Zuordnungen
    /// bleiben unberührt.
    /// </summary>
    public bool Retired { get; set; }
}
