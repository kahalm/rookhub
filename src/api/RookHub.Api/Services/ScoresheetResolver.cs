using Chess;

namespace RookHub.Api.Services;

/// <summary>Ein Eintrag des Formulars, wie das Modell ihn gelesen hat (ein Halbzug).</summary>
/// <param name="Written">Was dasteht, in der Sprache des Formulars (<c>Sf3</c>, <c>Lxd4</c>).</param>
/// <param name="San">Die Lesart des Modells in englischer SAN (<c>Nf3</c>) — darf falsch oder leer sein.</param>
/// <param name="Alternatives">Weitere Lesarten des Modells, die wahrscheinlichste zuerst.</param>
/// <param name="Confidence"><c>high</c>/<c>medium</c>/<c>low</c>.</param>
public sealed record ScannedPly(string Written, string? San, IReadOnlyList<string>? Alternatives = null, string? Confidence = null);

/// <summary>Eine mögliche Lesart an einer unsicheren Stelle — samt dem, was aus ihr folgt.</summary>
public sealed class ScoresheetOption
{
    public string San { get; set; } = string.Empty;
    public string Uci { get; set; } = string.Empty;
    /// <summary>Wie der Zug zum Eintrag passt (<see cref="ScoresheetResolver.Matches"/>).</summary>
    public string Match { get; set; } = string.Empty;
    /// <summary>Wie viele der FOLGENDEN Einträge sich mit dieser Lesart noch legal lesen lassen
    /// (gedeckelt bei <see cref="ScoresheetResolver.ReachHorizon"/>).</summary>
    public int Reach { get; set; }
    /// <summary>Die ersten Folgezüge dieser Lesart (Vorschau „und dann …").</summary>
    public List<string> Preview { get; set; } = new();
}

/// <summary>Ein aufgelöster Halbzug.</summary>
public sealed class ScoresheetPly
{
    /// <summary>Index des Formular-Eintrags, aus dem der Zug stammt; <c>null</c> = vom Nutzer eingefügt.</summary>
    public int? W { get; set; }
    public string Written { get; set; } = string.Empty;
    public string San { get; set; } = string.Empty;
    public string Uci { get; set; } = string.Empty;
    public string Match { get; set; } = string.Empty;
    /// <summary>Soll der Nutzer hier hinschauen? Siehe <see cref="ScoresheetResolver"/>.</summary>
    public bool Uncertain { get; set; }
    /// <summary>Vom Nutzer bestätigt oder selbst eingegeben — nie wieder als unsicher markieren.</summary>
    public bool Confirmed { get; set; }
    /// <summary>Die wahrscheinlichsten Lesarten (die gewählte zuerst), nur an unsicheren Stellen.</summary>
    public List<ScoresheetOption>? Options { get; set; }
}

/// <summary>Ergebnis einer Auflösung.</summary>
public sealed class ScoresheetResolution
{
    public List<ScoresheetPly> Plies { get; set; } = new();
    /// <summary>Formular-Eintrag, ab dem sich nichts Legales mehr fand; <c>null</c> = alles aufgelöst.</summary>
    public int? StuckAt { get; set; }
    /// <summary>Stellung, in der es hängen blieb (für die Nachfrage beim Modell).</summary>
    public string? StuckFen { get; set; }
    /// <summary>Die Einträge ab <see cref="StuckAt"/>, wie sie dastehen.</summary>
    public List<string> Unresolved { get; set; } = new();
}

