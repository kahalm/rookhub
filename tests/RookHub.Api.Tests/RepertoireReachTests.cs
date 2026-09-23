using Chess;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Die Rechnung hinter Lochfinder und „Häufigste zuerst" — ohne DB und HTTP, die
/// Explorer-Daten stehen als feste Zahlen im Test.</summary>
public class RepertoireReachTests
{
    private static string KeyAfter(params string[] sans)
    {
        var board = new ChessBoard();
        foreach (var san in sans) Assert.True(board.Move(san), san);
        return RepertoireReach.Key(board.ToFen());
    }

    /// <summary>Explorer-Daten; Gesamtzahl = Summe der Züge, wenn nicht angegeben.</summary>
    private static ExplorerPositionStats Stats(params (string San, long Games)[] moves) =>
        new(moves.Sum(m => m.Games), moves.Select(m => new ExplorerMoveStat("", m.San, m.Games, null, null)).ToList());

    private static RepertoireReach.Graph Graph(char color, params string[] pgns) =>
        RepertoireReach.Build(pgns.SelectMany(PgnMoveTree.ParseSections).Where(s => s.Moves.Count > 0), color);

    /// <summary>Delegat über eine Stellungs-Tabelle; merkt sich jede Anfrage.</summary>
    private sealed class Explorer
    {
        public readonly Dictionary<string, ExplorerPositionStats> Data = new();
        public readonly List<string> Asked = new();

        public Task<ExplorerPositionStats?> Get(RepertoireReach.Node node)
        {
            Asked.Add(node.Key);
            return Task.FromResult(Data.TryGetValue(node.Key, out var s) ? s : null);
        }
    }

    [Fact]
    public async Task BlackRepertoire_FindsFrequentUncoveredOpponentMoves_WithTheirFrequency()
    {
        var g = Graph('b', "[Event \"x\"]\n\n1. e4 c5 2. Nf3 d6 *");
        var ex = new Explorer();
        ex.Data[KeyAfter()] = Stats(("e4", 500), ("d4", 400), ("c4", 50), ("Nf3", 45), ("b3", 5));
        ex.Data[KeyAfter("e4", "c5")] = Stats(("Nf3", 600), ("Nc3", 300), ("c3", 100));

        var r = await RepertoireReach.EvaluateAsync(g, ex.Get, threshold: 0.01);

        var bySan = r.Holes.ToDictionary(h => h.Move.San);
        Assert.Equal(new[] { "d4", "c4", "Nf3", "Nc3", "c3" }.OrderBy(x => x), bySan.Keys.OrderBy(x => x));
        Assert.Equal(0.4, bySan["d4"].Frequency, 6);
        Assert.Equal(0.5 * 0.3, bySan["Nc3"].Frequency, 6);    // 1.e4 (50 %) × 2.Nc3 (30 %)
        Assert.Equal(1000, bySan["Nc3"].PositionGames);
        Assert.DoesNotContain("b3", bySan.Keys);                // 0,5 % liegt unter der Schwelle
        Assert.Equal(2, r.Analyzed);
        Assert.Equal(0, r.Pending);

        var (start, path) = RepertoireReach.PathTo(bySan["Nc3"].Position);
        Assert.Null(start);
        Assert.Equal(new[] { "e4", "c5" }, path);
    }

    [Fact]
    public async Task Threshold_DecidesWhatCountsAsHole()
    {
        var g = Graph('b', "[Event \"x\"]\n\n1. e4 e5 *");
        var ex = new Explorer();
        ex.Data[KeyAfter()] = Stats(("e4", 900), ("d4", 80), ("b3", 20));

        var strict = await RepertoireReach.EvaluateAsync(Graph('b', "[Event \"x\"]\n\n1. e4 e5 *"), ex.Get, threshold: 0.05);
        Assert.Equal(new[] { "d4" }, strict.Holes.Select(h => h.Move.San));

        var loose = await RepertoireReach.EvaluateAsync(g, ex.Get, threshold: 0.01);
        Assert.Equal(new[] { "b3", "d4" }, loose.Holes.Select(h => h.Move.San).OrderBy(s => s));
    }

