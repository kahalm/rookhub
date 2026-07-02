namespace RookHub.Api.Models;

/// <summary>
/// Teilt ein persönliches Repertoire mit einem ausgewählten anderen Nutzer. Der Empfänger sieht das
/// Repertoire dann in seiner Liste (Sektion „Mit mir geteilt"), kann es öffnen, das PGN herunterladen
/// und mit eigenem Spaced-Repetition-Fortschritt trainieren, es aber NICHT bearbeiten/löschen/
/// weiterteilen. Analog zu <see cref="CourseShare"/>. Cascade nur über das Repertoire (ein einziger
/// Cascade-Pfad, MySQL-tauglich); die beiden AppUser-FKs sind — wie bei <see cref="Friendship"/> —
/// Restrict.
/// </summary>
public class RepertoireShare
{
    public int Id { get; set; }

    public int RepertoireId { get; set; }
    public Repertoire Repertoire { get; set; } = null!;

    /// <summary>Wer geteilt hat (= Besitzer des Repertoires).</summary>
    public int OwnerId { get; set; }
    public AppUser Owner { get; set; } = null!;

    /// <summary>Mit wem geteilt wurde.</summary>
    public int RecipientId { get; set; }
    public AppUser Recipient { get; set; } = null!;

    public DateTime SharedAt { get; set; } = DateTime.UtcNow;
}
