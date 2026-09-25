using Chess;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Die FEHLER einer analysierten Partie — Ungenauigkeit, Fehler, grober Fehler, verpasste Chance — samt der Fakten,
/// aus denen ein Sprachmodell sie erklären darf (<see cref="GameMoveExplanationService"/>).
///
/// <para><b>SPIEGEL von <c>features/games/game-review.util.ts</c></b> (<c>classify</c>, <c>CLASS_LIMITS</c>, die
/// Miss-Regel in <c>specialClass</c>, die Zugauswahl in <c>reviewGame</c>) — genau wie <see cref="GameAccuracy"/>. Die
/// Seite zeigt die Klasse, der Server erklärt sie; laufen beide auseinander, erklärt der Text einen Zug, den die Seite
/// „gut" nennt. Beide Seiten haben Tests mit LITERALEN Grenzwerten (<c>GameMistakesTests</c> ↔
/// <c>game-review.util.spec.ts</c>). Brilliant/Great/Buch spiegelt der Server nicht: sie sind keine Fehler.</para>
/// </summary>
public static class GameMistakes
{
    /// <summary>Grenzen in Prozentpunkten Gewinnchance (chess.com-Bänder) — wie <c>CLASS_LIMITS</c> im Client.</summary>
    public const double ExcellentLimit = 2, GoodLimit = 5, InaccuracyLimit = 10, MistakeLimit = 20;

    /// <summary>Miss (verpasste Chance) wie <c>MISS_BEST_WIN</c>/<c>MISS_AFTER_WIN</c>/<c>MISS_MIN_AFTER_WIN</c>.</summary>
    public const double MissBestWin = 70, MissAfterWin = 60, MissMinAfterWin = 40;

    /// <summary>Zug-Klasse aus Sicht des Ziehenden (Spiegel von <c>classify</c>); die Grenze gehört zur BESSEREN Klasse.</summary>
    public static string Classify(double winBefore, double winAfter, bool playedIsBest)
    {
        var loss = winBefore - winAfter;
        if (playedIsBest || loss <= 0) return "best";
        if (loss <= ExcellentLimit) return "excellent";
        if (loss <= GoodLimit) return "good";
        if (loss <= InaccuracyLimit) return "inaccuracy";
        if (loss <= MistakeLimit) return "mistake";
        return "blunder";
    }

    private static bool IsError(string cls) => cls is "inaccuracy" or "mistake" or "blunder";

    /// <summary>Ein Fehler samt geprüfter Fakten. Bewertungen aus Sicht des ZIEHENDEN, Linien als SAN.</summary>
    public sealed record Flaw(
        int Ply, bool White, string Class, double WinBefore, double WinAfter,
        string FenBefore, string PlayedSan, string PlayedUci,
        string? BestSan, IReadOnlyList<string> BestLine, IReadOnlyList<string> Refutation,
        string EvalBefore, string EvalAfter)
    {
        public double Loss => WinBefore - WinAfter;
    }

    /// <summary>
    /// Alle Fehler der Partie in Halbzug-Reihenfolge. Bewertbar ist ein Zug nur mit gerechneter Stellung davor UND einer
    /// Bewertung danach (nächste Stellung, sonst der gespielte Kandidat) — dieselbe Regel wie im Client.
    /// </summary>
    public static List<Flaw> Find(IEnumerable<GameAnalysisPosition> positions, int plyCount)
    {
        var ordered = positions.OrderBy(p => p.Ply).ToList();
        var n = Math.Max(0, Math.Min(plyCount, ordered.Count));
        var byPly = ordered.Where(p => p.Ply >= 0 && p.Ply < n).ToDictionary(p => p.Ply);
        var rows = new Dictionary<int, DTOs.GameEvalPlyDto>();
        foreach (var p in byPly.Values)
            if (GameEvals.PlyOf(p.Ply, p.Fen, p.GameMoveUci, p.CandidatesJson, p.Depth) is { } dto) rows[p.Ply] = dto;
        var final = GameEvals.FinalOf(rows.Count > 0 ? rows[rows.Keys.Max()] : null, n);

        bool WhiteAt(int j) => byPly.TryGetValue(j, out var p) ? WhiteToMove(p.Fen)
            : j > 0 && byPly.TryGetValue(j - 1, out var q) && !WhiteToMove(q.Fen);
        (int? Cp, int? Mate)? EvalAt(int j) => j < n
            ? rows.TryGetValue(j, out var r) && (r.Cp is not null || r.Mate is not null) ? (r.Cp, r.Mate) : null
            : final is not null && (final.Cp is not null || final.Mate is not null) ? (final.Cp, final.Mate) : null;

        var flaws = new List<Flaw>();
        string? prevBase = null;
        for (var i = 0; i < n; i++)
        {
            string? baseClass = null;
            if (rows.TryGetValue(i, out var row) && EvalAt(i) is { } before
                && GameAccuracy.WinPercent(before.Cp, before.Mate, WhiteAt(i)) is double wb)
            {
                var after = EvalAt(i + 1)
                    ?? (row.PlayedCp is not null || row.PlayedMate is not null ? (row.PlayedCp, row.PlayedMate) : null);
                if (after is { } a && GameAccuracy.WinPercent(a.Cp, a.Mate, WhiteAt(i + 1)) is double wa)
                {
                    var white = WhiteAt(i);
                    var mb = white ? wb : 100 - wb;
                    var ma = white ? wa : 100 - wa;
                    var best = !string.IsNullOrEmpty(row.BestUci)
                        && string.Equals(row.BestUci, row.PlayedUci, StringComparison.OrdinalIgnoreCase);
                    baseClass = Classify(mb, ma, best);
                    if (IsError(baseClass))
                    {
                        // Miss: der Gegner hat davor gepatzt, die Chance war da (≥ 70 %), und sie ist nach dem Zug weg —
                        // aber keine Katastrophe (≥ 40 %), die bleibt ein grober Fehler.
                        var cls = prevBase is "mistake" or "blunder" && mb >= MissBestWin && ma <= MissAfterWin
                            && ma >= MissMinAfterWin ? "miss" : baseClass;
                        var p = byPly[i];
                        var bestLine = LineSans(p.Fen, LineOf(row), 8);
                        var refutation = byPly.TryGetValue(i + 1, out var next) && rows.TryGetValue(i + 1, out var nextRow)
                            ? LineSans(next.Fen, LineOf(nextRow), 6)
                            : new List<string>();
                        flaws.Add(new Flaw(i, white, cls, mb, ma, p.Fen, p.GameMoveSan, p.GameMoveUci,
                            bestLine.FirstOrDefault(), bestLine, refutation,
                            EvalText(before.Cp, before.Mate, white), EvalText(a.Cp, a.Mate, white)));
                    }
                }
            }
            prevBase = baseClass;
        }
        return flaws;
    }

