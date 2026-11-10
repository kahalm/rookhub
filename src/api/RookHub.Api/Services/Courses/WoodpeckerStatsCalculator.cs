using RookHub.Api.DTOs.Courses;

namespace RookHub.Api.Services.Courses;

/// <summary>
/// Ausführliche Woodpecker-Statistik (0.957.0, Wunsch 10.11.: „du weißt bei jedem Puzzle, wie lang ich in jeder Stufe
/// gebraucht hab — momentan 1 h für 50 Puzzle, dieselben Puzzle haben letztes Mal 2 h gebraucht“). Rein und statisch:
/// der Dienst lädt die Versuche des Plans in EINER Abfrage, hier wird im Speicher verbunden (≤ ~1 100 Aufgaben × 7 Zyklen).
///
/// <para><b>Gleiche-Aufgaben-Vergleich</b>: laufender Zyklus N gegen jeden früheren k auf der Schnittmenge über
/// <c>BookPuzzleId</c> (Aufgaben, die in N schon bearbeitet sind UND in k einen Versuch haben). Anteil
/// r = Zeit_N / Zeit_k (0,5 = halbe Zeit, das Buchziel), Faktor = Zeit_k / Zeit_N.</para>
/// <para><b>Hochrechnung</b> (nur gegen N−1, ab <see cref="MinForecastSample"/> gemeinsamen Aufgaben): Rest = r × Zeit von
/// N−1 auf den noch offenen Aufgaben des Satzes (offene ohne Versuch in N−1: Ø jetzt); gesamt = bisher + Rest
/// (≈ r × Gesamtzeit von N−1).</para>
/// </summary>
public static class WoodpeckerStatsCalculator
{
    /// <summary>Buchziel: jeder Zyklus in der halben Zeit.</summary>
    public const double GoalRatio = 0.5;
    /// <summary>Erst ab so vielen gemeinsamen Aufgaben wird hochgerechnet — darunter ist r Zufall.</summary>
    public const int MinForecastSample = 10;
    /// <summary>Höchstzahl der Sorgenkinder je Liste.</summary>
    public const int TroubleLimit = 10;
    /// <summary>Zeitstufen je Aufgabe: schnell unter 30 s, lang über 2 min, dazwischen mittel.</summary>
    public const int FastSeconds = 30;
    public const int SlowSeconds = 120;
    /// <summary>„Deutlich“ schneller/langsamer als damals: mindestens eine Minute Unterschied.</summary>
    public const int BigChangeSeconds = 60;

    /// <summary>
    /// Stichtag der Tipp-Daten: <c>WoodpeckerAttempts.HintsUsed</c> gibt es erst seit 0.956.0 (Migration
    /// <c>WoodpeckerAttemptHintsUsed</c>, Prod-API läuft damit seit 10.11.2026 17:33 UTC, Frontend 17:33:46 UTC). Ältere
    /// Versuche stehen auf 0 — auch die in Zyklus 1 aus Kurs-Versuchen übernommenen, denn sie wurden vor diesem Tag
    /// übernommen. Ein Versuch davor hat also KEINE belastbare Tipp-Zahl und zählt in keiner Tipp-Auswertung.
    /// (Ein Gerät mit noch altem Frontend schickt kurz nach dem Stichtag ggf. noch kein <c>hintsUsed</c> — nicht
    /// unterscheidbar, hingenommen.)
    /// </summary>
    public static readonly DateTime HintsTrackedSince = new(2026, 11, 10, 17, 34, 0, DateTimeKind.Utc);

    /// <summary>Hat dieser Versuch eine belastbare Tipp-Zahl?</summary>
    public static bool HintsTracked(Attempt a) => a.AttemptedAt >= HintsTrackedSince;

    public sealed record Attempt(int Cycle, int BookPuzzleId, int BookId, bool Solved, int TimeSeconds, int HintsUsed, DateTime AttemptedAt);

    public sealed record Cycle(int Number, DateTime StartedAt, DateTime? FinishedAt);

