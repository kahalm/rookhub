using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Antwort des LOKALEN Explorers (<c>LichessExplorer:LocalUrl</c>) für EINE Stellung und EINE Auswahl (Elo-Stufen + Bedenkzeiten) —
/// der dauerhafte Speicher hinter der Schätzung der Trainingslinien (0.884.0). Vorher lagen die Antworten nur 24 h im
/// Arbeitsspeicher, und jedes Deploy leerte ihn (Prod 07.11.: 4 177 Stellungen eines Gegners = 162 s). Bewusst NICHT
/// <see cref="LichessExplorerCacheEntry"/> (Online-Explorer): andere Gültigkeit (45 statt 90 Tage), zehnmal mehr Zeilen (je Nutzer
/// alle Gegnerzug-Stellungen × Stufenkombinationen), gebündeltes Schreiben statt Lesen-dann-Schreiben je Stellung — und ein
/// Schlüssel als Hash statt eines bis zu 255 Zeichen langen Textindex.
/// </summary>
public class LocalExplorerPosition
{
    /// <summary>Die ersten 16 Byte von SHA-256 über <c>local|{Auswahl}|{Stellung}</c> als 32 Hex-Zeichen (Primärschlüssel) —
    /// siehe <c>RepertoireExplorerService.LocalStoreKey</c>.</summary>
    [Required, MaxLength(32)]
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>Brotli-gepacktes kompaktes JSON (<see cref="Services.Engines.ExplorerPositionStats"/>).</summary>
    [Required]
    public byte[] Payload { get; set; } = [];

    /// <summary>Abgerufen (UTC); älter als <c>LichessExplorer:LocalStoreDays</c> (Vorgabe 45) gilt nicht mehr.</summary>
    public DateTime FetchedAt { get; set; }
}
