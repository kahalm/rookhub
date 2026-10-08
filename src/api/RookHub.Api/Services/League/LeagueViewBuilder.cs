using System.Text.Json.Nodes;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Baut die Ansicht einer Liga (alle Begegnungen aller Teams mit Prognose je Brett) — Portierung von
/// export_site.league_json. Die JSON-Feldnamen sind BEWUSST dieselben wie in der Python-Fassung
/// (snake_case): so lässt sich die C#-Ausgabe direkt gegen die Python-Ausgabe prüfen.
///
/// <para>Regel (Wunsch des Nutzers): Prognose nur für die NÄCHSTE Runde einer Liga — die übernächste
/// erst, wenn die nächste gespielt ist. Ausnahme Runden-Blöcke (<see cref="LeagueLevels.HasRoundBlocks"/>): Landesliga Samstag +
/// Sonntag gemeinsam, Bundesliga der ganze Block (Fr–So, fünf Tage am Stück, Doppelrunde an einem Tag). Gespielte
/// Runden zeigen die echte Aufstellung neben der Prognose, die VOR der Runde gegolten hätte.</para>
/// </summary>
public sealed class LeagueViewBuilder
{
    private static readonly string[] Weekday = { "So", "Mo", "Di", "Mi", "Do", "Fr", "Sa" };
    /// <summary>Backtest (Saison-Holdout 2020/21–2025/26): Anteil der Aufgestellten unter den Top-B der Prognose. Stufen seit der
    /// Bundesliga (0.719.0): Landesliga 3, 1. Klasse 4, 2. Klasse 5, Gebietsklasse 6 (Python: 1–4). Für die Bundesliga (1, 2) gibt es
    /// keinen Backtest — dort fehlt <c>hit</c>.</summary>
    private static readonly Dictionary<(int, string), double> Hits = new()
    {
        [(3, "R1")] = .58, [(3, "R2+")] = .64, [(3, "So vorab")] = .64, [(3, "So nach Sa")] = .76,
        [(4, "R1")] = .58, [(4, "R2+")] = .66, [(5, "R1")] = .52, [(5, "R2+")] = .63, [(6, "R1")] = .33, [(6, "R2+")] = .56,
    };

    /// <summary>Dasselbe für Bayern mit <c>Assets/league-model-bayern.json</c> (2026-10-07, <c>league-train --region bayern
    /// --holdout 2022/23,2023/24,2024/25,2025/26</c>: je Saison nur auf früheren gefittet). Stufen 1–4 Ligamanager (Oberliga …
    /// Bezirksliga Oberbayern), 5–8 Schachkreis Zugspitze (erst ab 2024/25 mit Brettern — daher wenige Kämpfe). „So vorab"
    /// kam im Backtest nicht vor (die Vorab-Mischung prüft er nicht) — wie in Tirol der Wert von R2+.</summary>
    private static readonly Dictionary<(int, string), double> HitsBayern = new()
    {
        [(1, "R1")] = .74, [(1, "R2+")] = .79, [(1, "So vorab")] = .79, [(1, "So nach Sa")] = .83,
        [(2, "R1")] = .71, [(2, "R2+")] = .79, [(3, "R1")] = .71, [(3, "R2+")] = .79, [(4, "R1")] = .75, [(4, "R2+")] = .82,
        [(5, "R1")] = .62, [(5, "R2+")] = .72, [(6, "R1")] = .69, [(6, "R2+")] = .71, [(7, "R1")] = .61, [(7, "R2+")] = .65,
        [(8, "R1")] = .58, [(8, "R2+")] = .65,
    };

    /// <summary>Erwartete Treffer-Quote einer Lage (Backtest der Region) — nur, wenn die Region mit IHREM Modell rechnet; fällt sie
    /// auf das Tiroler zurück, gibt es keinen passenden Backtest.</summary>
    internal static double? ExpectedHits(string region, bool ownModel, int level, string phase) =>
        !ownModel ? null
        : region == LeagueRegions.Tirol ? (Hits.TryGetValue((level, phase), out var t) ? t : null)
        : region == LeagueRegions.Bayern ? (HitsBayern.TryGetValue((level, phase), out var b) ? b : null)
        : null;

