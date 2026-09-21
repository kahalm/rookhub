using RookHub.Api.DTOs;

namespace RookHub.Api.Services;

/// <summary>
/// Die ARITHMETIK hinter den Versuchs-Statistiken — einmal, für Standard-Puzzles
/// (<see cref="PuzzleStatsService"/>) und Kurs-Linien (<see cref="CourseStatsService"/>).
///
/// <para>Die beiden Dienste fragen VERSCHIEDENE Tabellen ab (<c>PuzzleAttempts</c> gegen
/// <c>CourseAttempts</c>, Themen aus <c>PuzzleTags</c> gegen den <c>BookPuzzle.Tags</c>-String) —
/// die EF-Abfragen bleiben deshalb je Dienst. Gleich waren immer nur die Rechenschritte danach:
/// Serien aus den letzten Versuchen, Trefferquote auf eine Nachkommastelle, 200er-Rating-Bänder,
/// Aktivität der letzten 365 Tage, Themen-Top-20. Die standen zweimal ausgeschrieben da, und eine
/// Korrektur an einer Stelle (etwa die Rundung oder die Sortier-Zweitstufe) wäre in der anderen
/// still nicht angekommen.</para>
///
/// Reine Funktionen ohne DbContext: einzeln prüfbar (<c>AttemptStatsTests</c>), ohne Datenbank.
/// </summary>
public static class AttemptStats
{
    /// <summary>Die Serien zu einer Ergebnisliste in Reihenfolge NEUESTER ZUERST:
    /// <c>Current</c> = ununterbrochene Lösungen ab dem jüngsten Versuch (der erste Fehlversuch
    /// beendet sie), <c>Best</c> = längster Lauf irgendwo in der Liste. Beide 0 bei leerer Liste.
    /// <para>Die Reihenfolge ist Teil des Vertrags — mit ältester zuerst wäre <c>Current</c> die
    /// Serie von damals. Für <c>Best</c> ist sie egal (der längste Lauf ist symmetrisch).</para></summary>
    public static (int Current, int Best) Streaks(IReadOnlyList<bool> solvedNewestFirst)
    {
        var current = 0;
        foreach (var s in solvedNewestFirst)
        {
            if (s) current++;
            else break;
        }

        var best = 0;
        var run = 0;
        foreach (var s in solvedNewestFirst)
        {
            if (s) { run++; best = Math.Max(best, run); }
            else run = 0;
        }
        return (current, best);
    }

    /// <summary>Trefferquote in PROZENT mit einer Nachkommastelle; 0 bei <paramref name="total"/> = 0
    /// (eine Division wäre dort keine Aussage, sondern ein Wurf).</summary>
    public static double Accuracy(int solved, int total)
        => total == 0 ? 0 : Math.Round((double)solved / total * 100, 1);

    /// <summary>200er-Rating-Bänder aus den (Bucket, Versuche, Gelöst)-Gruppen der Datenbank —
    /// sortiert und auf die Grenzen <c>bucket*200 … +199</c> ausgerechnet. Der Bucket kommt aus
    /// einer Ganzzahl-Division im SQL (<c>Rating / 200</c>), die Sortierung erst hier: die
    /// Gruppierung selbst liefert keine verlässliche Reihenfolge.</summary>
    public static List<RatingBandStatDto> RatingBands(IEnumerable<(int Bucket, int Attempts, int Solved)> buckets)
        => buckets
            .OrderBy(b => b.Bucket)
            .Select(b => new RatingBandStatDto
            {
                From = b.Bucket * 200,
                To = b.Bucket * 200 + 199,
                Attempts = b.Attempts,
                Solved = b.Solved,
            })
            .ToList();

    /// <summary>Aktivität je Tag aus den (Tag, Anzahl)-Gruppen der Datenbank — chronologisch
    /// sortiert, Datum als <c>yyyy-MM-dd</c> (die Heatmap des Frontends liest genau dieses Format).</summary>
    public static List<ActivityDayDto> Activity(IEnumerable<(DateTime Day, int Count)> days)
        => days
            .OrderBy(x => x.Day)
            .Select(x => new ActivityDayDto { Date = x.Day.ToString("yyyy-MM-dd"), Count = x.Count })
            .ToList();

    /// <summary>Beginn des Aktivitäts-Fensters: der Tagesanfang vor 364 Tagen — zusammen mit heute
    /// sind das 365 Tage. Als eigene Funktion, weil das <c>-364</c> beim zweiten Abschreiben leicht
    /// zu <c>-365</c> wird und die Heatmap dann eine Spalte zu breit ist.</summary>
    public static DateTime ActivityWindowStart(DateTime nowUtc) => nowUtc.Date.AddDays(-364);

    /// <summary>Die häufigsten Themen: nach Versuchen absteigend, bei Gleichstand nach Namen —
    /// ohne die zweite Stufe hinge die Reihenfolge gleich häufiger Themen an der Datenbank und
    /// die Liste sähe bei jedem Aufruf anders aus.</summary>
    public static List<ThemeStatDto> TopThemes(IEnumerable<ThemeStatDto> themes, int take = 20)
        => themes
            .OrderByDescending(t => t.Attempts).ThenBy(t => t.Theme)
            .Take(take)
            .ToList();
}