    /// <param name="cycles">Alle Zyklen des Plans (Reihenfolge egal); der mit der höchsten Nummer ist der laufende.</param>
    /// <param name="attempts">Alle gewerteten Versuche des Plans (je Zyklus höchstens einer je Aufgabe).</param>
    /// <param name="setPuzzleIds">Die Aufgaben des Satzes (für „offen“ in der Hochrechnung).</param>
    /// <param name="nowUtc">Jetzt (für die Dauer des laufenden Zyklus).</param>
    /// <param name="utcOffsetMinutes">Zeitzone des Betrachters für die Tageszeilen (Minuten östlich von UTC).</param>
    public static WoodpeckerStatsDto Compute(int planId, IReadOnlyCollection<Cycle> cycles, IReadOnlyCollection<Attempt> attempts,
        IReadOnlyCollection<int> setPuzzleIds, DateTime nowUtc, int utcOffsetMinutes = 0)
    {
        var ordered = cycles.OrderBy(c => c.Number).ToList();
        var dto = new WoodpeckerStatsDto { PlanId = planId, SetSize = setPuzzleIds.Count, HintsTrackedSince = HintsTrackedSince };
        if (ordered.Count == 0) return dto;
        var n = ordered[^1].Number;
        dto.CurrentCycle = n;

        // Je Zyklus: Aufgabe → Versuch (Doppel kann es wegen des Unique-Index nicht geben; sonst zählt der erste).
        var byCycle = attempts.GroupBy(a => a.Cycle).ToDictionary(g => g.Key,
            g => g.OrderBy(a => a.AttemptedAt).GroupBy(a => a.BookPuzzleId).ToDictionary(x => x.Key, x => x.First()));
        var empty = new Dictionary<int, Attempt>();
        var now = byCycle.GetValueOrDefault(n) ?? empty;

        foreach (var c in ordered)
        {
            var list = (byCycle.GetValueOrDefault(c.Number) ?? empty).Values.ToList();
            var row = new WoodpeckerStatsCycleDto
            {
                Number = c.Number,
                TargetDays = SafeTargetDays(c.Number),
                StartedAt = c.StartedAt,
                FinishedAt = c.FinishedAt,
                Attempted = list.Count,
                Solved = list.Count(a => a.Solved),
                SolvedClean = list.Count(a => a.Solved && a.HintsUsed == 0),
                Hinted = list.Count(a => a.HintsUsed > 0),
                TimeSeconds = list.Sum(a => a.TimeSeconds),
                DaysUsed = Math.Round(Math.Max(0, ((c.FinishedAt ?? nowUtc) - c.StartedAt).TotalDays), 2),
                HintsTrackedCount = list.Count(HintsTracked),
            };
            if (row.Attempted > 0)
            {
                row.RatePct = 100.0 * row.Solved / row.Attempted;
                row.AvgSeconds = (double)row.TimeSeconds / row.Attempted;
            }
            if (c.Number < n) row.SameAsNow = Overlap(c.Number, now, byCycle.GetValueOrDefault(c.Number) ?? empty);
            dto.Cycles.Add(row);
        }

        var prev = byCycle.GetValueOrDefault(n - 1);
        if (n > 1 && prev != null)
        {
            dto.Changes = Changes(n - 1, now, prev);
            dto.Forecast = Forecast(n - 1, now, prev, setPuzzleIds);
            dto.HintChanges = HintChanges(n - 1, now, prev);
        }
        dto.Hints = Hints(now.Values);
        dto.TimeBuckets = Buckets(now.Values.Select(a => a.TimeSeconds));

        var offset = TimeSpan.FromMinutes(Math.Clamp(utcOffsetMinutes, -14 * 60, 14 * 60));
        dto.Days = now.Values
            .GroupBy(a => (a.AttemptedAt + offset).Date)
            .OrderBy(g => g.Key)
            .Select(g => new WoodpeckerStatsDayDto
            {
                Date = g.Key.ToString("yyyy-MM-dd"),
                Attempted = g.Count(),
                Solved = g.Count(a => a.Solved),
                TimeSeconds = g.Sum(a => a.TimeSeconds),
            })
            .ToList();

        dto.Slowest = now.Values
            .OrderByDescending(a => a.TimeSeconds).ThenBy(a => a.BookPuzzleId)
            .Take(TroubleLimit)
            .Select(a => new WoodpeckerTroubleDto
            {
                BookPuzzleId = a.BookPuzzleId, BookId = a.BookId, SecondsNow = a.TimeSeconds, SolvedNow = a.Solved,
                SecondsPrev = prev != null && prev.TryGetValue(a.BookPuzzleId, out var p) ? p.TimeSeconds : null,
                Cycles = byCycle.Count(kv => kv.Value.ContainsKey(a.BookPuzzleId)),
            })
            .ToList();

        // Sorgenkinder „immer verfehlt“: in mindestens zwei Zyklen bearbeitet, in keinem gelöst.
        var missed = byCycle.Values.SelectMany(d => d.Values)
            .GroupBy(a => a.BookPuzzleId)
            .Where(g => g.Count() >= 2 && g.All(a => !a.Solved))
            .Select(g => new
            {
                Id = g.Key, g.First().BookId, Count = g.Count(), Total = g.Sum(a => a.TimeSeconds),
                Now = now.TryGetValue(g.Key, out var x) ? x : null,
                Prev = prev != null && prev.TryGetValue(g.Key, out var y) ? y : null,
            })
            .OrderByDescending(m => m.Count).ThenByDescending(m => m.Total).ThenBy(m => m.Id)
            .ToList();
        dto.AlwaysMissedCount = missed.Count;
        dto.AlwaysMissed = missed.Take(TroubleLimit).Select(m => new WoodpeckerTroubleDto
        {
            BookPuzzleId = m.Id, BookId = m.BookId, SecondsNow = m.Now?.TimeSeconds, SolvedNow = m.Now?.Solved,
            SecondsPrev = m.Prev?.TimeSeconds, Cycles = m.Count,
        }).ToList();

        return dto;
    }

