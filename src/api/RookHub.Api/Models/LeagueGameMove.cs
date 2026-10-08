namespace RookHub.Api.Models;

/// <summary>
/// Die ersten Züge einer Brettpartie der Liga (2026-10-08, Wunsch: „bei jeder Partie die Möglichkeit, die ersten paar Züge
/// einzugeben"). Hängt am NATÜRLICHEN Schlüssel der Paarung (Tnr, Runde, Begegnung, Brett) — bewusst nicht an
/// <see cref="LeagueGame.Id"/>: die Zeilen einer Liga werden beim Aktualisieren zusammengeführt (<c>LeagueGameLinks</c>), der
/// Schlüssel ist das, was stabil bleibt. Die Züge sind je Paarung GLOBAL sichtbar (öffentliche Ligadaten); <see cref="ClubId"/>
/// sagt nur, aus welchem Verein sie eingetragen wurden.
/// </summary>
public class LeagueGameMove
{
    public int Id { get; set; }
    public int Tnr { get; set; }
    public int Round { get; set; }
    public int MatchNo { get; set; }
    public int Board { get; set; }
    /// <summary>Englische SAN mit Leerzeichen ab der Grundstellung, höchstens <see cref="Services.League.LeagueGameMoves.MaxPlies"/>
    /// Halbzüge, beim Speichern auf Legalität geprüft.</summary>
    public string Moves { get; set; } = string.Empty;
    /// <summary>Verein des Eintragenden (nur Zuordnung).</summary>
    public int ClubId { get; set; }
    /// <summary>Wer zuletzt gespeichert hat — kein Fremdschlüssel (die Züge bleiben, wenn das Konto geht).</summary>
    public int? UpdatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
}
