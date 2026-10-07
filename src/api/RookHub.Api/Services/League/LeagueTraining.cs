using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services.League;

/// <summary>
/// Ein Prognose-Modell aus der Historie einer Region trainieren und rückwärts prüfen (2026-10-07, „eigenes Modell für Bayern").
/// Port von <c>~/claude/league-analyzer</c>: <c>features.dataset</c> (<see cref="Dataset"/>), <c>model.fit</c> (<see cref="Fit"/>,
/// logistische Regression, Newton, L2 = 1, höchstens 30 Schritte, Abbruch bei Schritt &lt; 1e-6), <c>export_weights.py</c>
/// (<see cref="ExportJson"/>) und <c>evaluate.py</c> (<see cref="Backtest"/>). Läuft im Wartungswerkzeug
/// (<c>tools/LibraryImport league-train</c>) — nie in der API, nie gegen Prod.
/// </summary>
public static class LeagueTraining
{
    /// <summary>Ein Mannschaftskampf aus Sicht eines Teams: alle gemeldeten Spieler mit ihren Merkmalen vor der Runde, dazu wer
    /// tatsächlich spielte (<see cref="Lineup"/>: Spieler → Brett; auch Spieler ohne Meldeliste).</summary>
    public sealed record Group(int Tnr, string Team, int Round, string Season, int Level, string Phase,
        List<FeatureRow> Rows, int[] Y, IReadOnlyDictionary<string, int> Lineup);

    /// <summary>
    /// Merkmale des bayerischen Modells (gewählt 2026-10-07 im Backtest 2022/23–2025/26): die Tiroler OHNE deren Stufen-Dummies
    /// (<c>lvl2</c>–<c>lvl4</c>, <c>gk_q</c> — Tiroler Ligen), dafür die Kreisebene (<c>kreis</c>, Stufe ≥ 5) und ihre
    /// Wechselwirkungen mit der Vorsaison-Quote (<c>kreis_q</c>), „unter den ersten B" (<c>kreis_top</c>) und dem Meldeplatz
    /// (<c>kreis_pos</c>) — in Zugspitzliga/A-/B-Klasse rücken Ersatzleute anders nach als in Ober- bis Bezirksliga. Reine
    /// Stufen-Konstanten (<c>lvl_n</c>, <c>lvl5</c>…) änderten am Backtest nichts: die Normierung je Mannschaftskampf hebt sie auf.
    /// </summary>
    public static readonly IReadOnlyList<string> BayernFeatures =
    [
        "const", "top", "bench", "pos_c", "q_same", "q_higher", "q_lower", "new_prev", "new_ever", "first", "q_same_first",
        "cur", "cur_n", "last", "last2", "yesterday", "yest_played", "yest_not", "conflict_hi", "conflict_lo",
        "kreis", "kreis_q", "kreis_top", "kreis_pos",
    ];

    /// <summary>Vorgabe der Merkmale je Region: Bayern <see cref="BayernFeatures"/>, sonst die des eingebetteten Tiroler Modells.</summary>
    public static IReadOnlyList<string> DefaultFeatures(string region) =>
        region == LeagueRegions.Bayern ? BayernFeatures : LeagueModel.FromEmbedded().Features;

    /// <summary>Lage der Prognose wie im Tiroler Backtest (evaluate.py): erste Runde, nach einem gestrigen Spiel, sonst.</summary>
    public static string PhaseOf(FeatureRow r) => r.First == 1 ? "R1" : r.Yesterday == 1 ? "So nach Sa" : "R2+";