    /// <summary>Vergleich auf der Schnittmenge: Aufgaben, die in <paramref name="now"/> UND in <paramref name="then"/> einen Versuch haben.</summary>
    public static WoodpeckerOverlapDto Overlap(int cycle, IReadOnlyDictionary<int, Attempt> now, IReadOnlyDictionary<int, Attempt> then)
    {
        var o = new WoodpeckerOverlapDto { Cycle = cycle };
        foreach (var (id, a) in now)
        {
            if (!then.TryGetValue(id, out var b)) continue;
            o.Count++;
            o.SecondsNow += a.TimeSeconds;
            o.SecondsThen += b.TimeSeconds;
            if (a.Solved) o.SolvedNow++;
            if (b.Solved) o.SolvedThen++;
            if (a.HintsUsed > 0) o.HintedNow++;
            if (b.HintsUsed > 0) o.HintedThen++;
        }
        if (o.Count > 0)
        {
            o.RatePctNow = 100.0 * o.SolvedNow / o.Count;
            o.RatePctThen = 100.0 * o.SolvedThen / o.Count;
        }
        if (o.SecondsThen > 0) o.TimeRatio = (double)o.SecondsNow / o.SecondsThen;
        if (o.SecondsNow > 0) o.SpeedFactor = (double)o.SecondsThen / o.SecondsNow;
        return o;
    }

    private static WoodpeckerChangesDto? Changes(int cycle, IReadOnlyDictionary<int, Attempt> now, IReadOnlyDictionary<int, Attempt> then)
    {
        var c = new WoodpeckerChangesDto { Cycle = cycle };
        var any = false;
        foreach (var (id, a) in now)
        {
            if (!then.TryGetValue(id, out var b)) continue;
            any = true;
            if (a.Solved && b.Solved) c.BothSolved++;
            else if (a.Solved) c.Improved++;
            else if (b.Solved) c.Worsened++;
            else c.BothMissed++;
            var diff = b.TimeSeconds - a.TimeSeconds;   // > 0 = jetzt schneller
            if (diff > 0) { c.Faster++; c.SavedSeconds += diff; if (diff >= BigChangeSeconds) c.MuchFaster++; }
            else if (diff < 0) { c.Slower++; c.LostSeconds -= diff; if (-diff >= BigChangeSeconds) c.MuchSlower++; }
            else c.SameTime++;
            Add(c.BucketsNow, a.TimeSeconds);
            Add(c.BucketsThen, b.TimeSeconds);
        }
        return any ? c : null;
    }

