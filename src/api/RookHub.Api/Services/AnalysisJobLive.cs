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

    /// <summary>Spitzenwerte je Nutzer in STUNDENKÖRBEN (Schlüssel = Stunde seit Epoche): die höchste Zahl
    /// gleichzeitiger Läufe und das höchste Gesamttempo, die in dieser Stunde gesehen wurden. Daraus kommt
    /// „Spitze (24 h)" in der Auftragsliste — der Vergleichswert, an dem man sieht, ob gerade alle Engines
    /// rechnen (0.543.0; Anlass: „engines: 1 · 1 164 kN/s" am Schwanz einer Partie sah wie ein Ausfall aus).
    /// Wie der Rest dieser Klasse nur im Arbeitsspeicher: ein Neustart der API beginnt bei null.</summary>
    private readonly ConcurrentDictionary<int, Dictionary<long, (int Runs, long Nps)>> _peaks = new();

    /// <summary>So weit zurück reicht die Spitze — in ganzen Stundenkörben, also 24 bis 25 Stunden.</summary>
    public const int PeakHours = 24;

    /// <summary><paramref name="secondsBase"/> = bereits verbuchte Rechenzeit BEIM START dieses Laufs;
    /// die laufende Sekunde kommt aus der Wanduhr dazu.</summary>
    public void Start(int jobId, int userId, int secondsBase, DateTime startedUtc)
    {
        _runs[jobId] = new Run { UserId = userId, SecondsBase = Math.Max(0, secondsBase), StartedUtc = startedUtc };
        RecordPeak(userId, startedUtc);
    }

    /// <summary>Tiefe/Tempo der zuletzt empfangenen Zeile. 0 heißt „unbekannt" und lässt den alten Wert stehen
    /// (die ersten Zeilen eines Laufs tragen oft time=0, daraus lässt sich kein Tempo rechnen).
    /// <paramref name="nowUtc"/> nur für Tests — der Worker lässt es weg.</summary>
    public void Update(int jobId, int depth, int nps, DateTime? nowUtc = null)
    {
        if (!_runs.TryGetValue(jobId, out var r)) return;
        if (depth > 0) r.Depth = depth;
        if (nps > 0)
        {
            r.Nps = nps;
            RecordPeak(r.UserId, nowUtc ?? DateTime.UtcNow);
        }
    }

    /// <summary>Höchste Zahl gleichzeitiger Läufe und höchstes Gesamttempo dieses Nutzers in den letzten
    /// <see cref="PeakHours"/> Stunden; (0, 0) ohne Aufzeichnung (frisch gestartet oder nie gerechnet).</summary>
    public (int MaxRuns, long MaxNodesPerSecond) Peak(int userId, DateTime nowUtc)
    {
        if (!_peaks.TryGetValue(userId, out var hours)) return (0, 0);
        var oldest = HourOf(nowUtc) - PeakHours;
        int runs = 0; long nps = 0;
        lock (hours)
        {
            foreach (var (hour, v) in hours)
            {
                if (hour < oldest) continue;
                runs = Math.Max(runs, v.Runs);
                nps = Math.Max(nps, v.Nps);
            }
        }
        return (runs, nps);
    }

    private void RecordPeak(int userId, DateTime nowUtc)
    {
        var (runs, nps) = Summary(userId);
        var hour = HourOf(nowUtc);
        var hours = _peaks.GetOrAdd(userId, _ => new Dictionary<long, (int, long)>());
        lock (hours)
        {
            hours.TryGetValue(hour, out var cur);
            hours[hour] = (Math.Max(cur.Runs, runs), Math.Max(cur.Nps, nps));
            // Abgelaufene Körbe wegräumen — es bleiben höchstens PeakHours + 1.
            var oldest = hour - PeakHours;
            List<long>? stale = null;
            foreach (var k in hours.Keys) if (k < oldest) (stale ??= new()).Add(k);
            if (stale is not null) foreach (var k in stale) hours.Remove(k);
        }
    }

    private static long HourOf(DateTime utc) => utc.Ticks / TimeSpan.TicksPerHour;

    public void Stop(int jobId) => _runs.TryRemove(jobId, out _);

    /// <summary>Laufende Aufträge dieses Nutzers mit ihrem aktuellen Stand.</summary>
    public List<AnalysisJobLiveDto> ForUser(int userId, DateTime nowUtc)
        => _runs.Where(kv => kv.Value.UserId == userId)
                .Select(kv => new AnalysisJobLiveDto(kv.Key, kv.Value.Depth, kv.Value.Nps, SecondsOf(kv.Value, nowUtc)))
                .OrderBy(d => d.Id)
                .ToList();

    /// <summary>Wie viele Laeufe dieses Nutzers gerade rechnen und ihr Tempo zusammen — fuer die
    /// Zeile „Stellungen/min · Restdauer" der Partie-Analysen. Laeufe ohne gemeldetes Tempo zaehlen
    /// als Engine mit, tragen aber 0 zur Summe bei (die ersten Zeilen tragen oft time=0).</summary>
    public (int Runs, long NodesPerSecond) Summary(int userId)
    {
        int runs = 0; long nps = 0;
        foreach (var r in _runs.Values)
        {
            if (r.UserId != userId) continue;
            runs++;
            nps += r.Nps;
        }
        return (runs, nps);
    }

    private static int SecondsOf(Run r, DateTime nowUtc)
    {
        var elapsed = (nowUtc - r.StartedUtc).TotalSeconds;
        return r.SecondsBase + (int)Math.Max(0, elapsed);   // Uhrsprünge rückwärts zählen nicht ab
    }
}
