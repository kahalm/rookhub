using System.Text.Json;
using Chess;

namespace RookHub.Api.Services.Tactics;

/// <summary>
/// Die Regeln der Taktik-Ernte (0.657.0), rein und ohne Datenbank. Nachgebaut nach dem Lichess-Puzzler
/// (github.com/ornicar/lichess-puzzler, <c>generator/generator.py</c>; die Schwellen übernommen, kein Code — der steht unter
/// AGPL):
/// <list type="bullet">
/// <item><b>Kandidat</b>: die Gewinnchance der Seite am Zug stieg mit dem letzten Zug des Gegners um mehr als
/// <see cref="BlunderGain"/> (-1..1), sie stand vorher nicht schon klar besser (<see cref="AlreadyWinningCp"/>, außer es
/// geht jetzt um ein schnelles Matt), und jetzt gibt es Matt in höchstens <see cref="MateSoon"/> oder mindestens
/// <see cref="MinAdvantageCp"/>.</item>
/// <item><b>Eindeutig</b>: der beste Zug liegt um mehr als <see cref="UniqueGap"/> vor dem zweitbesten — bei jedem Zug des
/// Lösers, auch den späteren. Bei Matt-Aufgaben zählt ein zweites Matt als gleichwertig.</item>
/// </list>
/// Ob der Spieler die Taktik in der Partie gefunden hat, entscheidet nicht über die Aufnahme — es wird nur vermerkt.
/// </summary>
public static class TacticHarvest
{
    public const double BlunderGain = 0.6;
    public const double UniqueGap = 0.7;
    public const int AlreadyWinningCp = 300;
    public const int MinAdvantageCp = 200;
    public const int MateSoon = 15;
    /// <summary>Höchstens so viele Züge des Lösers.</summary>
    public const int MaxSolverMoves = 6;

    public sealed record Cand(string Uci, int? Cp, int? Mate, IReadOnlyList<string> Pv);

    /// <summary>Gewinnchance -1..1 aus Sicht der Seite am Zug (Lichess-Kurve; Matt = ±1).</summary>
    public static double WinChance(Cand c) => c.Mate is { } m ? (m > 0 ? 1 : -1) : WinChanceCp(c.Cp ?? 0);

    public static double WinChanceCp(int cp) => 2 / (1 + Math.Exp(-0.00368208 * Math.Clamp(cp, -1000, 1000))) - 1;

