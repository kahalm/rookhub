using System.Diagnostics;
using System.Globalization;
using Chess;

namespace RookHub.Api.Services;

/// <summary>Eine Engine für <see cref="ScoresheetPlausibility"/>. <c>null</c> = keine Engine da (Lesung bleibt, wie sie ist).</summary>
public interface IScoresheetEngine
{
    Task<IScoresheetEngineSession?> OpenAsync(CancellationToken ct = default);
}

/// <summary>Eine offene Engine — ein Prozess für eine ganze Prüfung, damit nicht jede Lesart einen neuen startet.</summary>
public interface IScoresheetEngineSession : IAsyncDisposable
{
    /// <summary>Je Stellung (Start + nach jedem Zug) die Bewertung aus WEISS-Sicht in Centibauern, Matt als
    /// ±<see cref="ScoresheetPlausibility.MateCp"/>; <c>null</c> = die Engine hat versagt.</summary>
    Task<List<int>?> EvaluateLineAsync(string startFen, IReadOnlyList<string> uciMoves, CancellationToken ct = default);
}

/// <summary>
/// Engine-Prüfung einer gelesenen Partie (0.646.0). Gemeldet 2026-10-03 an Gruber–Schöler (LeagueHub, Vereinspartie 153):
/// „viele Zickzack in der Bewertung — meist ein Zeichen, dass ein Zug nicht richtig erkannt wurde". So war es: das Modell
/// las „Tc1" als „Te1" — ein legaler Zug, also nahm der Auflöser ihn —, und ab da stand der Turm neben einem schwarzen
/// Bauern auf f2, der ihn zehn Züge lang mit Umwandlung hätte schlagen können und es nie tat; dasselbe bei „Ke4" →
/// „Kc4". Jeder Halbzug verlor in der Bewertung mehrere Bauern und der nächste gab sie zurück. Der Auflöser kennt nur
/// Schrift und Legalität — dass Spieler einen Gewinn nicht zehnmal liegen lassen, weiß erst eine Engine.
///
/// <para><b>Vorgehen.</b> Die Engine bewertet die gelesene Partie. Ein ZICKZACK ist ein Halbzug, der mindestens
/// <see cref="SwingPct"/> Prozentpunkte Gewinnchance verliert, gefolgt von einem, der ebenso viel verliert (also zurückgibt).
/// Für die Halbzüge davor (<see cref="Window"/>) werden Ersatz-Lesarten des Eintrags gesucht, die die Schrift fast genauso
/// gut erklären (<see cref="ScoresheetResolver.Score"/> höchstens <see cref="MaxExtraCost"/> teurer — ein leicht
/// verwechselbares Zeichen — und absolut höchstens <see cref="MaxCandidateCost"/>), der Rest wird ab dort über ein Fenster neu gelesen und neu bewertet.</para>
///
/// <para><b>Zwei Stufen, weil Amateure auch wirklich patzen.</b> Ein kurzes Zickzack (Patzer, verpasste Widerlegung)
/// kommt in echten Partien vor; dort wird NICHTS geändert — der Zug wird nur als unsicher markiert (<c>Check =
/// suggested</c>), die Lesart ohne Zickzack steht als zweite Option daneben. Ersetzt wird nur, wenn (1) mindestens
/// <see cref="PersistentPlies"/> Halbzüge nacheinander patzen (ein Gewinn wird über Züge hinweg ignoriert — das spielt so
/// niemand), (2) die Ersatz-Lesart nur um ein leicht verwechselbares Zeichen abweicht (<see cref="MaxExtraCost"/>,
/// c/e, 1/7 …), (3) sie mindestens <see cref="MinReplaceGain"/> Zickzacks im Fenster beseitigt und (4) der Rest der
/// Partie danach nicht schlechter zum Formular passt (nicht mehr zurechtgebogene Züge, nicht mehr Unaufgelöstes). Dann steht der Zug als
/// unsicher da (<c>Check = replaced</c>), die alte Lesung als zweite Option.</para>
///
/// <para>Eine Engine, die versagt, oder ein abgelaufenes Zeitbudget (<see cref="Budget"/>) lassen die Lesung, wie sie ist.</para>
/// </summary>
public static class ScoresheetPlausibility
{
    /// <summary>So viel GEWINNCHANCE (Prozentpunkte, Lichess-Formel <see cref="GameAccuracy.WinPercent"/>) verliert ein
    /// Halbzug mindestens, um als grober Patzer zu zählen — Lichess' Grenze für „Blunder" (0,3 auf −1..1). In Gewinnchance
    /// statt Centibauern, damit eine schon entschiedene Stellung (+15 → +9 nach einem verpassten Matt) nicht zählt.</summary>
    public const double SwingPct = 15;