/// <summary>
/// Macht aus den gelesenen Formular-Einträgen eine LEGALE Partie — und zwar die, die das GANZE Formular am
/// besten erklärt, nicht die, die Zug für Zug am nächsten liegt.
///
/// <para>Warum das Ganze: Auf Formularen entscheidet oft erst ein späterer Zug über einen früheren. „Sd2" kann
/// Sbd2 oder Sfd2 sein — erst „Sxd4" zehn Züge später sagt, welcher Springer noch auf f3 stand. Ein gelesenes
/// „Lxe4" kann legal sein und trotzdem falsch, weil derselbe Läufer vier Züge später noch von f1 zieht. Deshalb
/// eine Strahlsuche (<see cref="BeamWidth"/> Stellungen gleichzeitig) statt einer Entscheidung je Zug: jede
/// Lesart, die zum Eintrag passt, läuft weiter, bis spätere Einträge sie widerlegen.</para>
///
/// <para>Kosten je Halbzug (kleiner = näher an dem, was dasteht): die SAN-Lesart des Modells 0, der
/// Formular-Eintrag in seiner Sprache 0,2, eine Ersatz-Lesart des Modells 1, ein fehlender Zusatz
/// („Sd2" für Sbd2) +0,3, ein Lesefehler um ein Zeichen 2,5 je Zeichen, und — nur wenn es sonst nicht
/// weitergeht — ein unleserlicher Eintrag, den allein die FOLGENDEN Züge festlegen, 8.</para>
///
/// <para>Bleibt die Suche hängen, geht sie bis zu <see cref="MaxBacktrack"/> Einträge zurück und lässt dort
/// den Joker zu: ein früherer Lesefehler zeigt sich oft erst einen Zug später als illegaler Zug.</para>
///
/// <para>Unsichere Stellen bekommen die <see cref="BranchWidth"/> wahrscheinlichsten Lesarten mit, und jede
/// wird ein Stück weitergespielt (<see cref="ReachHorizon"/> Einträge): „passt zu den nächsten 12 Zügen"
/// gegen „Sackgasse nach 2". So wählt der Nutzer auf der Korrekturseite zwischen Ausgängen statt zu raten.</para>
/// </summary>
public static class ScoresheetResolver
{
    /// <summary>So viele Stellungen verfolgt die Suche gleichzeitig.</summary>
    public const int BeamWidth = 32;

    /// <summary>So viele Lesarten zeigt eine unsichere Stelle höchstens (die gewählte eingeschlossen).</summary>
    public const int BranchWidth = 3;

    /// <summary>So weit wird eine Ersatz-Lesart weitergespielt, um zu sehen, wohin sie führt. Bewusst weit:
    /// „Sfd2" statt Sbd2 scheitert in der Anlass-Partie erst 19 Züge später — mit 20 als Horizont galt die falsche
    /// Lesart als ebenbürtig.</summary>
    public const int ReachHorizon = 60;

    /// <summary>So viele Joker (erschlossene Einträge) darf eine Lesung höchstens enthalten: 1 + einer je
    /// <see cref="GuessEvery"/> Einträge. Ohne Deckel „löst" die Suche auch Unsinn auf.</summary>
    public const int GuessEvery = 15;

    /// <summary>So viele Einträge geht die Suche bei einer Sackgasse höchstens zurück.</summary>
    public const int MaxBacktrack = 3;

    /// <summary>Um so viel darf eine Ersatz-Lesart teurer sein und trotzdem als ebenbürtig gelten (eine
    /// fehlende Zusatzangabe kostet 0,3, ein Lesefehler 2,5).</summary>
    private const double RivalMargin = 0.5;

    /// <summary>So viele Stellen bekommen höchstens Ausgänge berechnet (Rechenzeit-Deckel).</summary>
    public const int MaxBranchPoints = 40;

    /// <summary>Die Arten, wie ein Zug zu einem Eintrag passt — von „steht so da" bis „erschlossen".</summary>
    public static class Matches
    {
        public const string Exact = "exact";
        public const string Written = "written";
        public const string Alternative = "alternative";
        public const string Loose = "loose";
        public const string Fuzzy = "fuzzy";
        public const string Guess = "guess";
        public const string User = "user";
    }

    private const double GuessCost = 8;
    private const int PerStateMoves = 6;

    private sealed record Step(Step? Parent, int W, string San, string Uci, string Match, double Cost, string FenBefore);

    private sealed record State(string Fen, double Cost, Step? Last, int Guesses = 0);

    /// <summary>Einstellungen einer Auflösung.</summary>
    /// <param name="Language">Sprache der Einträge; <c>null</c> = automatisch (alle Sprachen mit Aufschlag).</param>
    public sealed record Options(ScoresheetNotation.Language? Language);

