using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

// Rechenkern von LeagueHub: „wer spielt in Runde r für Team T?" — eine 1:1-Portierung der Python-Fassung
// (~/claude/league-analyzer: features.py, model.py). Jede Abweichung verschiebt die Prozente; die
// Vergleichswerte stehen in LeagueEngineTests (aus der Python-Fassung gerechnet).

/// <summary>Namen und Vereine: Schlüssel, die über Seiten und Saisonen hinweg dieselbe Person/denselben Verein treffen.</summary>
public static class LeagueNames
{
    private static readonly HashSet<string> Suffix = new(StringComparer.Ordinal)
    {
        "di", "dr", "ddr", "mag", "mmag", "ing", "dipl", "bsc", "msc", "ba", "ma", "mba", "phd", "bakk",
        "bed", "med", "fh", "mas", "llm", "mmsc", "prof", "univ", "dkfm", "mmmag",
    };

    internal static bool IsSuffix(string tok)
    {
        var t = tok.ToLowerInvariant().Trim('(', ')', '.', ',');
        if (tok.Contains('.') || tok.StartsWith('(') || Suffix.Contains(t)) return true;
        var parts = t.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && parts.All(Suffix.Contains);
    }

    public static string Clean(string? s) => Regex.Replace((s ?? "").Replace(' ', ' '), @"\s+", " ").Trim();

    /// <summary>„Schnabl, Andreas Dr." → „schnabl, andreas" (akad. Titel stehen nur in der Startrangliste).</summary>
    public static string NameKey(string? name)
    {
        var toks = Clean(name).Split(' ').ToList();
        while (toks.Count > 2 && IsSuffix(toks[^1])) toks.RemoveAt(toks.Count - 1);
        return string.Join(' ', toks).ToLowerInvariant();
    }

    /// <summary>Personen-Schlüssel: FIDE-ID, sonst „n:" + Namensschlüssel.</summary>
    public static string Pid(string? fide, string? nameKey) => !string.IsNullOrEmpty(fide) ? fide : "n:" + (nameKey ?? "");

    private static readonly (Regex Re, string Club)[] ClubRules =
    {
        (new Regex("rum|hall", RegexOptions.IgnoreCase), "Rum/Hall/Mils"),
        (new Regex("kufstein", RegexOptions.IgnoreCase), "Kufstein/Wörgl"),
        (new Regex("fügen|rattenberg|zillertal", RegexOptions.IgnoreCase), "Fügen/Zillertal/Rattenberg"),
        (new Regex("jenbach", RegexOptions.IgnoreCase), "Jenbach"),
        (new Regex("telfs", RegexOptions.IgnoreCase), "Telfs"),
        (new Regex("ohne grenzen", RegexOptions.IgnoreCase), "Schach ohne Grenzen"),
        (new Regex("union innsbruck", RegexOptions.IgnoreCase), "Schachsport Union Innsbruck"),
        (new Regex("absam", RegexOptions.IgnoreCase), "Absam"),
        (new Regex("zirl", RegexOptions.IgnoreCase), "Zirl"),
        (new Regex("wattens", RegexOptions.IgnoreCase), "Wattens"),
        (new Regex("svi/ivb|sportverein innsbruck", RegexOptions.IgnoreCase), "Sportverein Innsbruck"),
    };

    /// <summary>Kanonischer Verein: Spielgemeinschafts-Umbenennungen, Sponsoren, „1"/„2" zusammengeführt.</summary>
    public static string Club(string? team)
    {
        var t = team ?? "";
        foreach (var (re, club) in ClubRules)
            if (re.IsMatch(t)) return club;
        return Regex.Replace(t, @"\s+[12]$", "");
    }
}

public sealed record SchedEntry(int Round, DateOnly? Date, string Opp, bool Home);
public sealed record RosterEntry(string Pid, int? Rb, int Elo, string Name, string? Fide);

