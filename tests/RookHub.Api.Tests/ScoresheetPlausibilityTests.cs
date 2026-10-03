using Chess;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Engine-Prüfung einer gelesenen Partie (<see cref="ScoresheetPlausibility"/>). Die Engine ist hier eine ATTRAPPE, die
/// eine Stellung nach einer Regel bewertet — so lässt sich das Zickzack der Gruber-Partie (Turm auf e1 neben einem
/// schwarzen Bauern auf f2, zehn Züge lang nicht geschlagen) ohne Stockfish nachstellen.
/// </summary>
public class ScoresheetPlausibilityTests
{
    /// <summary>Eine kurze Partie, in der 10.Tc1 (Ta1–c1) und 10.Te1 (Tf1–e1) beide legal sind und alle Züge danach in
    /// beiden Fällen gehen — mit python-chess geprüft.</summary>
    private static readonly string[] SheetGerman =
    {
        "e4", "e5", "Sf3", "Sc6", "Lc4", "Lc5", "Sc3", "Sf6", "d3", "d6", "Lg5", "h6", "Lh4", "g5", "Lg3", "Lg4",
        "0-0", "Dd7", "Tc1", "0-0-0", "a3", "a6", "b4", "La7", "Sd5", "Sxd5", "Lxd5", "Thg8", "h3", "Le6", "c4", "f6",
        "Da4", "Kb8",
    };

    /// <summary>Der Halbzug 10.Tc1 (0-basiert).</summary>
    private const int RookPly = 18;

    private static readonly ScoresheetResolver.Options German = new(ScoresheetNotation.Find("de"));

    /// <summary>Das Formular, wie das Modell es las: „Tc1" als „Te1" — ein legaler Zug, den der Auflöser nimmt.</summary>
    private static List<ScannedPly> Misread()
    {
        var written = SheetGerman.ToArray();
        written[RookPly] = "Te1";
        return written.Select(w => new ScannedPly(w, null, null, "high")).ToList();
    }

    /// <summary>Feld e1 in <see cref="ScoresheetResolver.Squares"/> (a8 = 0 … h1 = 63).</summary>
    private const int E1 = 60;

    private static bool RookOnE1(string fen) => ScoresheetResolver.Squares(fen)[E1] == 'R';
    private static bool BlackToMove(string fen) => fen.Split(' ')[1] == "b";
    private static int FullMove(string fen) => int.Parse(fen.Split(' ')[5]);

    /// <summary>Wie in der Gruber-Partie: steht der Turm auf e1, könnte Schwarz am Zug ihn gewinnen (−900); tut Schwarz es
    /// nicht, ist wieder alles gleich (0). Jeder Halbzug, solange der Turm dort steht, ist ein Patzer.</summary>
    private static int Persistent(string fen) => RookOnE1(fen) && BlackToMove(fen) ? -900 : 0;

    [Fact]
    public async Task Improve_PersistentZigZag_ReplacesTheMisreadMove_AndKeepsTheOldOneAsReading()
    {
        var scanned = Misread();
        var r = ScoresheetResolver.Resolve(scanned, German);
        Assert.Equal("Re1", r.Plies[RookPly].San);

        var o = await ScoresheetPlausibility.ImproveAsync(scanned, German, r, new FakeScoresheetEngine(Persistent));

        Assert.Equal(new[] { RookPly }, o.Replaced);
        var ply = o.Resolution.Plies[RookPly];
        Assert.Equal("Rc1", ply.San);
        Assert.Equal("a1c1", ply.Uci);
        Assert.Equal("Te1", ply.Written);
        Assert.True(ply.Uncertain);
        Assert.Equal(ScoresheetPlausibility.Checks.Replaced, ply.Check);
        Assert.Equal(new[] { "Rc1", "Re1" }, ply.Options!.Take(2).Select(x => x.San));
        // Der Rest der Partie bleibt, wie er auf dem Formular steht.
        Assert.Equal(SheetGerman.Length, o.Resolution.Plies.Count);
        Assert.Empty(o.Resolution.Unresolved);
        Assert.Equal(r.Plies.Skip(RookPly + 1).Select(p => p.San), o.Resolution.Plies.Skip(RookPly + 1).Select(p => p.San));
    }