    /// <summary>
    /// Löst die Einträge ab <paramref name="writtenFrom"/> auf, nachdem <paramref name="prefix"/> (SAN, schon
    /// festgelegt — vom Nutzer bestätigt oder gerade gewählt) gespielt ist.
    /// </summary>
    public static ScoresheetResolution Resolve(IReadOnlyList<ScannedPly> scanned, Options options,
        IReadOnlyList<string>? prefix = null, int writtenFrom = 0, string? startFen = null, bool withBranches = true)
    {
        var fen = startFen ?? GamePlies.StartFen();
        var board = ChessBoard.LoadFromFen(fen);
        foreach (var san in prefix ?? Array.Empty<string>())
        {
            if (!board.Move(san)) throw new ArgumentException($"Illegal prefix move {san}.");
        }
        var startFenAfterPrefix = board.ToFen();

        var cache = new Dictionary<(string Fen, int W, bool Guess), List<(Move Move, string San, string Uci, string Match, double Cost, string After)>>();
        var (best, stuckAt, stuckFen, _) = Search(scanned, options, startFenAfterPrefix, writtenFrom, scanned.Count, BeamWidth, cache);

        var steps = Unwind(best?.Last);
        var result = new ScoresheetResolution
        {
            Plies = steps.Select(s => new ScoresheetPly
            {
                W = s.W, Written = scanned[s.W].Written, San = s.San, Uci = s.Uci, Match = s.Match,
            }).ToList(),
            StuckAt = stuckAt,
            StuckFen = stuckFen,
        };
        if (stuckAt is int from)
            result.Unresolved = scanned.Skip(from).Select(p => p.Written).ToList();

        if (withBranches && steps.Count > 0)
            AddBranches(result, steps, scanned, options, cache);
        return result;
    }

    /// <summary>Die Strahlsuche. Liefert den besten Endzustand, den Eintrag, an dem sie hängen blieb, und
    /// die Stellung dort.</summary>
    /// <param name="mergeKeys">Nur beim Weiterspielen einer Ersatz-Lesart: die Stellungen der gewählten Lesung je
    /// Eintrag. Trifft die Ersatz-Lesart eine davon, sind beide ab dort DIESELBE Partie (Zugumstellung) — dann
    /// endet die Suche mit <c>Merged</c>.</param>
    private static (State? Best, int? StuckAt, string? StuckFen, bool Merged) Search(IReadOnlyList<ScannedPly> scanned,
        Options options, string fen, int from, int to, int width,
        Dictionary<(string, int, bool), List<(Move, string, string, string, double, string)>> cache,
        IReadOnlyDictionary<int, string>? mergeKeys = null)
    {
        var maxGuesses = 1 + (to - from) / GuessEvery;
        var beams = new List<List<State>> { new() { new State(fen, 0, null) } };
        for (var w = from; w < to; w++)
        {
            var next = Expand(beams[^1], w, scanned, options, guess: false, width, cache);
            if (next.Count == 0)
            {
                next = Backtrack(beams, w, from, to, scanned, options, width, maxGuesses, cache);
                if (next.Count == 0)
                {
                    var best = beams[^1].OrderBy(s => s.Cost).First();
                    return (best.Last == null ? null : best, w, best.Fen, false);
                }
            }
            beams.Add(next);
            if (mergeKeys != null && mergeKeys.TryGetValue(w, out var key))
            {
                var merged = next.FirstOrDefault(st => PositionKey(st.Fen) == key);
                if (merged != null) return (merged, null, null, true);
            }
        }
        var final = beams[^1].OrderBy(s => s.Cost).FirstOrDefault();
        return (final?.Last == null ? null : final, null, null, false);
    }