/// <summary>Alle Einsätze, Termine und Meldelisten, einmal aus der DB geladen (Python: features.World).</summary>
public sealed class LeagueWorld
{
    public Dictionary<int, LeagueTournament> T { get; } = new();
    public List<string> Seasons { get; }
    public Dictionary<(int, int), DateOnly?> RDate { get; } = new();
    public Dictionary<int, int> Boards { get; } = new();
    public Dictionary<(int, int), List<LeagueMatch>> Matches { get; } = new();
    public Dictionary<(int, int, string), Dictionary<string, int>> Lineup { get; } = new();
    public HashSet<(int, int, string)> Real { get; } = new();
    public Dictionary<(int, string), List<RosterEntry>> Roster { get; } = new();
    public Dictionary<(string, int, string), int> AppsLvl { get; } = new();
    public Dictionary<(string, int), int> Mpt { get; } = new();
    public Dictionary<(int, string), List<SchedEntry>> Sched { get; } = new();
    public Dictionary<(string, string), HashSet<(int, string)>> ClubTeams { get; } = new();
    public List<LeagueGame> Games { get; }

    private static readonly Dictionary<string, int> None = new();

    public LeagueWorld(IEnumerable<LeagueTournament> tournaments, IEnumerable<LeagueRound> rounds,
        IEnumerable<LeagueMatch> matches, IEnumerable<LeagueGame> games, IEnumerable<LeaguePlayer> players)
    {
        foreach (var t in tournaments) T[t.Tnr] = t;
        Seasons = T.Values.Select(t => t.Season).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
        foreach (var r in rounds) RDate[(r.Tnr, r.Round)] = r.Date;
        Games = games.ToList();
        foreach (var tnr in T.Keys) Boards[tnr] = 0;
        foreach (var g in Games)
            if (Boards.TryGetValue(g.Tnr, out var b) && g.Board > b) Boards[g.Tnr] = g.Board;
        foreach (var m in matches.OrderBy(m => m.Id))
        {
            if (!Matches.TryGetValue((m.Tnr, m.Round), out var l)) Matches[(m.Tnr, m.Round)] = l = new();
            l.Add(m);
        }
        foreach (var g in Games.OrderBy(g => g.Id))
        {
            foreach (var home in new[] { true, false })
            {
                var team = home ? g.HomeTeam : g.AwayTeam;
                var p = home ? g.HomePlayer : g.AwayPlayer;
                if (g.Forfeit < 2 && g.HomeScore is not null) Real.Add((g.Tnr, g.Round, team));
                if (!string.IsNullOrEmpty(p))
                {
                    var key = (g.Tnr, g.Round, team);
                    if (!Lineup.TryGetValue(key, out var lu)) Lineup[key] = lu = new();
                    lu[LeagueNames.Pid(home ? g.HomeFide : g.AwayFide, LeagueNames.NameKey(p))] = g.Board;
                }
            }
        }
        foreach (var p in players.OrderBy(p => p.Tnr).ThenBy(p => p.Team, StringComparer.Ordinal)
                     .ThenBy(p => p.RosterBoard ?? int.MinValue).ThenBy(p => p.Id))
        {
            var key = (p.Tnr, p.Team);
            if (!Roster.TryGetValue(key, out var l)) Roster[key] = l = new();
            var elo = p.EloI is > 0 ? p.EloI.Value : p.EloN ?? 0;
            l.Add(new RosterEntry(LeagueNames.Pid(p.FideId, p.NameKey), p.RosterBoard, elo, p.Name,
                string.IsNullOrEmpty(p.FideId) ? null : p.FideId));
        }
        foreach (var ((tnr, rnd, team), lu) in Lineup)
        {
            if (!T.TryGetValue(tnr, out var t) || t.Stage != "Liga" || !Real.Contains((tnr, rnd, team))) continue;
            foreach (var p in lu.Keys)
            {
                var k = (t.Season, t.Level, p);
                AppsLvl[k] = AppsLvl.GetValueOrDefault(k) + 1;
            }
        }
        foreach (var t in T.Values.Where(t => t.Stage == "Liga"))
        {
            var k = (t.Season, t.Level);
            Mpt[k] = Math.Max(Mpt.GetValueOrDefault(k), MatchesPerTeam(t.Tnr));
        }
        foreach (var ((tnr, rnd), ms) in Matches)
        {
            foreach (var m in ms)
            {
                if (m.Away == "spielfrei") continue;
                var d = RDate.GetValueOrDefault((tnr, rnd));
                Add(Sched, (tnr, m.Home), new SchedEntry(rnd, d, m.Away, true));
                Add(Sched, (tnr, m.Away), new SchedEntry(rnd, d, m.Home, false));
            }
        }
        foreach (var k in Sched.Keys.ToList()) Sched[k] = Sched[k].OrderBy(s => s.Round).ToList();
        foreach (var (tnr, team) in Sched.Keys)
        {
            var k = (T[tnr].Season, LeagueNames.Club(team));
            if (!ClubTeams.TryGetValue(k, out var set)) ClubTeams[k] = set = new();
            set.Add((tnr, team));
        }
    }

