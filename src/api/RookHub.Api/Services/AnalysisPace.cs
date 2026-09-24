namespace RookHub.Api.Services;

/// <summary>
/// Tempo der Hintergrund-Analyse aus den Zeitstempeln fertiger Stellungen (<c>GameAnalysisPosition.AnalyzedAt</c>) —
/// EINE Regel fuer die Tempo-Zeile der Partie-Analysen (<see cref="GameAnalysisService.ThroughputAsync"/>) und die
/// Restdauer einer einzelnen Partie (<see cref="GameEvals.EtaMinutes"/>). Rein, ohne Datenbank testbar.
///
/// <para><b>Gemessen wird nur der juengste ZUSAMMENHAENGENDE Lauf.</b> Liegen zwischen zwei Ergebnissen mehr als
/// <see cref="PauseGap"/>, war dazwischen Pause (nichts eingereiht, Engine aus, Nacht) — und die zaehlt nicht als
/// Rechenzeit. Bis 0.521.2 mittelte die Tempo-Zeile ueber die letzte Stunde ab dem ERSTEN Ergebnis darin: lag davor
/// eine Pause, stand dort „0,56 Stellungen/min · noch ca. 1 h 44 min" fuer 58 offene Stellungen, die in Wahrheit
/// in einer Viertelstunde durch waren (gemeldet 2026-09-24).</para>
///
/// <para>Gemessen wird bis JETZT, solange der Lauf noch lebt (letztes Ergebnis juenger als <see cref="PauseGap"/>):
/// bleibt die Engine haengen, waechst die Restdauer. Ist das letzte Ergebnis aelter, ruht die Analyse gerade (neu
/// eingereiht, noch nicht angelaufen) — dann gilt das Tempo, das der Lauf hatte, statt eine Pause von Stunden
/// als Rechenzeit zu werten.</para>
/// </summary>
public static class AnalysisPace
{
    /// <summary>Ab dieser Luecke zwischen zwei Ergebnissen gilt: Pause. Eine zaehe Stellung bei Tiefe 25–30 mit
    /// fuenf Linien kann auf einer einzelnen Engine einige Minuten brauchen — sie darf keinen Lauf zerschneiden.</summary>
    public static readonly TimeSpan PauseGap = TimeSpan.FromMinutes(15);

    /// <summary>Kuerzeste Spanne: die Pumpe holt Ergebnisse im 20-s-Takt und oft mehrere mit demselben Zeitstempel;
    /// ohne Untergrenze machten drei Ergebnisse binnen Sekunden ein Fantasietempo.</summary>
    public const double MinSpanSeconds = 60;

    /// <summary>Ein gemessener Lauf: so viele Stellungen in so vielen Sekunden.</summary>
    public readonly record struct Run(int Count, double Seconds)
    {
        public double PerMinute => Count / (Seconds / 60);
        public double SecondsPerPosition => Seconds / Count;
    }

    /// <summary>Der juengste zusammenhaengende Lauf aus hoechstens <paramref name="maxCount"/> Ergebnissen;
    /// <c>null</c> ohne Ergebnisse.</summary>
    public static Run? Measure(IEnumerable<DateTime> analyzedAt, DateTime now, int maxCount)
    {
        var newestFirst = analyzedAt.OrderByDescending(t => t).Take(maxCount).ToList();
        if (newestFirst.Count == 0) return null;

        var count = 1;
        while (count < newestFirst.Count && newestFirst[count - 1] - newestFirst[count] <= PauseGap) count++;

        var oldest = newestFirst[count - 1];
        var end = now - newestFirst[0] <= PauseGap ? now : newestFirst[0];
        return new Run(count, Math.Max(MinSpanSeconds, (end - oldest).TotalSeconds));
    }
}
