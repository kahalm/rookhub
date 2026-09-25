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
    /// <summary>Einträge, die zu keinem Zug gehören (doppelt/versehentlich notiert) — übersprungen.</summary>
    public List<ScoresheetSkip> Skipped { get; set; } = new();
}

/// <summary>Ein übersprungener Formular-Eintrag.</summary>
public sealed class ScoresheetSkip
{
    public int W { get; set; }
    public string Written { get; set; } = string.Empty;
    /// <summary>Zahl der Halbzüge davor — der Eintrag stand hinter Halbzug <c>AfterPly</c>.</summary>
    public int AfterPly { get; set; }
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
        /// <summary>Ein Zug, der auf dem Formular FEHLT (der Spieler hat ihn nicht notiert) — erschlossen, weil der
        /// Eintrag danach sonst nicht passt.</summary>
        public const string Inserted = "inserted";
        /// <summary>Ein Eintrag, der zu KEINEM Zug gehört (doppelt oder versehentlich notiert).</summary>
        public const string Skip = "skip";
    }

    private const double GuessCost = 8;
    private const int PerStateMoves = 6;

    /// <summary>Kosten eines fehlenden bzw. überzähligen Eintrags. Teurer als ein Lesefehler um ein Zeichen (2,5),
    /// billiger als zwei: eine Verschiebung zieht sonst eine Kette von Lesefehlern nach sich, und DIE ist teurer.</summary>
    private const double EditCost = 5;

    /// <summary>Erst ab so hohen Kosten des besten normalen Treffers wird ein fehlender/überzähliger Eintrag
    /// erwogen — wo etwas glatt passt, bleibt es beim einfachen Lesen (und die Suche schnell).</summary>
    private const double EditThreshold = 1.0;

    /// <summary>So viele der besten Zustände eines Strahls dürfen einen fehlenden/überzähligen Eintrag versuchen.</summary>
    private const int EditStates = 4;

    /// <summary>Reservierte Plätze je Strahl für Wege kurz nach einem fehlenden/überzähligen Eintrag.</summary>
    private const int ShelterSlots = 8;

    /// <summary>So viele Einträge lang gilt der Schutz nach einem fehlenden/überzähligen Eintrag.</summary>
    private const int ShelterLayers = 3;

    /// <summary>So viele eingeschobene Züge je Zustand kommen in den Strahl (die mit dem besten Blick voraus).</summary>
    private const int InsertsPerState = 6;

    /// <summary>So gut muss der Eintrag NACH einem eingeschobenen Zug passen („glatt"): das ist die Bestätigung.</summary>
    private const double SmoothCost = 0.5;

    private sealed record Step(Step? Parent, int W, string San, string Uci, string Match, double Cost, string FenBefore,
        string FenAfter);

    /// <param name="Shelter">So viele Einträge lang bekommt ein Weg nach einem fehlenden/überzähligen Eintrag noch
    /// reservierte Plätze im Strahl (<see cref="ShelterSlots"/>) — sonst verdrängen ihn die vielen billigen
    /// Lesefehler-Varianten, bevor er sich als der glatte erweisen kann.</param>
    private sealed record State(string Fen, double Cost, Step? Last, int Guesses = 0, int Shelter = 0);

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
            Plies = steps.Where(s => s.Match != Matches.Skip).Select(s => new ScoresheetPly
            {
                W = s.W >= 0 ? s.W : null, Written = s.W >= 0 ? scanned[s.W].Written : string.Empty,
                San = s.San, Uci = s.Uci, Match = s.Match,
            }).ToList(),
            StuckAt = stuckAt,
            StuckFen = stuckFen,
        };
        var pliesSoFar = 0;
        foreach (var st in steps)
        {
            if (st.Match == Matches.Skip)
                result.Skipped.Add(new ScoresheetSkip { W = st.W, Written = scanned[st.W].Written, AfterPly = pliesSoFar });
            else pliesSoFar++;
        }
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
        IReadOnlyDictionary<int, string>? mergeKeys = null, bool allowEdits = true)
    {
        var maxGuesses = 1 + (to - from) / GuessEvery;
        var editBudget = allowEdits ? maxGuesses : 0;
        var beams = new List<List<State>> { new() { new State(fen, 0, null) } };
        for (var w = from; w < to; w++)
        {
            var next = Expand(beams[^1], w, scanned, options, guess: false, width, cache, editBudget);
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

    /// <summary>Steht derselbe Eintrag direkt davor oder danach noch einmal (doppelt notiert)?</summary>
    private static bool IsDuplicate(IReadOnlyList<ScannedPly> scanned, int w)
    {
        var key = ScoresheetNotation.Clean(scanned[w].Written).ToLowerInvariant();
        if (key.Length < 2) return false;
        bool Same(int k) => k >= 0 && k < scanned.Count
            && ScoresheetNotation.Clean(scanned[k].Written).ToLowerInvariant() == key;
        return Same(w - 1) || Same(w + 1);
    }

    /// <summary>Unleserlich markiert: leer, Fragezeichen, oder das Modell war sich nicht sicher.</summary>
    private static bool LooksUnreadable(ScannedPly ply)
        => string.IsNullOrWhiteSpace(ply.Written) || ply.Written.Contains('?') || ply.Confidence == "low";

    /// <param name="maxGuesses">Nur im normalen Lesen (nicht im Joker-Modus): ab so vielen erschlossenen
    /// Einträgen sind fehlende/überzählige Einträge nicht mehr erlaubt. <c>0</c> = gar nicht.</param>
    private static List<State> Expand(List<State> beam, int w, IReadOnlyList<ScannedPly> scanned, Options options,
        bool guess, int width, Dictionary<(string, int, bool), List<(Move, string, string, string, double, string)>> cache,
        int maxGuesses = 0)
    {
        var merged = new Dictionary<string, State>(StringComparer.Ordinal);
        void Add(State candidate)
        {
            var key = PositionKey(candidate.Fen);
            if (merged.TryGetValue(key, out var old) && old.Cost <= candidate.Cost) return;
            merged[key] = candidate;
        }

        var rank = 0;
        foreach (var state in beam)
        {
            var scored = Scored(state.Fen, w, scanned, options, guess, cache);
            var shelter = Math.Max(0, state.Shelter - 1);
            foreach (var (_, san, uci, match, cost, after) in scored)
            {
                var guesses = state.Guesses + (match == Matches.Guess ? 1 : 0);
                Add(new State(after, state.Cost + cost,
                    new Step(state.Last, w, san, uci, match, cost, state.Fen, after), guesses, shelter));
            }

            // Passt hier nichts glatt, kann das Formular verschoben sein: ein Zug fehlt (der Spieler hat ihn nicht
            // notiert) oder ein Eintrag ist zu viel. Beides kostet EditCost und zählt wie ein Joker.
            var bestNormal = scored.Count == 0 ? double.PositiveInfinity : scored.Min(x => x.Cost);
            if (!guess && bestNormal >= EditThreshold && rank < EditStates && state.Guesses < maxGuesses)
            {
                // Überzähliger Eintrag: verbrauchen, ohne zu ziehen — aber NUR einen doppelt notierten (gleich dem
                // Eintrag davor oder danach). Ein beliebiger Eintrag wäre sonst die billigste Art, Unleserliches
                // loszuwerden: die Lesung bliebe nicht mehr hängen, fragte nicht nach und ließe still echte Züge weg.
                if (IsDuplicate(scanned, w))
                    Add(new State(state.Fen, state.Cost + EditCost,
                        new Step(state.Last, w, string.Empty, string.Empty, Matches.Skip, EditCost, state.Fen, state.Fen),
                        state.Guesses + 1, ShelterLayers));
                // Fehlender Zug: irgendein legaler Zug, BESTÄTIGT dadurch, dass der Eintrag danach glatt passt. Meist
                // „passen" so dutzende Züge gleich gut (jeder ruhige Zug der Seite) — ungeordnet verstopften sie den
                // Strahl, und der richtige fiel heraus, bevor ein späterer Eintrag ihn bestätigen konnte (HCS-Beleg 03:
                // 21…Sg4 fehlt, erst 22…Dxa6 verrät ihn). Deshalb vorsortiert nach dem ÜBERNÄCHSTEN Eintrag, und nur
                // die besten kommen in den Strahl.
                var inserts = new List<(State State, double Rank)>();
                foreach (var ins in Scored(state.Fen, w, scanned, options, guess: true, cache))
                {
                    foreach (var (_, san2, uci2, match2, cost2, after2) in Scored(ins.After, w, scanned, options, false, cache))
                    {
                        if (cost2 > SmoothCost) continue;
                        var inserted = new Step(state.Last, -1, ins.San, ins.Uci, Matches.Inserted, EditCost, state.Fen, ins.After);
                        var next = w + 1 < scanned.Count ? Scored(after2, w + 1, scanned, options, false, cache) : null;
                        var ahead = next == null ? 0 : next.Count == 0 ? GuessCost : next.Min(x => x.Cost);
                        inserts.Add((new State(after2, state.Cost + EditCost + cost2,
                            new Step(inserted, w, san2, uci2, match2, cost2, ins.After, after2), state.Guesses + 1,
                            ShelterLayers), cost2 + ahead));
                    }
                }
                foreach (var (candidate, _) in inserts.OrderBy(x => x.Rank).ThenBy(x => x.State.Fen, StringComparer.Ordinal).Take(InsertsPerState))
                    Add(candidate);
            }
            rank++;
        }
        // Bei Gleichstand fest nach Stellung sortieren: die Zugreihenfolge der Schach-Bibliothek ist kein Vertrag, und
        // ein echter Gleichstand (Thh1/Tdh1) soll bei jedem Lauf gleich ausgehen.
        var ordered = merged.Values.OrderBy(s => s.Cost).ThenBy(s => s.Fen, StringComparer.Ordinal).ToList();
        var chosen = ordered.Take(width).ToList();
        // Dazu die geschützten Wege nach einem fehlenden/überzähligen Eintrag, die sonst herausfielen.
        chosen.AddRange(ordered.Skip(width).Where(s => s.Shelter > 0).Take(ShelterSlots));
        return chosen;
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
        var take = scored.OrderBy(s => s.Item5).ThenBy(s => s.Item3, StringComparer.Ordinal)
            .Take(guess ? 64 : PerStateMoves).ToList();

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

        // Was DASTEHT schlägt die Deutung des Modells, wenn beides legal ist (0 gegen 0,1): das Modell „korrigiert"
        // manchmal einen richtigen Eintrag, weil es die Stellung falsch im Kopf hat (10er-Testsatz, Beleg 06: „De4"
        // stand da und war richtig, das Modell hielt es für unmöglich und las Dxg4). Die Legalität prüft HIER die
        // Stellung, nicht das Modell. Ist der Eintrag illegal, trägt die Deutung weiter (Beleg 01: „bxa4" → bxc4).
        foreach (var c in ScoresheetNotation.Candidates(ply.San, null))
        {
            if (Same(c.Key)) Consider(0.1 + c.Cost, Matches.Exact);
            else if (Loose(c.Key)) Consider(0.4 + c.Cost, Matches.Loose);
        }
        var writtenCands = ScoresheetNotation.Candidates(ply.Written, sheet, others);
        foreach (var c in writtenCands)
        {
            if (Same(c.Key)) Consider(c.Cost, Matches.Written);
            else if (Loose(c.Key)) Consider(0.3 + c.Cost, Matches.Loose);
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
            var d = ScoresheetNotation.WeightedDistance(c.Key, key);
            if (d is < 1 or > 2.4 || c.Key.Length < 2) continue;
            // Ein Zeichen vertauscht, und zwar eines, das sich in Handschrift leicht verwechseln lässt (6/8, 1/7,
            // a/d …): billiger als ein beliebiger Lesefehler — so entscheidet bei „Rf8" zwischen Rf6 und Ra8 nicht
            // der Zufall (am HCS-Beleg 05 gemessen), sondern die Ähnlichkeit der Zeichen.
            var similar = d == 1.0 && ScoresheetNotation.IsConfusable(c.Key, key);
            Consider(2.5 * d - (similar ? 0.5 : 0) + c.Cost, Matches.Fuzzy);
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
        => steps.TakeWhile(s => s.Match is not (Matches.Fuzzy or Matches.Guess or Matches.Inserted or Matches.Skip))
            .Count(s => s.W >= 0);

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
        foreach (var st in steps.Where(st => st.W >= 0)) chosenKeys[st.W] = PositionKey(st.FenAfter);

        var points = 0;
        var plyIndex = -1;
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (step.Match == Matches.Skip) continue;       // kein Zug — steht in result.Skipped
            plyIndex++;
            var ply = result.Plies[plyIndex];
            if (step.Match == Matches.Inserted)
            {
                // Stand nicht auf dem Formular: immer ansehen lassen; Lesarten gibt es keine (es gibt nichts zu lesen).
                ply.Uncertain = true;
                continue;
            }
            var scannedPly = scanned[step.W];
            var bent = step.Match is Matches.Fuzzy or Matches.Guess or Matches.Alternative;
            if (points >= MaxBranchPoints)
            {
                // Das Budget für die Lesarten ist verbraucht — MARKIERT wird trotzdem, nur ohne Lesarten. Vorher endete
                // die Schleife hier: an einem langen Formular (Kufstein, 120 Halbzüge, fast alles „medium") verbrauchten
                // die glatten Züge die 40 Punkte, und ab Zug 28 stand jeder zurechtgebogene Zug da wie ein sicherer.
                if (bent || scannedPly.Confidence is "low") ply.Uncertain = true;
                continue;
            }
            var candidates = Scored(step.FenBefore, step.W, scanned, options, guess: false, cache)
                .Where(c => c.Uci != step.Uci).OrderBy(c => c.Cost).ToList();
            var smooth = step.Match is Matches.Exact or Matches.Written;
            var close = candidates.Any(c => c.Cost <= step.Cost + 0.5);
            // Zweifel des Modells: „low" allein genügt. „medium" vergibt das Modell freigiebig (05: fast jeder Turmzug,
            // alle richtig) — dort zählt erst eine genannte ALTERNATIVE, die ein anderer legaler Zug ist: am
            // 10er-Testsatz stand bei „medium" (02: 7…Se7 statt Sf6) und selbst bei „high" (08: 32…Kh8 statt Kf8) die
            // Wahrheit genau dort. Und ein Widerspruch zwischen Eintrag und Deutung (beide legal, verschiedene Züge)
            // zählt nur, wenn beide Lesarten gleich direkt sind — eine Lesart mit Aufschlag (klein geschriebenes „c"
            // als portugiesischer Springer, Fremdsprache) ist Rauschen, kein Widerspruch.
            var doubted = scannedPly.Confidence is "low";
            var conflict = candidates.Any(c => c.Match == Matches.Alternative
                || (c.Match is Matches.Written or Matches.Exact && c.Cost <= step.Cost + 0.25));
            if (smooth && !close && !doubted && !conflict && scannedPly.Confidence != "medium") continue;
            points++;

            var chosenReach = Math.Min(CleanReach(steps.Skip(i + 1)), ReachHorizon);
            var opts = new List<ScoresheetOption>
            {
                new()
                {
                    San = step.San, Uci = step.Uci, Match = step.Match, Reach = chosenReach,
                    Preview = steps.Skip(i + 1).Where(s => s.Match != Matches.Skip).Take(4).Select(s => s.San).ToList(),
                },
            };
            var rival = false;
            foreach (var alt in candidates.Take(BranchWidth - 1))
            {
                var horizon = Math.Min(end, step.W + 1 + ReachHorizon);
                // Ohne fehlende/überzählige Einträge: gemessen wird die GLATTE Reichweite, und die endet dort ohnehin.
                var (bestAlt, _, _, merged) = Search(scanned, options, alt.After, step.W + 1, horizon, 8, cache, chosenKeys,
                    allowEdits: false);
                var altSteps = Unwind(bestAlt?.Last);
                var reach = merged ? chosenReach : CleanReach(altSteps);
                opts.Add(new ScoresheetOption
                {
                    San = alt.San, Uci = alt.Uci, Match = alt.Match, Reach = Math.Min(reach, ReachHorizon),
                    Preview = altSteps.Where(s => s.Match != Matches.Skip).Take(4).Select(s => s.San).ToList(),
                });

                // Ebenbürtig ist eine Lesart, die GLEICH WEIT trägt und dabei nicht teurer ist — verglichen über
                // dieselbe Strecke (bis zur Zusammenführung bzw. über die glatte Reichweite der gewählten).
                if (reach < chosenReach) continue;
                var span = merged ? altSteps.Count : chosenReach;
                var altCost = alt.Cost + altSteps.Take(span).Sum(s => s.Cost);
                var chosenCost = step.Cost + steps.Skip(i + 1).Take(span).Sum(s => s.Cost);
                if (altCost <= chosenCost + RivalMargin) rival = true;
            }

            ply.Uncertain = bent || doubted || conflict || rival;
            ply.Options = ply.Uncertain ? opts : null;
        }
    }
}
