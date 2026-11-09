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

    /// <summary>AES-verschlüsselter Lichess-Token (EncryptionService, v2/GCM). LEER erlaubt (seit dem eigenen
    /// Engine-Broker): ein Nutzer ohne Lichess-Konto braucht die Zeile trotzdem — sie trägt
    /// <see cref="BackgroundEngineIds"/> (dann „RookHub direkt"-Engines, <c>rhe_…</c>) und
    /// <see cref="ShareAsHouseEngine"/>. Jeder Leser prüft deshalb auf „Token vorhanden", nicht auf „Zeile
    /// vorhanden" (<c>TryDecrypt("")</c> liefert <c>null</c>).</summary>
    [Required]
    public string EncryptedToken { get; set; } = string.Empty;

    /// <summary>
    /// Die Engines, auf denen Hintergrund-Analyseauftraege laufen duerfen — als
    /// kommagetrennte Liste von Engine-Kennungen — Lichess (<c>eei_…</c>) und „RookHub direkt" (<c>rhe_…</c>)
    /// gemischt erlaubt, beide 16 Zeichen lang —, leer/<c>null</c> = keine.
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

    /// <summary>
    /// Diese Hintergrund-Engines stehen auch FREMDEN Partie-Analysen der Punktepartie offen
    /// („Haus-Engine"). Gesetzt nur von einem Admin (<c>PUT /api/engine/house</c>).
    ///
    /// <para><b>Warum es das braucht:</b> Analyseauftraege laufen ueber den Lichess-Token und die
    /// registrierten External Engines des AUFTRAGGEBERS. Ein normal registrierter Nutzer hat keine —
    /// sein eingeworfenes PGN prallte an „No background engine configured" ab, und das Einwerfen
    /// waere ein Angebot fuer die Handvoll Leute, die selbst eine Maschine betreiben.</para>
    ///
    /// <para>Ein Flag an der EINEN Zugangsdatenzeile und keine eigene Tabelle: es ist genau diese
    /// eine Frage, sie haengt an nichts weiter, und sie faellt mit dem Token, dem sie gilt (Cascade).
    /// Freigegeben sind immer ALLE <see cref="BackgroundEngines"/> dieser Zeile — eine Auswahl davon
    /// waere eine zweite Liste, die mit der ersten auseinanderlaufen kann.</para>
    /// </summary>
    public bool ShareAsHouseEngine { get; set; }

    /// <summary>
    /// Namen von Hintergrund-Engines, deren Registrierung das Aufräumen entfernt hat (Provider zu lange weg, 0.919.0).
    /// Meldet sich ein Provider später wieder mit genau diesem Namen an, bekommt er eine NEUE Kennung — die kommt dann
    /// von selbst wieder in <see cref="BackgroundEngineIds"/>, statt still aus der Hintergrund-Arbeit zu fallen.
    /// Zeilenweise; gelesen und geschrieben nur über <see cref="PrunedBackground"/> / <see cref="SetPrunedBackground"/>.
    /// </summary>
    [MaxLength(4000)]
    public string? PrunedBackgroundNames { get; set; }

    public IReadOnlyList<string> PrunedBackground =>
        string.IsNullOrEmpty(PrunedBackgroundNames) ? [] : PrunedBackgroundNames.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Setzt die Liste (Duplikate raus; passt sie nicht in die Spalte, fallen die ältesten weg).</summary>
    public void SetPrunedBackground(IEnumerable<string> names)
    {
        var clean = names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).ToList();
        while (clean.Count > 0 && string.Join('\n', clean).Length > 4000) clean.RemoveAt(0);
        PrunedBackgroundNames = clean.Count == 0 ? null : string.Join('\n', clean);
    }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
