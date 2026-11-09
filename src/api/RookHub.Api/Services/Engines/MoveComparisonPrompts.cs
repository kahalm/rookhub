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
        IReadOnlyList<ReplyTest> Tests)
    {
        public IEnumerable<string> AllowedMoves() => new[] { BestSan, WeakSan }
            .Concat(BestReplies.SelectMany(p => p.Line)).Concat(WeakReplies.SelectMany(p => p.Line))
            .Concat(Tests.Select(t => t.ReplySan)).Concat(Tests.SelectMany(t => t.Answer));
    }

    internal static PairFacts? PairFactsOf(MoveComparison c, MoveComparisonLine weak)
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
            tests);
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

    internal static string UserPrompt(PairFacts f)
    {
        var you = f.MoverWhite ? "White" : "Black";
        var number = MoveNumber(f.Fen) + (f.MoverWhite ? "." : "...");
        var best = number + f.BestSan;
        var weak = number + f.WeakSan;
        var lines = new List<string>
        {
            $"Position (FEN): {f.Fen}",
            $"The reader plays {you} and is to move. The engine compared the reader's candidate moves at depth {f.Depth}.",
            $"Better move: {best} — evaluation from the reader's view afterwards: {f.BestEval ?? "?"}{Level(f.BestEval)}.",
            $"Weaker move: {weak} — evaluation from the reader's view afterwards: {f.WeakEval ?? "?"}{Level(f.WeakEval)}.",
        };
        if (ExplanationFacts.Parse(f.BestEval) is { Mate: null } b && ExplanationFacts.Parse(f.WeakEval) is { Mate: null } w
            && b.Level == w.Level && b.Pawns - w.Pawns < 0.3)
            lines.Add($"The two moves are almost equal (difference {(b.Pawns - w.Pawns).ToString("0.0", CultureInfo.InvariantCulture)} pawns).");

        if (f.WeakReplies.Count > 0)
        {
            lines.Add($"After {weak}, the opponent's best answers (evaluation from the reader's view, engine line):");
            foreach (var p in f.WeakReplies)
                lines.Add($"- {Numbered(f.WeakFen, [p.San])} ({p.MoverEval(f.MoverWhite)}): {Numbered(f.WeakFen, p.Line)}");
            var top = f.WeakReplies[0].Line;
            var facts = ExplanationFacts.FirstMoveAttacks(f.WeakFen, top)
                .Concat(ExplanationFacts.LineEvents(f.Fen, top, gainerWhite: !f.MoverWhite, lead: f.WeakSan)).ToList();
            if (facts.Count > 0)
                lines.Add($"Concretely, after {weak} and the opponent's best answer line: {string.Join("; ", facts)}.");
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
        lines.Add($"Reader: plays {you} — \"you\" is the reader, {(f.MoverWhite ? "Black" : "White")} is \"your opponent\".");
        return string.Join('\n', lines);
    }

    private static string Level(string? moverEval)
        => ExplanationFacts.Parse(moverEval) is { } e ? $" ({ExplanationFacts.LevelName(e)})" : "";

    private static string MoveNumber(string fen)
        => fen.Split(' ') is { Length: >= 6 } parts && int.TryParse(parts[5], out var n) ? n.ToString(CultureInfo.InvariantCulture) : "?";

    private static string Numbered(string fen, IReadOnlyList<string> sans) => GameRecapService.Numbered(fen, sans);
}