    /// <summary>Ein Matt kommt als ±so viele Centibauern — die Gewinnchance kappt ohnehin bei ±1000.</summary>
    public const int MateCp = 1500;

    /// <summary>So viele Halbzüge bis EINSCHLIESSLICH des ersten Patzers kommt der Lesefehler in Frage. Bei „Kc4" statt
    /// „Ke4" kam der erste Patzer zwei Halbzüge später (53.Sf5+).</summary>
    public const int Window = 4;

    /// <summary>So viel teurer als die gewählte darf eine Ersatz-Lesart sein: EIN leicht verwechselbares Zeichen (c/e, 1/7,
    /// 5/6 … kostet 2,0, ein beliebiges 2,5). Gemessen am 10er-Testsatz (perfekte Lesung und Abschrift, 20 Durchläufe): mit
    /// 2,5 standen 13 richtige Züge als unsicher da — echte Amateur-Patzer, die eine beliebige Ein-Zeichen-Lesart
    /// „glättet" —, mit 2,0 drei. Die Fehllesungen der Gruber-Partie (Te1/Tc1, Kc4/Ke4) liegen beide bei 2,0.</summary>
    public const double MaxExtraCost = 2.0;

    /// <summary>… an einem Eintrag, den das Modell selbst als „low" gelesen hat, ein beliebiges Zeichen (nur angeboten).</summary>
    public const double MaxExtraCostDoubted = 2.5;

    /// <summary>… und absolut höchstens so teuer — eine Lesart mit zwei Lesefehlern erklärt den Eintrag nicht mehr.</summary>
    public const double MaxCandidateCost = 3.0;

    /// <summary>Ab so vielen aufeinanderfolgenden Patzer-Halbzügen gilt ein Zickzack als anhaltend.</summary>
    public const int PersistentPlies = 4;

    /// <summary>So viele Zickzacks muss eine Ersatz-Lesart im Fenster beseitigen, um übernommen zu werden.</summary>
    public const int MinReplaceGain = 2;

    /// <summary>So weit hinter dem Zickzack wird eine Ersatz-Lesart bewertet (Halbzüge).</summary>
    public const int Horizon = 16;

    /// <summary>Höchstens so viele Stellen je Partie.</summary>
    public const int MaxFixes = 4;

    /// <summary>Höchstens so viele gleich gute Stellen werden je Zickzack angeboten.</summary>
    public const int MaxSuggestionsPerZigZag = 3;

    /// <summary>Höchstens so viele Ersatz-Lesarten je Halbzug werden durchgerechnet (die billigsten).</summary>
    public const int MaxCandidates = 6;

    /// <summary>Zeitbudget der ganzen Prüfung — danach bleibt der Rest, wie er ist.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(90);

    /// <summary>Werte von <see cref="ScoresheetPly.Check"/>.</summary>
    public static class Checks
    {
        public const string Replaced = "replaced";
        public const string Suggested = "suggested";
    }

    public sealed record Outcome(ScoresheetResolution Resolution, List<int> Replaced, List<int> Flagged);

    private sealed record Candidate(int K, string San, string Uci, string Match, double Cost, double Extra,
        List<ScoresheetPly> After, (int Zig, double Excess) Bad);