    [Fact]
    public async Task Improve_ShortZigZag_OnlySuggests_TheReadMoveStays()
    {
        // Nur EIN Paar (10.Te1?? und 10…0-0-0?? lässt es liegen) — das kommt in echten Partien vor.
        static int Pair(string fen) => RookOnE1(fen) && BlackToMove(fen) && FullMove(fen) == 10 ? -900 : 0;
        var scanned = Misread();
        var r = ScoresheetResolver.Resolve(scanned, German);

        var o = await ScoresheetPlausibility.ImproveAsync(scanned, German, r, new FakeScoresheetEngine(Pair));

        Assert.Empty(o.Replaced);
        Assert.Equal(new[] { RookPly }, o.Flagged);
        var ply = o.Resolution.Plies[RookPly];
        Assert.Equal("Re1", ply.San);
        Assert.True(ply.Uncertain);
        Assert.Equal(ScoresheetPlausibility.Checks.Suggested, ply.Check);
        Assert.Equal(new[] { "Re1", "Rc1" }, ply.Options!.Take(2).Select(x => x.San));
    }

    [Fact]
    public async Task Improve_ZigZagThatNoReadingExplains_LeavesTheReadingAlone()
    {
        // Ein echter Patzer: nach 3.Lc4 „verliert" Weiß, gleich wie man die Einträge liest — nichts zu korrigieren.
        static int RealBlunder(string fen) => BlackToMove(fen) && FullMove(fen) == 3 ? -900 : 0;
        var scanned = SheetGerman.Select(w => new ScannedPly(w, null, null, "high")).ToList();
        var r = ScoresheetResolver.Resolve(scanned, German);

        var o = await ScoresheetPlausibility.ImproveAsync(scanned, German, r, new FakeScoresheetEngine(RealBlunder));

        Assert.Empty(o.Replaced);
        Assert.Empty(o.Flagged);
        Assert.Same(r, o.Resolution);
        Assert.All(o.Resolution.Plies, p => Assert.Null(p.Check));
    }

    [Fact]
    public async Task Improve_NoZigZag_ChangesNothing()
    {
        var scanned = Misread();
        var r = ScoresheetResolver.Resolve(scanned, German);

        var o = await ScoresheetPlausibility.ImproveAsync(scanned, German, r, new FakeScoresheetEngine(_ => 0));

        Assert.Same(r, o.Resolution);
        Assert.Empty(o.Replaced);
        Assert.Empty(o.Flagged);
    }

    [Fact]
    public async Task Improve_WithoutEngine_ReturnsTheReadingUnchanged()
    {
        var scanned = Misread();
        var r = ScoresheetResolver.Resolve(scanned, German);

        var o = await ScoresheetPlausibility.ImproveAsync(scanned, German, r, new FakeScoresheetEngine(Persistent) { Missing = true });

        Assert.Same(r, o.Resolution);
        Assert.Equal("Re1", o.Resolution.Plies[RookPly].San);
    }

    [Fact]
    public async Task Improve_EngineFailsOnTheFirstLine_ReturnsTheReadingUnchanged()
    {
        var scanned = Misread();
        var r = ScoresheetResolver.Resolve(scanned, German);

        var o = await ScoresheetPlausibility.ImproveAsync(scanned, German, r, new FakeScoresheetEngine(Persistent) { FailAfter = 0 });

        Assert.Same(r, o.Resolution);
    }

    [Fact]
    public void Alternatives_OnlyReadingsOneConfusableCharacterAway()
    {
        // „Te1": Tc1 (c/e verwechselbar, 2,0) ja — Tb1 (b/e, ein beliebiges Zeichen, 2,5) nein.
        var board = ChessBoard.LoadFromFen(GamePlies.StartFen());
        foreach (var san in SheetGerman.Take(RookPly).Select(ToEnglish)) Assert.True(board.Move(san), san);
        var fen = board.ToFen();
        var entry = new ScannedPly("Te1", null, null, "high");

        var alts = ScoresheetPlausibility.Alternatives(fen, entry, German, ScoresheetResolver.Squares(fen), "f1e1", 0);

        Assert.Equal(new[] { "Rc1" }, alts.Select(a => a.San));

        // Hat das Modell selbst gezweifelt („low"), zählt auch ein beliebiges Zeichen.
        var doubted = ScoresheetPlausibility.Alternatives(fen, entry with { Confidence = "low" }, German,
            ScoresheetResolver.Squares(fen), "f1e1", 0);
        Assert.Contains("Rb1", doubted.Select(a => a.San));
    }

