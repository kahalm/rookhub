using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Services.Engines;

/// <summary>Reihum-Wechsel eines Hintergrund-Auftrags auf die nächste hinterlegte Engine, nachdem die bisherige
/// stehen blieb oder der Broker 503/504 meldete (aus <see cref="AnalysisJobWorker"/> gezogen, Zerlegung Z11).</summary>
internal static class BackgroundEngineRotation
{
    /// <summary>Den Auftrag auf die NAECHSTE hinterlegte Hintergrund-Engine umhaengen, nachdem die
    /// bisherige stehen geblieben ist. Gibt zurueck, ob gewechselt wurde (gespeichert wird erst vom
    /// aufrufenden Pause/Fail).
    /// <para>Bewusst REIHUM und nicht ueber die kuerzeste Schlange: eine haengende Engine hat gerade gar
    /// nichts zu tun und gewaenne jeden Schlangenvergleich — der Auftrag liefe ihr sofort wieder in die
    /// Arme. Bewusst nur, wenn die aktuelle Engine ueberhaupt in der Liste steht: eine von Hand
    /// festgelegte Engine ist eine Entscheidung des Nutzers und bleibt.</para></summary>
    internal static async Task<bool> SwitchAsync(AppDbContext db, AnalysisJob job, int engineOwnerId,
        IReadOnlySet<string> explicitOnly, CancellationToken ct)
    {
        var cred = await db.LichessEngineCredentials.FirstOrDefaultAsync(c => c.UserId == engineOwnerId, ct);
        var engines = cred?.BackgroundEngines ?? [];
        // Reihum, aber nur über Engines, die gerade Arbeit nehmen (0.711.0): sonst wanderte ein Auftrag nach einem 503
        // über alle ausgeschalteten Engines einer Maschine, je 15 s Pause, bevor er eine laufende traf.
        var usable = await EngineAvailability.UsableAsync(db, engineOwnerId, engines, job.Background, quiet: null,
            DateTimeOffset.UtcNow, ct);
        if (NextEngineAfter(engines, job.EngineId, explicitOnly, usable.ToHashSet(StringComparer.Ordinal)) is not { } next)
            return false;
        job.EngineId = next;
        return true;
    }

    /// <summary>Die naechste Engine REIHUM nach <paramref name="current"/>; <c>null</c>, wenn es keinen
    /// Wechsel gibt — weil nur eine hinterlegt ist oder weil die aktuelle gar nicht in der Liste steht
    /// (dann hat der Nutzer sie von Hand gewaehlt, und das bleibt seine Entscheidung).</summary>
    /// <param name="usable">Engines, die gerade Arbeit nehmen: reihum wird die nächste DAVON gewählt. Ist keine andere
    /// brauchbar (oder fehlt die Angabe), bleibt es bei der nächsten in der Liste — der Auftrag wartet dann dort.</param>
    internal static string? NextEngineAfter(IReadOnlyList<string> engines, string current,
        IReadOnlySet<string>? explicitOnly = null, IReadOnlySet<string>? usable = null)
    {
        // Eine Nur-auf-Anforderung-Engine rotiert weder hinein noch heraus: ein Auftrag, der sie nannte, bleibt dort,
        // und ein Stockfish-Auftrag landet nicht bei Lc0.
        if (explicitOnly is { Count: > 0 })
        {
            if (explicitOnly.Contains(current)) return null;
            engines = ExplicitOnlyEngines.Automatic(engines, explicitOnly);
        }
        if (engines.Count < 2) return null;
        for (var i = 0; i < engines.Count; i++)
        {
            if (engines[i] != current) continue;
            if (usable is { Count: > 0 })
                for (var k = 1; k < engines.Count; k++)
                    if (usable.Contains(engines[(i + k) % engines.Count])) return engines[(i + k) % engines.Count];
            return engines[(i + 1) % engines.Count];
        }
        return null;
    }
}