    public static async Task<Outcome> ImproveAsync(IReadOnlyList<ScannedPly> scanned, ScoresheetResolver.Options options,
        ScoresheetResolution resolution, IScoresheetEngine engine, string? startFen = null, CancellationToken ct = default,
        Action<string>? trace = null)
    {
        var unchanged = new Outcome(resolution, new(), new());
        if (resolution.Plies.Count < 2) return unchanged;
        await using var session = await engine.OpenAsync(ct);
        if (session == null) return unchanged;

        var clock = Stopwatch.StartNew();
        var fen0 = startFen ?? GamePlies.StartFen();
        var whiteFirst = fen0.Split(' ').ElementAtOrDefault(1) != "b";
        var plies = resolution.Plies.ToList();
        var unresolved = resolution.Unresolved;
        var stuckAt = resolution.StuckAt;
        var stuckFen = resolution.StuckFen;
        var skipped = resolution.Skipped.ToList();
        var replaced = new List<int>();
        var flagged = new List<int>();
        var evals = await session.EvaluateLineAsync(fen0, plies.Select(p => p.Uci).ToList(), ct);
        if (evals == null || evals.Count != plies.Count + 1) return unchanged;

        var from = 0;
        for (var round = 0; round < MaxFixes && clock.Elapsed < Budget; round++)
        {
            var s = FindZigZag(evals, whiteFirst, from);
            if (s < 0) break;
            var run = BlunderRun(evals, whiteFirst, s);
            var fens = Fens(fen0, plies);
            var lo = Math.Max(0, s - Window + 1);
            var spanEnd = Math.Min(plies.Count, s + Horizon);
            var baseline = Badness(evals, whiteFirst, lo, spanEnd);
            trace?.Invoke($"zigzag at {s} ({plies[s].San}), run {run}, losses {Loss(evals, whiteFirst, s):F0}/{Loss(evals, whiteFirst, s + 1):F0}, baseline {baseline}");

            var found = new List<Candidate>();
            for (var k = lo; k <= s && k < fens.Count && clock.Elapsed < Budget; k++)
            {
                if (plies[k].W is not int w || w >= scanned.Count) continue;
                var squares = ScoresheetResolver.Squares(fens[k]);
                var chosen = ScoresheetResolver.Score(scanned[w], plies[k].San, plies[k].Uci, options, squares).Cost;
                if (double.IsInfinity(chosen)) continue;
                // Wie viele Einträge das Fenster braucht: so viele, wie die gelesene Partie bis spanEnd verbraucht hat.
                var lastW = plies.Take(spanEnd).LastOrDefault(p => p.W != null)?.W ?? w;
                foreach (var (san, uci, match, cost) in Alternatives(fens[k], scanned[w], options, squares, plies[k].Uci, chosen))
                {
                    ct.ThrowIfCancellationRequested();
                    var prefix = plies.Take(k).Select(p => p.San).Append(san).ToList();
                    ScoresheetResolution rest;
                    try
                    {
                        rest = ScoresheetResolver.Resolve(scanned, options, prefix, w + 1, fen0, withBranches: false,
                            writtenTo: lastW + 1);
                    }
                    catch (ArgumentException) { continue; }
                    // Bleibt die neue Lesung im Fenster hängen, wo die alte durchkam, erklärt sie das Formular schlechter.
                    if (rest.StuckAt is int st && (stuckAt == null || st < stuckAt)) continue;
                    var line = rest.Plies.Select(p => p.Uci).Prepend(uci).ToList();
                    var end = Math.Min(k + line.Count, spanEnd);
                    var part = await session.EvaluateLineAsync(fens[k], line.Take(end - k).ToList(), ct);
                    if (part == null) return new(Rebuild(plies, unresolved, stuckAt, stuckFen, skipped), replaced, flagged);
                    var combined = evals.Take(k).Concat(part).ToList();
                    var bad = Badness(combined, whiteFirst, lo, end);
                    trace?.Invoke($"  k={k} {plies[k].San}->{san} cost {cost:F1} (chosen {chosen:F1}) bad {bad}");
                    if (bad.Zig < baseline.Zig) found.Add(new Candidate(k, san, uci, match, cost, cost - chosen, rest.Plies, bad));
                }
            }

            if (found.Count == 0)
            {
                // Nichts erklärt das Zickzack besser — vielleicht ist es echt. Nichts markieren: ein echter Patzer ist kein
                // Lesefehler, und eine Markierung ohne Alternative hilft niemandem.
                from = s + run;
                continue;
            }

            // Die beste je Halbzug; GLEICH GUTE an verschiedenen Halbzügen (gleich viele Zickzacks, gleich teure Lesart)
            // kann die Engine nicht unterscheiden — „Ra6" statt „Ra5+" beseitigt das Zickzack von „Kc4" genauso wie
            // „Ke4". Dann wird nichts ersetzt, sondern jede dieser Stellen angeboten.
            var perPly = found.GroupBy(c => c.K).Select(g => g.OrderBy(c => c, CandidateOrder.Instance).First())
                .OrderBy(c => c, CandidateOrder.Instance).ToList();
            var best = perPly[0];
            var tied = perPly.Where(c => c.Bad.Zig == best.Bad.Zig && Math.Abs(c.Extra - best.Extra) < 0.01).ToList();
            var replace = tied.Count == 1 && run >= PersistentPlies && best.Extra <= MaxExtraCost + 1e-9
                && baseline.Zig - best.Bad.Zig >= MinReplaceGain;
            ScoresheetResolution? full = null;
            if (replace)
            {
                // Übernehmen: der Rest kommt aus einer vollen neuen Auflösung — mit Lesarten, wie sie die Korrekturseite
                // braucht. Erklärt sie das Formular über das Fenster hinaus schlechter, wird nur angeboten.
                var prefixSans = plies.Take(best.K).Select(p => p.San).Append(best.San).ToList();
                full = ScoresheetResolver.Resolve(scanned, options, prefixSans, plies[best.K].W!.Value + 1, fen0, withBranches: true);
                // Der Rest muss mindestens so glatt zum Formular passen wie vorher. Am Testsatz (Beleg 04, Abschrift mit
                // echten Notationsfehlern) glättete „Lf3+" statt „Lxf5" das Zickzack, bog dafür aber den Rest der Partie
                // an elf Stellen zurecht — eine Lesart, die das Formular schlechter erklärt, ist keine Korrektur.
                if (full.Unresolved.Count > unresolved.Count || Bent(full.Plies) > Bent(plies.Skip(best.K + 1))) full = null;
            }
            if (full == null)
            {
                // Nur anbieten: der gelesene Zug bleibt, unsicher, die Lesart ohne Zickzack als zweite.
                foreach (var c in tied.Take(MaxSuggestionsPerZigZag))
                {
                    Suggest(plies, c);
                    flagged.Add(c.K);
                    trace?.Invoke($"  -> suggest {c.San} at {c.K} extra {c.Extra:F1} conf {scanned[plies[c.K].W!.Value].Confidence}");
                }
                from = s + run;
                continue;
            }

            var original = plies[best.K];
            var (newOption, oldOption, others) = OptionsFor(plies, best);
            var head = plies.Take(best.K).ToList();
            head.Add(new ScoresheetPly
            {
                W = original.W, Written = original.Written, San = best.San, Uci = best.Uci, Match = best.Match,
                Uncertain = true, Check = Checks.Replaced,
                Options = new[] { newOption, oldOption }.Concat(others).ToList(),
            });
            plies = head.Concat(full.Plies).ToList();
            unresolved = full.Unresolved;
            stuckAt = full.StuckAt;
            stuckFen = full.StuckFen;
            // Doppelt notierte Einträge: die vor der Stelle bleiben, die dahinter kommen aus der neuen Auflösung (deren
            // Zählung beginnt hinter dem Präfix).
            skipped = skipped.Where(x => x.AfterPly <= best.K)
                .Concat(full.Skipped.Select(x => new ScoresheetSkip { W = x.W, Written = x.Written, AfterPly = x.AfterPly + best.K + 1 }))
                .ToList();
            replaced.Add(best.K);
            trace?.Invoke($"  -> replace {original.San} by {best.San} at {best.K}");
            var again = await session.EvaluateLineAsync(fen0, plies.Select(p => p.Uci).ToList(), ct);
            if (again == null || again.Count != plies.Count + 1) break;
            evals = again;
            from = best.K + 1;
        }
        return replaced.Count == 0 && flagged.Count == 0
            ? unchanged
            : new(Rebuild(plies, unresolved, stuckAt, stuckFen, skipped), replaced, flagged);
    }

