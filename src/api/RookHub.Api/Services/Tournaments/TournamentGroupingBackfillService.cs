using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.TournamentDirectory;

namespace RookHub.Api.Services.Tournaments;

/// <summary>
/// Traegt Basisname und Gruppenschluessel im ALTBESTAND des Turnierverzeichnisses nach.
///
/// Beide Felder kamen mit v0.408.0 dazu und werden nur dort gesetzt, wo der Sweep eine Zeile
/// schreibt. Nach dem Deploy stand deshalb ein voll gefuelltes Verzeichnis ohne einen einzigen
/// Schluessel da (Dev: 0 von 1689) — die Abfrage faellt dann auf <c>"id:" + Id</c> zurueck, also
/// steht jede Gruppe wieder einzeln in der Liste. Sichtbar wurde die Zusammenfassung erst nach dem
/// naechsten naechtlichen Lauf, und Turniere, die aus der chess-results-Suche herausgefallen sind,
/// haetten NIE einen Schluessel bekommen.
///
/// Laeuft beim Start, in Stapeln, und rechnet ueber dieselbe
/// <see cref="TournamentDirectoryService.ApplyGrouping"/> wie der Sweep — sonst gruppiert der
/// Bestand anders als der Zulauf.
///
/// <para><b>UI-Sweep 2026-10-11 (r2-t-dup-cards): jetzt ein ABGLEICH, nicht nur ein Auffuellen.</b>
/// Der Schluessel liest seitdem den VERORTETEN Ort (<see cref="TournamentDirectoryService.GroupPlace"/>).
/// Zwei Folgen: (1) jeder bestehende Schluessel war nach dem Deploy veraltet — „nur leere fuellen"
/// haette den Bestand nie umgeschluesselt; (2) die Verortung laeuft bei den Zusatzquellen NACH
/// <c>ApplyClassification</c>, ein neuer Eintrag traegt also zunaechst einen Schluessel aus dem
/// Ortstext. Deshalb vergleicht der Lauf JEDEN Eintrag mit der aktuellen Rechnung und schreibt nur
/// die abweichenden — beim Start und als letzter Schritt jedes Nachtlaufs
/// (<see cref="TournamentDirectoryScheduler"/>).</para>
///
/// <para>Bewusst KEINE Fassungs-Spalte wie bei den gecrawlten Daten: dort kostet die Wiederholung
/// Seitenabrufe, hier nur eine Hash-Rechnung je Zeile (lokal, ~42 000 Zeilen, schmale Projektion).
/// Ein Vergleich gegen die Rechnung selbst kann nicht vergessen werden — eine Fassungsnummer, die
/// bei der naechsten Regelaenderung niemand erhoeht, schon. Idempotent: ein zweiter Lauf findet
/// nichts mehr.</para>
/// </summary>
public class TournamentGroupingBackfillService : BackgroundService
{
    /// <summary>Stapelgroesse — klein genug, dass der Start nicht auf einer Riesen-Transaktion haengt.</summary>
    internal const int BatchSize = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TournamentGroupingBackfillService> _logger;

    public TournamentGroupingBackfillService(IServiceScopeFactory scopeFactory,
        ILogger<TournamentGroupingBackfillService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var changed = await RegroupAsync(db, stoppingToken);
            if (changed > 0)
                _logger.LogInformation("Turnierverzeichnis: Gruppenschluessel fuer {Count} Eintraege neu gesetzt.", changed);
        }
        catch (OperationCanceledException)
        {
            // Herunterfahren waehrend des Nachtragens: der naechste Start macht weiter.
        }
        catch (Exception ex)
        {
            // Kein Grund, den Start scheitern zu lassen — mit alten Schluesseln steht die Liste
            // gruppiert wie vorher.
            _logger.LogWarning(ex, "Turnierverzeichnis: Nachziehen der Gruppenschluessel fehlgeschlagen.");
        }
    }

    /// <summary>
    /// Bringt Basisname und Gruppenschluessel JEDES Eintrags auf die aktuelle Rechnung; gibt die
    /// Anzahl der geaenderten zurueck.
    ///
    /// <para>Gelesen wird eine schmale Projektion (ohne Nachverfolgung) in Stapeln entlang der Id;
    /// nachgeladen und geschrieben werden nur die Eintraege, deren Schluessel abweicht. Beim
    /// Schreiben wird auf dem frisch geladenen Stand neu gerechnet — hat ein paralleler Lauf
    /// inzwischen Ort oder Termin geaendert, gilt dessen Stand.</para>
    /// </summary>
    internal static async Task<int> RegroupAsync(AppDbContext db, CancellationToken ct = default)
    {
        var changed = 0;
        var lastId = 0;
        while (!ct.IsCancellationRequested)
        {
            var batch = await db.TournamentDirectoryEntries.AsNoTracking()
                .Where(e => e.Id > lastId)
                .OrderBy(e => e.Id)
                .Take(BatchSize)
                .Select(e => new GroupingRow(e.Id, e.Name, e.BaseName, e.GroupKey, e.Federation,
                    e.StartDate, e.EndDate, e.LocationText, e.Lat, e.GeoSource, e.GeoPlaceName))
                .ToListAsync(ct);
            if (batch.Count == 0) break;
            lastId = batch[^1].Id;

            var stale = batch.Where(IsStale).Select(r => r.Id).ToList();
            if (stale.Count == 0) continue;

            var entries = await db.TournamentDirectoryEntries
                .Where(e => stale.Contains(e.Id))
                .ToListAsync(ct);
            foreach (var entry in entries)
                TournamentDirectoryService.ApplyGrouping(entry);
            await db.SaveChangesAsync(ct);
            changed += entries.Count;
            // Der Start-Kontext lebt nur fuer diesen Lauf; ohne Leeren sammelte er alle
            // umgeschluesselten Eintraege an.
            db.ChangeTracker.Clear();
        }
        return changed;
    }

    /// <summary>Weicht Basisname oder Schluessel von der aktuellen Rechnung ab?</summary>
    private static bool IsStale(GroupingRow row)
    {
        var probe = new TournamentDirectoryEntry
        {
            Name = row.Name,
            Federation = row.Federation,
            StartDate = row.StartDate,
            EndDate = row.EndDate,
            LocationText = row.LocationText,
            Lat = row.Lat,
            GeoSource = row.GeoSource,
            GeoPlaceName = row.GeoPlaceName,
        };
        TournamentDirectoryService.ApplyGrouping(probe);
        return probe.GroupKey != row.GroupKey || probe.BaseName != row.BaseName;
    }

    private sealed record GroupingRow(int Id, string Name, string? BaseName, string? GroupKey,
        string? Federation, DateOnly? StartDate, DateOnly? EndDate, string? LocationText,
        double? Lat, GeoSource GeoSource, string? GeoPlaceName);
}