    /// <summary>Welt einer Region aus der Datenbank (nur deren Ligen samt Runden, Begegnungen, Brettern, Meldelisten).</summary>
    public static async Task<LeagueWorld> LoadWorldAsync(AppDbContext db, string region, CancellationToken ct)
    {
        var ts = await db.LeagueTournaments.AsNoTracking().InRegion(region).ToListAsync(ct);
        var tnrs = ts.Select(t => t.Tnr).ToList();
        return new LeagueWorld(ts,
            await db.LeagueRounds.AsNoTracking().Where(r => tnrs.Contains(r.Tnr)).ToListAsync(ct),
            await db.LeagueMatches.AsNoTracking().Where(m => tnrs.Contains(m.Tnr)).ToListAsync(ct),
            await db.LeagueGames.AsNoTracking().Where(g => tnrs.Contains(g.Tnr)).ToListAsync(ct),
            await db.LeaguePlayers.AsNoTracking().Where(p => tnrs.Contains(p.Tnr)).ToListAsync(ct));
    }

    /// <summary>
    /// Trainingszeilen (Python: features.dataset): jede gespielte Runde jedes Teams jeder Liga (Stufe „Liga", nicht abgebrochen)
    /// der Region, je gemeldetem Spieler eine <see cref="FeatureRow"/> über <see cref="LeagueFeatures.RowsFor"/> mit dem Label
    /// „stand in der Aufstellung dieser Runde" (<see cref="LeagueWorld.LineupOf"/>).
    /// </summary>
    public static List<Group> Dataset(LeagueWorld w, string region, Func<string, bool>? season = null)
    {
        var groups = new List<Group>();
        foreach (var t in w.T.Values.OrderBy(t => t.Tnr))
        {
            if (t.Stage != "Liga" || t.Aborted || LeagueRegions.Of(t.Source) != region || season?.Invoke(t.Season) == false) continue;
            foreach (var ((tnr, team), sched) in w.Sched.Where(k => k.Key.Item1 == t.Tnr).OrderBy(k => k.Key.Item2, StringComparer.Ordinal))
                foreach (var s in sched)
                {
                    if (!w.Real.Contains((tnr, s.Round, team))) continue;
                    var rows = LeagueFeatures.RowsFor(w, tnr, team, s.Round);
                    if (rows.Count == 0) continue;
                    var lu = w.LineupOf(tnr, s.Round, team);
                    groups.Add(new Group(tnr, team, s.Round, t.Season, t.Level, PhaseOf(rows[0]), rows,
                        rows.Select(r => lu.ContainsKey(r.Pid) ? 1 : 0).ToArray(), lu));
                }
        }
        return groups;
    }

    /// <summary>
    /// Logistische Regression (Python: model.fit): Newton-Verfahren mit L2-Strafe <paramref name="l2"/> auf allen Gewichten außer
    /// dem ersten (Achsenabschnitt), höchstens <paramref name="iters"/> Schritte, Abbruch, sobald kein Gewicht sich mehr als 1e-6 bewegt.
    /// </summary>
    public static double[] Fit(IReadOnlyList<double[]> x, IReadOnlyList<int> y, double l2 = 1.0, int iters = 30)
    {
        if (x.Count == 0) throw new ArgumentException("Keine Trainingszeilen", nameof(x));
        var d = x[0].Length;
        var w = new double[d];
        for (var it = 0; it < iters; it++)
        {
            var g = new double[d];
            var h = new double[d, d];
            for (var n = 0; n < x.Count; n++)
            {
                var xi = x[n];
                double z = 0;
                for (var j = 0; j < d; j++) z += xi[j] * w[j];
                var p = 1 / (1 + Math.Exp(-z));
                var r = p - y[n];
                var s = p * (1 - p);
                for (var j = 0; j < d; j++)
                {
                    if (xi[j] == 0) continue;
                    g[j] += xi[j] * r;
                    var sj = s * xi[j];
                    for (var k = 0; k <= j; k++) h[j, k] += sj * xi[k];
                }
            }
            for (var j = 0; j < d; j++)
            {
                for (var k = j + 1; k < d; k++) h[j, k] = h[k, j];
                if (j > 0) { g[j] += l2 * w[j]; h[j, j] += l2; }
            }
            var step = Solve(h, g);
            double max = 0;
            for (var j = 0; j < d; j++) { w[j] -= step[j]; max = Math.Max(max, Math.Abs(step[j])); }
            if (max < 1e-6) break;
        }
        return w;
    }