    private readonly LeagueWorld _w;
    private readonly LeagueModels _models;
    private readonly IReadOnlyDictionary<string, int> _gameCounts;
    private readonly IReadOnlyDictionary<string, List<LeagueOnlineAccount>> _accounts;

    /// <summary>Ein Modell für alle Ligen (Tests, Paritätsprüfung gegen Python).</summary>
    public LeagueViewBuilder(LeagueWorld w, LeagueModel m, IReadOnlyDictionary<string, int> gameCounts,
        IReadOnlyDictionary<string, List<LeagueOnlineAccount>> accounts)
        : this(w, new LeagueModels(m), gameCounts, accounts)
    {
    }

    /// <summary>Modell je Region der Liga (<see cref="LeagueModels.For"/>).</summary>
    public LeagueViewBuilder(LeagueWorld w, LeagueModels models, IReadOnlyDictionary<string, int> gameCounts,
        IReadOnlyDictionary<string, List<LeagueOnlineAccount>> accounts)
    {
        _w = w; _models = models; _gameCounts = gameCounts; _accounts = accounts;
    }

    public static string? FmtDate(DateOnly? d) => d is null ? null : $"{Weekday[(int)d.Value.DayOfWeek]} {d.Value:dd.MM.yyyy}";

    public static string? ScoreStr(double? x)
    {
        if (x is null) return null;
        var v = x.Value;
        if (Math.Abs(v - 0.5) < 1e-9) return "½";
        var whole = (int)Math.Floor(v);
        return v - whole > 1e-9 ? $"{whole}½" : $"{whole}";
    }

    private static double R3(double x) => Math.Round(x, 3, MidpointRounding.ToEven);

    private (string Prev, string Cur) Notes(int tnr, string pid, string team)
    {
        var t = _w.T[tnr];
        var ps = _w.PrevSeason(t.Season);
        var prev = new List<string>();
        foreach (var lvl in LeagueLevels.Of(t.Source))
        {
            var lg = LeagueLevels.Short(t.Source, lvl);
            var n = _w.Apps(t.Source, ps, lvl, pid);
            if (ps is not null && n > 0) prev.Add($"{lg} {n}/{_w.MptOf(t.Source, ps, lvl)}");
        }
        var cur = new List<string>();
        if (_w.ClubTeamsOf(tnr, team) is { } teams)
        {
            foreach (var (tnr2, team2) in teams.OrderBy(x => _w.T[x.Item1].Level))
            {
                var n = _w.SchedOf(tnr2, team2).Count(s => _w.Real.Contains((tnr2, s.Round, team2))
                                                         && _w.LineupOf(tnr2, s.Round, team2).ContainsKey(pid));
                if (n == 0) continue;
                var same = teams.Count(x => x.Item1 == tnr2) > 1;
                var lg = LeagueLevels.Short(_w.T[tnr2].Source, _w.T[tnr2].Level) ?? "?";
                cur.Add($"{lg}{(same ? $" ({team2.Split(' ')[^1]})" : "")} {n}×");
            }
        }
        return (string.Join(", ", prev), string.Join(", ", cur));
    }

    /// <summary>(Zeilen, Wahrscheinlichkeiten, Lage) — Prognose für OPP in Runde RND mit dem Wissen vor der Runde.</summary>
    /// <remarks>Runden-Blöcke (<see cref="LeagueLevels.Consecutive"/>): ist der Vortag (bzw. die Runde am Vormittag) noch nicht gespielt,
    /// wird diese Runde als Mischung „dort gespielt ja/nein" gerechnet — mit der Prognose des Vortags, die in einem Bundesliga-Block
    /// selbst schon eine solche Mischung sein kann (rekursiv; Landesliga: der Samstag ist eine gewöhnliche Prognose, also dieselben
    /// Zahlen wie vorher). Ist eine Runde am SELBEN Tag schon gespielt (Bundesliga-Doppelrunde), zählt sie mit
    /// (<c>asof</c> = Tag danach).</remarks>
    private (List<FeatureRow> Rows, double[] P, string Phase) Forecast(LeagueModel m, int tnr, string opp, int rnd, int level)
    {
        var src = _w.T[tnr].Source;
        var date = _w.RDate.GetValueOrDefault((tnr, rnd));
        var prev = _w.SchedOf(tnr, opp).Where(s => s.Round < rnd).ToList();
        var sat = prev.Count > 0 ? prev[^1] : null;
        var weekend = sat is not null && LeagueLevels.Consecutive(src, level, sat.Date, date);
        var playedBefore = prev.Any(s => _w.Real.Contains((tnr, s.Round, opp)));
        if (weekend && !_w.Real.Contains((tnr, sat!.Round, opp)))
        {
            var sun = LeagueFeatures.RowsFor(_w, tnr, opp, rnd, asof: sat.Date);
            var (satRows, pSat, _) = Forecast(m, tnr, opp, sat.Round, level);
            return (sun, m.SundayAdvance(satRows, pSat, sun), "So vorab");
        }
        var rows = weekend && sat!.Date == date
            ? LeagueFeatures.RowsFor(_w, tnr, opp, rnd, asof: date!.Value.AddDays(1))
            : LeagueFeatures.RowsFor(_w, tnr, opp, rnd);
        return (rows, m.Predict(rows), weekend ? "So nach Sa" : playedBefore ? "R2+" : "R1");
    }

