namespace RookHub.Api.Models;

/// <summary>
/// Mitglied des privaten Verteilers einer Kalkulations-Serie (Phase 2). Wer hier für ein Buch steht,
/// darf den (dann nicht mehr öffentlichen) Kurs sehen — der Zugriff hängt an dieser Mitgliedschaft
/// statt an <see cref="Book.IsPublic"/> (siehe <c>CourseAccess.CanAccessAsync</c>). Das Häkchen
/// <see cref="IsTester"/> gibt einem Mitglied Frühzugang: es sieht eine Woche schon ab deren
/// <c>CalcEdition.TesterPreviewAt</c> statt erst ab <c>PublishAt</c>.
/// </summary>
public class CalcSeriesMember
{
    public int Id { get; set; }

    /// <summary>Das Serien-Buch (Kalkulationsbuch).</summary>
    public int BookId { get; set; }
    public Book? Book { get; set; }

    /// <summary>Der freigeschaltete Nutzer.</summary>
    public int UserId { get; set; }

    /// <summary>Tester sehen terminierte Wochen schon ab dem früheren <c>TesterPreviewAt</c>.</summary>
    public bool IsTester { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
