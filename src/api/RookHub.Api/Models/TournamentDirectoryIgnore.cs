using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// „Dieses Turnier will ich nicht sehen" — je Nutzer.
///
/// <para>Das Verzeichnis kennt tausende Turniere, und ein Teil davon ist fuer einen bestimmten
/// Menschen dauerhaft uninteressant: die Liga, in der er nicht spielt, das Jugendturnier seiner
/// Nachbarstadt, die Serie, die er jedes Jahr auslaesst. Ein Filter hilft da nicht — die Auswahl
/// ist nicht regelhaft, sie ist persoenlich. Ausgeblendete Turniere verschwinden aus Liste, Karte
/// und Kalender, es sei denn, der Filter fragt sie ausdruecklich mit an.</para>
///
/// <para>Sie verschwinden AUCH aus der naechtlichen Umkreis-Meldung: eine Meldung ueber ein
/// Turnier, das man weggeklickt hat, ist genau die Art Benachrichtigung, die einen dazu bringt,
/// alle abzuschalten.</para>
///
/// <para>Gemerkt wird die chess-results-Nummer und nicht die Eintrags-Id: der Eintrag kann
/// verschwinden und wiederkommen (die Absage-Karenz setzt <c>RemovedAt</c>), und dann soll die
/// Entscheidung des Nutzers noch gelten. Deshalb auch kein Fremdschluessel — dieselbe Ueberlegung
/// wie bei <c>TournamentSubscription</c>.</para>
/// </summary>
public class TournamentDirectoryIgnore
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>
    /// Die Identitaet des ausgeblendeten Turniers (<see cref="TournamentDirectoryEntry.PublicId"/>)
    /// — nicht die Eintrags-Id: der Eintrag kann verschwinden und wiederkommen (die Absage-Karenz
    /// setzt <c>RemovedAt</c>), und dann soll die Entscheidung des Nutzers noch gelten. Deshalb
    /// auch kein Fremdschluessel, dieselbe Ueberlegung wie bei <c>TournamentSubscription</c>.
    /// </summary>
    [Required, MaxLength(24)]
    public string PublicId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