    private static void Add<TK, TV>(Dictionary<TK, List<TV>> d, TK k, TV v) where TK : notnull
    {
        if (!d.TryGetValue(k, out var l)) d[k] = l = new();
        l.Add(v);
    }

    /// <summary>Höchste Zahl an Mannschaftskämpfen eines Teams (ohne Freilos) — Nenner der Einsatzquote.</summary>
    public int MatchesPerTeam(int tnr)
    {
        var count = new Dictionary<string, int>();
        foreach (var ((t, _), ms) in Matches)
        {
            if (t != tnr) continue;
            foreach (var m in ms.Where(m => m.Away != "spielfrei"))
            {
                count[m.Home] = count.GetValueOrDefault(m.Home) + 1;
                count[m.Away] = count.GetValueOrDefault(m.Away) + 1;
            }
        }
        return count.Count == 0 ? 0 : count.Values.Max();
    }

    public string? PrevSeason(string s)
    {
        var i = Seasons.IndexOf(s);
        return i > 0 ? Seasons[i - 1] : null;
    }

    public Dictionary<string, int> LineupOf(int tnr, int rnd, string team) =>
        Lineup.TryGetValue((tnr, rnd, team), out var lu) ? lu : None;

    public List<SchedEntry> SchedOf(int tnr, string team) =>
        Sched.TryGetValue((tnr, team), out var s) ? s : new();

    public int Apps(string? season, int level, string pid) =>
        season is null ? 0 : AppsLvl.GetValueOrDefault((season, level, pid));

    public int BoardsOf(int tnr) =>
        Boards.GetValueOrDefault(tnr) is > 0 and var b ? b : T[tnr].Level switch { 1 => 6, 2 => 6, 3 => 5, 4 => 4, _ => 6 };
}

/// <summary>Merkmale eines gemeldeten Spielers für eine Runde (Python: features.rows_for).</summary>
public sealed record FeatureRow
{
    public int Tnr { get; init; }
    public string Team { get; init; } = "";
    public int Round { get; init; }
    public string Pid { get; init; } = "";
    public string? Fide { get; init; }
    public string Name { get; init; } = "";
    public int? Rb { get; init; }
    public int Elo { get; init; }
    public int Level { get; init; }
    public int B { get; init; }
    public double Pos { get; init; }
    public int Top { get; init; }
    public int Bench { get; init; }
    public double QSame { get; init; }
    public double QHigher { get; init; }
    public double QLower { get; init; }
    public int NewPrev { get; init; }
    public int NewEver { get; init; }
    public int N { get; init; }
    public int First { get; init; }
    public double Cur { get; init; }
    public int Last { get; init; }
    public int Last2 { get; init; }
    public int Yesterday { get; init; }
    public int YestPlayed { get; init; }
    public double ConflictHi { get; init; }
    public double ConflictLo { get; init; }
}

