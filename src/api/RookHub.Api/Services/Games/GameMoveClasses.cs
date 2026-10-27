using RookHub.Api.DTOs.Games;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Die Zug-Klassen einer analysierten Partie, VORGERECHNET am Server (0.854.0, Wunsch 2026-10-27: „die Bewertungen in einem
/// eigenen Lauf ernten, damit große Listen nicht alles live rechnen — mit Versionsnummer, damit nach Änderungen neu
/// gerechnet wird"). Der SPIEGEL von <c>reviewGame</c> + <c>specialClass</c> in <c>features/games/game-review.util.ts</c>,
/// ohne „Book" (das hängt am Repertoire des Betrachters und kommt im Client obendrauf).
///
/// <para>Abgelegt an der Analyse als eine Zeichenkette, ein Zeichen je Halbzug (<see cref="GameAnalysis.MoveClasses"/>,
/// Codes in <see cref="Codes"/>), zusammen mit <see cref="Version"/> (<see cref="GameAnalysis.MoveClassesVersion"/>).
/// Gerechnet wird beim Fertigwerden und nach der Vertiefung (wie die Genauigkeit, <see cref="GameAccuracy"/>) und vom
/// Erntelauf <see cref="MoveClassHarvestWorker"/> für alles ohne oder mit alter Version.</para>
///
/// <para><b>Regeln ändern = <see cref="Version"/> hochzählen</b> und den Client im selben Commit mitziehen. Beide Seiten
/// prüfen Vereinspartie 228 mit den Markierungen von chess.com als LITERALE Erwartung (<c>GameMoveClassesTests</c> ↔
/// <c>game-review.util.spec.ts</c>).</para>
/// </summary>
public static class GameMoveClasses
{
    /// <summary>Stand der Regeln. 1 = 0.853.0 (König schlägt im Abtausch zuletzt, Good-Brillant nur mit gezogener Figur,
    /// Great = einziger guter Zug in Gewinnchance).</summary>
    public const int Version = 1;

    /// <summary>Zeichen je Klasse in <see cref="GameAnalysis.MoveClasses"/>; <c>-</c> = nicht bewertbar.</summary>
    public static readonly IReadOnlyDictionary<string, char> Codes = new Dictionary<string, char>
    {
        ["brilliant"] = 'B', ["great"] = 'G', ["best"] = 'S', ["excellent"] = 'E', ["good"] = 'O',
        ["inaccuracy"] = 'I', ["mistake"] = 'M', ["miss"] = 'X', ["blunder"] = 'P',
    };
    public const char None = '-';

    // Spiegel der Konstanten in game-review.util.ts (Werte dort, Begründung dort).
    public const double BrilliantWinningAnywayPawns = 7;
    public const double BrilliantMinAfterPawns = -1;
    public const double GreatMinGapWin = 8;
    public const double GreatMinAfterWin = 45;
    private const int MateBasePawns = 1000;

    /// <summary>Halbzüge mit Klasse <paramref name="code"/> (z. B. <c>'B'</c> = „!!") aus einer abgelegten Kette.</summary>
    public static List<int> PliesOf(string? classes, char code)
    {
        var result = new List<int>();
        if (classes is null) return result;
        for (var i = 0; i < classes.Length; i++) if (classes[i] == code) result.Add(i);
        return result;
    }

    /// <summary>Die Klassen an der Analyse ablegen (geladene <see cref="GameAnalysis.Positions"/>), mit dem aktuellen Regelstand.
    /// Wirft nie: eine Stellung, die das Brett nicht lesen kann, ist einfach ohne Sonderklasse.</summary>
    public static void Store(GameAnalysis analysis)
    {
        analysis.MoveClasses = FromPositions(analysis.Positions, analysis.PlyCount);
        analysis.MoveClassesVersion = Version;
    }

    /// <summary>Aus den Positionszeilen einer Analyse (wie <see cref="GameAccuracy.FromPositions"/>).</summary>
    public static string FromPositions(IEnumerable<GameAnalysisPosition> positions, int plyCount)
    {
        var ordered = positions.OrderBy(p => p.Ply).ToList();
        var plies = ordered
            .Select(p => GameEvals.PlyOf(p.Ply, p.Fen, p.GameMoveUci, p.CandidatesJson, p.Depth))
            .OfType<GameEvalPlyDto>()
            .ToList();
        var final = GameEvals.FinalOfPlies(plies, plyCount);
        var moveRows = ordered.Where(p => p.Ply < plyCount).ToList();
        var fens = moveRows.Select(p => p.Fen).ToList();
        var ucis = moveRows.Select(p => p.GameMoveUci).ToList();
        // Stellung nach dem letzten Zug: die Endstellungs-Zeile (0.689.0), sonst den letzten Zug selbst ausführen.
        if (fens.Count == plyCount && plyCount > 0)
        {
            var end = ordered.FirstOrDefault(p => p.Ply == plyCount)?.Fen
                ?? TacticsBoard.TryParse(fens[^1])?.Apply(ucis[^1])?.ToFen();
            if (end is not null) fens.Add(end);
        }
        return Compute(plies, final, fens, ucis, plyCount);
    }

    /// <summary>
    /// Die Klassen-Kette. <paramref name="fens"/>: Stellung vor jedem Halbzug, dazu möglichst die nach dem letzten (sonst
    /// entfallen dort die Sonderklassen); <paramref name="ucis"/> je Halbzug. Bewertungen in Weiß-Sicht wie <c>GET …/evals</c>.
    /// </summary>
    public static string Compute(IReadOnlyList<GameEvalPlyDto> plies, GameEvalScoreDto? final, IReadOnlyList<string> fens,
        IReadOnlyList<string> ucis, int plyCount)
    {
        var n = Math.Max(0, Math.Min(plyCount, fens.Count));
        var rows = new Dictionary<int, GameEvalPlyDto>();
        foreach (var p in plies) if (p.Ply >= 0 && p.Ply < n) rows[p.Ply] = p;

        bool WhiteToMoveAt(int j) => j < fens.Count ? FenFields.WhiteToMove(fens[j]) : n > 0 && !FenFields.WhiteToMove(fens[n - 1]);

        var evalAt = new (int? Cp, int? Mate)?[n + 1];
        for (var j = 0; j < n; j++)
            evalAt[j] = rows.TryGetValue(j, out var r) && (r.Cp is not null || r.Mate is not null) ? (r.Cp, r.Mate) : null;
        evalAt[n] = final is not null && (final.Cp is not null || final.Mate is not null) ? (final.Cp, final.Mate) : null;

        var codes = new char[n];
        string? prevBase = null;
        for (var i = 0; i < n; i++)
        {
            codes[i] = None;
            var wbNull = evalAt[i] is { } s0 ? GameAccuracy.WinPercent(s0.Cp, s0.Mate, WhiteToMoveAt(i)) : null;
            if (!rows.TryGetValue(i, out var row) || evalAt[i] is not { } before || wbNull is not double wb)
            {
                prevBase = null;
                continue;
            }
            var after = evalAt[i + 1] ?? (row.PlayedCp is not null || row.PlayedMate is not null ? (row.PlayedCp, row.PlayedMate) : null);
            if (after is not { } a || GameAccuracy.WinPercent(a.Cp, a.Mate, WhiteToMoveAt(i + 1)) is not double wa)
            {
                prevBase = null;
                continue;
            }
            var white = WhiteToMoveAt(i);
            var mb = white ? wb : 100 - wb;
            var ma = white ? wa : 100 - wa;
            var best = !string.IsNullOrEmpty(row.BestUci) && string.Equals(row.BestUci, row.PlayedUci, StringComparison.OrdinalIgnoreCase);
            var cls = GameMistakes.Classify(mb, ma, best);
            var label = cls;
            var uci = i < ucis.Count ? ucis[i] : null;
            if (!string.IsNullOrEmpty(uci))
            {
                label = Special(cls, prevBase, white, mb, ma, row, before, a, fens[i],
                    i + 1 < fens.Count ? fens[i + 1] : null, uci);
            }
            codes[i] = Codes[label];
            prevBase = cls;
        }
        return new string(codes);
    }

    /// <summary>Die Sonderklassen Miss / Brilliant / Great — Spiegel von <c>specialClass</c> (Regeln dort beschrieben).</summary>
    private static string Special(string baseCls, string? prevBase, bool white, double winBefore, double winAfter,
        GameEvalPlyDto row, (int? Cp, int? Mate) before, (int? Cp, int? Mate) after, string fenBefore, string? fenAfter, string uci)
    {
        var opponentErred = prevBase is "mistake" or "blunder";
        (int? Cp, int? Mate)? second = row.SecondCp is not null || row.SecondMate is not null ? (row.SecondCp, row.SecondMate) : null;
        var secondCp = second is { } sc ? MoverCp(sc, white, white) : null;

        if (baseCls is "inaccuracy" or "mistake" or "blunder" && opponentErred
            && winBefore >= GameMistakes.MissBestWin && winAfter <= GameMistakes.MissAfterWin && winAfter >= GameMistakes.MissMinAfterWin)
            return "miss";
        if (baseCls is not ("best" or "excellent" or "good")) return baseCls;
        if (fenAfter is null) return baseCls;

        MoveTactics.Sacrifice? sacrifice = null;
        var searched = false;
        MoveTactics.Sacrifice? Sacrificed()
        {
            if (!searched) { sacrifice = MoveTactics.SacrificedPiece(fenBefore, fenAfter, uci); searched = true; }
            return sacrifice;
        }

        var winningAnyway = second is { } s && secondCp is double scp
            && ((s.Mate is not null && scp > 0) || scp >= BrilliantWinningAnywayPawns * 100);
        var afterCp = MoverCp(after, white, FenFields.WhiteToMove(fenAfter));
        if (!MoveTactics.IsPromotion(uci) && !winningAnyway && afterCp is double ac && ac >= BrilliantMinAfterPawns * 100
            && !MoveTactics.InCheck(fenBefore))
        {
            if (Sacrificed() is { } sac && (baseCls != "good" || sac.Square == uci.Substring(2, 2).ToLowerInvariant()))
                return "brilliant";
        }

        if (baseCls == "best" && second is { } sec && !winningAnyway && winAfter >= GreatMinAfterWin)
        {
            var secondWin = GameAccuracy.WinPercent(sec.Cp, sec.Mate, FenFields.WhiteToMove(fenBefore));
            double? moverSecond = secondWin is double sw ? (white ? sw : 100 - sw) : null;
            if (moverSecond is double ms && winBefore - ms >= GreatMinGapWin
                && !MoveTactics.CapturesPiece(fenBefore, uci) && Sacrificed() is null)
                return "great";
        }
        return baseCls;
    }

    /// <summary>Bewertung in Centipawns aus Sicht des Ziehenden; Matt in n = ±(1000 − n) Bauern (Spiegel von <c>moverCp</c>).</summary>
    private static double? MoverCp((int? Cp, int? Mate) score, bool moverWhite, bool whiteToMoveHere)
    {
        double whiteCp;
        if (score.Mate is int mate)
        {
            var n = Math.Min(Math.Abs(mate), 999);
            var mateCp = (MateBasePawns - n) * 100.0;
            whiteCp = mate > 0 ? mateCp : mate < 0 ? -mateCp : whiteToMoveHere ? -mateCp : mateCp;
        }
        else if (score.Cp is int cp) whiteCp = cp;
        else return null;
        return moverWhite ? whiteCp : 0 - whiteCp;
    }
}
