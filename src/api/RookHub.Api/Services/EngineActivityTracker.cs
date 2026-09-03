using System.Collections.Concurrent;

namespace RookHub.Api.Services;

/// <summary>
/// Buchführung über die laufenden LIVE-Analyse-Streams (Singleton) — mit zwei Blickwinkeln, weil zwei
/// verschiedene Fragen daran hängen:
///
/// <list type="bullet">
/// <item><b>je Nutzer</b>: Wie viele Streams hat er offen? (Deckel gegen hundert Tabs, <see cref="ActiveCount"/>)</item>
/// <item><b>je Engine</b>: Rechnet gerade jemand live darauf? Ein Stockfish-Prozess kann nur EINE Suche —
/// der Hintergrund-Worker muss deshalb genau den Auftrag pausieren, der auf DIESER Engine liegt, und darf
/// Aufträge auf anderen Engines ungestört weiterlaufen lassen.</item>
/// </list>
/// </summary>
public class EngineActivityTracker
{
    private readonly ConcurrentDictionary<int, int> _byUser = new();
    private readonly ConcurrentDictionary<string, int> _byEngine = new();
    private readonly ConcurrentDictionary<string, DateTime> _engineLastEnded = new();
    private readonly Func<DateTime> _now;

    public EngineActivityTracker() : this(() => DateTime.UtcNow) { }
    public EngineActivityTracker(Func<DateTime> now) { _now = now; }

    /// <summary>Feuert mit der Engine-ID, sobald dort der erste Live-Stream beginnt (nicht bei jedem weiteren).</summary>
    public event Action<string>? LiveStarted;

    /// <summary>Diagnose-Haken für Fehler eines <see cref="LiveStarted"/>-Abnehmers (Program.cs verdrahtet Logging).</summary>
    public event Action<Exception>? OnNotifyFailed;

    /// <summary>Live-Stream beginnt; liefert die neue Anzahl offener Streams DES NUTZERS (für den Deckel).</summary>
    public int Begin(int userId, string engineId)
    {
        var perUser = _byUser.AddOrUpdate(userId, 1, (_, c) => c + 1);
        var perEngine = _byEngine.AddOrUpdate(engineId, 1, (_, c) => c + 1);
        // Der Zähler ist hier schon erhöht: wirft ein Abnehmer, käme der Aufrufer nie zu seinem End() und
        // die Engine bliebe für immer „belegt" — ein Benachrichtigungsfehler darf die Buchführung nicht kippen.
        if (perEngine == 1)
        {
            try { LiveStarted?.Invoke(engineId); }
            catch (Exception ex) { OnNotifyFailed?.Invoke(ex); }
        }
        return perUser;
    }

    /// <summary>Live-Stream endet; auf 0 verschwindet der Eintrag (kein Wachstum über die Zeit).
    ///
    /// Entfernt wird WERT-bedingt (Schlüssel UND erwarteter Wert 0). Zwischen dem Herunterzählen und
    /// dem Entfernen kann ein <see cref="Begin"/> derselben Engine liegen — das Analysebrett bricht bei
    /// jedem Tiefen-/Linienwechsel Stream A ab und öffnet sofort B. Ein unbedingtes
    /// <c>TryRemove(key)</c> löschte dort den Eintrag des NEUEN Streams: die Engine sähe frei aus,
    /// der Worker startete daneben einen Hintergrund-Auftrag, und Stockfish bekäme zwei Suchen —
    /// genau die Invariante „ein Prozess, eine Suche", auf der der Vorrang der Live-Analyse beruht.</summary>
    public void End(int userId, string engineId)
    {
        _engineLastEnded[engineId] = _now();
        if (_byUser.AddOrUpdate(userId, 0, (_, c) => c - 1) <= 0)
            _byUser.TryRemove(new KeyValuePair<int, int>(userId, 0));
        if (_byEngine.AddOrUpdate(engineId, 0, (_, c) => c - 1) <= 0)
            _byEngine.TryRemove(new KeyValuePair<string, int>(engineId, 0));
    }

    /// <summary>Offene Live-Streams dieses Nutzers (Deckel im Controller).</summary>
    public int ActiveCount(int userId) => _byUser.TryGetValue(userId, out var n) ? n : 0;

    /// <summary>Rechnet gerade jemand live auf dieser Engine? Dann gehört sie ihm, nicht den Aufträgen.</summary>
    public bool IsEngineBusy(string engineId) => _byEngine.TryGetValue(engineId, out var n) && n > 0;

    /// <summary>Wie lange auf dieser Engine kein Live-Stream mehr offen ist (Zero, solange einer läuft;
    /// MaxValue, wenn dort nie einer lief) — der Worker wartet damit eine Ruhephase ab, statt sofort
    /// wieder loszurechnen, während der Nutzer noch zieht.</summary>
    public TimeSpan EngineIdleFor(string engineId)
    {
        if (IsEngineBusy(engineId)) return TimeSpan.Zero;
        return _engineLastEnded.TryGetValue(engineId, out var t) ? _now() - t : TimeSpan.MaxValue;
    }
}