    /// <summary>Weniger Zickzacks, dann die billigere Lesart (die SCHRIFT entscheidet vor kleinen Bewertungsunterschieden —
    /// „Te1" → Tc1 kostet 2,0, Td1 2,5, und beide beseitigen das Zickzack), dann weniger Überschuss, dann die frühere Stelle.</summary>
    private sealed class CandidateOrder : IComparer<Candidate>
    {
        public static readonly CandidateOrder Instance = new();

        public int Compare(Candidate? a, Candidate? b)
        {
            if (a == null || b == null) return 0;
            if (a.Bad.Zig != b.Bad.Zig) return a.Bad.Zig.CompareTo(b.Bad.Zig);
            if (Math.Abs(a.Extra - b.Extra) > 1e-9) return a.Extra.CompareTo(b.Extra);
            if (Math.Abs(a.Bad.Excess - b.Bad.Excess) > 1e-6) return a.Bad.Excess.CompareTo(b.Bad.Excess);
            return a.K.CompareTo(b.K);
        }
    }

    /// <summary>Lesarten für die Korrekturseite: die neue mit ihrer Reichweite, die alte (aus dem Auflöser, sonst neu
    /// gezählt), die übrigen des Auflösers.</summary>
    private static (ScoresheetOption New, ScoresheetOption Old, List<ScoresheetOption> Others) OptionsFor(
        List<ScoresheetPly> plies, Candidate c)
    {
        var original = plies[c.K];
        var newOption = new ScoresheetOption
        {
            San = c.San, Uci = c.Uci, Match = c.Match, Reach = CleanReach(c.After),
            Preview = c.After.Take(4).Select(p => p.San).ToList(),
        };
        var oldOption = original.Options?.FirstOrDefault(o => o.Uci == original.Uci) ?? new ScoresheetOption
        {
            San = original.San, Uci = original.Uci, Match = original.Match, Reach = CleanReach(plies.Skip(c.K + 1)),
            Preview = plies.Skip(c.K + 1).Take(4).Select(p => p.San).ToList(),
        };
        var others = (original.Options ?? new()).Where(o => o.Uci != c.Uci && o.Uci != original.Uci).ToList();
        return (newOption, oldOption, others);
    }

