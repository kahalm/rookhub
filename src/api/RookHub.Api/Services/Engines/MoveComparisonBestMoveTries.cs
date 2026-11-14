using System.Globalization;
using Chess;
using RookHub.Api.Models;
using RookHub.Api.Services.EngineBroker;
using RookHub.Api.Services.Games;
using static RookHub.Api.Services.Engines.MoveComparisonBoard;
using static RookHub.Api.Services.Engines.MoveComparisonEval;

namespace RookHub.Api.Services.Engines;

/// <summary>
/// Die VERLOCKENDEN Antworten auf den besten Zug (0.974.0, Befund an Vereinspartie 251, Fehler 20...dxc3 statt 20...e4:
/// „die eigentliche Begründung, warum e4 geht, ist, dass Txe4 nicht geht“). Der Spiegelfall zu
/// <see cref="MoveComparisonDefences"/>: dort die anderen Verteidigungen in der Widerlegung des FEHLZUGS, hier die Züge des
/// Gegners nach dem BESTEN Zug, die ein Leser am Brett sofort fragt. Die Kandidatenlinie des besten Zugs trägt nur die
/// beste Antwort der Engine (21.Lf1 …), die Antwort-Tests nur die Antworten auf den Fehlzug — 21.Txe4 stand nirgends, und
/// das Modell darf nur Züge aus den Linien nennen.
///
/// <para><b>Nicht jeder Schlag</b> (Nutzer 2026-11-14: „vermutlich viel zu häufig offensichtlich. Gibt es eine Möglichkeit
/// zu sehen, dass der Schlagzug irgendwie menschlich ist, aber taktisch nicht geht? Stockfish Tiefe 1/2/3?“) — zwei Sichten:</para>
/// <list type="number">
/// <item><b>Verlockend = flache Sicht</b> (<see cref="PlanGlance"/>, Zeile <see cref="MoveComparisonLineKind.BestMoveGlance"/>):
/// EINE Suche der Stellung nach dem besten Zug mit Tiefe <see cref="Settings.ShallowDepth"/> (1) und
/// <see cref="GlanceLines"/> Linien (mehr lässt das Protokoll nicht zu — für „unter den besten drei“ reicht es), mit
/// LEEREM Hash (<see cref="AnalysisJobEngineResolver.FreshSessionMaxDepth"/>): auf der warmen Hashtabelle der tiefen
/// Rechnung sieht eine Tiefe-1-Suche die Widerlegung schon — gemessen am Fall 251: 21.Txe4 leer Rang 2 (0,40 hinter dem
/// Ersten), warm Rang 4 (0,57). Verlockend ist ein Zug — JEDER, nicht nur ein Schlag —, der dort unter den besten
/// <see cref="Settings.Top"/> (3) liegt UND höchstens <see cref="Settings.WithinCp"/> (0,5 Bauern) hinter dem Ersten. Die
/// beste Antwort der tiefen Rechnung zählt nicht (sie ist schon die Kandidatenlinie).</item>
/// <item><b>Taktisch falsch = tiefe Sicht</b> (<see cref="Plan"/>, Zeilen <see cref="MoveComparisonLineKind.BestMoveTry"/>):
/// nur die verlockenden Züge — höchstens <see cref="Settings.Max"/> (2) — rechnet die Engine in der Tiefe des Vergleichs,
/// eine Linie. In den Prompt kommt ein Zug nur, wenn er dort für den Gegner mindestens <see cref="Settings.LossCp"/>
/// (1,0 Bauern) schlechter ist als seine beste Antwort (<see cref="Of"/>); sonst fällt er still weg — kein „geht
/// ebenfalls“.</item>
/// </list>
/// <para>Steht ein verlockender Zug schon als gerechneter Antwort-Test da (<see cref="MoveComparisonLineKind.Reply"/>) oder
/// zeigt eine weitere Linie des besten Kandidaten, dass er tief in Ordnung ist, braucht er keine eigene Rechnung.</para>
/// <para><b>Zeilen</b>: flache Sicht — <see cref="MoveComparisonLine.CandidateUci"/> = der beste Zug,
/// <see cref="MoveComparisonLine.Fen"/> = die Stellung danach (der Gegner am Zug), kein <c>ReplyUci</c>. Tiefe Sicht —
/// <c>CandidateUci</c> = der beste Zug, <c>ReplyUci</c> = die verlockende Antwort, <c>Ordinal</c> = ihr Rang in der flachen
/// Sicht (0 = Erster), <c>Fen</c> = die Stellung danach (der Leser am Zug).</para>
/// </summary>
internal static class MoveComparisonBestMoveTries
{
    /// <summary>Linien der flachen Sicht: das Protokoll erlaubt höchstens fünf (<see cref="EngineProtocol.MaxMultiPv"/>).</summary>
    public const int GlanceLines = EngineProtocol.MaxMultiPv;