    /// <summary>Liest <see cref="Models.GameAnalysisPosition.CandidatesJson"/> (Sicht der Seite am Zug).</summary>
    public static List<Cand> Parse(string? json)
    {
        var list = new List<Cand>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (!e.TryGetProperty("uci", out var u) || u.GetString() is not { Length: >= 4 } uci) continue;
                int? cp = e.TryGetProperty("cp", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null;
                int? mate = e.TryGetProperty("mate", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : null;
                var pv = e.TryGetProperty("pv", out var p) && p.ValueKind == JsonValueKind.Array
                    ? p.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length >= 4).ToList()
                    : new List<string>();
                list.Add(new Cand(uci, cp, mate, pv));
            }
        }
        catch (JsonException) { list.Clear(); }
        return list;
    }

    public sealed record Found(string Kind, int? MateIn, Cand Best);

    /// <summary>
    /// Ist die Stellung (<paramref name="here"/>, Seite am Zug) eine Aufgabe, nachdem der Gegner in <paramref name="before"/>
    /// (er am Zug) gezogen hat? → Art und bester Zug, sonst <c>null</c>.
    /// </summary>
    public static Found? Detect(IReadOnlyList<Cand> before, IReadOnlyList<Cand> here)
    {
        if (before.Count == 0 || here.Count < 2) return null;
        var best = here[0];
        if (best.Mate is < 0) return null;
        var prevForMe = -WinChance(before[0]);                 // vor dem Fehler, aus meiner Sicht
        if (!(WinChance(best) > prevForMe + BlunderGain)) return null;
        var mateSoon = best.Mate is > 0 and <= MateSoon;
        var prevCp = before[0].Mate is null ? -(before[0].Cp ?? 0) : (int?)null;
        if (prevCp > AlreadyWinningCp && !mateSoon) return null;
        if (prevCp is null && before[0].Mate < 0 && !mateSoon) return null;   // ich hatte schon Matt
        if (!mateSoon && (best.Mate is not null || (best.Cp ?? 0) < MinAdvantageCp)) return null;
        if (!IsUnique(here, mateSoon)) return null;
        return new Found(mateSoon ? "mate" : "material", mateSoon ? best.Mate : null, best);
    }

    /// <summary>Genau ein guter Zug? Bei Matt-Aufgaben ist ein zweites Matt gleich gut.</summary>
    public static bool IsUnique(IReadOnlyList<Cand> cands, bool mate)
    {
        if (cands.Count == 0) return false;
        if (cands.Count == 1) return true;              // nur ein Kandidat geliefert: die Engine sah keinen zweiten
        // Matt-Aufgabe: eindeutig, solange kein zweiter Zug ebenfalls mattsetzt (ein großer Vorteil ist dort keine Lösung)
        if (mate) return cands[1].Mate is not > 0;
        return WinChance(cands[0]) > WinChance(cands[1]) + UniqueGap;
    }

    /// <summary>Text der Bewertung aus Sicht des Lösers (<c>+5.7</c> / <c>#3</c>).</summary>
    public static string EvalText(Cand c) =>
        c.Mate is { } m ? $"#{m}" : ((c.Cp ?? 0) / 100.0).ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture);

    // ── Brett ──

    public static ChessBoard? Load(string fen)
    {
        try { return ChessBoard.LoadFromFen(fen); } catch { return null; }
    }

    /// <summary>Den Zug ausführen → neue Stellung (FEN) und ob die Partie damit vorbei ist; <c>null</c> = geht nicht.</summary>
    public static (string Fen, bool Over, Move Move)? Play(string fen, string uci)
    {
        var board = Load(fen);
        if (board is null) return null;
        try
        {
            var move = MoveComparisonService.FindMove(board.Moves(generateSan: true), uci);
            if (move is null) return null;
            board.Move(move);
            return (board.ToFen(), board.IsEndGame || board.Moves().Length == 0, move);
        }
        catch { return null; }
    }

    private static int Value(PieceType? t) =>
        t == PieceType.Pawn ? 1 : t == PieceType.Knight || t == PieceType.Bishop ? 3 : t == PieceType.Rook ? 5 :
        t == PieceType.Queen ? 9 : t == PieceType.King ? 100 : 0;

    /// <summary>
    /// Themen der Lösung (eigene, einfache Erkennung): <c>mateInN</c>/<c>mate</c> (nur wenn die Lösung mattsetzt), <c>oneMove</c>/<c>short</c>/<c>long</c>,
    /// <c>promotion</c>, <c>check</c> (erster Zug gibt Schach), <c>hangingPiece</c> (erster Zug schlägt, der Gegner kann nicht
    /// zurückschlagen), <c>fork</c> (die gezogene Figur greift danach zwei wertvolle Figuren an, König zählt mit).
    /// </summary>
    public static List<string> Themes(string fen, IReadOnlyList<string> solution, string kind)
    {
        var themes = new List<string>();
        var solverMoves = (solution.Count + 1) / 2;
        if (kind == "mate")
        {
            var cur = fen; var over = false;
            foreach (var u in solution) { var p = Play(cur, u); if (p is null) break; cur = p.Value.Fen; over = p.Value.Over; }
            if (over) themes.Add(solverMoves <= 5 ? $"mateIn{solverMoves}" : "mate");   // endet die Lösung vorher, kein Matt-Etikett
        }
        themes.Add(solverMoves == 1 ? "oneMove" : solverMoves == 2 ? "short" : "long");
        if (solution.Count == 0) return themes;
        var first = Play(fen, solution[0]);
        if (first is null) return themes;
        var mv = first.Value.Move;
        if (solution.Any(u => u.Length == 5)) themes.Add("promotion");
        if (mv.IsCheck) themes.Add("check");
        if (mv.CapturedPiece is not null && !mv.IsEnPassant)
        {
            var after = Load(first.Value.Fen);
            var dest = mv.NewPosition.ToString();
            if (after is not null && !after.Moves().Any(m => m.NewPosition.ToString() == dest)) themes.Add("hangingPiece");
        }
        if (IsFork(first.Value.Fen, mv)) themes.Add("fork");
        return themes;
    }

    /// <summary>Gabel: in der Stellung danach (Seite am Zug probeweise gedreht) könnte die gezogene Figur zwei Figuren ab
    /// Wert 3 schlagen; gibt der Zug Schach, zählt der König als eines der beiden Ziele. Lädt die gedrehte Stellung nicht
    /// (die Bibliothek lehnt manche Stellung mit Schach ab), gibt es eben kein Etikett.</summary>
    private static bool IsFork(string fenAfter, Move mv)
    {
        if (mv.Piece.Type == PieceType.King) return false;
        var parts = fenAfter.Split(' ');
        if (parts.Length < 4) return false;
        var flipped = string.Join(' ', new[] { parts[0], parts[1] == "w" ? "b" : "w", parts[2], "-" }.Concat(parts.Skip(4)));
        var board = Load(flipped);
        if (board is null) return false;
        try
        {
            var from = mv.NewPosition.ToString();
            var targets = board.Moves().Where(m => m.OriginalPosition.ToString() == from && m.CapturedPiece is not null)
                .Count(m => Value(m.CapturedPiece!.Type) is >= 3 and < 100);
            return targets + (mv.IsCheck ? 1 : 0) >= 2;
        }
        catch { return false; }
    }
}