    /// <summary>Den Halbzug unsicher machen; der gelesene Zug bleibt erste Lesart, die ohne Zickzack wird zweite.</summary>
    private static void Suggest(List<ScoresheetPly> plies, Candidate c)
    {
        var (newOption, oldOption, others) = OptionsFor(plies, c);
        var ply = plies[c.K];
        ply.Uncertain = true;
        ply.Check = Checks.Suggested;
        ply.Options = new[] { oldOption, newOption }.Concat(others).ToList();
    }

    private static ScoresheetResolution Rebuild(List<ScoresheetPly> plies, List<string> unresolved, int? stuckAt,
        string? stuckFen, List<ScoresheetSkip> skipped) => new()
    {
        Plies = plies,
        Unresolved = unresolved,
        StuckAt = stuckAt,
        StuckFen = stuckFen,
        Skipped = skipped,
    };

    /// <summary>Zurechtgebogene Züge (Lesefehler, Joker, eingeschoben).</summary>
    private static int Bent(IEnumerable<ScoresheetPly> plies) => plies.Count(p => p.Match is ScoresheetResolver.Matches.Fuzzy
        or ScoresheetResolver.Matches.Guess or ScoresheetResolver.Matches.Inserted);

    /// <summary>Wie weit eine Lesart glatt trägt — dieselbe Zählung wie <c>ScoresheetResolver.CleanReach</c>.</summary>
    private static int CleanReach(IEnumerable<ScoresheetPly> after) => Math.Min(ScoresheetResolver.ReachHorizon,
        after.TakeWhile(p => p.Match is not (ScoresheetResolver.Matches.Fuzzy or ScoresheetResolver.Matches.Guess
            or ScoresheetResolver.Matches.Inserted)).Count(p => p.W != null));

    /// <summary>Die legalen Züge, die den Eintrag fast so gut erklären wie der gewählte — billigste zuerst.</summary>
    internal static List<(string San, string Uci, string Match, double Cost)> Alternatives(string fen, ScannedPly entry,
        ScoresheetResolver.Options options, char[] squares, string chosenUci, double chosenCost)
    {
        List<Move> moves;
        try { moves = ChessBoard.LoadFromFen(fen).Moves(generateSan: true).ToList(); }
        catch { return new(); }
        var maxExtra = entry.Confidence == "low" ? MaxExtraCostDoubted : MaxExtraCost;
        var result = new List<(string San, string Uci, string Match, double Cost)>();
        foreach (var m in moves)
        {
            var uci = GamePlies.ToUci(m);
            if (uci == chosenUci || m.San == null) continue;
            var (cost, match) = ScoresheetResolver.Score(entry, m.San, uci, options, squares);
            if (double.IsInfinity(cost) || cost > chosenCost + maxExtra + 1e-9 || cost > MaxCandidateCost) continue;
            result.Add((m.San, uci, match, cost));
        }
        return result.OrderBy(r => r.Cost).ThenBy(r => r.Uci, StringComparer.Ordinal).Take(MaxCandidates).ToList();
    }

