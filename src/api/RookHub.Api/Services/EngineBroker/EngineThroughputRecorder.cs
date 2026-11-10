using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>Wie ein Lauf des <c>AnalysisJobWorker</c> auf einer Engine endete (Zählung für die Admin-Seite „Engines").</summary>
public enum EngineRunOutcome
{
    /// <summary>Ziel erreicht.</summary>
    Done,
    /// <summary>Auftrag endgültig gescheitert.</summary>
    Failed,
    /// <summary>Von uns unterbrochen (Live-Vorrang, Admin-Pause, Ändern/Löschen).</summary>
    Interrupted,
    /// <summary>Vorzeitig beendet, wird fortgesetzt.</summary>
    Retried,
    /// <summary>Broker 503/504 — kein Provider für diese Engine.</summary>
    Unavailable,
    /// <summary>Keine Kopfzeilen, keine erste Zeile, Stillstand.</summary>
    Timeout,
    /// <summary>Sonstiger Fehler.</summary>
    Error,
}

/// <summary>
/// Zählt die Läufe je Engine in 10-Minuten-Körben (<see cref="EngineThroughputBucket"/>), Singleton. Der Worker meldet
/// jedes Laufende hier (<see cref="Record"/>, nur Speicher, kein DB-Zugriff im Lauf); <c>EngineBrokerMaintenanceService</c>
/// schreibt die Zuwächse minütlich in die Datenbank (<see cref="FlushAsync"/>) und räumt dabei stündlich Körbe älter als
/// <see cref="EngineThroughputBucket.RetentionDays"/> Tage weg. Bis zum Schreiben sieht die Admin-Seite die Zuwächse über
/// <see cref="Pending"/> — ein Neustart verliert höchstens die letzte Minute.
/// </summary>
public sealed class EngineThroughputRecorder
{
    private readonly object _lock = new();
    private Dictionary<(string EngineId, DateTime Bucket), EngineThroughputBucket> _pending = new();
    private DateTime _lastPrune = DateTime.MinValue;
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);

    /// <summary>Beginn des Korbs, in den <paramref name="utc"/> fällt.</summary>
    public static DateTime BucketOf(DateTime utc)
    {
        var ticks = TimeSpan.FromMinutes(EngineThroughputBucket.BucketMinutes).Ticks;
        return new DateTime(utc.Ticks - utc.Ticks % ticks, DateTimeKind.Utc);
    }

    /// <summary>Ein Lauf ist zu Ende. <paramref name="doneJobSeconds"/> = gesamte Rechenzeit des Auftrags, nur bei
    /// <see cref="EngineRunOutcome.Done"/>; <paramref name="nodes"/> = Knoten der letzten Zeile (0 = keine gemeldet).</summary>
    /// <param name="searchSeconds">Suchzeit laut Engine (Knoten ÷ gemeldetes nps der letzten Zeile, 0.953.0) — geht statt der
    /// Wanduhr in <c>NodeSeconds</c> ein, damit kN/s nicht die Wartezeit auf die Kopfzeilen, den Start der Engine und das
    /// Hochladen mitzählt. <c>null</c> (kein nps gemeldet) = Wanduhr wie bisher.</param>
    public void Record(string engineId, EngineRunOutcome outcome, double runSeconds, long nodes, int doneJobSeconds, DateTime nowUtc,
        double? searchSeconds = null)
    {
        if (string.IsNullOrEmpty(engineId)) return;
        var seconds = (long)Math.Max(0, Math.Round(runSeconds));
        lock (_lock)
        {
            var key = (engineId, BucketOf(nowUtc));
            if (!_pending.TryGetValue(key, out var b))
                _pending[key] = b = new EngineThroughputBucket { EngineId = engineId, BucketStart = key.Item2 };
            b.Runs++;
            switch (outcome)
            {
                case EngineRunOutcome.Done: b.Done++; b.DoneJobSeconds += Math.Max(0, doneJobSeconds); break;
                case EngineRunOutcome.Failed: b.Failed++; break;
                case EngineRunOutcome.Interrupted: b.Interrupted++; break;
                case EngineRunOutcome.Retried: b.Retried++; break;
                case EngineRunOutcome.Unavailable: b.Unavailable++; break;
                case EngineRunOutcome.Timeout: b.Timeouts++; break;
                default: b.Errors++; break;
            }
            b.RunSeconds += seconds;
            if (nodes > 0)
            {
                b.Nodes += nodes;
                b.NodeSeconds += searchSeconds is > 0 ? Math.Max(1, (long)Math.Round(searchSeconds.Value)) : seconds;
            }
        }
    }

    /// <summary>Noch nicht geschriebene Zuwächse (Kopien).</summary>
    public IReadOnlyList<EngineThroughputBucket> Pending()
    {
        lock (_lock) return _pending.Values.Select(Copy).ToList();
    }

    /// <summary>Zuwächse in die Datenbank addieren (eine Zeile je Engine und Korb). Scheitert das Schreiben, kommen sie
    /// zurück in den Speicher und gehen beim nächsten Mal mit. Danach höchstens stündlich alte Körbe weg.</summary>
    public async Task FlushAsync(AppDbContext db, DateTime nowUtc, CancellationToken ct)
    {
        Dictionary<(string, DateTime), EngineThroughputBucket> batch;
        lock (_lock)
        {
            batch = _pending;
            _pending = new();
        }
        if (batch.Count > 0)
        {
            try
            {
                var engines = batch.Keys.Select(k => k.Item1).Distinct().ToList();
                var oldest = batch.Keys.Min(k => k.Item2);
                var existing = await db.EngineThroughputBuckets
                    .Where(b => engines.Contains(b.EngineId) && b.BucketStart >= oldest)
                    .ToListAsync(ct);
                var byKey = existing.ToDictionary(b => (b.EngineId, b.BucketStart));
                foreach (var (key, add) in batch)
                {
                    if (byKey.TryGetValue(key, out var row)) Add(row, add);
                    else db.EngineThroughputBuckets.Add(Copy(add));
                }
                await db.SaveChangesAsync(ct);
            }
            catch
            {
                db.ChangeTracker.Clear();
                lock (_lock)
                {
                    foreach (var (key, add) in batch)
                    {
                        if (_pending.TryGetValue(key, out var cur)) Add(cur, add);
                        else _pending[key] = add;
                    }
                }
                throw;
            }
        }

        if (nowUtc - _lastPrune >= PruneInterval)
        {
            _lastPrune = nowUtc;
            var cutoff = nowUtc.AddDays(-EngineThroughputBucket.RetentionDays);
            var old = await db.EngineThroughputBuckets.Where(b => b.BucketStart < cutoff).ToListAsync(ct);
            if (old.Count > 0)
            {
                db.EngineThroughputBuckets.RemoveRange(old);
                await db.SaveChangesAsync(ct);
            }
        }
    }

    internal static void Add(EngineThroughputBucket into, EngineThroughputBucket add)
    {
        into.Runs += add.Runs;
        into.Done += add.Done;
        into.Failed += add.Failed;
        into.Interrupted += add.Interrupted;
        into.Retried += add.Retried;
        into.Unavailable += add.Unavailable;
        into.Timeouts += add.Timeouts;
        into.Errors += add.Errors;
        into.RunSeconds += add.RunSeconds;
        into.DoneJobSeconds += add.DoneJobSeconds;
        into.Nodes += add.Nodes;
        into.NodeSeconds += add.NodeSeconds;
    }

    private static EngineThroughputBucket Copy(EngineThroughputBucket b)
    {
        var c = new EngineThroughputBucket { EngineId = b.EngineId, BucketStart = b.BucketStart };
        Add(c, b);
        return c;
    }
}