    /// <summary>A·x = b (Gauß mit Spaltenpivot; A wird überschrieben).</summary>
    internal static double[] Solve(double[,] a, double[] b)
    {
        var n = b.Length;
        var x = (double[])b.Clone();
        for (var c = 0; c < n; c++)
        {
            var piv = c;
            for (var r = c + 1; r < n; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[piv, c])) piv = r;
            if (Math.Abs(a[piv, c]) < 1e-300) throw new InvalidOperationException("Matrix singulär — ein Merkmal ist überall gleich?");
            if (piv != c)
            {
                for (var k = 0; k < n; k++) (a[c, k], a[piv, k]) = (a[piv, k], a[c, k]);
                (x[c], x[piv]) = (x[piv], x[c]);
            }
            for (var r = c + 1; r < n; r++)
            {
                var f = a[r, c] / a[c, c];
                if (f == 0) continue;
                for (var k = c; k < n; k++) a[r, k] -= f * a[c, k];
                x[r] -= f * x[c];
            }
        }
        for (var r = n - 1; r >= 0; r--)
        {
            var s = x[r];
            for (var k = r + 1; k < n; k++) s -= a[r, k] * x[k];
            x[r] = s / a[r, r];
        }
        return x;
    }

    /// <summary>Gewichte für die Merkmale <paramref name="features"/> aus den Gruppen fitten.</summary>
    public static LeagueModel Train(IReadOnlyList<string> features, IEnumerable<Group> groups, double l2 = 1.0, int iters = 30)
    {
        var shape = new LeagueModel(features, new double[features.Count]);
        var x = new List<double[]>();
        var y = new List<int>();
        foreach (var g in groups)
            for (var i = 0; i < g.Rows.Count; i++) { x.Add(shape.Vec(g.Rows[i])); y.Add(g.Y[i]); }
        return new LeagueModel(features, Fit(x, y, l2, iters));
    }

    /// <summary>Das Modell-JSON im Format von <c>Assets/league-model.json</c> (features, weights, trainedOn, rows, note).</summary>
    public static JsonObject ExportJson(LeagueModel m, IEnumerable<string> trainedOn, int rows, string note) => new()
    {
        ["features"] = new JsonArray(m.Features.Select(f => (JsonNode)f).ToArray()),
        ["weights"] = new JsonArray(m.Weights.Select(w => (JsonNode)w).ToArray()),
        ["trainedOn"] = new JsonArray(trainedOn.Distinct().OrderBy(s => s, StringComparer.Ordinal).Select(s => (JsonNode)s).ToArray()),
        ["rows"] = rows,
        ["note"] = note,
    };

    // ---- Backtest ---------------------------------------------------------------------------------

    /// <summary>Zähler eines Backtests (eine Zeile der Ausgabe: gesamt, je Stufe, je Stufe + Lage).</summary>
    public sealed class Tally
    {
        /// <summary>Mannschaftskämpfe (Gruppen).</summary>
        public int Matches;
        /// <summary>Aufgestellte, die in der Meldeliste stehen (Nenner von <see cref="Hits"/>).</summary>
        public int Act;
        /// <summary>Aufgestellte unter den B wahrscheinlichsten (Modell) bzw. unter den ersten B der Meldeliste (Basis).</summary>
        public int Hits, BaseHits;
        /// <summary>Besetzte Bretter (auch mit Spielern ohne Meldeliste — die zählen als Fehlschuss).</summary>
        public int Boards;
        /// <summary>Echter Spieler auf Platz 1 bzw. unter den ersten 3 der Brett-Vorschläge; Basis: Meldelisten-Reihenfolge
        /// (Brett k ← Gemeldeter k, dann k+1, k+2 — Ersatzleute rücken nach).</summary>
        public int Top1, Top3, BaseTop1, BaseTop3;
        /// <summary>Summe der Log-Verluste über alle Zeilen (Modell) bzw. bei gleichmäßigem Raten B/N (Basis).</summary>
        public double LogLoss, BaseLogLoss;
        public int Rows;

        public void Add(Tally o)
        {
            Matches += o.Matches; Act += o.Act; Hits += o.Hits; BaseHits += o.BaseHits; Boards += o.Boards;
            Top1 += o.Top1; Top3 += o.Top3; BaseTop1 += o.BaseTop1; BaseTop3 += o.BaseTop3;
            LogLoss += o.LogLoss; BaseLogLoss += o.BaseLogLoss; Rows += o.Rows;
        }

        public double HitRate => Act == 0 ? 0 : (double)Hits / Act;
        public double BaseHitRate => Act == 0 ? 0 : (double)BaseHits / Act;
    }

    /// <summary>Ergebnis eines Backtests: gesamt, je Stufe, je (Stufe, Lage), Kalibrierung in 10 Stufen [Fälle, Σp, eingetroffen].</summary>
    public sealed class BacktestResult
    {
        public Tally Total { get; } = new();
        public SortedDictionary<int, Tally> ByLevel { get; } = new();
        public SortedDictionary<(int Level, string Phase), Tally> ByPhase { get; } = new();
        public SortedDictionary<string, Tally> ByPhaseOnly { get; } = new(StringComparer.Ordinal);
        public double[,] Calibration { get; } = new double[10, 3];
    }

    /// <summary>
    /// Wie gut lag <paramref name="model"/> auf den Gruppen <paramref name="test"/> (Python: evaluate.py)? Je Gruppe die normierten
    /// Wahrscheinlichkeiten (<see cref="LeagueModel.Predict"/>), Brett-Wahrscheinlichkeiten (<see cref="LeagueModel.BoardProbs"/>)
    /// und die Zähler aus <see cref="Tally"/>. Die Sonntags-Vorab-Mischung bleibt außen vor (wie im Tiroler Backtest).
    /// </summary>
    public static BacktestResult Backtest(LeagueModel model, IEnumerable<Group> test)
    {
        var res = new BacktestResult();
        foreach (var g in test)
        {
            var p = model.Predict(g.Rows);
            var b = g.Rows[0].B;
            var t = new Tally { Matches = 1, Rows = g.Rows.Count };
            var pick = Enumerable.Range(0, p.Length).OrderByDescending(i => p[i]).Take(b).ToHashSet();
            var uniform = Math.Clamp((double)Math.Min(b, g.Rows.Count) / g.Rows.Count, 1e-6, 1 - 1e-6);
            for (var i = 0; i < p.Length; i++)
            {
                var y = g.Y[i];
                t.Act += y;
                if (pick.Contains(i)) t.Hits += y;
                if (i < b) t.BaseHits += y;
                var pi = Math.Clamp(p[i], 1e-6, 1 - 1e-6);
                t.LogLoss -= y == 1 ? Math.Log(pi) : Math.Log(1 - pi);
                t.BaseLogLoss -= y == 1 ? Math.Log(uniform) : Math.Log(1 - uniform);
                var bin = Math.Min(9, (int)(p[i] * 10));
                res.Calibration[bin, 0]++;
                res.Calibration[bin, 1] += p[i];
                res.Calibration[bin, 2] += y;
            }
            var bp = LeagueModel.BoardProbs(p, b);
            var cols = bp.GetLength(1);
            var index = new Dictionary<string, int>();
            for (var i = 0; i < g.Rows.Count; i++) index.TryAdd(g.Rows[i].Pid, i);
            foreach (var (pid, board) in g.Lineup)
            {
                t.Boards++;
                var k = board - 1;
                if (!index.TryGetValue(pid, out var idx) || k < 0) continue;
                if (k < cols)
                {
                    var order = Enumerable.Range(0, g.Rows.Count).OrderByDescending(i => bp[i, k]).ToList();
                    var rank = 1 + order.IndexOf(idx);
                    if (rank == 1) t.Top1++;
                    if (rank <= 3) t.Top3++;
                }
                if (idx == k) t.BaseTop1++;
                if (idx >= k && idx <= k + 2) t.BaseTop3++;
            }
            res.Total.Add(t);
            Get(res.ByLevel, g.Level).Add(t);
            Get(res.ByPhase, (g.Level, g.Phase)).Add(t);
            Get(res.ByPhaseOnly, g.Phase).Add(t);
        }
        return res;

        static Tally Get<TK>(SortedDictionary<TK, Tally> d, TK k) where TK : notnull =>
            d.TryGetValue(k, out var x) ? x : d[k] = new Tally();
    }

    /// <summary>Lesbarer Bericht eines Backtests (Konsole des Wartungswerkzeugs).</summary>
    public static string Report(string title, BacktestResult r)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine($"  {"",-16}{"Kämpfe",7}{"Treffer",9}{"Basis",8}{"Top-1",8}{"Basis",8}{"Top-3",8}{"Basis",8}{"LogLoss",9}{"Basis",8}");
        void Line(string name, Tally t) => sb.AppendLine(
            $"  {name,-16}{t.Matches,7}{Pct(t.HitRate),9}{Pct(t.BaseHitRate),8}{Pct(Div(t.Top1, t.Boards)),8}{Pct(Div(t.BaseTop1, t.Boards)),8}"
            + $"{Pct(Div(t.Top3, t.Boards)),8}{Pct(Div(t.BaseTop3, t.Boards)),8}{Div(t.LogLoss, t.Rows),9:0.000}{Div(t.BaseLogLoss, t.Rows),8:0.000}");
        Line("gesamt", r.Total);
        foreach (var (l, t) in r.ByLevel) Line($"Stufe {l}", t);
        foreach (var (k, t) in r.ByPhase) Line($"Stufe {k.Level} {k.Phase}", t);
        foreach (var (k, t) in r.ByPhaseOnly) Line(k, t);
        sb.AppendLine("  Kalibrierung (vorhergesagt → eingetroffen):");
        for (var i = 0; i < 10; i++)
        {
            var n = r.Calibration[i, 0];
            if (n == 0) continue;
            sb.AppendLine($"    {i * 10,3}–{i * 10 + 10,3} %  n={n,6:0}  Ø Prognose {Pct(r.Calibration[i, 1] / n),6}  eingetroffen {Pct(r.Calibration[i, 2] / n),6}");
        }
        return sb.ToString();

        static double Div(double a, double b) => b == 0 ? 0 : a / b;
        static string Pct(double x) => $"{x * 100:0.0} %";
    }
    // ---- Ein ganzer Lauf (Wartungswerkzeug league-train) -----------------------------------------

    /// <summary>Datenlage einer Saison: Ligen, Mannschaftskämpfe mit Trainingszeilen, Zeilen, Meldelisten-Einträge (mit FIDE-ID).</summary>
    public sealed record SeasonStats(string Season, int Leagues, int Groups, int Rows, int Players, int PlayersWithFide);

    /// <summary>Ergebnis von <see cref="TrainAsync"/>.</summary>
    public sealed record TrainReport(LeagueModel Model, JsonObject Json, IReadOnlyList<string> TrainedOn, int Rows,
        IReadOnlyList<string> Holdout, BacktestResult Backtest, BacktestResult? Reference, IReadOnlyList<SeasonStats> Seasons);

    /// <summary>
    /// Ein Lauf: Welt der Region laden, Zeilen bauen, je Holdout-Saison S auf den Saisonen VOR S fitten und auf S prüfen (wie
    /// evaluate.py — nie mit Wissen aus der Zukunft; <paramref name="holdout"/> leer = jüngste vollständige Saison), das Vergleichs-
    /// modell <paramref name="reference"/> (z. B. das Tiroler) auf denselben Holdout-Gruppen prüfen, dann die endgültigen Gewichte auf
    /// ALLEN Saisonen außer der laufenden fitten (wie export_weights.py).
    /// </summary>
    public static async Task<TrainReport> TrainAsync(AppDbContext db, string region, IReadOnlyList<string> features,
        IReadOnlyList<string>? holdout, LeagueModel? reference, CancellationToken ct)
    {
        var w = await LoadWorldAsync(db, region, ct);
        if (w.Seasons.Count == 0) throw new InvalidOperationException($"Keine Ligen der Region {region} in der Datenbank");
        var groups = Dataset(w, region);
        var current = w.Seasons[^1];
        var complete = groups.Select(g => g.Season).Where(x => x != current).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (complete.Count == 0) throw new InvalidOperationException("Keine abgeschlossene Saison mit gespielten Runden");
        var hold = holdout is { Count: > 0 } ? holdout.ToList() : [complete[^1]];

        var bt = new BacktestResult();
        var refBt = reference is null ? null : new BacktestResult();
        foreach (var s in hold)
        {
            var train = groups.Where(g => string.CompareOrdinal(g.Season, s) < 0).ToList();
            var test = groups.Where(g => g.Season == s).ToList();
            if (train.Count == 0 || test.Count == 0) throw new InvalidOperationException($"Holdout {s}: keine Trainings- oder Testzeilen");
            Merge(bt, Backtest(Train(features, train), test));
            if (reference is not null) Merge(refBt!, Backtest(reference, test));
        }

        var final = groups.Where(g => g.Season != current).ToList();
        var model = Train(features, final);
        var rows = final.Sum(g => g.Rows.Count);
        var trainedOn = final.Select(g => g.Season).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        var json = ExportJson(model, trainedOn, rows,
            $"Logistische Regression (Newton, L2=1), Region {region}; trainiert mit tools/LibraryImport league-train (LeagueTraining)");

        var allTnrs = w.T.Keys.ToList();
        var players = await db.LeaguePlayers.AsNoTracking().Where(p => allTnrs.Contains(p.Tnr))
            .Select(p => new { p.Tnr, p.FideId }).ToListAsync(ct);
        var stats = w.Seasons.Select(se =>
        {
            var tn = w.T.Values.Where(t => t.Season == se).Select(t => t.Tnr).ToHashSet();
            var gs = groups.Where(g => g.Season == se).ToList();
            var ps = players.Where(p => tn.Contains(p.Tnr)).ToList();
            return new SeasonStats(se, tn.Count, gs.Count, gs.Sum(g => g.Rows.Count), ps.Count, ps.Count(p => !string.IsNullOrEmpty(p.FideId)));
        }).ToList();
        return new TrainReport(model, json, trainedOn, rows, hold, bt, refBt, stats);
    }

    private static void Merge(BacktestResult into, BacktestResult from)
    {
        into.Total.Add(from.Total);
        foreach (var (k, t) in from.ByLevel) (into.ByLevel.TryGetValue(k, out var x) ? x : into.ByLevel[k] = new Tally()).Add(t);
        foreach (var (k, t) in from.ByPhase) (into.ByPhase.TryGetValue(k, out var x) ? x : into.ByPhase[k] = new Tally()).Add(t);
        foreach (var (k, t) in from.ByPhaseOnly) (into.ByPhaseOnly.TryGetValue(k, out var x) ? x : into.ByPhaseOnly[k] = new Tally()).Add(t);
        for (var i = 0; i < 10; i++)
            for (var j = 0; j < 3; j++) into.Calibration[i, j] += from.Calibration[i, j];
    }
}