    [Fact]
    public void Loss_IsTheMoversDropInWinningChance()
    {
        // Weiß zieht (Halbzug 0) und fällt von 0 auf −300 cp: Gewinnchance 50 → 24,89 (Lichess-Formel).
        var evals = new[] { 0, -300, 0 };
        Assert.Equal(25.11, ScoresheetPlausibility.Loss(evals, whiteFirst: true, 0), 2);
        // Schwarz zieht (Halbzug 1) und gibt es zurück: derselbe Verlust aus SEINER Sicht.
        Assert.Equal(25.11, ScoresheetPlausibility.Loss(evals, whiteFirst: true, 1), 2);
        // Ein verpasstes Matt in entschiedener Stellung ist kein Patzer (+1500 → +900, gekappt bei 1000: 97,54 → 96,49).
        Assert.True(ScoresheetPlausibility.Loss(new[] { 1500, 900 }, true, 0) < ScoresheetPlausibility.SwingPct);
    }

    [Fact]
    public void FindZigZag_AndBlunderRun()
    {
        // Stellungen 0..6; Weiß patzt mit Halbzug 2, Schwarz gibt es mit 3 zurück, und so weiter bis Halbzug 5.
        var evals = new[] { 0, 0, 0, -600, 0, -600, 0 };
        Assert.Equal(2, ScoresheetPlausibility.FindZigZag(evals, true, 0));
        Assert.Equal(4, ScoresheetPlausibility.BlunderRun(evals, true, 2));
        Assert.Equal(-1, ScoresheetPlausibility.FindZigZag(evals, true, 5));
        var (zig, _) = ScoresheetPlausibility.Badness(evals, true, 0, evals.Length);
        Assert.Equal(3, zig);
    }

    [Theory]
    [InlineData("info depth 10 seldepth 14 multipv 1 score cp 34 nodes 1000 pv e2e4", 34)]
    [InlineData("info depth 10 score cp -120 upperbound nodes 5", -120)]
    [InlineData("info depth 5 score mate 3 nodes 9 pv d1h5", ScoresheetPlausibility.MateCp)]
    [InlineData("info depth 5 score mate -2 pv a1a2", -ScoresheetPlausibility.MateCp)]
    public void ParseScore_ReadsCentipawnsAndMate(string line, int expected)
        => Assert.Equal(expected, StockfishScoresheetEngine.ParseScore(line));

    [Theory]
    [InlineData("info string NNUE evaluation using nn.nnue")]
    [InlineData("bestmove e2e4 ponder e7e5")]
    public void ParseScore_IgnoresLinesWithoutScore(string line)
        => Assert.Null(StockfishScoresheetEngine.ParseScore(line));

    private static string ToEnglish(string german) => german switch
    {
        "0-0" => "O-O",
        "0-0-0" => "O-O-O",
        _ => string.Concat(german.Select(c => c switch { 'S' => 'N', 'L' => 'B', 'T' => 'R', 'D' => 'Q', _ => c })),
    };
}

/// <summary>Eine Engine-Attrappe: bewertet jede Stellung (WEISS-Sicht, Centibauern) nach einer Regel über die FEN.</summary>
internal sealed class FakeScoresheetEngine(Func<string, int> evaluate) : IScoresheetEngine
{
    private readonly Func<string, int> _evaluate = evaluate;

    /// <summary>Keine Engine da (<see cref="OpenAsync"/> liefert <c>null</c>).</summary>
    public bool Missing { get; init; }

    /// <summary>Nach so vielen Zugfolgen versagt die Engine (<c>null</c>); −1 = nie.</summary>
    public int FailAfter { get; init; } = -1;

    public int Lines { get; private set; }

    public Task<IScoresheetEngineSession?> OpenAsync(CancellationToken ct = default)
        => Task.FromResult<IScoresheetEngineSession?>(Missing ? null : new Session(this));

    private sealed class Session(FakeScoresheetEngine owner) : IScoresheetEngineSession
    {
        public Task<List<int>?> EvaluateLineAsync(string startFen, IReadOnlyList<string> uciMoves, CancellationToken ct = default)
        {
            if (owner.FailAfter >= 0 && owner.Lines >= owner.FailAfter) return Task.FromResult<List<int>?>(null);
            owner.Lines++;
            var board = ChessBoard.LoadFromFen(startFen);
            var result = new List<int> { owner._evaluate(board.ToFen()) };
            foreach (var uci in uciMoves)
            {
                var move = board.Moves(generateSan: true).First(m => GamePlies.ToUci(m) == uci);
                board.Move(move);
                result.Add(owner._evaluate(board.ToFen()));
            }
            return Task.FromResult<List<int>?>(result);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
