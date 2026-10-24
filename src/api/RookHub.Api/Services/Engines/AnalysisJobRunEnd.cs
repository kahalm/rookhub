using RookHub.Api.Models;

namespace RookHub.Api.Services.Engines;

/// <summary>Wohin ein Lauf den Auftrag stellt.</summary>
internal enum RunEndKind { Done, Paused, Failed }

/// <summary>Was mit <see cref="AnalysisJob.FruitlessAttempts"/> geschieht, bevor der Status gesetzt wird.</summary>
internal enum FruitlessChange { Keep, Reset, Increment }

/// <summary>
/// Ein Statusübergang am Ende (oder Abbruch) eines Worker-Laufs, ohne Datenbank: Zielstatus, Meldung
/// (<see cref="AnalysisJob.LastError"/>), Wartezeit bis zum nächsten Anlauf und der Fehlversuchs-Zähler.
/// Angewandt von <see cref="AnalysisJobWorker"/>.
/// </summary>
internal sealed record RunOutcome(RunEndKind Kind, string? Error = null, TimeSpan? Backoff = null,
    FruitlessChange Fruitless = FruitlessChange.Keep)
{
    internal static RunOutcome Done(FruitlessChange fruitless = FruitlessChange.Reset) => new(RunEndKind.Done, Fruitless: fruitless);
    internal static RunOutcome Pause(string? error, TimeSpan? backoff = null, FruitlessChange fruitless = FruitlessChange.Keep)
        => new(RunEndKind.Paused, error, backoff, fruitless);
    internal static RunOutcome Fail(string error, FruitlessChange fruitless = FruitlessChange.Keep)
        => new(RunEndKind.Failed, error, null, fruitless);
}

/// <summary>Was über einen beendeten Stream-Lauf feststeht, wenn <see cref="AnalysisJobRunEnd.ClassifyRunEnd"/> entscheidet.</summary>
/// <param name="ReachedDepth">Gespeicherte Tiefe NACH dem letzten Sichern (frisch geladen).</param>
/// <param name="DepthAtStart">Gespeicherte Tiefe beim Start des Laufs (bei Knotenaufträgen 0).</param>
/// <param name="EngineFinished">Der Stream trug ein <c>bestmove</c>.</param>
/// <param name="PreviousNodes">Knoten des vorigen Laufs (Knotenauftrag), siehe <see cref="AnalysisJobStream.StoppedEarlyAgain"/>.</param>
/// <param name="DirectEngine">Engine hängt direkt an RookHub (<c>rhe_</c>).</param>
/// <param name="CancelledByUs">Der Lauf wurde von uns abgebrochen (Live-Vorrang, Löschen, Pause, Shutdown).</param>
/// <param name="LiveOnEngine">Auf der Engine läuft gerade eine Live-Analyse.</param>
/// <param name="GotData">Mindestens eine frische Datenzeile kam an.</param>
/// <param name="RunTime">Dauer des Laufs ab den Kopfzeilen.</param>
/// <param name="FruitlessMinRuntime">Ab dieser Laufzeit zählt „kein Fortschritt" nicht mehr als Fehlversuch.</param>
/// <param name="FruitlessAttempts">Bisherige Fehlversuche des Auftrags.</param>
internal readonly record struct RunFacts(
    long? TargetNodes, int TargetDepth, int ReachedDepth, int DepthAtStart,
    bool NodeGoalReached, long CurrentNodes, bool EngineFinished, long? PreviousNodes,
    bool DirectEngine, bool CancelledByUs, bool LiveOnEngine, bool GotData,
    TimeSpan RunTime, TimeSpan FruitlessMinRuntime, int FruitlessAttempts);

/// <summary>
/// Die Statusentscheidung des Auftrags-Workers als reine Funktionen (Zerlegung Z11): wie ein beendeter Stream
/// (<see cref="ClassifyRunEnd"/>) und ein stehengebliebener (<see cref="ClassifyStall"/>) den Auftrag stellen.
/// Die Antworten des Brokers selbst ordnet <see cref="BrokerFailurePolicy"/> ein.
/// </summary>
internal static class AnalysisJobRunEnd
{
    /// <summary>Wartezeit nach einem Stream, der vor dem Ziel endete.</summary>
    internal static readonly TimeSpan RetryAfterEarlyEnd = TimeSpan.FromSeconds(30);

    /// <summary>Hat die Engine einen Knotenauftrag VOR dem Ziel aus eigenem Ermessen beendet (Lc0 Smart Pruning)?
    /// Dann gilt das Ergebnis — siehe <see cref="AnalysisJobStream.EngineEndedOwnSearch"/>.</summary>
    internal static bool EndedOwnSearch(in RunFacts f)
        => f.TargetNodes is { } earlyGoal && !f.NodeGoalReached
           && !AnalysisJobStream.NodeGoalMet(earlyGoal, f.CurrentNodes, streamEnded: true)
           && AnalysisJobStream.EngineEndedOwnSearch(f.DirectEngine, f.EngineFinished, f.CancelledByUs,
               f.LiveOnEngine, f.PreviousNodes, f.CurrentNodes);

