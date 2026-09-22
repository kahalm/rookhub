using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Zwischengespeicherte Antwort des Lichess-Eröffnungs-Explorers für EINE Stellung und EINE
/// Partien-Auswahl (Datenbank + Elo-Stufen + Bedenkzeiten). Geteilt über alle Nutzer: die ersten
/// Züge fast jedes Repertoires sind dieselben, und der Explorer verlangt seit 2025 einen Token und
/// drosselt — ohne diesen Speicher fragte jeder Lochfinder-Lauf dieselben Stellungen neu ab.
/// </summary>
public class LichessExplorerCacheEntry
{
    public int Id { get; set; }

    /// <summary><c>{Auswahl}|{Stellung}</c> — Auswahl aus <see cref="Services.ExplorerQuery.CachePrefix"/>,
    /// Stellung als die ersten DREI FEN-Felder (Brett, Zugrecht, Rochade). Eindeutig.</summary>
    [Required, MaxLength(255)]
    public string CacheKey { get; set; } = string.Empty;

    /// <summary>Kompaktes JSON (<see cref="Services.ExplorerPositionStats"/>).</summary>
    [Required]
    public string Json { get; set; } = string.Empty;

    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}
