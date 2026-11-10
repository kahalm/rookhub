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
    [MaxLength(BackgroundEngineIdsMaxLength)]
    public string? BackgroundEngineIds { get; set; }

    /// <summary>Höchstzahl hinterlegbarer Hintergrund-Engines — EINE Stelle für alle Wege, die die Liste vergrößern
    /// (Profil, Admin-Reiter „Engines“, Wiedereintragen bei Neuanmeldung), geprüft über <see cref="CheckBackgroundList"/>.
    ///
    /// <para>Verlauf: 8 → 16 (2026-09-11, 40-Kern-Maschine mit zwölf Hintergrund-Engines) → 32 (2026-10-05, zweiter
    /// Rechner mit sechzehn) → 64 (2026-11-10, 0.952.0: fünf Maschinen — Gross 12, Server 4, PC 16, Laptop 4, Spark —
    /// passten nicht mehr in 32; Prod stand von Hand schon bei 35). 64 ist zugleich
    /// <c>ExternalEngineRegistrationService.MaxEnginesPerUser</c>: mehr Engines als Registrierungen gibt es nicht.</para></summary>
    public const int MaxBackgroundEngines = 64;

    /// <summary>Spaltenlänge von <see cref="BackgroundEngineIds"/>: eine Kennung ist 16 Zeichen (<c>rhe_</c>/<c>eei_</c> + 12),
    /// mit Komma 17 — 64 Kennungen belegen 64 × 17 − 1 = 1 087 Zeichen; 1 200 lässt Reserve für längere fremde Kennungen.
    /// Bis 0.951.x 600 Zeichen (= höchstens 35 Kennungen).</summary>
    public const int BackgroundEngineIdsMaxLength = 1200;

    /// <summary>Warum eine Liste nicht gespeichert werden darf.</summary>
    public enum BackgroundListProblem { None, TooMany, TooLong }

    /// <summary>
    /// Darf <paramref name="next"/> an die Stelle einer Liste mit <paramref name="currentCount"/> Einträgen treten?
    /// <see cref="BackgroundListProblem.TooMany"/> nur, wenn sie über <see cref="MaxBackgroundEngines"/> liegt UND
    /// länger wird als bisher — eine Liste, die schon über dem Deckel steht (von Hand geschrieben), lässt sich so weiter
    /// verkleinern oder umstellen, nur nicht vergrößern. <see cref="BackgroundListProblem.TooLong"/>, wenn sie nicht in die
    /// Spalte passt — dann wird nie abgeschnitten gespeichert (eine halbe Kennung in der CSV wäre schlimmer).
    /// </summary>
    public static BackgroundListProblem CheckBackgroundList(IReadOnlyCollection<string> next, int currentCount)
    {
        if (next.Count > MaxBackgroundEngines && next.Count > currentCount) return BackgroundListProblem.TooMany;
        if (string.Join(',', next).Length > BackgroundEngineIdsMaxLength) return BackgroundListProblem.TooLong;
        return BackgroundListProblem.None;
    }

    /// <summary>Hängt <paramref name="engineId"/> hinten an, wenn Deckel und Spalte es erlauben (<see cref="CheckBackgroundList"/>).
    /// Steht sie schon drin: <see cref="BackgroundListProblem.None"/>, nichts geändert.</summary>
    public BackgroundListProblem TryAppendBackgroundEngine(string engineId)
    {
        var current = BackgroundEngines;
        if (current.Contains(engineId)) return BackgroundListProblem.None;
        var next = current.Append(engineId).ToList();
        var problem = CheckBackgroundList(next, current.Count);
        if (problem == BackgroundListProblem.None) SetBackgroundEngines(next);
        return problem;
    }

    /// <summary>Die hinterlegten Hintergrund-Engines, in der gespeicherten Reihenfolge.</summary>
    public IReadOnlyList<string> BackgroundEngines =>
        string.IsNullOrWhiteSpace(BackgroundEngineIds)
            ? []
            : BackgroundEngineIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Setzt die Liste (Duplikate und Leerwerte fallen raus, Reihenfolge bleibt). Den Deckel prüft der Aufrufer
    /// (<see cref="CheckBackgroundList"/>); passt die Liste nicht in die Spalte, wirft es — lieber kein Speichern als ein
    /// abgeschnittenes.</summary>
    public void SetBackgroundEngines(IEnumerable<string> ids)
    {
        var clean = ids.Select(i => i?.Trim() ?? string.Empty)
                       .Where(i => i.Length > 0)
                       .Distinct(StringComparer.Ordinal)
                       .ToList();
        var csv = string.Join(',', clean);
        if (csv.Length > BackgroundEngineIdsMaxLength)
            throw new InvalidOperationException(
                $"Background engine list too long ({csv.Length} > {BackgroundEngineIdsMaxLength} characters)");
        BackgroundEngineIds = clean.Count == 0 ? null : csv;
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
