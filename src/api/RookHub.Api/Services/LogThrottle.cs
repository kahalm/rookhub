using System.Collections.Concurrent;

namespace RookHub.Api.Services;

/// <summary>
/// „Einmal je Schlüssel und Fenster laut, dazwischen leise" für Log-Zeilen, die ein Client beliebig oft
/// auslösen kann. Dasselbe Verfahren wie <c>PatScopeFenceMiddleware.ShouldWarn</c>, als eigene Klasse,
/// damit Stellen ohne Middleware-Instanz (die statischen Ereignisse in <see cref="JwtTokenGate"/>) es
/// nutzen können und es sich einzeln testen lässt.
///
/// <para>Bei echter Gleichzeitigkeit auf demselben Schlüssel kann die Update-Factory mehrfach laufen und
/// eine Zeile zu viel durchlassen — für eine Rausch-Drossel unkritisch, ein Lock wäre teurer als der
/// Schaden.</para>
/// </summary>
public sealed class LogThrottle
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _last = new();

    public LogThrottle(TimeSpan window, int maxEntries)
    {
        if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
        if (maxEntries < 1) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        Window = window;
        MaxEntries = maxEntries;
    }

    /// <summary>Höchstens eine laute Zeile je Schlüssel in diesem Abstand.</summary>
    public TimeSpan Window { get; }

    /// <summary>Harter Deckel der Tabelle — auch wenn innerhalb EINES Fensters mehr Schlüssel kommen
    /// (dann fliegen die ältesten raus; Kosten: für einen verworfenen Schlüssel höchstens eine laute
    /// Zeile mehr).</summary>
    public int MaxEntries { get; }

    /// <summary>Aktuelle Größe der Tabelle (für Tests).</summary>
    public int Count => _last.Count;

    /// <summary><c>true</c> für die erste Zeile je <paramref name="key"/> und danach je <see cref="Window"/>.</summary>
    public bool ShouldLog(string key, DateTimeOffset now)
    {
        var loud = false;
        _last.AddOrUpdate(
            key,
            _ => { loud = true; return now; },
            (_, last) =>
            {
                if (now - last < Window) return last;
                loud = true;
                return now;
            });

        if (_last.Count > MaxEntries) Prune(now);
        return loud;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (key, last) in _last)
            if (now - last >= Window)
                _last.TryRemove(key, out _);

        if (_last.Count <= MaxEntries) return;
        foreach (var entry in _last.OrderBy(e => e.Value).Take(_last.Count - MaxEntries))
            _last.TryRemove(entry.Key, out _);
    }
}
