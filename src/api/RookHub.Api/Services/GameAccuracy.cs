using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Genauigkeit je Seite einer analysierten Partie — der SPIEGEL von <c>features/games/game-review.util.ts</c>
/// (<c>winPercent</c>, <c>moveAccuracy</c>, <c>volatilityWeights</c>, <c>sideAccuracy</c>, die Zug-Auswahl
/// in <c>reviewGame</c>). Die Seite rechnet dieselben Zahlen live fuer die Kurve; hier werden sie EINMAL
/// beim Fertigwerden der Analyse gerechnet und an ihr abgelegt (<see cref="GameAnalysis.AccuracyWhite"/>),
/// damit die Partienliste sie zeigen kann, ohne je Partie die Stellungen zu laden.
///
/// <para>Formeln von Lichess (https://lichess.org/page/accuracy, lila <c>WinPercent</c>/<c>AccuracyPercent</c>):
/// Gewinnchance <c>50 + 50 · (2 / (1 + e^(−0,00368208 · cp)) − 1)</c>, Matt = Rand (100/0), Zug-Genauigkeit
/// <c>103,1668 · e^(−0,04354 · Verlust) − 3,1669 + 1</c> (kein Verlust = 100; das +1 ist lilas „uncertainty
/// bonus", Bewertung fuer die Gewinnchance bei ±1000 cp gekappt — beides seit 0.521.1), Gewicht = Standardabweichung der
/// Gewinnchance im Fenster (Breite ⌊Halbzuege/10⌋ auf 2..8, das erste Fenster fuer die ersten Breite−2 Zuege
/// wiederholt) auf 0,5..12, Seite = Mittel aus gewichtetem und harmonischem Mittel.</para>
///
/// <para><b>Beide Seiten bekommen Tests mit LITERALEN Werten</b> (<c>GameAccuracyTests</c> ↔
/// <c>game-review.util.spec.ts</c>) — nie einen, der die Gegenseite importiert. Wer hier eine Zahl aendert,
/// aendert sie drueben mit, sonst zeigt die Liste eine andere Genauigkeit als die Partie-Seite.</para>
/// </summary>
public static class GameAccuracy
{
    /// <summary>Lichess-Konstante der Gewinnchance (lila <c>WinPercent</c>, PR #11148).</summary>
    public const double WinMultiplier = -0.00368208;

    /// <summary>Lichess kappt die Bewertung fuer die Gewinnchance bei ±1000 cp (lila <c>Centipawns.ceiled</c>).</summary>
    public const int WinCpCap = 1000;

    /// <summary>lila <c>AccuracyPercent.fromWinPercents</c>: +1 auf jede Zug-Genauigkeit („uncertainty bonus (due to
    /// imperfect analysis)"). Fehlte bis 0.521.1 — Spiegel von <c>ACCURACY_UNCERTAINTY_BONUS</c> im Client.</summary>
    public const double UncertaintyBonus = 1;

    public sealed record Result(double? White, double? Black);

    /// <summary>Gewinnchance fuer WEISS in Prozent (0..100); <c>null</c> ohne Bewertung. <c>mate == 0</c> heisst,
    /// die Seite am Zug IST matt — dafuer braucht es <paramref name="whiteToMoveHere"/>.</summary>
    public static double? WinPercent(int? cp, int? mate, bool whiteToMoveHere = true)
    {
        if (mate is int m)
        {
            if (m > 0) return 100;
            if (m < 0) return 0;
            return whiteToMoveHere ? 0 : 100;
        }
        if (cp is null) return null;
        var capped = Math.Clamp(cp.Value, -WinCpCap, WinCpCap);
        return 50 + 50 * (2 / (1 + Math.Exp(WinMultiplier * capped)) - 1);
    }

    /// <summary>Genauigkeit EINES Zuges aus Sicht des Ziehenden; kein Verlust = 100, nie unter 0.</summary>
    public static double MoveAccuracy(double winBefore, double winAfter)
    {
        if (winAfter >= winBefore) return 100;
        var raw = 103.1668 * Math.Exp(-0.04354 * (winBefore - winAfter)) - 3.1669 + UncertaintyBonus;
        return Math.Min(100, Math.Max(0, raw));
    }

    /// <summary>Fensterbreite der Volatilitaet: Halbzuege / 10, ganzzahlig, auf 2..8 begrenzt.</summary>
    public static int WindowSizeFor(int plies) => Math.Min(8, Math.Max(2, plies / 10));

    /// <summary>Gewicht je Zug (n Gewichte fuer n+1 Stellungen), Luecken fallen aus ihrem Fenster.</summary>
    public static double[] VolatilityWeights(IReadOnlyList<double?> series)
    {
        var n = series.Count - 1;
        if (n <= 0) return Array.Empty<double>();
        var size = WindowSizeFor(n);
        var windows = new List<IReadOnlyList<double?>>();
        var first = series.Take(size).ToList();
        for (var i = 0; i < Math.Min(size, series.Count) - 2; i++) windows.Add(first);
        for (var k = 0; k + size <= series.Count; k++) windows.Add(series.Skip(k).Take(size).ToList());
        return windows.Take(n).Select(w => Math.Min(12, Math.Max(0.5, StdDev(w)))).ToArray();
    }

