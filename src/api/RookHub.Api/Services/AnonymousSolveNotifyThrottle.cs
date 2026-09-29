namespace RookHub.Api.Services;

/// <summary>
/// Entprellt den schach-bot-Webhook für ANONYME Buch-Puzzle-Solves (Review-Fund A2-003): höchstens EINE
/// Meldung je Puzzle und <see cref="DefaultWindow"/>. Was in ein laufendes Fenster fällt, geht nicht verloren,
/// sondern wird zu genau EINER Nachmeldung am Fensterende gebündelt — der Worker liest die Löser erst beim
/// Ausführen (<c>GetResultsAsync</c>), die Nachmeldung trägt also jeden Solve dazwischen, und der Discord-Post
/// steht spätestens ein Fenster später auf dem richtigen Stand (der Bot pollt nicht, er lebt vom Webhook).
///
/// <para>Warum: <c>POST /api/book-puzzles/{id}/attempt/anonymous</c> nimmt je neuer Session-Id einen Solve an.
/// Vorher hieß jede davon eine volle Löser-Aggregation + ein HMAC-Webhook, und der Bot editiert beim
/// Tagespuzzle jeden gemerkten Discord-Post — ein Skript mit frischen UUIDs trieb ihn damit in Discords
/// Rate-Limit und füllte die Webhook-Queue (FullMode.Wait), hinter der auch die Meldungen echter Löser warten.
/// Eingeloggte Versuche melden weiter sofort (Namen/Erwähnungen).</para>
///
/// <para>Singleton; Zustand nur im Arbeitsspeicher — ein Neustart vergisst höchstens eine Nachmeldung.</para>
/// </summary>
public sealed class AnonymousSolveNotifyThrottle
{
    /// <summary>Höchstens eine Meldung je Puzzle in diesem Zeitraum.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(30);

    /// <summary>Ab so vielen gemerkten Puzzles werden abgelaufene Einträge weggeräumt.</summary>
    private const int PruneThreshold = 1024;

    private readonly ILogger<AnonymousSolveNotifyThrottle>? _logger;
    private readonly TimeProvider _time;
    private readonly TimeSpan _window;
    private readonly object _gate = new();
    private readonly Dictionary<int, DateTimeOffset> _lastSent = new();
    private readonly HashSet<int> _trailing = new();

    // Alle Parameter optional: DI setzt nur den Logger, Tests zusätzlich Uhr und Fenster.
    public AnonymousSolveNotifyThrottle(ILogger<AnonymousSolveNotifyThrottle>? logger = null,
        TimeProvider? time = null, TimeSpan? window = null)
    {
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _window = window ?? DefaultWindow;
    }

    /// <summary>
    /// <c>true</c> = der Aufrufer meldet JETZT. <c>false</c> = das Fenster läuft noch; dann ist genau EINE
    /// Nachmeldung eingeplant (von diesem oder einem früheren Aufruf), die am Fensterende
    /// <paramref name="notifyLater"/> ruft. Der Rückruf darf nichts aus dem Request-Scope festhalten.
    /// </summary>
    public bool TryNotifyNow(int puzzleId, Func<ValueTask> notifyLater)
    {
        TimeSpan delay;
        lock (_gate)
        {
            if (_trailing.Contains(puzzleId)) return false;
            var now = _time.GetUtcNow();
            if (!_lastSent.TryGetValue(puzzleId, out var last) || now - last >= _window)
            {
                _lastSent[puzzleId] = now;
                if (_lastSent.Count > PruneThreshold) Prune(now);
                return true;
            }
            _trailing.Add(puzzleId);
            delay = last + _window - now;
        }
        _ = FireLaterAsync(puzzleId, delay, notifyLater);
        return false;
    }

    private async Task FireLaterAsync(int puzzleId, TimeSpan delay, Func<ValueTask> notifyLater)
    {
        try
        {
            await Task.Delay(delay, _time);
        }
        finally
        {
            // Vor dem Melden freigeben: ein Solve NACH diesem Punkt ist in der Nachmeldung evtl. nicht mehr
            // enthalten und plant deshalb die nächste ein.
            lock (_gate)
            {
                _trailing.Remove(puzzleId);
                _lastSent[puzzleId] = _time.GetUtcNow();
            }
        }
        try
        {
            await notifyLater();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SchachBot-Nachmeldung fuer anonyme Solves fehlgeschlagen (puzzleId={PuzzleId})", puzzleId);
        }
    }

    /// <summary>Räumt Puzzles weg, deren Fenster abgelaufen ist und für die nichts eingeplant ist.</summary>
    private void Prune(DateTimeOffset now)
    {
        foreach (var id in _lastSent.Where(kv => now - kv.Value >= _window && !_trailing.Contains(kv.Key))
                     .Select(kv => kv.Key).ToList())
            _lastSent.Remove(id);
    }
}
