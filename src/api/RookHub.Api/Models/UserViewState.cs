using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RookHub.Api.Models;

/// <summary>
/// Der Anzeige-Zustand EINER Seite fuer EINEN Nutzer — heute die Filterleiste des
/// Turnierkalenders.
///
/// <para><b>Warum serverseitig.</b> Der Zustand lag nur im `localStorage` und war damit an ein
/// Geraet gebunden: der Umkreis, den man sich am Rechner eingestellt hat, war am Handy weg. Es
/// ist die Einstellung eines NUTZERS, nicht die eines Browsers.</para>
///
/// <para><b>Der Inhalt ist fuer den Server OPAK</b> (gepruefst werden nur JSON-Gueltigkeit,
/// Objekt-Form und Groesse). Er wird nirgends abgefragt oder gefiltert, und die Filterleiste
/// bekommt weiter Felder — jedes davon als Spalte zu fuehren hiesse eine Migration je Feld, ohne
/// dass irgendetwas davon in SQL gebraucht wird. Dasselbe Vorgehen wie beim Analysebaum des
/// Kalkulations-Modus (<see cref="CalculationTree.TreeJson"/>).</para>
///
/// <para><b>`ViewKey` ist eine ERLAUBTE Kennung, kein freier Name</b> (siehe
/// <c>ViewStateService.AllowedKeys</c>): ohne diese Liste waere das ein Schluessel-Wert-Speicher
/// je Nutzer fuer beliebige Inhalte — ein Feature soll es sein, kein offenes Lager.</para>
/// </summary>
public class UserViewState
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>Welche Ansicht (z. B. <c>turnier.directory</c>) — aus der Erlaubnis-Liste.</summary>
    [Required, MaxLength(64)]
    public string ViewKey { get; set; } = string.Empty;

    /// <summary>
    /// Der Zustand als JSON-Objekt. Opak; nur Form und Groesse werden geprueft.
    ///
    /// <para>Bewusst <c>text</c> und nicht <c>varchar(8192)</c>: in utf8mb4 zaehlt letzteres mit
    /// 32 KB gegen das 64-KB-Zeilenlimit von MariaDB — unnoetig knapp fuer ein Feld, das nie in
    /// einer Bedingung vorkommt. <c>text</c> liegt ausserhalb der Zeile; die Obergrenze erzwingt
    /// <c>ViewStateService.MaxJsonLength</c>.</para>
    /// </summary>
    [Required, MaxLength(8192), Column(TypeName = "text")]
    public string Json { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