    /// <summary>Zeitstufen einer Menge von Lösezeiten (schnell &lt; <see cref="FastSeconds"/>, lang &gt; <see cref="SlowSeconds"/>).</summary>
    public static WoodpeckerTimeBucketsDto Buckets(IEnumerable<int> seconds)
    {
        var b = new WoodpeckerTimeBucketsDto();
        foreach (var s in seconds) Add(b, s);
        return b;
    }

    private static void Add(WoodpeckerTimeBucketsDto b, int seconds)
    {
        if (seconds < FastSeconds) b.Fast++;
        else if (seconds > SlowSeconds) b.Slow++;
        else b.Medium++;
    }

    /// <summary>Tipp-Stufen der Versuche ab dem Stichtag; ältere zählen nur als <c>Untracked</c>.</summary>
    public static WoodpeckerHintBreakdownDto Hints(IEnumerable<Attempt> attempts)
    {
        var h = new WoodpeckerHintBreakdownDto();
        foreach (var a in attempts)
        {
            if (!HintsTracked(a)) { h.Untracked++; continue; }
            h.Tracked++;
            if (!a.Solved) { if (a.HintsUsed > 0) h.MissedWithHint++; else h.MissedNoHint++; continue; }
            switch (a.HintsUsed)
            {
                case <= 0: h.SolvedNoHint++; break;
                case 1: h.SolvedHint1++; break;
                case 2: h.SolvedHint2++; break;
                default: h.SolvedHint3++; break;
            }
        }
        return h;
    }

    /// <summary>Tipps je gemeinsamer Aufgabe — nur, wo BEIDE Versuche ab dem Stichtag liegen; sonst <c>null</c>.</summary>
    private static WoodpeckerHintChangesDto? HintChanges(int cycle, IReadOnlyDictionary<int, Attempt> now, IReadOnlyDictionary<int, Attempt> then)
    {
        var c = new WoodpeckerHintChangesDto { Cycle = cycle };
        foreach (var (id, a) in now)
        {
            if (!then.TryGetValue(id, out var b) || !HintsTracked(a) || !HintsTracked(b)) continue;
            c.Count++;
            c.SumNow += a.HintsUsed;
            c.SumThen += b.HintsUsed;
            if (a.HintsUsed > b.HintsUsed) c.More++;
            else if (a.HintsUsed < b.HintsUsed) c.Fewer++;
            else c.Same++;
        }
        return c.Count > 0 ? c : null;
    }

    private static WoodpeckerForecastDto? Forecast(int basis, IReadOnlyDictionary<int, Attempt> now,
        IReadOnlyDictionary<int, Attempt> prev, IReadOnlyCollection<int> setPuzzleIds)
    {
        var o = Overlap(basis, now, prev);
        if (o.Count < MinForecastSample || o.TimeRatio is not { } r) return null;
        var spent = now.Values.Sum(a => a.TimeSeconds);
        var avgNow = now.Count > 0 ? (double)spent / now.Count : 0;
        var open = setPuzzleIds.Distinct().Where(id => !now.ContainsKey(id)).ToList();
        var rest = open.Sum(id => prev.TryGetValue(id, out var p) ? r * p.TimeSeconds : avgNow);
        var remaining = (int)Math.Round(rest);
        return new WoodpeckerForecastDto
        {
            BasisCycle = basis,
            Sample = o.Count,
            TimeRatio = r,
            BasisTotalSeconds = prev.Values.Sum(a => a.TimeSeconds),
            SpentSeconds = spent,
            OpenCount = open.Count,
            RemainingSeconds = remaining,
            ProjectedTotalSeconds = spent + remaining,
            GoalRatio = GoalRatio,
            GoalReached = r <= GoalRatio,
        };
    }

    private static int SafeTargetDays(int number) =>
        number is >= 1 and <= WoodpeckerRules.MaxCycles ? WoodpeckerRules.TargetDays(number) : 0;
}
