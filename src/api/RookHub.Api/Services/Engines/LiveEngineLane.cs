namespace RookHub.Api.Services.Engines;

/// <summary>
/// Absprache zwischen dem <see cref="AnalysisJobWorker"/> und der Live-Bewertung der Live-Bretter (Singleton, 1.027.0, Wunsch
/// 2026-11-26: „kannst du die nicht einfach in die Hintergrundengines werfen?“). Die Live-Bretter rechnen auf den
/// HINTERGRUND-Engines — mit Vorrang vor gewöhnlichen Aufträgen, aber ohne einen laufenden Auftrag abzuwürgen:
/// <list type="bullet">
/// <item>Die Live-Seite nimmt nur eine Engine, auf der gerade KEIN Auftrag rechnet (<see cref="JobRunning"/>).</item>
/// <item>Solange Live-Stellungen warten, beansprucht sie die Engines (<see cref="Claim"/>): der Worker startet dort keinen NEUEN
///   Auftrag (<see cref="IsClaimed"/>) — wird eine frei, bekommt sie die Live-Stellung. Laufende Aufträge rechnen zu Ende.</item>
/// <item>Endet ein Auftrag, weckt der Worker die Live-Seite (<see cref="JobEnded"/>), damit sie nicht erst beim nächsten Takt
///   zugreift.</item>
/// </list>
/// Ein eigener Baustein statt gegenseitiger Abhängigkeit (Worker ↔ Live-Warteschlange wäre ein Kreis im DI).
/// </summary>
public sealed class LiveEngineLane
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();
    private volatile Func<string, bool>? _jobRunning;
    private volatile IReadOnlySet<string> _claimed = None;

    /// <summary>Der Worker meldet, wie man fragt, ob auf einer Engine gerade ein Auftrag rechnet.</summary>
    public void AttachWorker(Func<string, bool> isRunning) => _jobRunning = isRunning;

    /// <summary>Rechnet auf dieser Engine gerade ein Hintergrund-Auftrag?</summary>
    public bool JobRunning(string engineId) => _jobRunning?.Invoke(engineId) ?? false;

    /// <summary>Live-Seite: diese Engines will sie haben (leer = nichts wartet).</summary>
    public void Claim(IReadOnlySet<string> engineIds) => _claimed = engineIds.Count == 0 ? None : engineIds;

    /// <summary>Worker: hier keinen neuen Auftrag starten — die Live-Bretter warten auf die Engine.</summary>
    public bool IsClaimed(string engineId) => _claimed.Contains(engineId);

    /// <summary>Ein Auftrag ist zu Ende (Engine frei).</summary>
    public event Action? JobEnded;

    public void NotifyJobEnded()
    {
        try { JobEnded?.Invoke(); }
        catch { /* ein Weckruf darf den Worker nie stören */ }
    }
}
