using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

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
/// Laeuft einmal beim Start, in Stapeln, und rechnet ueber dieselbe
/// <see cref="TournamentDirectoryService.ApplyGrouping"/> wie der Sweep — sonst gruppiert der
/// Bestand anders als der Zulauf. Idempotent: erledigt sind genau die Zeilen ohne Schluessel,
/// ein zweiter Start findet nichts mehr.
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
            var filled = await BackfillAsync(db, stoppingToken);
            if (filled > 0)
                _logger.LogInformation("Turnierverzeichnis: Gruppenschluessel fuer {Count} Alt-Eintraege nachgetragen.", filled);
        }
        catch (OperationCanceledException)
        {
            // Herunterfahren waehrend des Nachtragens: der naechste Start macht weiter.
        }
        catch (Exception ex)
        {
            // Kein Grund, den Start scheitern zu lassen — ohne Schluessel steht die Liste
            // ungruppiert da, das ist der Zustand von vorher.
            _logger.LogWarning(ex, "Turnierverzeichnis: Nachtragen der Gruppenschluessel fehlgeschlagen.");
        }
    }

    /// <summary>Fuellt alle Eintraege ohne Gruppenschluessel; gibt die Anzahl zurueck.</summary>
    internal static async Task<int> BackfillAsync(AppDbContext db, CancellationToken ct = default)
    {
        var filled = 0;
        while (!ct.IsCancellationRequested)
        {
            var batch = await db.TournamentDirectoryEntries
                .Where(e => e.GroupKey == null)
                .OrderBy(e => e.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0) break;

            foreach (var entry in batch)
                TournamentDirectoryService.ApplyGrouping(entry);

            await db.SaveChangesAsync(ct);
            filled += batch.Count;
        }
        return filled;
    }
}