    /// <summary>Mittel aus gewichtetem und harmonischem Mittel; <c>null</c> ohne Zug.</summary>
    public static double? SideAccuracy(IReadOnlyList<(double Accuracy, double Weight)> entries)
    {
        if (entries.Count == 0) return null;
        var weightSum = entries.Sum(e => e.Weight);
        var weighted = entries.Sum(e => e.Accuracy * e.Weight) / weightSum;
        var harmonic = entries.Count / entries.Sum(e => 1 / Math.Max(1, e.Accuracy));
        return (weighted + harmonic) / 2;
    }

    /// <summary>
    /// Beide Seiten aus den Bewertungen (Weiss-Sicht, wie <c>GET …/evals</c>), der Endbewertung und den FENs der
    /// Stellungen 0..n−1 (die Seite am Zug kommt aus der FEN, nie aus der Paritaet; Stellung n ist die
    /// Gegenseite von n−1). Bewertbar ist ein Zug nur mit gerechneter Stellung davor UND einer Bewertung
    /// danach — die naechste Stellung, sonst der gespielte Kandidat.
    /// </summary>
    public static Result Compute(IReadOnlyList<GameEvalPlyDto> plies, GameEvalScoreDto? final, IReadOnlyList<string> fens, int plyCount)
    {
        var n = Math.Max(0, Math.Min(plyCount, fens.Count));
        var rows = new Dictionary<int, GameEvalPlyDto>();
        foreach (var p in plies) if (p.Ply >= 0 && p.Ply < n) rows[p.Ply] = p;

        bool WhiteToMoveAt(int j) => j < n ? WhiteToMove(fens[j]) : n > 0 && !WhiteToMove(fens[n - 1]);

        var evalAt = new (int? Cp, int? Mate)?[n + 1];
        for (var j = 0; j < n; j++)
            evalAt[j] = rows.TryGetValue(j, out var r) && (r.Cp is not null || r.Mate is not null) ? (r.Cp, r.Mate) : null;
        evalAt[n] = final is not null && (final.Cp is not null || final.Mate is not null) ? (final.Cp, final.Mate) : null;
        var series = new double?[n + 1];
        for (var j = 0; j <= n; j++) series[j] = evalAt[j] is { } s ? WinPercent(s.Cp, s.Mate, WhiteToMoveAt(j)) : null;

        var weights = VolatilityWeights(series);
        var white = new List<(double, double)>();
        var black = new List<(double, double)>();
        for (var i = 0; i < n; i++)
        {
            if (!rows.TryGetValue(i, out var row) || evalAt[i] is null || series[i] is not double wb) continue;
            var after = evalAt[i + 1]
                ?? (row.PlayedCp is not null || row.PlayedMate is not null ? (row.PlayedCp, row.PlayedMate) : null);
            if (after is not { } a) continue;
            if (WinPercent(a.Cp, a.Mate, WhiteToMoveAt(i + 1)) is not double wa) continue;

            var whiteMoves = WhiteToMoveAt(i);
            var mb = whiteMoves ? wb : 100 - wb;
            var ma = whiteMoves ? wa : 100 - wa;
            var entry = (MoveAccuracy(mb, ma), i < weights.Length ? weights[i] : 0.5);
            (whiteMoves ? white : black).Add(entry);
        }
        return new Result(SideAccuracy(white), SideAccuracy(black));
    }

    /// <summary>Aus den Positionszeilen einer Analyse — der Weg beim Fertigwerden und beim Nachtrag.</summary>
    public static Result FromPositions(IEnumerable<GameAnalysisPosition> positions, int plyCount)
    {
        var ordered = positions.OrderBy(p => p.Ply).ToList();
        var fens = ordered.Select(p => p.Fen).ToList();
        var plies = ordered
            .Select(p => GameEvals.PlyOf(p.Ply, p.Fen, p.GameMoveUci, p.CandidatesJson, p.Depth))
            .OfType<GameEvalPlyDto>()
            .ToList();
        var final = GameEvals.FinalOf(plies.LastOrDefault(), plyCount);
        return Compute(plies, final, fens, plyCount);
    }

    /// <summary>Wie <c>whiteToMove</c> im Client: nur ein <c>b</c> im zweiten Feld heisst Schwarz.</summary>
    private static bool WhiteToMove(string fen)
        => !(fen.Split(' ') is { Length: >= 2 } parts && parts[1] == "b");

    /// <summary>Standardabweichung der Grundgesamtheit (lila <c>Maths.standardDeviation</c>), Luecken ausgelassen.</summary>
    private static double StdDev(IReadOnlyList<double?> values)
    {
        var known = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (known.Count == 0) return 0;
        var mean = known.Average();
        return Math.Sqrt(known.Sum(v => (v - mean) * (v - mean)) / known.Count);
    }
}