    /// <summary>Sackgasse bei Eintrag <paramref name="w"/>: ab ein, zwei, drei Einträgen vorher den Joker
    /// zulassen und von dort normal bis <paramref name="w"/> weiterlesen. Die Strahlen davor werden dabei
    /// ersetzt, damit die Suche mit dem geretteten Stand weiterläuft.
    ///
    /// <para>Ein Joker braucht eine BESTÄTIGUNG: mindestens ein folgender Eintrag muss ohne Joker passen. Sonst
    /// ist er kein erschlossener Zug, sondern ein erfundener — und mit einem Joker je Eintrag ließe sich jedes
    /// Formular „auflösen". Der letzte Eintrag hat keinen Nachfolger; ihn erschließt die Suche nur, wenn er
    /// als unleserlich gilt (<see cref="LooksUnreadable"/>).</para></summary>
    private static List<State> Backtrack(List<List<State>> beams, int w, int from, int to, IReadOnlyList<ScannedPly> scanned,
        Options options, int width, int maxGuesses,
        Dictionary<(string, int, bool), List<(Move, string, string, string, double, string)>> cache)
    {
        // Erst der Joker genau hier (der Eintrag selbst ist unleserlich), dann weiter zurück.
        for (var back = 0; back <= MaxBacktrack; back++)
        {
            var start = w - back;
            if (start < from) break;
            var beamIndex = beams.Count - 1 - back;
            if (beamIndex < 0) break;
            var source = beams[beamIndex].Where(st => st.Guesses < maxGuesses).ToList();
            if (source.Count == 0) continue;
            var cur = Expand(source, start, scanned, options, guess: true, width, cache);
            var rebuilt = new List<List<State>> { cur };
            for (var k = start + 1; k <= w && cur.Count > 0; k++)
            {
                cur = Expand(cur, k, scanned, options, guess: false, width, cache);
                rebuilt.Add(cur);
            }
            if (cur.Count == 0) continue;
            if (back == 0)
            {
                if (w + 1 < to)
                {
                    // Bestätigung durch den nächsten Eintrag; nur die Zustände behalten, die sie haben.
                    var confirmed = Expand(cur, w + 1, scanned, options, guess: false, width, cache);
                    if (confirmed.Count == 0) continue;
                    var keep = new HashSet<string>(confirmed.Select(c => c.Last!.FenBefore), StringComparer.Ordinal);
                    cur = cur.Where(c => keep.Contains(c.Fen)).ToList();
                    rebuilt[^1] = cur;
                }
                else if (!LooksUnreadable(scanned[w]))
                {
                    continue;
                }
            }
            // Die Strahlen ab start durch die geretteten ersetzen (der letzte kommt als Rückgabe dazu).
            beams.RemoveRange(beamIndex + 1, beams.Count - beamIndex - 1);
            beams.AddRange(rebuilt.Take(rebuilt.Count - 1));
            return cur;
        }
        return new();
    }

    /// <summary>Unleserlich markiert: leer, Fragezeichen, oder das Modell war sich nicht sicher.</summary>
    private static bool LooksUnreadable(ScannedPly ply)
        => string.IsNullOrWhiteSpace(ply.Written) || ply.Written.Contains('?') || ply.Confidence == "low";

    private static List<State> Expand(List<State> beam, int w, IReadOnlyList<ScannedPly> scanned, Options options,
        bool guess, int width, Dictionary<(string, int, bool), List<(Move, string, string, string, double, string)>> cache)
    {
        var merged = new Dictionary<string, State>(StringComparer.Ordinal);
        foreach (var state in beam)
        {
            foreach (var (_, san, uci, match, cost, after) in Scored(state.Fen, w, scanned, options, guess, cache))
            {
                var total = state.Cost + cost;
                var key = PositionKey(after);
                if (merged.TryGetValue(key, out var old) && old.Cost <= total) continue;
                var guesses = state.Guesses + (match == Matches.Guess ? 1 : 0);
                merged[key] = new State(after, total, new Step(state.Last, w, san, uci, match, cost, state.Fen), guesses);
            }
        }
        return merged.Values.OrderBy(s => s.Cost).Take(width).ToList();
    }

    /// <summary>Die passenden legalen Züge zu Eintrag <paramref name="w"/> in Stellung <paramref name="fen"/>,
    /// billigste zuerst — gecacht, weil Strahlen und Ausgänge dieselben Stellungen oft mehrfach fragen.</summary>
    private static List<(Move Move, string San, string Uci, string Match, double Cost, string After)> Scored(
        string fen, int w, IReadOnlyList<ScannedPly> scanned, Options options, bool guess,
        Dictionary<(string, int, bool), List<(Move, string, string, string, double, string)>> cache)
    {
        if (cache.TryGetValue((fen, w, guess), out var hit)) return hit;

        var board = ChessBoard.LoadFromFen(fen);
        var legal = board.Moves(generateSan: true);
        var ply = scanned[w];
        var scored = new List<(Move, string, string, string, double)>();
        foreach (var m in legal)
        {
            var san = string.IsNullOrEmpty(m.San) ? GamePlies.ToUci(m) : m.San;
            var uci = GamePlies.ToUci(m);
            var (cost, match) = Score(ply, san, uci, options);
            if (double.IsPositiveInfinity(cost))
            {
                if (!guess) continue;
                (cost, match) = (GuessCost, Matches.Guess);
            }
            scored.Add((m, san, uci, match, cost));
        }
        var take = scored.OrderBy(s => s.Item5).Take(guess ? 64 : PerStateMoves).ToList();

        var result = new List<(Move, string, string, string, double, string)>(take.Count);
        foreach (var (m, san, uci, match, cost) in take)
        {
            if (!board.Move(m)) continue;
            result.Add((m, san, uci, match, cost, board.ToFen()));
            board.Cancel();
        }
        cache[(fen, w, guess)] = result;
        return result;
    }

