using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using static RookHub.Api.Services.Engines.MoveComparisonBoard;
using static RookHub.Api.Services.Engines.MoveComparisonEval;

namespace RookHub.Api.Services.Engines;

/// <summary>Fakten und Texte für die Begründung eines Zugvergleichs (aus <see cref="MoveComparisonService"/> herausgezogen,
/// 2026-11-09): was das Modell für ein Paar „besser/schwächer" bekommt (<see cref="PairFacts"/>), System- und Nutzer-Prompt,
/// Antwort-Schema und das Lesen der Antwort.</summary>
internal static class MoveComparisonPrompts
{
    internal static readonly JsonNode Schema = JsonNode.Parse(
        """{"type":"object","properties":{"explanation":{"type":"string"}},"required":["explanation"],"additionalProperties":false}""")!;

    internal static string? TextOf(string? json)
    {
        if (json == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement.TryGetProperty("explanation", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()?.Trim() : null;
            return string.IsNullOrWhiteSpace(text) || text.Length > 1500 ? null : text;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Eine Antwort nach dem besten Zug: geht sie, wie steht es dann (Sicht des Lesers), was antwortet er.</summary>
    internal sealed record ReplyTest(string ReplySan, MoveComparisonLineState State, string? Eval, IReadOnlyList<string> Answer);

    /// <summary>Alles, was das Modell für „warum ist <see cref="BestSan"/> besser als <see cref="WeakSan"/>" bekommt —
    /// Bewertungen aus Sicht des LESERS (der Seite am Zug), Linien als SAN.</summary>
    internal sealed record PairFacts(
        string Fen, bool MoverWhite, int Depth,
        string BestSan, string BestFen, string? BestEval, IReadOnlyList<Pv> BestReplies,
        string WeakSan, string WeakFen, string? WeakEval, IReadOnlyList<Pv> WeakReplies,
        IReadOnlyList<ReplyTest> Tests, IReadOnlyList<MoveComparisonDefences.Decision>? Defences = null,
        IReadOnlyList<MoveComparisonBestMoveTries.Try>? Tries = null)
    {
        /// <summary>Jeder Zug, den der Text nennen darf: beide Kandidaten, alle gegebenen Linien, die Antwort-Tests, — seit
        /// 0.971.0 — die anderen Verteidigungen in der Widerlegung samt ihrer Linien und — seit 0.974.0 — die
        /// verlockenden, tief widerlegten Antworten auf den besten Zug samt ihrer Linien.</summary>
        public IEnumerable<string> AllowedMoves() => new[] { BestSan, WeakSan }
            .Concat(BestReplies.SelectMany(p => p.Line)).Concat(WeakReplies.SelectMany(p => p.Line))
            .Concat(Tests.Select(t => t.ReplySan)).Concat(Tests.SelectMany(t => t.Answer))
            .Concat((Defences ?? []).SelectMany(d => d.Prefix.Append(d.MainSan)
                .Concat(d.Defences.SelectMany(x => x.Line.Prepend(x.San)))))
            .Concat((Tries ?? []).SelectMany(t => t.Line.Prepend(t.San)));
    }

    /// <param name="tries">Schwellen für die verlockenden Antworten auf den besten Zug
    /// (<see cref="MoveComparisonBestMoveTries"/>); <c>null</c> = die Vorgaben.</param>
    internal static PairFacts? PairFactsOf(MoveComparison c, MoveComparisonLine weak, MoveComparisonBestMoveTries.Settings? tries = null)
    {
        var best = c.Lines.FirstOrDefault(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == c.BestUci);
        if (best is null) return null;
        var moverWhite = FenFields.WhiteToMove(c.Fen);
        var bestSan = SanOf(c.Fen, best.CandidateUci);
        var weakSan = SanOf(c.Fen, weak.CandidateUci);
        if (bestSan is null || weakSan is null) return null;
        var weakReplies = Pvs(weak.Fen, weak.ResultJson);
        var tests = c.Lines.Where(l => l.Kind == MoveComparisonLineKind.Reply && l.CandidateUci == weak.CandidateUci
                && l.State != MoveComparisonLineState.Skipped)
            .OrderBy(l => l.Ordinal)
            .Select(l =>
            {
                var san = weakReplies.FirstOrDefault(p => p.Uci == l.ReplyUci)?.San ?? l.ReplyUci ?? "?";
                if (l.State == MoveComparisonLineState.Illegal) return new ReplyTest(san, l.State, null, []);
                var pvs = Pvs(l.Fen, l.ResultJson);
                var ev = EvalOf(l.Fen, l.ResultJson);
                return new ReplyTest(san, l.State, ev is null ? null : MoverText(ev, moverWhite), pvs.FirstOrDefault()?.Line ?? []);
            })
            .ToList();
        return new PairFacts(c.Fen, moverWhite, c.Depth,
            bestSan, best.Fen, EvalOf(best.Fen, best.ResultJson) is { } be ? MoverText(be, moverWhite) : null, Pvs(best.Fen, best.ResultJson),
            weakSan, weak.Fen, EvalOf(weak.Fen, weak.ResultJson) is { } we ? MoverText(we, moverWhite) : null, weakReplies,
            tests, MoveComparisonDefences.Of(c, weak),
            MoveComparisonBestMoveTries.Of(c, weak, tries ?? MoveComparisonBestMoveTries.Settings.Default));
    }

    internal static string SystemPrompt(string lang) =>
        $"""
        You are a friendly, precise chess coach. The reader is to move and considered two candidate moves; the engine
        rates one of them higher. Explain WHY it is better. Write in {GameMoveExplanationService.LanguageName(lang)}, three
        to five short sentences, at most 90 words:
        1. What the opponent's best answer to the weaker move achieves — name it and take what it does from the
           "Concretely" facts.
        2. Why that answer does not work (as well) against the better move: either it is not possible at all, or the
           reader's reply from the given line punishes it — name that reply and what it achieves, again from the facts.
        3. What it means in plain words, from the evaluations (e.g. the better move keeps a clear advantage, the weaker one
           only leads to an equal position). You may add the evaluations in pawns with a decimal POINT and one decimal, in
           every language (e.g. +0.8 against -0.4).
        If the facts say the two moves are almost equal, say that first and keep it short.
        Never give the evaluation itself as the reason: "the engine rates it higher" or "it keeps the advantage" only
        restate THAT it is better, not WHY. Claim an attack, a threat, a capture or a material gain ONLY if a "Concretely"
        fact says so — if none does, describe what the moves of the lines do (which piece goes where), without inventing
        tactics. Avoid empty phrases like "more active" or "better coordination" unless you say exactly what.
        Use ONLY the facts and lines given — never calculate your own variations and never mention a move that is not in
        the given lines. Name at most the first two moves of a line — never recite a whole engine line. Write moves
        exactly as given, in English algebraic notation (e.g. Nf3, Bxh7+, O-O); every other word in
        {GameMoveExplanationService.LanguageName(lang)}, no English words. Address the reader as "you", informally where the
        language distinguishes (German "du", French "tu", …); the other side is "your opponent". Do not mention which
        colour the reader plays. No centipawns, no percentages. Return the JSON object {"{"}"explanation": "..."{"}"}.
        """;

    /// <param name="detailed">Der ausführliche Faktenblock für Fable (0.971.0): Figurenliste, Gabel-Fakten
    /// (<see cref="ExplanationFacts.CheckForks"/>), die anderen Verteidigungen in der Widerlegung
    /// (<see cref="MoveComparisonDefences"/>) und — seit 0.974.0 — die verlockenden, tief widerlegten Antworten auf den
    /// besten Zug (<see cref="MoveComparisonBestMoveTries"/>). Das lokale Modell bekommt ihn NICHT — sein Auftrag (90 Wörter, höchstens zwei
    /// Züge je Linie) ist dafür nicht gebaut, und sein Prompt bleibt Zeichen für Zeichen wie bisher.</param>
    internal static string UserPrompt(PairFacts f, bool detailed = false)
    {
        var you = f.MoverWhite ? "White" : "Black";
        var number = MoveNumber(f.Fen) + (f.MoverWhite ? "." : "...");
        var best = number + f.BestSan;
        var weak = number + f.WeakSan;
        var lines = new List<string> { $"Position (FEN): {f.Fen}" };
        if (detailed && PieceList(f.Fen) is { } pieces) lines.Add($"Pieces in this position, before the move — {pieces}");
        lines.AddRange(
        [
            $"The reader plays {you} and is to move. The engine compared the reader's candidate moves at depth {f.Depth}.",
            $"Better move: {best} — evaluation from the reader's view afterwards: {f.BestEval ?? "?"}{Level(f.BestEval)}.",
            $"Weaker move: {weak} — evaluation from the reader's view afterwards: {f.WeakEval ?? "?"}{Level(f.WeakEval)}.",
        ]);
        if (ExplanationFacts.Parse(f.BestEval) is { Mate: null } b && ExplanationFacts.Parse(f.WeakEval) is { Mate: null } w
            && b.Level == w.Level && b.Pawns - w.Pawns < 0.3)
            lines.Add($"The two moves are almost equal (difference {(b.Pawns - w.Pawns).ToString("0.0", CultureInfo.InvariantCulture)} pawns).");

        if (f.WeakReplies.Count > 0)
        {
            lines.Add($"After {weak}, the opponent's best answers (evaluation from the reader's view, engine line):");
            foreach (var p in f.WeakReplies)
                lines.Add($"- {Numbered(f.WeakFen, [p.San])} ({p.MoverEval(f.MoverWhite)}): {Numbered(f.WeakFen, p.Line)}");
            var top = f.WeakReplies[0].Line;
            var facts = WithForks(detailed ? ExplanationFacts.CheckForks(f.WeakFen, top) : [],
                ExplanationFacts.FirstMoveAttacks(f.WeakFen, top)
                    .Concat(ExplanationFacts.LineEvents(f.Fen, top, gainerWhite: !f.MoverWhite, lead: f.WeakSan)));
            if (facts.Count > 0)
                lines.Add($"Concretely, after {weak} and the opponent's best answer line: {string.Join("; ", facts)}.");
            if (detailed && f.Defences is { Count: > 0 } decisions) lines.AddRange(DefenceBlock(f, decisions, you, weak));
        }

        if (f.Tests.Count > 0)
        {
            lines.Add($"The same answers against {best}:");
            foreach (var t in f.Tests)
            {
                var reply = Numbered(f.BestFen, [t.ReplySan]);
                if (t.State == MoveComparisonLineState.Illegal) { lines.Add($"- {reply}: not possible after {best}."); continue; }
                if (t.State == MoveComparisonLineState.Failed || t.Eval is null) { lines.Add($"- {reply}: could not be calculated."); continue; }
                var answer = t.Answer.Count > 0 && AfterMove(f.BestFen, t.ReplySan) is { } afterReply
                    ? $" The reader's best reply, with the engine line: {Numbered(afterReply, t.Answer)}." : "";
                lines.Add($"- {reply}: evaluation from the reader's view {t.Eval}{Level(t.Eval)}.{answer}");
                if (t.Answer.Count > 0 && AfterMove(f.BestFen, t.ReplySan) is { } fenAfter)
                {
                    var facts = ExplanationFacts.FirstMoveAttacks(fenAfter, t.Answer)
                        .Concat(ExplanationFacts.LineEvents(f.BestFen, t.Answer, gainerWhite: f.MoverWhite, lead: t.ReplySan)).ToList();
                    if (facts.Count > 0) lines.Add($"  Concretely, after {best} {t.ReplySan} and the reader's reply line: {string.Join("; ", facts)}.");
                }
            }
        }
        if (f.BestReplies.Count > 0)
        {
            var p = f.BestReplies[0];
            lines.Add($"The opponent's best answer to {best} itself: {Numbered(f.BestFen, [p.San])} ({p.MoverEval(f.MoverWhite)}): {Numbered(f.BestFen, p.Line)}");
        }
        if (detailed && f.Tries is { Count: > 0 } tries) lines.AddRange(TryBlock(f, tries, best));
        lines.Add($"Reader: plays {you} — \"you\" is the reader, {(f.MoverWhite ? "Black" : "White")} is \"your opponent\".");
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Der Block „Other defences in the refutation line“ (0.971.0): je Entscheidung des Verteidigers, was er dort an Zügen
    /// hat, und je gerechneter anderer Verteidigung Bewertung, Engine-Linie und geprüfte Fakten (Gabel, Feld, Materialbilanz
    /// ab der Stellung VOR dem Fehlzug). Ist eine Verteidigung etwa so gut wie die Hauptlinie, steht das dabei — dann
    /// scheitert sie nicht.
    /// </summary>
    private static List<string> DefenceBlock(PairFacts f, IReadOnlyList<MoveComparisonDefences.Decision> decisions, string you, string weak)
    {
        var lines = new List<string>
        {
            $"Other defences in the refutation line (the opponent's best answer line after {weak}) and why they fail — each "
            + $"calculated by the engine at depth {f.Depth}; evaluation from the reader's view, engine line, checked facts:",
        };
        foreach (var d in decisions)
        {
            var main = Numbered(d.FenBefore, [d.MainSan]);
            var after = Numbered(f.WeakFen, d.Prefix);
            var has = d.InCheck ? $"{you} is in check and has" : $"{you} has";
            lines.Add(d.Complete
                ? $"After {after} {has} exactly {d.LegalMoves} legal moves: {d.MainSan} (the main line above), {string.Join(", ", d.Defences.Select(x => x.San))}."
                : $"After {after} {has} {d.LegalMoves} legal moves; besides {main} (the main line above) only the following were calculated — do not discuss the others.");
            foreach (var x in d.Defences)
            {
                var alt = Numbered(d.FenBefore, [x.San]);
                var holds = HoldsAsWell(x.Eval, f.WeakEval) ? " — about as good as the main line, so this defence does NOT fail" : "";
                var engine = x.Line.Count > 0 ? $" Engine line: {Numbered(x.FenAfter, x.Line)}." : "";
                lines.Add($"- {alt} instead of {main}: evaluation from the reader's view {x.Eval}{Level(x.Eval)}{holds}.{engine}");
                if (x.Line.Count == 0) continue;
                var facts = WithForks(ExplanationFacts.CheckForks(x.FenAfter, x.Line),
                    x.SquareFacts.Concat(ExplanationFacts.FirstMoveAttacks(x.FenAfter, x.Line))
                        .Concat(ExplanationFacts.LineEvents(f.Fen, x.Line, gainerWhite: !f.MoverWhite, lead: f.WeakSan, leads: [.. d.Prefix, x.San])));
                if (facts.Count > 0) lines.Add($"  Concretely, after {alt}: {string.Join("; ", facts)}.");
            }
        }
        return lines;
    }

    /// <summary>
    /// Der Block „Obvious replies to the better move“ (0.974.0): die Züge des Gegners, die auf den ersten Blick gut
    /// aussehen (flache Sicht) und in der tiefen Rechnung klar scheitern — die Frage, die der Leser am Brett zuerst hat
    /// (Auswahl: <see cref="MoveComparisonBestMoveTries"/>). Je Antwort ihr Platz in der flachen Sicht, die Bewertung gegen
    /// die nach der besten Antwort, die Erwiderung des Lesers mit Engine-Linie und geprüfte Fakten: Gabel, Abzugsangriff
    /// (<see cref="ExplanationFacts.DiscoveredAttacks"/>), verlorene Figur (<see cref="ExplanationFacts.HangingCaptures"/>),
    /// Materialbilanz ab der Stellung VOR dem besten Zug.
    /// </summary>
    private static List<string> TryBlock(PairFacts f, IReadOnlyList<MoveComparisonBestMoveTries.Try> tries, string best)
    {
        var main = f.BestReplies.Count > 0 ? Numbered(f.BestFen, [f.BestReplies[0].San]) : null;
        var lines = new List<string>
        {
            $"Obvious replies to the better move {best} and why they fail — moves that look good at first sight but lose when "
            + $"calculated to depth {f.Depth}, the first thing the reader asks at the board; evaluation from the reader's view, "
            + "the reader's reply with the engine line, checked facts:",
        };
        foreach (var t in tries)
        {
            var reply = Numbered(f.BestFen, [t.San]);
            var sight = $"{(t.Takes is null ? "" : t.Takes + "; ")}looks good at first sight: {t.FirstSight}";
            var against = main is null ? "" : $", against {f.BestEval ?? "?"} after the opponent's best answer {main}";
            var engine = t.Line.Count > 0 ? $" The reader's reply, with the engine line: {Numbered(t.FenAfter, t.Line)}." : "";
            lines.Add($"- {reply} ({sight}): evaluation from the reader's view {t.Eval}{Level(t.Eval)}{against} — this reply fails.{engine}");
            if (t.Line.Count == 0) continue;
            var facts = WithForks(ExplanationFacts.CheckForks(t.FenAfter, t.Line),
                ExplanationFacts.DiscoveredAttacks(t.FenAfter, t.Line)
                    .Concat(ExplanationFacts.FirstMoveAttacks(t.FenAfter, t.Line))
                    .Concat(ExplanationFacts.HangingCaptures(f.BestFen, t.Line, leads: [t.San]))
                    .Concat(ExplanationFacts.LineEvents(f.Fen, t.Line, gainerWhite: f.MoverWhite, lead: f.BestSan, leads: [t.San])));
            if (facts.Count > 0) lines.Add($"  Concretely, after {best} {reply}: {string.Join("; ", facts)}.");
        }
        return lines;
    }

    /// <summary>Gabel-Fakten voran; „X gives check“ fällt weg, wenn die Gabel desselben Zuges das Schach schon nennt.</summary>
    private static List<string> WithForks(List<string> forks, IEnumerable<string> others)
        => forks.Concat(others.Where(o => !(o.EndsWith(" gives check", StringComparison.Ordinal) && forks.Any(k => k.StartsWith(o + " and ", StringComparison.Ordinal))))).ToList();

    /// <summary>Hält die andere Verteidigung etwa so gut wie die Hauptlinie (höchstens 0,3 Bauern schlechter)? Dann ist sie
    /// keine gescheiterte Verteidigung — die Engine-Linie des Kandidaten war an der Stelle nur eine von mehreren.</summary>
    private static bool HoldsAsWell(string? altEval, string? mainEval)
    {
        static double Score(ExplanationFacts.Eval e) => e.Mate is int m ? (m >= 0 ? 1000 - m : -1000 - m) : e.Pawns;
        return ExplanationFacts.Parse(altEval) is { } a && ExplanationFacts.Parse(mainEval) is { } main && Score(a) >= Score(main) - 0.3;
    }

    /// <summary>Die Figuren der Stellung in Worten („White: king g2, queen e3, rooks a1 c3, …“) — Modelle lesen eine FEN
    /// unzuverlässig (Befund Vereinspartie 245: „der Läufer steht in der c-Linie vor dem König“, der König stand auf b8).
    /// Bewusst keine SAN-ähnlichen Kürzel wie „Kb8“: die läse die Zugprüfung im Text als Zug.</summary>
    internal static string? PieceList(string fen)
    {
        if (BoardGeometry.FromFen(fen) is not { } geo) return null;
        string Side(bool white)
        {
            var all = geo.Pieces(white);
            return string.Join(", ", "kqrbnp"
                .Select(t => (Type: t, Squares: all.Where(p => char.ToLowerInvariant(p.Piece) == t).Select(p => p.Square).OrderBy(q => q, StringComparer.Ordinal).ToList()))
                .Where(g => g.Squares.Count > 0)
                .Select(g => $"{ExplanationFacts.PieceName(g.Type)}{(g.Squares.Count > 1 ? "s" : "")} {string.Join(' ', g.Squares)}"));
        }
        return $"White: {Side(true)}. Black: {Side(false)}.";
    }

    private static string Level(string? moverEval)
        => ExplanationFacts.Parse(moverEval) is { } e ? $" ({ExplanationFacts.LevelName(e)})" : "";

    private static string MoveNumber(string fen)
        => fen.Split(' ') is { Length: >= 6 } parts && int.TryParse(parts[5], out var n) ? n.ToString(CultureInfo.InvariantCulture) : "?";

    private static string Numbered(string fen, IReadOnlyList<string> sans) => GameRecapService.Numbered(fen, sans);
}
