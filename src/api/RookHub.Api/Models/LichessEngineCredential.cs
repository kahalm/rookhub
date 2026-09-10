using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Persistierter Lichess-API-Token (Scope <c>engine:read</c>) eines RookHub-Users für die
/// External-Engine-Anbindung: RookHub listet damit die auf dem Lichess-Konto des Users
/// registrierten External Engines (eigene Maschine via offiziellem Provider, Miet-Anbieter
/// wie stockfishcloud) und reicht Analyse-Anfragen an den Lichess-Broker durch.
/// AES-verschlüsselt wie der Chessable-Bearer; Plaintext nie persistiert.
/// </summary>
public class LichessEngineCredential
{
    public int Id { get; set; }

    /// <summary>Besitzer; Cascade-Delete mit dem User. 1:1.</summary>
    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>AES-verschlüsselter Lichess-Token (EncryptionService, v2/GCM).</summary>
    [Required]
    public string EncryptedToken { get; set; } = string.Empty;

    /// <summary>
    /// Die Engines, auf denen Hintergrund-Analyseauftraege laufen duerfen — als
    /// kommagetrennte Liste von Lichess-Kennungen (<c>eei_…</c>), leer/<c>null</c> = keine.
    ///
    /// <para><b>Warum mehrere:</b> der Worker rechnet je ENGINE genau einen Auftrag (ein
    /// Stockfish-Prozess kann nur eine Suche). Mit EINER Engine ist die Warteschlange also strikt
    /// seriell — ein einziger langer Auftrag legt alles still, und wer zwei Rechner stehen hat,
    /// kann den zweiten nicht nutzen. Mit mehreren laufen ebenso viele Auftraege nebeneinander.</para>
    ///
    /// <para>CSV statt eigener Tabelle: es sind eine Handvoll unveraenderliche Kennungen ohne
    /// eigene Felder, und sie werden immer als GANZE Liste gelesen und geschrieben (dasselbe
    /// Muster wie <c>CalcEdition.TesterAnnouncedUserIds</c>). Gelesen und geschrieben wird
    /// ausschliesslich ueber <see cref="BackgroundEngines"/> / <see cref="SetBackgroundEngines"/>,
    /// damit die Zerlegung an EINER Stelle steht.</para>
    /// </summary>
    [MaxLength(600)]
    public string? BackgroundEngineIds { get; set; }

    /// <summary>Die hinterlegten Hintergrund-Engines, in der gespeicherten Reihenfolge.</summary>
    public IReadOnlyList<string> BackgroundEngines =>
        string.IsNullOrWhiteSpace(BackgroundEngineIds)
            ? []
            : BackgroundEngineIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Setzt die Liste (Duplikate und Leerwerte fallen raus, Reihenfolge bleibt).</summary>
    public void SetBackgroundEngines(IEnumerable<string> ids)
    {
        var clean = ids.Select(i => i?.Trim() ?? string.Empty)
                       .Where(i => i.Length > 0)
                       .Distinct(StringComparer.Ordinal)
                       .ToList();
        BackgroundEngineIds = clean.Count == 0 ? null : string.Join(',', clean);
    }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