    /// <summary>
    /// Wie gut passt der legale Zug (<paramref name="san"/>/<paramref name="uci"/>) zum Eintrag? Unendlich =
    /// gar nicht (dann bleibt nur der Joker).
    /// </summary>
    public static (double Cost, string Match) Score(ScannedPly ply, string san, string uci, Options options)
    {
        var key = ScoresheetNotation.Key(san);
        var loose = ScoresheetNotation.LooseKey(key);
        var piece = key.Length > 0 && "KQRBN".Contains(key[0]) ? key[0].ToString() : string.Empty;
        var longKey = uci.Length >= 4 ? uci[..4] : uci;
        var longPiece = piece + longKey;

        double best = double.PositiveInfinity;
        var match = string.Empty;
        void Consider(double c, string m) { if (c < best) { best = c; match = m; } }

        bool Same(string cand) => cand == key || cand == longKey || cand == longPiece
            // Umwandlung ohne Figur geschrieben („e8") — fast immer die Dame.
            || (key.Length == 3 && key[2] == 'Q' && cand == key[..2]);
        bool Loose(string cand) => ScoresheetNotation.LooseKey(cand) == loose && cand.Length <= key.Length;

        var english = new[] { ScoresheetNotation.Languages[0] };
        var sheet = options.Language;
        // Bei „auto" laufen alle Sprachen mit (mit Aufschlag); sonst die gewählte + Englisch.
        var others = sheet == null ? ScoresheetNotation.Languages : english;

        foreach (var c in ScoresheetNotation.Candidates(ply.San, null))
        {
            if (Same(c.Key)) Consider(c.Cost, Matches.Exact);
            else if (Loose(c.Key)) Consider(c.Cost + 0.3, Matches.Loose);
        }
        var writtenCands = ScoresheetNotation.Candidates(ply.Written, sheet, others);
        foreach (var c in writtenCands)
        {
            if (Same(c.Key)) Consider(0.2 + c.Cost, Matches.Written);
            else if (Loose(c.Key)) Consider(0.5 + c.Cost, Matches.Loose);
        }
        foreach (var alt in ply.Alternatives ?? Array.Empty<string>())
        {
            foreach (var c in ScoresheetNotation.Candidates(alt, null))
            {
                if (Same(c.Key)) Consider(1.0 + c.Cost, Matches.Alternative);
                else if (Loose(c.Key)) Consider(1.3 + c.Cost, Matches.Alternative);
            }
        }
        if (best <= 0.5) return (best, match);

        // Lesefehler: ein, zwei Zeichen daneben („Dc1" für De1, „Qxd4" für exd4).
        foreach (var c in ScoresheetNotation.Candidates(ply.San, null).Concat(writtenCands))
        {
            var d = ScoresheetNotation.Distance(c.Key, key);
            if (d is >= 1 and <= 2 && c.Key.Length >= 2) Consider(2.5 * d + c.Cost, Matches.Fuzzy);
        }
        return (best, match);
    }

    /// <summary>
    /// Wie viele Einträge passen GLATT hintereinander — bis zum ersten zurechtgebogenen (Lesefehler) oder
    /// erschlossenen (Joker). Die Länge des ganzen Weges wäre kein Maß: mit genug Reparaturen lässt sich fast
    /// jede falsche Lesart bis zum Ende weiterspielen (am Anlass gemessen: „Sfd2" kam mit einem „Qxd4" statt
    /// des unmöglichen „Sxd4" bis zum letzten Zug).
    /// </summary>
    private static int CleanReach(IEnumerable<Step> steps)
        => steps.TakeWhile(s => s.Match is not (Matches.Fuzzy or Matches.Guess)).Count();

    /// <summary>Stellungsschlüssel ohne Zugzähler — zwei Wege zur selben Stellung sind derselbe Strahl.</summary>
    private static string PositionKey(string fen)
    {
        var parts = fen.Split(' ');
        return parts.Length >= 4 ? string.Join(' ', parts.Take(4)) : fen;
    }

    private static List<Step> Unwind(Step? last)
    {
        var list = new List<Step>();
        for (var s = last; s != null; s = s.Parent) list.Add(s);
        list.Reverse();
        return list;
    }

