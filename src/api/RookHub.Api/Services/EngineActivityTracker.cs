using System.Collections.Concurrent;

namespace RookHub.Api.Services;

/// <summary>
/// Zählt je User die offenen LIVE-Analyse-Streams über externe Engines (Singleton). Zwei Abnehmer:
/// der <c>EngineController</c> (Deckel gleichzeitiger Streams) und der <c>AnalysisJobWorker</c>, der
/// Hintergrund-Aufträge pausiert, sobald live gerechnet wird (<see cref="LiveStarted"/>), und sie erst
/// nach einer Ruhephase (<see cref="IdleFor"/>) fortsetzt — sonst konkurrierten zwei Stockfish-Prozesse
/// um dieselben Kerne und die Live-Analyse wäre nicht mehr „schnell".
/// </summary>
public class EngineActivityTracker
{
    private readonly ConcurrentDictionary<int, int> _active = new();
    private readonly ConcurrentDictionary<int, DateTime> _lastEnded = new();
    private readonly Func<DateTime> _now;

    public EngineActivityTracker() : this(() => DateTime.UtcNow) { }
    public EngineActivityTracker(Func<DateTime> now) { _now = now; }

    /// <summary>Feuert, wenn ein User von 0 auf 1 offene Live-Streams geht (nicht bei jedem weiteren).</summary>
    public event Action<int>? LiveStarted;

    /// <summary>Live-Stream beginnt; liefert die neue Anzahl offener Streams des Users.</summary>
    public int Begin(int userId)
    {
        var n = _active.AddOrUpdate(userId, 1, (_, c) => c + 1);
        if (n == 1) LiveStarted?.Invoke(userId);
        return n;
    }

    /// <summary>Live-Stream endet; auf 0 verschwindet der Eintrag (kein Wachstum über die Zeit).</summary>
    public void End(int userId)
    {
        _lastEnded[userId] = _now();
        if (_active.AddOrUpdate(userId, 0, (_, c) => c - 1) <= 0)
            _active.TryRemove(userId, out _);
    }

    public int ActiveCount(int userId) => _active.TryGetValue(userId, out var n) ? n : 0;

    public bool IsLiveActive(int userId) => ActiveCount(userId) > 0;

    /// <summary>Wie lange der User schon keinen Live-Stream mehr offen hat (Zero, solange einer läuft;
    /// MaxValue, wenn er nie einen hatte).</summary>
    public TimeSpan IdleFor(int userId)
    {
        if (IsLiveActive(userId)) return TimeSpan.Zero;
        return _lastEnded.TryGetValue(userId, out var t) ? _now() - t : TimeSpan.MaxValue;
    }
}