    private static List<string> Fens(string fen0, IReadOnlyList<ScoresheetPly> plies)
    {
        var board = ChessBoard.LoadFromFen(fen0);
        var fens = new List<string> { board.ToFen() };
        foreach (var p in plies)
        {
            if (!board.Move(p.San)) break;
            fens.Add(board.ToFen());
        }
        return fens;
    }

    /// <summary>Verlust des Halbzugs <paramref name="i"/> an Gewinnchance (Prozentpunkte) aus Sicht des Ziehenden
    /// (Bewertungen nach <paramref name="i"/> bzw. <paramref name="i"/>+1 Halbzügen).</summary>
    internal static double Loss(IReadOnlyList<int> evals, bool whiteFirst, int i)
    {
        var whiteMoves = (i % 2 == 0) == whiteFirst;
        var delta = GameAccuracy.WinPercent(evals[i + 1], null)!.Value - GameAccuracy.WinPercent(evals[i], null)!.Value;
        return whiteMoves ? -delta : delta;
    }

    /// <summary>Der erste Halbzug ab <paramref name="from"/>, der mindestens <see cref="SwingPct"/> verliert, wenn der
    /// nächste ebenso viel verliert; −1 = keiner.</summary>
    internal static int FindZigZag(IReadOnlyList<int> evals, bool whiteFirst, int from)
    {
        for (var i = Math.Max(0, from); i + 2 < evals.Count; i++)
            if (Loss(evals, whiteFirst, i) >= SwingPct && Loss(evals, whiteFirst, i + 1) >= SwingPct) return i;
        return -1;
    }

    /// <summary>Wie viele Halbzüge ab <paramref name="s"/> nacheinander mindestens <see cref="SwingPct"/> verlieren.</summary>
    internal static int BlunderRun(IReadOnlyList<int> evals, bool whiteFirst, int s)
    {
        var n = 0;
        for (var i = s; i + 1 < evals.Count && Loss(evals, whiteFirst, i) >= SwingPct; i++) n++;
        return n;
    }

    /// <summary>Zickzack-Paare im Bereich [<paramref name="from"/>, <paramref name="to"/>) und die Summe der Verluste über
    /// <see cref="SwingPct"/> — weniger ist besser.</summary>
    internal static (int Zig, double Excess) Badness(IReadOnlyList<int> evals, bool whiteFirst, int from, int to)
    {
        var zig = 0;
        var excess = 0.0;
        for (var i = Math.Max(0, from); i < to && i + 1 < evals.Count; i++)
        {
            var loss = Loss(evals, whiteFirst, i);
            if (loss < SwingPct) continue;
            excess += loss - SwingPct;
            if (i + 2 < evals.Count && Loss(evals, whiteFirst, i + 1) >= SwingPct) zig++;
        }
        return (zig, excess);
    }
}

/// <summary>
/// Stockfish über UCI: ein Prozess je Prüfung, je Stellung <c>go depth</c>, die letzte Bewertung zählt. Pfad
/// <c>Scoresheet:EnginePath</c>, sonst <c>/usr/games/stockfish</c> (Debian-Paket im API-Image — dort NICHT im PATH),
/// sonst <c>stockfish</c>; Tiefe <c>Scoresheet:EngineDepth</c> (Vorgabe 10 — gemessen ~7 ms je Stellung). Fehlt die
/// Engine oder antwortet sie nicht, kommt <c>null</c>: die Lesung bleibt, wie sie ist.
/// </summary>
public sealed class StockfishScoresheetEngine : IScoresheetEngine
{
    private readonly string _path;
    private readonly int _depth;
    private readonly ILogger<StockfishScoresheetEngine> _logger;

    /// <summary>Höchstens so lange je Antwort, bevor die Engine als hängend gilt.</summary>
    internal static readonly TimeSpan PerAnswer = TimeSpan.FromSeconds(10);

    public StockfishScoresheetEngine(IConfiguration config, ILogger<StockfishScoresheetEngine> logger)
    {
        _logger = logger;
        var configured = config["Scoresheet:EnginePath"];
        _path = !string.IsNullOrWhiteSpace(configured) ? configured
            : File.Exists("/usr/games/stockfish") ? "/usr/games/stockfish" : "stockfish";
        _depth = int.TryParse(config["Scoresheet:EngineDepth"], out var d) && d is >= 4 and <= 24 ? d : 10;
    }