    /// <summary>
    /// Unsichere Stellen finden und ihnen die wahrscheinlichsten Lesarten samt Folgen mitgeben.
    ///
    /// <para>KANDIDAT ist eine Stelle, deren Zug nicht glatt zum Eintrag passt (weder die Lesart des Modells
    /// noch der Eintrag selbst), die das Modell selbst unsicher nannte, oder an der ein anderer Zug fast genauso
    /// gut passt. UNSICHER bleibt sie nur, wenn der Zug geraten oder zurechtgebogen ist, das Modell zweifelte,
    /// oder eine andere Lesart GENAUSO WEIT trägt. Eine Mehrdeutigkeit, die spätere Züge klar entschieden haben
    /// („Sd2" ist Sbd2, weil Sfd2 viele Züge später scheitert), braucht den Nutzer nicht.</para>
    ///
    /// <para>Läuft eine Ersatz-Lesart in dieselbe Stellung wie die gewählte (17. Sbd4/Sfd4 Sxd4 18. Sxd4), ist
    /// sie ebenbürtig — beide erklären das Formular gleich gut, und nur der Spieler weiß, welcher Springer es war.</para>
    /// </summary>
    private static void AddBranches(ScoresheetResolution result, List<Step> steps, IReadOnlyList<ScannedPly> scanned,
        Options options, Dictionary<(string, int, bool), List<(Move, string, string, string, double, string)>> cache)
    {
        var end = result.StuckAt ?? scanned.Count;
        // Stellung der gewählten Lesung NACH jedem Eintrag — dort trifft sich eine Zugumstellung wieder.
        var chosenKeys = new Dictionary<int, string>();
        for (var i = 0; i + 1 < steps.Count; i++) chosenKeys[steps[i].W] = PositionKey(steps[i + 1].FenBefore);

        var points = 0;
        for (var i = 0; i < steps.Count && points < MaxBranchPoints; i++)
        {
            var step = steps[i];
            var ply = result.Plies[i];
            var scannedPly = scanned[step.W];
            var candidates = Scored(step.FenBefore, step.W, scanned, options, guess: false, cache)
                .Where(c => c.Uci != step.Uci).OrderBy(c => c.Cost).ToList();
            var smooth = step.Match is Matches.Exact or Matches.Written;
            var close = candidates.Any(c => c.Cost <= step.Cost + 0.5);
            var doubted = scannedPly.Confidence is "low" or "medium";
            if (smooth && !close && !doubted) continue;
            points++;

            var chosenReach = Math.Min(CleanReach(steps.Skip(i + 1)), ReachHorizon);
            var opts = new List<ScoresheetOption>
            {
                new()
                {
                    San = step.San, Uci = step.Uci, Match = step.Match, Reach = chosenReach,
                    Preview = steps.Skip(i + 1).Take(4).Select(s => s.San).ToList(),
                },
            };
            var rival = false;
            foreach (var alt in candidates.Take(BranchWidth - 1))
            {
                var horizon = Math.Min(end, step.W + 1 + ReachHorizon);
                var (bestAlt, _, _, merged) = Search(scanned, options, alt.After, step.W + 1, horizon, 8, cache, chosenKeys);
                var altSteps = Unwind(bestAlt?.Last);
                var reach = merged ? chosenReach : CleanReach(altSteps);
                opts.Add(new ScoresheetOption
                {
                    San = alt.San, Uci = alt.Uci, Match = alt.Match, Reach = Math.Min(reach, ReachHorizon),
                    Preview = altSteps.Take(4).Select(s => s.San).ToList(),
                });

                // Ebenbürtig ist eine Lesart, die GLEICH WEIT trägt und dabei nicht teurer ist — verglichen über
                // dieselbe Strecke (bis zur Zusammenführung bzw. über die glatte Reichweite der gewählten).
                if (reach < chosenReach) continue;
                var span = merged ? altSteps.Count : chosenReach;
                var altCost = alt.Cost + altSteps.Take(span).Sum(s => s.Cost);
                var chosenCost = step.Cost + steps.Skip(i + 1).Take(span).Sum(s => s.Cost);
                if (altCost <= chosenCost + RivalMargin) rival = true;
            }

            var bent = step.Match is Matches.Fuzzy or Matches.Guess or Matches.Alternative;
            ply.Uncertain = bent || scannedPly.Confidence == "low" || rival;
            ply.Options = ply.Uncertain ? opts : null;
        }
    }
}
