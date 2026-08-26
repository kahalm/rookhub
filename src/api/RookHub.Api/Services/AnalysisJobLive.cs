using System.Collections.Concurrent;
using RookHub.Api.DTOs;

namespace RookHub.Api.Services;

/// <summary>Laufender Stand der gerade rechnenden Analyseaufträge — NUR im Arbeitsspeicher.
///
/// Die Auftragsliste soll sekündlich mitlaufen (Zeit tickt, Tempo aktuell). Über die DB ginge das nicht
/// ohne Schreiblast: der Worker sichert seinen Stand bewusst nur alle paar Sekunden, und öfter zu
/// schreiben hieße, für eine reine Anzeige jede Sekunde eine Zeile anzufassen. Hier steht derselbe Stand
/// ohne Datenbank — die Sekunden wachsen aus der Startzeit, also auch dann, wenn die Engine gerade
/// SCHWEIGT (nach einer Fortsetzung rechnet sie erst wieder von Tiefe 1 hoch).
///
/// Der Inhalt ist bewusst flüchtig: nach einem API-Neustart ist er leer, bis der Worker die Aufträge
/// wieder aufgreift — die dauerhaften Werte stehen in <see cref="Models.AnalysisJob"/>.
/// </summary>
public sealed class AnalysisJobLive
{
    private sealed class Run
    {
        public required int UserId { get; init; }
        public required int SecondsBase { get; init; }
        public required DateTime StartedUtc { get; init; }
        public int Depth;
        public int Nps;
    }

    private readonly ConcurrentDictionary<int, Run> _runs = new();   // key = JobId

    /// <summary><paramref name="secondsBase"/> = bereits verbuchte Rechenzeit BEIM START dieses Laufs;
    /// die laufende Sekunde kommt aus der Wanduhr dazu.</summary>
    public void Start(int jobId, int userId, int secondsBase, DateTime startedUtc)
        => _runs[jobId] = new Run { UserId = userId, SecondsBase = Math.Max(0, secondsBase), StartedUtc = startedUtc };

    /// <summary>Tiefe/Tempo der zuletzt empfangenen Zeile. 0 heißt „unbekannt" und lässt den alten Wert stehen
    /// (die ersten Zeilen eines Laufs tragen oft time=0, daraus lässt sich kein Tempo rechnen).</summary>
    public void Update(int jobId, int depth, int nps)
    {
        if (!_runs.TryGetValue(jobId, out var r)) return;
        if (depth > 0) r.Depth = depth;
        if (nps > 0) r.Nps = nps;
    }

    public void Stop(int jobId) => _runs.TryRemove(jobId, out _);

    /// <summary>Laufende Aufträge dieses Nutzers mit ihrem aktuellen Stand.</summary>
    public List<AnalysisJobLiveDto> ForUser(int userId, DateTime nowUtc)
        => _runs.Where(kv => kv.Value.UserId == userId)
                .Select(kv => new AnalysisJobLiveDto(kv.Key, kv.Value.Depth, kv.Value.Nps, SecondsOf(kv.Value, nowUtc)))
                .OrderBy(d => d.Id)
                .ToList();

    private static int SecondsOf(Run r, DateTime nowUtc)
    {
        var elapsed = (nowUtc - r.StartedUtc).TotalSeconds;
        return r.SecondsBase + (int)Math.Max(0, elapsed);   // Uhrsprünge rückwärts zählen nicht ab
    }
}
