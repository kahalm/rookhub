using Chess;
using static RookHub.Api.Services.Repertoires.RepertoireLineSource;

namespace RookHub.Api.Services.Repertoires;

/// <summary>
/// „Kommt diese Stellung in meinem Repertoire vor — und welcher Zug steht dort?“ für MEHRERE Stellungen in einem Lauf
/// (Trainer-Notizbuch, Eröffnungsempfehlungen). Verglichen wird nur das Figurenfeld: die Stellungen der Trainerstunden
/// raten Zugrecht und Rochade teils (<c>TrainingBoardKey</c>); das Zugrecht des Treffers geht mit (<see cref="Hit.Side"/>).
///
/// Nur EIGENE Repertoires (geteilte nicht — die Frage ist „mein Repertoire“). Je Stellung und Repertoire EIN Treffer: die
/// erste Linie (für den Link) und alle Fortsetzungen aus allen Linien (Hauptzug + abzweigende Varianten, ohne Doppelte).
/// Ein Durchlauf über alle Linien wie der Baummodus von <see cref="RepertoirePositionLookupService"/>, gedeckelt mit
/// <see cref="Budget"/>; gecacht ist nur das Parsen (<see cref="RepertoireLineSource"/>).
/// </summary>
public class RepertoireBoardContinuations
{
    private readonly RepertoireLineSource _lines;

    public RepertoireBoardContinuations(RepertoireLineSource lines) => _lines = lines;

    /// <summary>Zeitbudget je Anfrage über alle Linien; danach der bisherige Stand mit <c>Truncated</c>.</summary>
    public TimeSpan Budget { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>Ein Treffer: Repertoire, erste Linie (Kapitel = <c>[Black]</c>, Name = <c>[White]</c>, <c>GameIndex</c>
    /// wie bei der Stellungssuche, <c>Ply</c> −1 = nur in einer Variante), Zugrecht der Repertoire-Stellung und die
    /// Züge (SAN, leer = die Linie endet hier).</summary>
    public sealed record Hit(int RepertoireId, string RepertoireName, string Chapter, string LineName, int GameIndex,
        int Ply, char Side, List<string> Moves);

    /// <summary><c>HasOwn</c> = das Konto hat überhaupt eigene Linien.</summary>
    public sealed record Result(Dictionary<string, List<Hit>> ByBoard, bool HasOwn, bool Truncated);

    /// <param name="boards">Figurenfelder (erstes FEN-Feld).</param>
    public async Task<Result> FindAsync(int userId, IEnumerable<string> boards, CancellationToken ct)
    {
        var targets = new HashSet<string>(boards.Where(b => !string.IsNullOrWhiteSpace(b)), StringComparer.Ordinal);
        var byBoard = new Dictionary<string, List<Hit>>(StringComparer.Ordinal);
        var games = (await _lines.GetGamesAsync(userId, ct)).Where(g => !g.Shared).ToList();
        if (targets.Count == 0 || games.Count == 0) return new Result(byBoard, games.Count > 0, false);

        var budget = new ScanBudget(Budget);
        foreach (var game in games)
        {
            ct.ThrowIfCancellationRequested();
            if (budget.Spent()) break;
            var board = BoardFor(game.StartFen);
            if (board == null) continue;   // unbrauchbare [FEN] → Linie überspringen
            var walk = new Walk(game, targets, byBoard, budget);
            try
            {
                walk.Check(board, game.Moves, 0, 0);
                walk.Run(board, game.Moves, 0, mainline: true);
            }
            catch { /* eine kaputte Linie darf den Abgleich nicht kippen */ }
        }
        return new Result(byBoard, true, budget.Exhausted);
    }

    private sealed class Walk(RepGame game, HashSet<string> targets, Dictionary<string, List<Hit>> byBoard, ScanBudget budget)
    {
        /// <summary>Spielt die Zugliste samt Varianten nach; geprüft wird jede Stellung NACH einem Zug (die Anfangsstellung
        /// einer Variante ist die ihres Elternzugs und wurde dort schon geprüft).</summary>
        public void Run(ChessBoard board, List<PgnMove> moves, int ply, bool mainline)
        {
            var made = 0;
            for (var i = 0; i < moves.Count; i++)
            {
                if (budget.Spent()) break;
                foreach (var variation in moves[i].Variations)
                    Run(board, variation, ply, mainline: false);
                bool ok;
                try { ok = board.Move(moves[i].San); }
                catch { ok = false; }
                if (!ok) break;
                made++;
                ply++;
                Check(board, moves, i + 1, mainline ? ply : -1);
            }
            for (var i = 0; i < made; i++) board.Cancel();
        }

        /// <summary>Steht das Brett auf einer gesuchten Stellung, die Fortsetzungen ab <paramref name="index"/> merken.</summary>
        public void Check(ChessBoard board, List<PgnMove> moves, int index, int ply)
        {
            var fen = board.ToFen();
            var space = fen.IndexOf(' ');
            var key = space < 0 ? fen : fen[..space];
            if (!targets.Contains(key)) return;
            var side = space >= 0 && space + 1 < fen.Length ? fen[space + 1] : 'w';
            var list = byBoard.TryGetValue(key, out var l) ? l : (byBoard[key] = new List<Hit>());
            var hit = list.FirstOrDefault(h => h.RepertoireId == game.RepertoireId && h.Side == side);
            if (hit == null)
            {
                hit = new Hit(game.RepertoireId, game.RepertoireName, game.Chapter, game.LineName, game.GameIndex, ply, side, new());
                list.Add(hit);
            }
            if (index >= moves.Count) return;   // die Linie endet hier
            Add(hit.Moves, moves[index].San);
            foreach (var variation in moves[index].Variations)
                if (variation.Count > 0) Add(hit.Moves, variation[0].San);
        }

        private static void Add(List<string> into, string san)
        {
            if (!string.IsNullOrWhiteSpace(san) && !into.Contains(san)) into.Add(san);
        }
    }
}