    /// <summary>Schwellen (<c>ClubMistakeComparisons:BestMoveTries</c>, <c>:BestMoveTryShallowDepth</c>, <c>:BestMoveTryTop</c>,
    /// <c>:BestMoveTryWithinPawns</c>, <c>:BestMoveTryLossPawns</c>); <see cref="Max"/> 0 = aus.</summary>
    /// <param name="Max">Höchstens so viele tiefe Rechnungen je Vergleich.</param>
    /// <param name="ShallowDepth">Tiefe der flachen Sicht.</param>
    /// <param name="Top">Verlockend nur unter den besten so vielen Zügen der flachen Sicht.</param>
    /// <param name="WithinCp">… und höchstens so viele Hundertstelbauern hinter ihrem Ersten.</param>
    /// <param name="LossCp">In den Prompt nur, wenn tief mindestens so viele Hundertstelbauern schlechter als die beste Antwort.</param>
    internal sealed record Settings(int Max, int ShallowDepth, int Top, int WithinCp, int LossCp)
    {
        public static readonly Settings Default = new(2, 1, 3, 50, 100);

        public static Settings From(IConfiguration? config)
        {
            static int Cp(double pawns) => (int)Math.Round(pawns * 100);
            return new Settings(
                Math.Clamp(config?.GetValue<int?>("ClubMistakeComparisons:BestMoveTries") ?? Default.Max, 0, 4),
                Math.Clamp(config?.GetValue<int?>("ClubMistakeComparisons:BestMoveTryShallowDepth") ?? Default.ShallowDepth, 1, AnalysisJobEngineResolver.FreshSessionMaxDepth),
                Math.Clamp(config?.GetValue<int?>("ClubMistakeComparisons:BestMoveTryTop") ?? Default.Top, 1, GlanceLines),
                Math.Clamp(Cp(config?.GetValue<double?>("ClubMistakeComparisons:BestMoveTryWithinPawns") ?? Default.WithinCp / 100.0), 0, 1000),
                Math.Clamp(Cp(config?.GetValue<double?>("ClubMistakeComparisons:BestMoveTryLossPawns") ?? Default.LossCp / 100.0), 0, 2000));
        }
    }

    /// <summary>Ein verlockender Zug nach der flachen Sicht: Rang dort (1 = Erster), Abstand zum Ersten in Hundertstelbauern
    /// (<c>null</c> bei Mattwerten), Zahl der legalen Züge und — wenn er schlägt — was.</summary>
    private sealed record Tempting(string Uci, string San, int Rank, int? GapCp, int LegalMoves, string? Takes);

    /// <summary>Eine verlockende, tief widerlegte Antwort als Fakt: Zug, was sie schlägt, ihr Platz in der flachen Sicht,
    /// die Stellung danach, die Bewertung aus Sicht des Lesers und die Linie der Engine (wie der Leser sie widerlegt).</summary>
    internal sealed record Try(string San, string? Takes, int ShallowDepth, int Rank, int? GapCp, int LegalMoves,
        string FenAfter, string Eval, IReadOnlyList<string> Line)
    {
        /// <summary>„the shallow search (depth 1) ranks it 2nd of 51 legal moves, 0.40 pawns behind its first choice“.</summary>
        public string FirstSight => $"the shallow search (depth {ShallowDepth}) ranks it {Ordinal(Rank)} of {LegalMoves} legal moves"
            + (Rank > 1 && GapCp is int gap ? $", {(gap / 100.0).ToString("0.00", CultureInfo.InvariantCulture)} pawns behind its first choice" : "");

        private static string Ordinal(int n) => n switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th" };
    }

