using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Ein Spieler, dessen Turnierverlauf ein Nutzer mitverfolgen will — ohne dass dieser Spieler ein
/// Konto haette.
///
/// <para><b>Warum das neben den Freunden steht.</b> Der Verlauf war bis hierher an KONTEN
/// gebunden: der eigene und der von angenommenen Freunden. Die Leute, deren Ergebnisse man
/// tatsaechlich verfolgt, haben aber meist gar kein Konto hier — das eigene Kind, ein
/// Vereinskamerad, der Gegner aus der naechsten Runde. Sie einzuladen, damit man ihre oeffentlich
/// auf chess-results stehenden Turniere sehen kann, ist keine Loesung.</para>
///
/// <para><b>Was gespeichert wird, ist eine SUCHE, kein Personendatensatz.</b> Genau die vier
/// Felder, aus denen <c>TournamentHistoryService.IdentityOf</c> einen Spielerschluessel baut:
/// Nachname (die chess-results-Spielersuche sucht ueber den Namen), Vorname zur Eingrenzung und
/// die beiden Kennungen, die bei Namensgleichheit entscheiden. Der Verlauf selbst liegt weiterhin
/// am Spielerschluessel (<see cref="PlayerTournamentResult.PlayerKey"/>) und wird geteilt: wer
/// denselben Spieler verfolgt wie jemand anderes, loest keinen zweiten Abruf aus.</para>
///
/// <para><b>Sichtbarkeit.</b> Die Liste gehoert dem Nutzer, der sie angelegt hat, und wird
/// nirgends veroeffentlicht. Die Turnierdaten dahinter sind auf chess-results oeffentlich; die
/// Verknuepfung „Konto X verfolgt Spieler Y" ist es nicht.</para>
/// </summary>
public class TrackedPlayer
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>
    /// Der Spielerschluessel wie in <see cref="PlayerTournamentResult.PlayerKey"/>
    /// (<c>fide:1693034</c>, <c>cr:144749</c> oder <c>name:oberschmid-patrik</c>). Steht hier
    /// mit, damit derselbe Spieler nicht zweimal in derselben Liste landet — ueber den Namen
    /// gesucht und ueber die FIDE-Nummer waere sonst zweimal dieselbe Zeile.
    /// </summary>
    [Required, MaxLength(40)]
    public string PlayerKey { get; set; } = string.Empty;

    /// <summary>Was am Reiter steht — der Name aus der Trefferliste, also die Schreibweise der Quelle.</summary>
    [Required, MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(20)]
    public string? FideId { get; set; }

    /// <summary>Die chess-results-Ident-Nummer; <c>null</c>, wenn die Quelle keine fuehrt.</summary>
    [MaxLength(20)]
    public string? ChessResultsId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
