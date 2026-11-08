using RookHub.Api.DTOs.Games;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Welche Züge einer analysierten Partie „Fehler nachspielen" abfragt — der SPIEGEL von <c>collectMistakes</c>
/// (<c>features/games/mistakes.util.ts</c>) am Server (0.908.0, für die Zugvergleiche der Vereinspartien,
/// <see cref="ClubMistakeComparisons"/>). Dieselben Regeln: Grundklasse Ungenauigkeit/Fehler/grober Fehler (Etikett
/// „Miss" zählt mit — es ersetzt nur diese drei), beide Seiten, und übersprungen wird ohne Bestzug, mit Bestzug = Partiezug
/// und mit einem Bestzug, der in der Stellung nicht geht. Die Klassen kommen aus <see cref="GameMoveClasses"/>.
///
/// <para><b>Bewusst ohne Buchzüge und ohne „nicht mehr zeigen"</b>: beides hängt am BETRACHTER (Repertoire, ausgeblendete
/// Halbzüge) — die Auswahl hier ist die Obermenge, die der Client je Betrachter verkleinert.</para>
///
/// <para>Beide Seiten prüfen dieselbe kleine Partie mit LITERALEN Werten (<c>GameMistakeSelectionTests</c> ↔
/// <c>mistakes.util.spec.ts</c>, „server mirror"); wer die Regeln hier oder dort ändert, zieht die andere Seite im selben
/// Commit nach.</para>
/// </summary>
public static class GameMistakeSelection
{
    /// <summary>Ein Fehler: Halbzug, Stellung davor, gespielter und bester Zug (Standard-UCI, klein).</summary>
    public sealed record Selected(int Ply, string FenBefore, string PlayedUci, string BestUci);

    /// <summary>Zeichen der Klassen aus <see cref="GameMoveClasses.Codes"/>, deren GRUNDklasse trainiert wird: I/M/P und
    /// „Miss" (X), das nur über diesen dreien steht.</summary>
    private static readonly HashSet<char> Trained = ['I', 'M', 'P', 'X'];

    /// <summary>Aus den Positionszeilen einer Analyse (wie <see cref="GameMoveClasses.FromPositions"/>).</summary>
    public static List<Selected> FromPositions(IEnumerable<GameAnalysisPosition> positions, int plyCount)
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
        if (fens.Count == plyCount && plyCount > 0)
        {
            var end = ordered.FirstOrDefault(p => p.Ply == plyCount)?.Fen
                ?? TacticsBoard.TryParse(fens[^1])?.Apply(ucis[^1])?.ToFen();
            if (end is not null) fens.Add(end);
        }
        return Select(plies, final, fens, ucis, plyCount);
    }

    /// <summary>
    /// Die Auswahl. Eingaben wie <see cref="GameMoveClasses.Compute"/>: Bewertungen in Weiß-Sicht wie <c>GET …/evals</c>,
    /// <paramref name="fens"/> = Stellung vor jedem Halbzug (dazu möglichst die nach dem letzten), <paramref name="ucis"/> je Halbzug.
    /// </summary>
    public static List<Selected> Select(IReadOnlyList<GameEvalPlyDto> plies, GameEvalScoreDto? final, IReadOnlyList<string> fens,
        IReadOnlyList<string> ucis, int plyCount)
    {
        var classes = GameMoveClasses.Compute(plies, final, fens, ucis, plyCount);
        var rows = new Dictionary<int, GameEvalPlyDto>();
        foreach (var p in plies) rows[p.Ply] = p;
        var result = new List<Selected>();
        for (var i = 0; i < classes.Length; i++)
        {
            if (!Trained.Contains(classes[i])) continue;
            if (!rows.TryGetValue(i, out var row) || i >= fens.Count || i >= ucis.Count) continue;
            var best = (row.BestUci ?? "").ToLowerInvariant();
            var played = (ucis[i] ?? "").ToLowerInvariant();
            if (best.Length == 0 || played.Length == 0 || best == played) continue;
            if (MoveComparisonService.SanOf(fens[i], best) is null) continue;   // unspielbare „Lösung" — wie im Client
            result.Add(new Selected(i, fens[i], played, best));
        }
        return result;
    }
}