    /// <summary>Ziel erreicht? Knotenauftrag: Ziel gesehen, Engine hat selbst beendet, oder der Stream endete (nicht
    /// durch uns) knapp darunter. Tiefenauftrag: gespeicherte Tiefe ≥ Zieltiefe.</summary>
    internal static bool GoalMet(in RunFacts f)
        => f.TargetNodes is { } goalNodes
            ? f.NodeGoalReached || EndedOwnSearch(f)
              || (!f.CancelledByUs && f.GotData && AnalysisJobStream.NodeGoalMet(goalNodes, f.CurrentNodes, streamEnded: true))
            : f.ReachedDepth >= f.TargetDepth;

    /// <summary>
    /// Wie ein beendeter Stream den Auftrag stellt — in dieser Reihenfolge:
    /// <list type="number">
    /// <item>Ziel erreicht → fertig, Fehlversuche auf 0.</item>
    /// <item>Von uns abgebrochen (Live hatte Vorrang / gelöscht / Shutdown) → pausiert, ohne Meldung, kein Fehlversuch.</item>
    /// <item>Tiefer als beim Start → pausiert (30 s), Fehlversuche auf 0: es ging voran, einfach weiter.</item>
    /// <item>Live rechnet auf derselben Engine → pausiert (30 s), ohne Meldung, kein Fehlversuch: teilen sich Live und
    /// Hintergrund dieselbe Engine (nur eine registriert), verdrängt jede Live-Anfrage den laufenden Auftrag — der Stream
    /// endet dann von SELBST (nicht durch unser Cancel) und sähe wie ein Fehlschlag aus; nach drei solchen Runden hätte
    /// der Auftrag fälschlich als „gescheitert" gegolten.</item>
    /// <item>Lange gerechnet (≥ <see cref="RunFacts.FruitlessMinRuntime"/>) ohne tiefere Zeile → pausiert (30 s), kein
    /// Fehlversuch: keine Sackgasse, sondern eine Umgebung, die den Stream vorzeitig kappt — der klassische Fall ist der
    /// Wachhund des offiziellen Providers, der seinen „zuletzt benutzt"-Stempel erst am Stream-ENDE setzt und eine Suche,
    /// die länger als <c>--keep-alive</c> dauert, mitten im Rechnen terminiert (bei Tiefe 29+ mit 5 Linien der Normalfall;
    /// Abhilfe dort: KEEP_ALIVE hochsetzen). Sonst gälte ein gesunder Auftrag nach drei Runden als gescheitert (gesehen
    /// bei 29/40).</item>
    /// <item>Sonst — kein Fortschritt in KURZER Zeit, die deterministische Sackgasse (Matt-/Patt-Stellung, vom Provider
    /// abgelehnte Arbeit), dort endet der Stream sofort → Fehlversuch; ab <see cref="AnalysisJob.MaxFruitlessAttempts"/>
    /// gescheitert. Ohne Zähler liefe der Auftrag ewig im 30-s-Takt gegen dieselbe Wand.</item>
    /// </list>
    /// </summary>
    internal static RunOutcome ClassifyRunEnd(in RunFacts f)
    {
        if (GoalMet(f)) return RunOutcome.Done();
        if (f.CancelledByUs) return RunOutcome.Pause(null);
        if (f.ReachedDepth > f.DepthAtStart)
            return RunOutcome.Pause("Stream vor der Zieltiefe beendet", RetryAfterEarlyEnd, FruitlessChange.Reset);
        if (f.LiveOnEngine) return RunOutcome.Pause(null, RetryAfterEarlyEnd);
        if (f.RunTime >= f.FruitlessMinRuntime)
            return RunOutcome.Pause("Stream vorzeitig beendet — wird fortgesetzt", RetryAfterEarlyEnd);
        var attempts = f.FruitlessAttempts + 1;
        return attempts >= AnalysisJob.MaxFruitlessAttempts
            ? RunOutcome.Fail($"Engine lieferte in {attempts} Läufen sofort keine Bewertung", FruitlessChange.Increment)
            : RunOutcome.Pause("Stream vor der Zieltiefe beendet", RetryAfterEarlyEnd, FruitlessChange.Increment);
    }

    /// <summary>
    /// Der Stillstands-Wächter hat ausgelöst (keine frische Datenzeile in der Frist): ohne jede Datenzeile pausiert
    /// (5 min) — der Provider rechnet gar nicht; sonst ist die Engine stehen geblieben → Fehlversuch, ab
    /// <see cref="AnalysisJob.MaxFruitlessAttempts"/> gescheitert, darunter pausiert (60 s).
    /// </summary>
    internal static RunOutcome ClassifyStall(bool gotData, int fruitlessAttempts, int currentDepth)
    {
        if (!gotData) return RunOutcome.Pause("Engine lieferte keine Daten", TimeSpan.FromMinutes(5));
        var attempts = fruitlessAttempts + 1;
        return attempts >= AnalysisJob.MaxFruitlessAttempts
            ? RunOutcome.Fail($"Engine blieb in {attempts} Läufen stehen", FruitlessChange.Increment)
            : RunOutcome.Pause($"Engine blieb bei Tiefe {currentDepth} stehen", TimeSpan.FromSeconds(60), FruitlessChange.Increment);
    }
}