public static class LeagueFeatures
{
    public static List<FeatureRow> RowsFor(LeagueWorld w, int tnr, string team, int rnd, DateOnly? asof = null)
    {
        var t = w.T[tnr];
        var season = t.Season;
        var level = t.Level;
        var b = w.BoardsOf(tnr);
        var date = w.RDate.GetValueOrDefault((tnr, rnd));
        var sched = w.SchedOf(tnr, team);
        var cut = asof ?? date;
        var past = sched.Where(s => s.Round < rnd && w.Real.Contains((tnr, s.Round, team))
                                    && (cut is null || s.Date is null || s.Date < cut)).ToList();
        if (!sched.Any(s => s.Round == rnd)) return new();
        var n = past.Count;
        var soFar = new Dictionary<string, int>();
        foreach (var s in past)
            foreach (var p in w.LineupOf(tnr, s.Round, team).Keys)
                soFar[p] = soFar.GetValueOrDefault(p) + 1;
        var last = n > 0 ? w.LineupOf(tnr, past[^1].Round, team) : new Dictionary<string, int>();
        var last2 = n > 1 ? w.LineupOf(tnr, past[^2].Round, team) : new Dictionary<string, int>();
        var yesterday = n > 0 && date is not null && past[^1].Date is not null
                        && date.Value.DayNumber - past[^1].Date!.Value.DayNumber == 1;
        var ps = w.PrevSeason(season);
        var mptPrev = ps is null ? 0 : w.Mpt.GetValueOrDefault((ps, level));
        var club = LeagueNames.Club(team);
        var sameDay = new List<(int Tnr, string Team, int Round)>();
        if (w.ClubTeams.TryGetValue((season, club), out var clubTeams))
            foreach (var (tnr2, team2) in clubTeams)
            {
                if (tnr2 == tnr && team2 == team) continue;
                foreach (var s2 in w.SchedOf(tnr2, team2))
                    if (date is not null && s2.Date == date) sameDay.Add((tnr2, team2, s2.Round));
            }
        double MptOr1(string? s, int l) => Math.Max(1, s is null ? 1 : w.Mpt.TryGetValue((s, l), out var v) ? v : 1);

        var roster = w.Roster.TryGetValue((tnr, team), out var r) ? r : new List<RosterEntry>();
        var rows = new List<FeatureRow>(roster.Count);
        for (var i = 0; i < roster.Count; i++)
        {
            var e = roster[i];
            var p = e.Pid;
            var qSame = mptPrev > 0 ? (double)w.Apps(ps, level, p) / mptPrev : 0;
            double qHigher = 0, qLower = 0;
            if (ps is not null)
            {
                for (var l = 1; l < level; l++) qHigher += w.Apps(ps, l, p) / MptOr1(ps, l);
                for (var l = level + 1; l < 5; l++) qLower += w.Apps(ps, l, p) / MptOr1(ps, l);
            }
            var knownPrev = qSame + qHigher + qLower > 0;
            var knownEver = w.Seasons.Where(s => string.CompareOrdinal(s, season) < 0)
                .Any(s => Enumerable.Range(1, 4).Any(l => w.Apps(s, l, p) > 0));
            double cHi = 0, cLo = 0;
            foreach (var (tnr2, team2, r2) in sameDay)
            {
                var past2 = w.SchedOf(tnr2, team2).Where(s => s.Round < r2 && w.Real.Contains((tnr2, s.Round, team2))
                                                              && (cut is null || s.Date is null || s.Date < cut))
                    .Select(s => s.Round).ToList();
                var lvl2 = w.T[tnr2].Level;
                double rate = past2.Count > 0
                    ? (double)past2.Count(x => w.LineupOf(tnr2, x, team2).ContainsKey(p)) / past2.Count
                    : ps is not null ? w.Apps(ps, lvl2, p) / MptOr1(ps, lvl2) : 0;
                var inRoster = w.Roster.TryGetValue((tnr2, team2), out var r2list) && r2list.Any(x => x.Pid == p);
                if (!inRoster) continue;
                if (lvl2 < level) cHi = Math.Max(cHi, rate); else cLo = Math.Max(cLo, rate);
            }
            rows.Add(new FeatureRow
            {
                Tnr = tnr, Team = team, Round = rnd, Pid = p, Fide = e.Fide, Name = e.Name, Rb = e.Rb, Elo = e.Elo,
                Level = level, B = b, Pos = (double)i / b, Top = i < b ? 1 : 0, Bench = i >= b && i < 2 * b ? 1 : 0,
                QSame = Math.Min(qSame, 1.0), QHigher = Math.Min(qHigher, 1.0), QLower = Math.Min(qLower, 1.0),
                NewPrev = knownPrev ? 0 : 1, NewEver = knownEver ? 0 : 1,
                N = n, First = n == 0 ? 1 : 0, Cur = n > 0 ? (double)soFar.GetValueOrDefault(p) / n : 0.0,
                Last = last.ContainsKey(p) ? 1 : 0, Last2 = last2.ContainsKey(p) ? 1 : 0,
                Yesterday = yesterday ? 1 : 0, YestPlayed = yesterday && last.ContainsKey(p) ? 1 : 0,
                ConflictHi = cHi, ConflictLo = cLo,
            });
        }
        return rows;
    }
}