    [Fact]
    public async Task Transposition_IsNoHole_AndBothPathsAddUp()
    {
        // 1.c4 Nf6 2.d4 landet in derselben Stellung wie 1.d4 Nf6 2.c4 — das ist abgedeckt.
        var g = Graph('b',
            "[Event \"a\"]\n\n1. d4 Nf6 2. c4 e6 *",
            "[Event \"b\"]\n\n1. c4 Nf6 2. Nc3 e6 *");
        var ex = new Explorer();
        ex.Data[KeyAfter()] = Stats(("d4", 50), ("c4", 50));
        ex.Data[KeyAfter("d4", "Nf6")] = Stats(("c4", 100));
        ex.Data[KeyAfter("c4", "Nf6")] = Stats(("Nc3", 50), ("d4", 50));

        var r = await RepertoireReach.EvaluateAsync(g, ex.Get, threshold: 0.01);

        Assert.Empty(r.Holes);
        // Linie a: 0,5 über 1.d4 + 0,25 über 1.c4 Nf6 2.d4.
        Assert.Equal(0.75, r.LineFrequencies[KeyAfter("d4", "Nf6", "c4", "e6")], 6);
        Assert.Equal(0.25, r.LineFrequencies[KeyAfter("c4", "Nf6", "Nc3", "e6")], 6);
    }

    [Fact]
    public async Task LineEnd_IsNotAHole_AndIsNeverQueried()
    {
        // Die Linie endet mit dem eigenen Zug 2.Nf3 — was Schwarz dann spielt, ist nicht vorbereitet,
        // aber auch kein Loch: das Repertoire hört dort bewusst auf.
        var g = Graph('w', "[Event \"x\"]\n\n1. e4 e5 2. Nf3 *");
        var ex = new Explorer();
        ex.Data[KeyAfter("e4")] = Stats(("e5", 60), ("c5", 40));

        var r = await RepertoireReach.EvaluateAsync(g, ex.Get, threshold: 0.01);

        Assert.DoesNotContain(KeyAfter("e4", "e5", "Nf3"), ex.Asked);
        Assert.Equal(new[] { "c5" }, r.Holes.Select(h => h.Move.San));
        Assert.Equal(0.4, r.Holes[0].Frequency, 6);   // Weiß spielt immer 1.e4 → Stellung hat P = 1
    }

    [Fact]
    public async Task OwnAlternatives_SplitEvenly()
    {
        var g = Graph('w', "[Event \"x\"]\n\n1. e4 (1. d4 d5) e5 *");
        var ex = new Explorer();
        ex.Data[KeyAfter("e4")] = Stats(("e5", 100));
        ex.Data[KeyAfter("d4")] = Stats(("d5", 50), ("Nf6", 50));

        var r = await RepertoireReach.EvaluateAsync(g, ex.Get, threshold: 0.01);

        var hole = Assert.Single(r.Holes);
        Assert.Equal("Nf6", hole.Move.San);
        Assert.Equal(0.25, hole.Frequency, 6);   // 1.d4 ist eine von zwei eigenen Möglichkeiten
    }

    [Fact]
    public async Task MissingData_CountsAsPending_AndLinesKeepDeepestKnownFrequency()
    {
        var g = Graph('b', "[Event \"x\"]\n\n1. e4 c5 2. Nf3 d6 3. d4 cxd4 *");
        var ex = new Explorer();
        ex.Data[KeyAfter()] = Stats(("e4", 80), ("d4", 20));
        // Nach 1.e4 c5 liegen (noch) keine Daten vor.

        var r = await RepertoireReach.EvaluateAsync(g, ex.Get, threshold: 0.01);

        Assert.Equal(1, r.Analyzed);
        Assert.Equal(1, r.Pending);
        Assert.DoesNotContain(KeyAfter("e4", "c5", "Nf3", "d6"), ex.Asked);   // darunter unbekannt
        Assert.Equal(0.8, r.LineFrequencies[KeyAfter("e4", "c5", "Nf3", "d6", "d4", "cxd4")], 6);
    }

    [Fact]
    public async Task RarePositions_AreNotQueried()
    {
        var g = Graph('b', "[Event \"x\"]\n\n1. e4 c5 2. b4 cxb4 3. a3 *");
        var ex = new Explorer();
        ex.Data[KeyAfter()] = Stats(("e4", 10_000));
        ex.Data[KeyAfter("e4", "c5")] = Stats(("Nf3", 9_999), ("b4", 1));   // P(2.b4) = 0,0001 < MinReach

        await RepertoireReach.EvaluateAsync(g, ex.Get, threshold: 0.01);

        Assert.DoesNotContain(KeyAfter("e4", "c5", "b4", "cxb4"), ex.Asked);
    }