    /// <summary>Die Engine-Linie des besten Kandidaten; ohne gespeicherte Variante (Analysen vor 0.521.0) nur der Zug.</summary>
    private static IReadOnlyList<string>? LineOf(DTOs.GameEvalPlyDto row)
        => row.Candidates.FirstOrDefault()?.Pv is { Count: > 0 } pv ? pv
            : string.IsNullOrEmpty(row.BestUci) ? null : new[] { row.BestUci };

    /// <summary>Höchstens <paramref name="max"/> Fehler — die schwersten zuerst ausgewählt, dann wieder in Zugfolge.</summary>
    public static List<Flaw> Worst(IEnumerable<Flaw> flaws, int max)
        => flaws.OrderByDescending(f => f.Loss).ThenBy(f => f.Ply).Take(max).OrderBy(f => f.Ply).ToList();

    /// <summary>Eine UCI-Linie ab der Stellung als SAN; bricht am ersten Zug ab, der dort nicht geht (Rochade als
    /// König-schlägt-Turm vom Broker wird über die Legalität aufgelöst: nur legale Züge zählen).</summary>
    public static List<string> LineSans(string fen, IReadOnlyList<string>? ucis, int max)
    {
        var sans = new List<string>();
        if (ucis is null || ucis.Count == 0) return sans;
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            foreach (var raw in ucis.Take(max))
            {
                var moves = board.Moves(generateSan: true);
                var uci = raw.ToLowerInvariant();
                var move = Array.Find(moves, m => GamePlies.ToUci(m) == uci) ?? CastlingAsKingTakesRook(moves, uci);
                if (move is null) break;
                sans.Add(move.San ?? uci);
                board.Move(move);
            }
        }
        catch { /* ungültige FEN → was bis hierher ging */ }
        return sans;
    }

    /// <summary>Der Broker schreibt Rochaden als König-schlägt-Turm (<c>e1h1</c>) — Gera.Chess kennt <c>e1g1</c>.</summary>
    private static Move? CastlingAsKingTakesRook(Move[] moves, string uci)
    {
        var target = uci switch { "e1h1" => "e1g1", "e1a1" => "e1c1", "e8h8" => "e8g8", "e8a8" => "e8c8", _ => null };
        return target == null ? null : Array.Find(moves, m => GamePlies.ToUci(m) == target && (m.San ?? "").StartsWith("O-O"));
    }

    /// <summary>Bewertung aus Sicht des Ziehenden, lesbar: „+1.35", „−0.40", „Matt in 3", „wird in 2 matt gesetzt".</summary>
    public static string EvalText(int? cp, int? mate, bool moverIsWhite)
    {
        var sign = moverIsWhite ? 1 : -1;
        if (mate is int m)
        {
            var own = m * sign;
            return own > 0 ? $"mate in {own}" : own < 0 ? $"gets mated in {-own}" : "mate";
        }
        var pawns = (cp ?? 0) * sign / 100.0;
        return pawns.ToString("+0.00;-0.00;0.00", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool WhiteToMove(string fen) => !(fen.Split(' ') is { Length: >= 2 } parts && parts[1] == "b");
}