    /// <summary>Die Zeile der flachen Sicht (offen), oder <c>null</c>: kein bester Kandidat, nichts zu begründen, der beste
    /// Zug beendet die Partie oder der Gegner hat nur einen Zug.</summary>
    internal static MoveComparisonLine? PlanGlance(MoveComparison c)
    {
        if (BestLine(c) is not { } best || !MoveComparisonSteps.WeakerCandidates(c).Any() || Terminal(best.Fen) is not null) return null;
        try { if (ChessBoard.LoadFromFen(best.Fen).Moves().Length < 2) return null; }
        catch { return null; }
        return new MoveComparisonLine { Kind = MoveComparisonLineKind.BestMoveGlance, CandidateUci = best.CandidateUci, Ordinal = 0, Fen = best.Fen };
    }

    /// <summary>Die Zeilen der tiefen Sicht nach fertiger flacher Sicht, höchstens <see cref="Settings.Max"/> — offen, oder
    /// gleich erledigt, wenn die Stellung danach zu Ende ist. Leer, wenn nichts verlockt oder alles schon gerechnet ist.</summary>
    internal static List<MoveComparisonLine> Plan(MoveComparison c, Settings s)
    {
        var lines = new List<MoveComparisonLine>();
        if (s.Max <= 0 || BestLine(c) is not { } best || Glance(c, best) is not { } glance) return lines;
        var weaker = MoveComparisonSteps.WeakerCandidates(c).ToList();
        if (weaker.Count == 0) return lines;
        var known = Pvs(best.Fen, best.ResultJson);
        foreach (var t in TemptingMoves(best, glance, s))
        {
            if (lines.Count >= s.Max) break;
            if (weaker.All(w => TestRow(c, w, t.Uci) is not null)) continue;   // steht schon als Antwort-Test da
            // Eine weitere Linie des besten Kandidaten kennt den Zug schon tief: nicht widerlegt → fiele ohnehin weg.
            if (known.Count > 1 && known.Skip(1).FirstOrDefault(p => SameMove(best.Fen, p.Uci, t.Uci)) is { } pv
                && OpponentScore(best.Fen, known[0]) - OpponentScore(best.Fen, pv) < s.LossCp) continue;
            if (PlayUci(best.Fen, t.Uci) is not { } after) continue;
            var line = new MoveComparisonLine
            {
                Kind = MoveComparisonLineKind.BestMoveTry, CandidateUci = best.CandidateUci, ReplyUci = t.Uci, Ordinal = t.Rank - 1, Fen = after,
            };
            if (Terminal(after) is not null) line.State = MoveComparisonLineState.Done;
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>Die verlockenden UND tief widerlegten Antworten auf den besten Zug als Fakten, in der Reihenfolge der flachen
    /// Sicht — aus den eigenen Zeilen oder, wo derselbe Zug schon Antwort-Test zu <paramref name="weak"/> ist, von dort.
    /// Gescheiterte, nicht (mehr) zur Stellung passende und tief nicht widerlegte Zeilen fehlen einfach.</summary>
    internal static List<Try> Of(MoveComparison c, MoveComparisonLine weak, Settings s)
    {
        var result = new List<Try>();
        if (s.Max <= 0 || BestLine(c) is not { } best || Glance(c, best) is not { } glance
            || EvalOf(best.Fen, best.ResultJson) is not { } mainEv) return result;
        var rows = c.Lines.Where(l => l.Kind == MoveComparisonLineKind.BestMoveTry && l.CandidateUci == best.CandidateUci
            && l.State == MoveComparisonLineState.Done).ToList();
        var moverWhite = FenFields.WhiteToMove(c.Fen);
        double Reader(Ev e) => moverWhite ? e.Score : -e.Score;
        foreach (var t in TemptingMoves(best, glance, s))
        {
            var row = rows.FirstOrDefault(r => r.ReplyUci == t.Uci) ?? TestRow(c, weak, t.Uci);
            if (row is null || PlayUci(best.Fen, t.Uci) is not { } after || !MoveComparisonFromAnalysis.SameFen(after, row.Fen)) continue;
            if (EvalOf(row.Fen, row.ResultJson) is not { } ev || Reader(ev) - Reader(mainEv) < s.LossCp) continue;
            var line = Pvs(row.Fen, row.ResultJson).FirstOrDefault()?.Line ?? [];
            result.Add(new Try(t.San, t.Takes, Math.Max(1, glance.ReachedDepth), t.Rank, t.GapCp, t.LegalMoves, row.Fen, MoverText(ev, moverWhite), line));
        }
        return result;
    }

    private static MoveComparisonLine? BestLine(MoveComparison c) => c.BestUci is null ? null
        : c.Lines.FirstOrDefault(l => l.Kind == MoveComparisonLineKind.Candidate && l.CandidateUci == c.BestUci
            && l.State == MoveComparisonLineState.Done && !string.IsNullOrEmpty(l.Fen));

    /// <summary>Die fertige flache Sicht zum besten Zug (dieselbe Stellung).</summary>
    private static MoveComparisonLine? Glance(MoveComparison c, MoveComparisonLine best)
        => c.Lines.FirstOrDefault(l => l.Kind == MoveComparisonLineKind.BestMoveGlance && l.CandidateUci == best.CandidateUci
            && l.State == MoveComparisonLineState.Done && MoveComparisonFromAnalysis.SameFen(l.Fen, best.Fen));

    /// <summary>Der fertige Antwort-Test zu <paramref name="weak"/>, der genau diesen Zug nach dem besten gerechnet hat.</summary>
    private static MoveComparisonLine? TestRow(MoveComparison c, MoveComparisonLine weak, string uci)
        => c.Lines.FirstOrDefault(l => l.Kind == MoveComparisonLineKind.Reply && l.CandidateUci == weak.CandidateUci
            && l.State == MoveComparisonLineState.Done && !string.IsNullOrEmpty(l.Fen)
            && string.Equals(l.ReplyUci, uci, StringComparison.OrdinalIgnoreCase));

    /// <summary>Die verlockenden Züge nach der flachen Sicht, in deren Reihenfolge, ohne die beste Antwort der tiefen
    /// Rechnung. Leer, wenn eine der beiden Zeilen nicht lesbar ist.</summary>
    private static List<Tempting> TemptingMoves(MoveComparisonLine best, MoveComparisonLine glance, Settings s)
    {
        var result = new List<Tempting>();
        if (BrokerCandidates.Parse(best.ResultJson, best.Fen) is not { Count: > 0 } deep
            || BrokerCandidates.Parse(glance.ResultJson, best.Fen) is not { Count: > 0 } shallow) return result;
        try
        {
            var legal = ChessBoard.LoadFromFen(best.Fen).Moves(generateSan: true);
            if (FindMove(legal, deep[0].Uci) is not { } main) return result;
            var mainUci = GamePlies.ToUci(main);
            var first = Score(shallow[0]);
            for (var i = 0; i < shallow.Count && i < s.Top; i++)
            {
                if (FindMove(legal, shallow[i].Uci) is not { San: not null } move) continue;
                var uci = GamePlies.ToUci(move);
                var gap = first - Score(shallow[i]);
                if (uci == mainUci || gap > s.WithinCp) continue;
                var exact = shallow[0].Mate is null && shallow[i].Mate is null;
                result.Add(new Tempting(uci, move.San!, i + 1, exact ? (int)gap : null, legal.Length, TakesText(move)));
            }
        }
        catch
        {
            // Stellung nicht nachzuspielen — was bis hierher gefunden ist, gilt.
        }
        return result;
    }

    private static string? TakesText(Move m) => m.CapturedPiece is not { } captured ? null
        : m.Parameter is MoveEnPassant ? "takes the pawn en passant"
        : $"takes the {ExplanationFacts.PieceName(char.ToLowerInvariant(captured.Type.AsChar))} on {m.NewPosition}";

    /// <summary>Rangwert eines Kandidaten aus Sicht der Seite am Zug (Matt weit außerhalb jeder Bauernzahl).</summary>
    private static double Score(BrokerCandidates.Candidate cand)
        => cand.Mate is int m ? (m > 0 ? 100_000 - m : -100_000 - m) : cand.Cp ?? 0;

    /// <summary>Bewertung einer Linie ab <paramref name="fen"/> aus Sicht der Seite, die dort am Zug ist (der Gegner).</summary>
    private static double OpponentScore(string fen, Pv pv)
    {
        var sign = FenFields.WhiteToMove(fen) ? 1 : -1;
        return pv.MateWhite is int m ? sign * (m > 0 ? 100_000 - m : -100_000 - m) : sign * (pv.CpWhite ?? 0);
    }

    private static bool SameMove(string fen, string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            var legal = ChessBoard.LoadFromFen(fen).Moves();
            return FindMove(legal, a) is { } x && FindMove(legal, b) is { } y && GamePlies.ToUci(x) == GamePlies.ToUci(y);
        }
        catch { return false; }
    }
}
