namespace RookHub.Api.Models;

/// <summary>
/// Teilt einen persönlichen Kurs (Buch mit gesetztem <see cref="Book.OwnerUserId"/>) mit einem
/// ausgewählten anderen Nutzer. Der Empfänger sieht den Kurs dann in seiner Kursliste und darf ihn
/// mit eigenem Fortschritt durcharbeiten, aber NICHT verwalten (kein Löschen/Weiterteilen). Analog
/// zu <see cref="BookGroupAccess"/>, nur person-zu-person statt gruppenweit. Cascade nur über das
/// Buch (ein einziger Cascade-Pfad, MySQL-tauglich); die beiden AppUser-FKs sind — wie bei
/// <see cref="Friendship"/> — Restrict.
/// </summary>
public class CourseShare
{
    public int Id { get; set; }

    public int BookId { get; set; }
    public Book Book { get; set; } = null!;

    /// <summary>Wer geteilt hat (= Besitzer des Buchs zum Zeitpunkt des Teilens).</summary>
    public int OwnerId { get; set; }
    public AppUser Owner { get; set; } = null!;

    /// <summary>Mit wem geteilt wurde.</summary>
    public int RecipientId { get; set; }
    public AppUser Recipient { get; set; } = null!;

    public DateTime SharedAt { get; set; } = DateTime.UtcNow;
}