    private JsonArray AccShort(string? fide) => new(
        (fide is not null && _accounts.TryGetValue(fide, out var l) ? l : new())
        .Select(a => (JsonNode)new JsonObject { ["site"] = a.Site, ["user"] = a.UserName, ["url"] = a.Url, ["conf"] = a.Confidence })
        .ToArray());

    public JsonObject Build(int tnr, IEnumerable<LeagueGame> leagueGames)
    {
        var t = _w.T[tnr];
        var level = t.Level;
        var b = _w.BoardsOf(tnr);
        var region = LeagueRegions.Of(t.Source);
        var model = _models.ForRegion(region);
        var ownModel = _models.Has(region);
        var games = leagueGames.Where(g => g.Tnr == tnr).ToList();
        var rounds = _w.Matches.Keys.Where(k => k.Item1 == tnr).Select(k => k.Item2).Distinct().OrderBy(x => x).ToList();
        var teams = _w.Sched.Keys.Where(k => k.Item1 == tnr).Select(k => k.Item2).Distinct()
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        var played = rounds.ToDictionary(r => r, r => teams.Any(tm => _w.Real.Contains((tnr, r, tm))));
        int? r0 = rounds.Where(r => !played[r]).Select(r => (int?)r).FirstOrDefault();
        var open = new HashSet<int>();
        if (r0 is not null)
        {
            open.Add(r0.Value);
            // Runden-Block (Landesliga Sa + So, Bundesliga Fr–So bzw. fünf Tage am Stück): der ganze Block ist offen.
            for (var r = r0.Value; rounds.Contains(r + 1) && Linked(r, r + 1); r++) open.Add(r + 1);
        }
        bool Linked(int a, int b) =>
            LeagueLevels.Consecutive(t.Source, level, _w.RDate.GetValueOrDefault((tnr, a)), _w.RDate.GetValueOrDefault((tnr, b)));
        // Genauer wird eine vorläufige Runde, sobald die Runde VOR ihrem Block gespielt ist.
        int UnlockAfter(int r)
        {
            var first = r;
            while (first > 1 && Linked(first - 1, first)) first--;
            return first - 1;
        }
        var venues = new Dictionary<(int, string), string?>();
        var times = new Dictionary<(int, string), string?>();
        var res = new Dictionary<(int, string, string), (double?, double?)>();
        foreach (var ((tn, r), ms) in _w.Matches)
        {
            if (tn != tnr) continue;
            foreach (var m in ms)
            {
                venues[(r, m.Home)] = venues[(r, m.Away)] = m.Venue;
                var tm = string.IsNullOrEmpty(m.Time) ? null : m.Time.Replace(" Uhr", "");
                times[(r, m.Home)] = times[(r, m.Away)] = string.IsNullOrEmpty(tm) ? null : tm;
                res[(r, m.Home, m.Away)] = (m.HomePts, m.AwayPts);
            }
        }

        var fixtures = new JsonObject();
        foreach (var team in teams)
        {
            var fx = new JsonObject();
            var mine = _w.SchedOf(tnr, team).GroupBy(s => s.Round).ToDictionary(g => g.Key, g => g.First());
            foreach (var r in rounds)
            {
                var d = _w.RDate.GetValueOrDefault((tnr, r));
                if (!mine.TryGetValue(r, out var s))
                {
                    fx[r.ToString()] = new JsonObject { ["bye"] = true, ["date"] = FmtDate(d) };
                    continue;
                }
                var e = new JsonObject
                {
                    ["opp"] = s.Opp, ["home"] = s.Home, ["date"] = FmtDate(s.Date ?? d),
                    ["time"] = times.GetValueOrDefault((r, team)), ["venue"] = venues.GetValueOrDefault((r, team)),
                };
                var isPlayed = _w.Real.Contains((tnr, r, team));
                if (isPlayed)
                {
                    var (a, bb) = res.TryGetValue((r, team, s.Opp), out var x) ? x
                        : res.TryGetValue((r, s.Opp, team), out var y) ? (y.Item2, y.Item1) : (null, null);
                    e["status"] = "played";
                    e["score"] = $"{ScoreStr(a)} : {ScoreStr(bb)}";
                }
                else if (open.Contains(r) || played[r])
                {
                    e["status"] = "open";
                }
                else
                {
                    // Spätere Runden (Wunsch 2026-10-06: „lass mich auch zukünftige Runden sehen — Prognosen kannst du machen
                    // und dann anpassen"): dieselbe Rechnung mit dem Wissen von heute, als VORLÄUFIG markiert. Genauer wird
                    // sie, sobald `unlock_after` gespielt ist — jedes „Daten aktualisieren" rechnet sie neu.
                    e["status"] = "open";
                    e["provisional"] = true;
                    e["unlock_after"] = UnlockAfter(r);
                }
                var (rows, p, phase) = Forecast(model, tnr, s.Opp, r, level);
                if (rows.Count == 0)
                {
                    e["status"] = "nodata";
                    fx[r.ToString()] = e;
                    continue;
                }
                var bp = LeagueModel.BoardProbs(p, b);
                var bcols = bp.GetLength(1);
                var act = isPlayed ? ActualBoards(games, r, s.Opp) : new();
                var boards = new JsonArray();
                for (var k = 0; k < Math.Min(b, bcols); k++)
                {
                    var order = Enumerable.Range(0, rows.Count).OrderByDescending(i => bp[i, k]).ToList();
                    var cand = new JsonArray(order.Where(i => bp[i, k] >= 0.005).Take(3).Select(i => (JsonNode)new JsonObject
                    {
                        ["n"] = rows[i].Name, ["elo"] = rows[i].Elo > 0 ? rows[i].Elo : null, ["rb"] = rows[i].Rb,
                        ["p"] = R3(bp[i, k]), ["fide"] = rows[i].Fide,
                        // Partien im Bestand (0.649.0) — wie die Spalte „Partien" der Meldeliste
                        ["g"] = rows[i].Fide is { } cf ? _gameCounts.GetValueOrDefault(cf) : 0,
                    }).ToArray());
                    var sumTop = cand.Sum(c => c!["p"]!.GetValue<double>());
                    var oppWhite = (k % 2 == 0) != s.Home;   // Heimteam hat an ungeraden Brettern Weiß
                    var bo = new JsonObject
                    {
                        ["board"] = k + 1, ["opp_color"] = oppWhite ? "w" : "s", ["cand"] = cand,
                        ["other"] = R3(Math.Max(0.0, 1 - sumTop)),
                    };
                    if (act.TryGetValue(k + 1, out var ab))
                    {
                        int? rank = null;
                        if (ab.Name is not null)
                        {
                            var pid = LeagueNames.Pid(ab.Fide, LeagueNames.NameKey(ab.Name));
                            var idx = rows.FindIndex(x => x.Pid == pid);
                            if (idx >= 0)
                            {
                                rank = 1 + order.IndexOf(idx);
                                bo["actual_p"] = R3(bp[idx, k]);
                            }
                        }
                        bo["actual"] = new JsonObject
                        {
                            ["n"] = ab.Name ?? "nicht besetzt", ["elo"] = ab.Elo, ["rank"] = rank,
                            ["score"] = ScoreStr(ab.Score), ["own"] = ScoreStr(ab.Own),
                            ["vs"] = ab.Vs ?? "nicht besetzt", ["vs_elo"] = ab.VsElo,
                        };
                    }
                    boards.Add(bo);
                }
                var roster = new JsonArray();
                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    var (prevN, curN) = Notes(tnr, row.Pid, s.Opp);
                    var f = row.Fide;
                    var g = f is not null ? _gameCounts.GetValueOrDefault(f) : 0;
                    roster.Add(new JsonObject
                    {
                        ["rb"] = row.Rb, ["n"] = row.Name, ["elo"] = row.Elo > 0 ? row.Elo : null, ["p"] = R3(p[i]),
                        ["prev"] = prevN, ["cur"] = curN, ["fide"] = f, ["g"] = g, ["acc"] = AccShort(f),
                    });
                }
                if (isPlayed && act.Count > 0) e["eval"] = Evaluate(rows, p, bp, b, act);
                e["boards"] = boards;
                e["roster"] = roster;
                e["phase"] = phase;
                // Backtest je Region (Tirol: Python, Bayern: league-train) — nur, wenn die Region mit ihrem eigenen Modell rechnet.
                e["hit"] = ExpectedHits(region, ownModel, level, phase) is { } hit ? Math.Round(hit * b, 1) : null;
                fx[r.ToString()] = e;
            }
            fixtures[team] = fx;
        }
        var name = t.League + (string.IsNullOrEmpty(t.Grp) ? "" : $" {t.Grp}");
        return new JsonObject
        {
            ["tnr"] = tnr, ["name"] = name, ["season"] = t.Season, ["level"] = level, ["boards"] = b,
            ["teams"] = new JsonArray(teams.Select(x => (JsonNode)x).ToArray()),
            ["rounds"] = new JsonArray(rounds.Select(r => (JsonNode)new JsonObject
            {
                ["round"] = r, ["date"] = FmtDate(_w.RDate.GetValueOrDefault((tnr, r))), ["played"] = played[r], ["open"] = open.Contains(r),
            }).ToArray()),
            ["fixtures"] = fixtures,
            // Ligamanager: Spielplan aus der Quelle (Region/Saison/Slug-Id) — die Tnr ist dort versetzt
            // (LigamanagerSource.TnrOffset) und taugt NIE für eine chess-results-Adresse.
            ["source"] = t.Source == LigamanagerSource.Source || LigamanagerSource.IsLigamanagerTnr(tnr)
                ? (t.SourceRef is { } sr ? $"{LigamanagerSource.SiteUrl}/{sr}/spielplan" : LigamanagerSource.SiteUrl)
                // Schachkreis Zugspitze (2026-10-07): die Ergebnis-Seite der Liga in ihrer Saison.
                : t.Source == ZugspitzeSource.Source || ZugspitzeSource.IsZugspitzeTnr(tnr)
                ? (ZugspitzeSource.LeagueRef.Parse(t.SourceRef) is { } zr ? zr.Url
                    : ZugspitzeSource.RefOf(tnr) is { } zt ? new ZugspitzeSource.LeagueRef(zt.LigaId, zt.Season).Url : ZugspitzeSource.SiteUrl)
                : $"https://chess-results.com/tnr{tnr}.aspx?lan=0",
        };
    }

    private sealed record ActualBoard(string? Name, int? Elo, string? Fide, double? Score, double? Own, string? Vs, int? VsElo);

    /// <summary>
    /// Wie gut die Prognose einer GESPIELTEN Begegnung lag (Treffer-Statistik je Runde/Liga/gesamt; seit 0.658.0 nach Wunsch
    /// 2026-10-05 „an wie vielen Brettern saß genau der Erste, der Erste oder Zweite, einer der ersten drei"): je besetztem
    /// Brett der Platz des tatsächlichen Spielers in der Vorschlagsliste dieses Bretts (dieselbe stabile Reihenfolge wie die
    /// Anzeige) → <c>top1</c>, <c>top2</c>, <c>top3</c> (kumulativ) und <c>of</c> = besetzte Bretter (ohne „nicht besetzt").
    /// Ein Spieler, der in der Meldeliste nicht gefunden wird, zählt als Fehlschuss.
    /// <para><b>Wie gut passen die Prozente?</b> (Wunsch 2026-10-05: „50 % für Spieler A ist nicht falsch, wenn B kommt — kannst
    /// du ausrechnen, wie genau die Prozentangaben passen?") <c>e1</c>/<c>e2</c>/<c>e3</c> = Summe der angesagten
    /// Wahrscheinlichkeiten der ersten 1/2/3 Vorschläge (Tausendstel) — so oft HÄTTE es treffen sollen; <c>cal</c> = je Stufe
    /// 0–10 %, 10–20 % … 90–100 % <c>[Fälle, Summe der Angaben in Tausendsteln, eingetroffen]</c> über jeden Spieler eines
    /// besetzten Bretts mit mindestens <see cref="CalibrationMinP"/>.</para>
    /// </summary>
    private static JsonObject Evaluate(List<FeatureRow> rows, double[] p, double[,] bp, int b, Dictionary<int, ActualBoard> act)
    {
        int top1 = 0, top2 = 0, top3 = 0, of = 0;
        double e1 = 0, e2 = 0, e3 = 0, pa = 0, pb = 0;
        var cal = new long[10, 3];
        foreach (var (board, ab) in act)
        {
            if (ab.Name is null) continue;
            var k = board - 1;
            if (k < 0 || k >= bp.GetLength(1)) { of++; continue; }
            of++;
            var order = Enumerable.Range(0, rows.Count).OrderByDescending(i => bp[i, k]).ToList();
            e1 += order.Take(1).Sum(i => bp[i, k]);
            e2 += order.Take(2).Sum(i => bp[i, k]);
            e3 += order.Take(3).Sum(i => bp[i, k]);
            var pid = LeagueNames.Pid(ab.Fide, LeagueNames.NameKey(ab.Name));
            var idx = rows.FindIndex(x => x.Pid == pid);
            // Mehrwert gegenüber Raten (0.659.0): was die Prognose dem gab, der wirklich kam — gegen gleichmäßiges Raten
            // über die Meldeliste (1 / Zahl der Gemeldeten)
            if (idx >= 0) pa += bp[idx, k];
            if (rows.Count > 0) pb += 1.0 / rows.Count;
            foreach (var i in order)
            {
                var pr = bp[i, k];
                if (pr < CalibrationMinP) break;
                var bin = Math.Min(9, (int)(pr * 10));
                cal[bin, 0]++;
                cal[bin, 1] += (long)Math.Round(pr * 1000);
                if (i == idx) cal[bin, 2]++;
            }
            if (idx < 0) continue;
            var rank = 1 + order.IndexOf(idx);
            if (rank == 1) top1++;
            if (rank <= 2) top2++;
            if (rank <= 3) top3++;
        }
        var calJson = new JsonArray();
        for (var i = 0; i < 10; i++) calJson.Add(new JsonArray(cal[i, 0], cal[i, 1], cal[i, 2]));
        return new JsonObject
        {
            ["top1"] = top1, ["top2"] = top2, ["top3"] = top3, ["of"] = of,
            ["e1"] = (int)Math.Round(e1 * 1000), ["e2"] = (int)Math.Round(e2 * 1000), ["e3"] = (int)Math.Round(e3 * 1000),
            ["pa"] = (int)Math.Round(pa * 1000), ["pb"] = (int)Math.Round(pb * 1000),
            ["cal"] = calJson,
        };
    }

    /// <summary>Kleinere Angaben zählen in der Kalibrierung nicht — sonst bestünde die unterste Stufe aus Tausenden
    /// Ersatzspielern mit 0 %, die nichts über die angezeigten Prozente sagen.</summary>
    public const double CalibrationMinP = 0.02;

    private static Dictionary<int, ActualBoard> ActualBoards(List<LeagueGame> games, int rnd, string team)
    {
        var out_ = new Dictionary<int, ActualBoard>();
        foreach (var g in games.Where(g => g.Round == rnd && (g.HomeTeam == team || g.AwayTeam == team)))
        {
            var home = g.HomeTeam == team;
            out_[g.Board] = new ActualBoard(home ? g.HomePlayer : g.AwayPlayer, home ? g.HomeElo : g.AwayElo,
                home ? g.HomeFide : g.AwayFide, home ? g.HomeScore : g.AwayScore, home ? g.AwayScore : g.HomeScore,
                home ? g.AwayPlayer : g.HomePlayer, home ? g.AwayElo : g.HomeElo);
        }
        return out_;
    }
}