    public async Task<IScoresheetEngineSession?> OpenAsync(CancellationToken ct = default)
    {
        Process? p = null;
        try
        {
            p = Process.Start(new ProcessStartInfo(_path)
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            });
            if (p == null) return null;
            var session = new Session(p, _depth, _logger);
            if (await session.HandshakeAsync(ct)) return session;
            await session.DisposeAsync();
            _logger.LogWarning("Formular-Plausibilität: Engine {Path} antwortet nicht.", _path);
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Kill(p);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Formular-Plausibilität: Engine {Path} nicht nutzbar.", _path);
            Kill(p);
            return null;
        }
    }

    private static void Kill(Process? p)
    {
        try { if (p is { HasExited: false }) p.Kill(); } catch { /* schon weg */ }
        p?.Dispose();
    }

    private sealed class Session(Process p, int depth, ILogger logger) : IScoresheetEngineSession
    {
        private bool _broken;

        public async Task<bool> HandshakeAsync(CancellationToken ct)
        {
            await SendAsync("uci", ct);
            if (await ReadUntilAsync("uciok", null, ct) == null) return false;
            await SendAsync("setoption name Threads value 1", ct);
            await SendAsync("setoption name Hash value 32", ct);
            await SendAsync("isready", ct);
            return await ReadUntilAsync("readyok", null, ct) != null;
        }

        public async Task<List<int>?> EvaluateLineAsync(string startFen, IReadOnlyList<string> uciMoves, CancellationToken ct = default)
        {
            if (_broken) return null;
            try
            {
                var whiteToMove = startFen.Split(' ').ElementAtOrDefault(1) != "b";
                var result = new List<int>(uciMoves.Count + 1);
                for (var n = 0; n <= uciMoves.Count; n++)
                {
                    var moves = n == 0 ? "" : " moves " + string.Join(' ', uciMoves.Take(n));
                    await SendAsync($"position fen {startFen}{moves}", ct);
                    await SendAsync($"go depth {depth}", ct);
                    int? score = null;
                    var bestmove = await ReadUntilAsync("bestmove", line => { if (ParseScore(line) is int s) score = s; }, ct);
                    if (bestmove == null) { _broken = true; return null; }
                    var stmWhite = (n % 2 == 0) == whiteToMove;
                    // Ohne Bewertung (Patt, oder Matt auf dem Brett meldet Stockfish als „mate 0"): 0 bzw. der Wert der Zeile.
                    var value = Math.Clamp(score ?? 0, -ScoresheetPlausibility.MateCp, ScoresheetPlausibility.MateCp);
                    result.Add(stmWhite ? value : -value);
                }
                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Formular-Plausibilität: Engine-Antwort fehlerhaft.");
                _broken = true;
                return null;
            }
        }

        private async Task SendAsync(string line, CancellationToken ct)
        {
            await p.StandardInput.WriteLineAsync(line.AsMemory(), ct);
            await p.StandardInput.FlushAsync(ct);
        }

        private async Task<string?> ReadUntilAsync(string prefix, Action<string>? each, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(PerAnswer);
            try
            {
                while (true)
                {
                    var line = await p.StandardOutput.ReadLineAsync(cts.Token);
                    if (line == null) return null;
                    each?.Invoke(line);
                    if (line.StartsWith(prefix, StringComparison.Ordinal)) return line;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!p.HasExited)
                {
                    await p.StandardInput.WriteLineAsync("quit");
                    await p.StandardInput.FlushAsync();
                    if (!p.WaitForExit(500)) p.Kill();
                }
            }
            catch { /* schon weg */ }
            p.Dispose();
        }
    }

    /// <summary>Bewertung aus einer <c>info</c>-Zeile (Sicht der Seite am Zug): <c>score cp</c>, Matt als ±MateCp
    /// (<c>mate 0</c> = die Seite am Zug IST matt).</summary>
    internal static int? ParseScore(string line)
    {
        if (!line.StartsWith("info ", StringComparison.Ordinal)) return null;
        var parts = line.Split(' ');
        var i = Array.IndexOf(parts, "score");
        if (i < 0 || i + 2 >= parts.Length) return null;
        if (!int.TryParse(parts[i + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return null;
        return parts[i + 1] switch
        {
            "cp" => v,
            "mate" => v > 0 ? ScoresheetPlausibility.MateCp : -ScoresheetPlausibility.MateCp,
            _ => null,
        };
    }
}