/// <summary>Logistisches Modell + Normierung auf B Bretter + Brett-Wahrscheinlichkeiten (Python: model.py).</summary>
public sealed class LeagueModel
{
    public IReadOnlyList<string> Features { get; }
    public double[] Weights { get; }

    public LeagueModel(IReadOnlyList<string> features, double[] weights)
    {
        Features = features;
        Weights = weights;
    }

    /// <summary>Gewichte aus der eingebetteten <c>Assets/league-model.json</c>.</summary>
    public static LeagueModel FromEmbedded()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().First(n => n.EndsWith("league-model.json", StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(name)!;
        using var doc = JsonDocument.Parse(s);
        var f = doc.RootElement.GetProperty("features").EnumerateArray().Select(x => x.GetString()!).ToList();
        var w = doc.RootElement.GetProperty("weights").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        return new LeagueModel(f, w);
    }

    /// <summary>Merkmalsvektor in der Reihenfolge von <see cref="Features"/> (Python: model.vec).</summary>
    public double[] Vec(FeatureRow r)
    {
        var lvl4 = r.Level == 4 ? 1 : 0;
        var v = new double[Features.Count];
        for (var i = 0; i < Features.Count; i++)
        {
            v[i] = Features[i] switch
            {
                "const" => 1,
                "top" => r.Top,
                "bench" => r.Bench,
                "pos_c" => Math.Min(r.Pos, 4.0),
                "q_same" => r.QSame,
                "q_higher" => r.QHigher,
                "q_lower" => r.QLower,
                "new_prev" => r.NewPrev,
                "new_ever" => r.NewEver,
                "first" => r.First,
                "q_same_first" => r.QSame * r.First,
                "cur" => r.Cur,
                "cur_n" => r.Cur * Math.Min(r.N, 6) / 6.0,
                "last" => r.Last,
                "last2" => r.Last2,
                "yesterday" => r.Yesterday,
                "yest_played" => r.YestPlayed,
                "yest_not" => r.Yesterday == 1 && r.YestPlayed == 0 ? 1 : 0,
                "conflict_hi" => r.ConflictHi,
                "conflict_lo" => r.ConflictLo,
                "lvl2" => r.Level == 2 ? 1 : 0,
                "lvl3" => r.Level == 3 ? 1 : 0,
                "lvl4" => lvl4,
                "gk_q" => r.QSame * lvl4,
                var x => throw new InvalidOperationException($"Unbekanntes Merkmal im Modell: {x}"),
            };
        }
        return v;
    }

    public double Logit(FeatureRow r)
    {
        var v = Vec(r);
        double s = 0;
        for (var i = 0; i < v.Length; i++) s += v[i] * Weights[i];
        return s;
    }

    private static double Sigmoid(double x) => 1 / (1 + Math.Exp(-x));

    /// <summary>Verschiebung s, so dass Σ sigmoid(logit+s) = B (genau B Spieler werden aufgestellt).</summary>
    public static double[] Normalize(IReadOnlyList<double> logits, int b)
    {
        b = Math.Min(b, logits.Count);
        double lo = -20, hi = 20;
        for (var it = 0; it < 60; it++)
        {
            var s = (lo + hi) / 2;
            var tot = logits.Sum(l => Sigmoid(l + s));
            if (tot < b) lo = s; else hi = s;
        }
        var shift = (lo + hi) / 2;
        return logits.Select(l => Sigmoid(l + shift)).ToArray();
    }

    /// <summary>Einsatz-Wahrscheinlichkeiten eines Matches, normiert auf B.</summary>
    public double[] Predict(IReadOnlyList<FeatureRow> rows) =>
        rows.Count == 0 ? Array.Empty<double>() : Normalize(rows.Select(Logit).ToList(), rows[0].B);

    /// <summary>Sonntag vorhersagen, bevor der Samstag gespielt ist: Mischung aus „hat Samstag gespielt" und „nicht".</summary>
    public double[] SundayAdvance(IReadOnlyList<FeatureRow> rowsSat, IReadOnlyList<FeatureRow> rowsSun)
    {
        var pSat = Predict(rowsSat);
        var sat = new Dictionary<string, double>();
        for (var i = 0; i < rowsSat.Count; i++) sat[rowsSat[i].Pid] = pSat[i];
        var mix = new List<double>(rowsSun.Count);
        foreach (var r in rowsSun)
        {
            var n = r.N;
            var a = r with { First = 0, N = n + 1, Yesterday = 1, Last2 = r.Last };
            var a1 = a with { Last = 1, YestPlayed = 1, Cur = (r.Cur * n + 1) / (n + 1) };
            var a0 = a with { Last = 0, YestPlayed = 0, Cur = r.Cur * n / (n + 1) };
            var ps = sat.GetValueOrDefault(r.Pid, 0.0);
            var m = Math.Clamp(ps * Sigmoid(Logit(a1)) + (1 - ps) * Sigmoid(Logit(a0)), 1e-6, 1 - 1e-6);
            mix.Add(Math.Log(m / (1 - m)));
        }
        return rowsSun.Count == 0 ? Array.Empty<double>() : Normalize(mix, rowsSun[0].B);
    }

    // ---- Brett-Wahrscheinlichkeiten ---------------------------------------------------------------

    /// <summary>Elementare symmetrische Polynome e_0..e_kmax.</summary>
    private static double[] Esp(IEnumerable<double> o, int kmax)
    {
        var e = new double[kmax + 1];
        e[0] = 1;
        foreach (var x in o)
            for (var k = kmax; k >= 1; k--) e[k] += x * e[k - 1];
        return e;
    }

    private static double[,] BoardMatrix(double[] o, int b)
    {
        var n = o.Length;
        var tot = Esp(o, b)[b];
        var m = new double[n, b];
        for (var i = 0; i < n; i++)
        {
            var before = Esp(o.Take(i), b);
            var after = Esp(o.Skip(i + 1), b);
            for (var k = 0; k < b; k++) m[i, k] = o[i] * before[k] * after[b - k - 1] / tot;
        }
        return m;
    }

    /// <summary>Gewichte der bedingten Bernoulli-Verteilung so anpassen, dass die Einsatz-Randwerte p ergeben.</summary>
    public static double[] FittedOdds(IReadOnlyList<double> p0, int b, int iters = 200)
    {
        var p = p0.Select(x => Math.Clamp(x, 1e-4, 1 - 1e-4)).ToArray();
        b = Math.Min(b, p.Length);
        var o = p.Select(x => x / (1 - x)).ToArray();
        for (var it = 0; it < iters; it++)
        {
            var m = BoardMatrix(o, b);
            double maxDiff = 0;
            var pi = new double[p.Length];
            for (var i = 0; i < p.Length; i++)
            {
                for (var k = 0; k < b; k++) pi[i] += m[i, k];
                maxDiff = Math.Max(maxDiff, Math.Abs(pi[i] - p[i]));
            }
            if (maxDiff < 1e-4) break;
            for (var i = 0; i < p.Length; i++) o[i] *= Math.Pow(p[i] / Math.Max(pi[i], 1e-9), 0.7);
        }
        return o;
    }

    /// <summary>P(Spieler i sitzt an Brett k) — Spieler in Meldelisten-Reihenfolge.</summary>
    public static double[,] BoardProbs(IReadOnlyList<double> p, int b)
    {
        b = Math.Min(b, p.Count);
        return BoardMatrix(FittedOdds(p, b), b);
    }
}

internal static class LeagueDates
{
    public static DateOnly? Parse(string? s) =>
        DateOnly.TryParseExact(s ?? "", "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