    [Fact]
    public async Task FewGames_NoHoles()
    {
        var g = Graph('b', "[Event \"x\"]\n\n1. e4 e5 *");
        var ex = new Explorer();
        ex.Data[KeyAfter()] = Stats(("e4", 5), ("d4", 4));   // 9 Partien < MinGames

        var r = await RepertoireReach.EvaluateAsync(g, ex.Get, threshold: 0.01);

        Assert.Empty(r.Holes);
        Assert.Equal(1, r.Analyzed);
    }

    [Fact]
    public async Task StartFenLines_AreRoots_AndReportTheirStartPosition()
    {
        const string fen = "rnbqkb1r/pppppppp/5n2/8/3P4/8/PPP1PPPP/RNBQKBNR w KQkq - 1 2";
        var g = Graph('b', $"[Event \"x\"]\n[FEN \"{fen}\"]\n\n2. c4 e6 *");
        var ex = new Explorer();
        ex.Data[RepertoireReach.Key(fen)] = Stats(("c4", 70), ("Nf3", 30));

        var r = await RepertoireReach.EvaluateAsync(g, ex.Get, threshold: 0.01);

        var hole = Assert.Single(r.Holes);
        Assert.Equal("Nf3", hole.Move.San);
        var (start, path) = RepertoireReach.PathTo(hole.Position);
        Assert.Equal(RepertoireReach.Key(fen), RepertoireReach.Key(start!));
        Assert.Empty(path);
    }

    [Fact]
    public async Task PrefetchLayer_GetsEachLayersQueries_BeforeTheyAreAsked()
    {
        var g = Graph('b', "[Event \"x\"]\n\n1. e4 c5 2. Nf3 d6 *", "[Event \"y\"]\n\n1. d4 d5 2. c4 e6 *");
        var ex = new Explorer();
        ex.Data[KeyAfter()] = Stats(("e4", 50), ("d4", 50));
        ex.Data[KeyAfter("e4", "c5")] = Stats(("Nf3", 100));
        ex.Data[KeyAfter("d4", "d5")] = Stats(("c4", 100));
        var layers = new List<List<string>>();

        await RepertoireReach.EvaluateAsync(g, ex.Get, 0.01, default, nodes =>
        {
            layers.Add(nodes.Select(n => n.Key).ToList());
            return Task.CompletedTask;
        });

        Assert.Equal(2, layers.Count);
        Assert.Equal(new[] { KeyAfter() }, layers[0]);
        Assert.Equal(new[] { KeyAfter("d4", "d5"), KeyAfter("e4", "c5") }.OrderBy(k => k), layers[1].OrderBy(k => k));
    }

    [Fact]
    public async Task PositionFrequencies_CoverEveryKnownPosition_NotOnlyLineEnds()
    {
        var g = Graph('b', "[Event \"x\"]\n\n1. e4 c5 2. Nf3 d6 *");
        var ex = new Explorer();
        ex.Data[KeyAfter()] = Stats(("e4", 50), ("d4", 50));
        ex.Data[KeyAfter("e4", "c5")] = Stats(("Nf3", 80), ("Nc3", 20));

        var r = await RepertoireReach.EvaluateAsync(g, ex.Get, 0.01);

        Assert.Equal(1.0, r.PositionFrequencies[KeyAfter()], 6);
        Assert.Equal(0.5, r.PositionFrequencies[KeyAfter("e4")], 6);
        Assert.Equal(0.5, r.PositionFrequencies[KeyAfter("e4", "c5")], 6);   // eigener Zug: bleibt 0,5
        Assert.Equal(0.4, r.PositionFrequencies[KeyAfter("e4", "c5", "Nf3")], 6);
        Assert.Equal(0.4, r.PositionFrequencies[KeyAfter("e4", "c5", "Nf3", "d6")], 6);
    }

    [Fact]
    public void Key_DropsEnPassantAndCounters_LikeTheClient()
    {
        // Spiegel von normalizeFen (position-filter.util.ts): Brett, Zugrecht, Rochade.
        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq",
            RepertoireReach.Key("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1"));
    }

    [Theory]
    [InlineData("O-O", "O-O+", true)]
    [InlineData("0-0-0", "O-O-O", true)]
    [InlineData("Nf3", "Nf3#", true)]
    [InlineData("Nf3", "Ng3", false)]
    public void SameSan_IgnoresCheckSigns(string a, string b, bool same) =>
        Assert.Equal(same, RepertoireReach.SameSan(a, b));
}
