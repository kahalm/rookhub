using RookHub.Api.Models;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Tests.Engines;

/// <summary>
/// Golden-Tabelle der Statusentscheidung des Auftrags-Workers (Zerlegung Z11): jeder Zweig von
/// <see cref="AnalysisJobRunEnd.ClassifyRunEnd"/>, <see cref="AnalysisJobRunEnd.ClassifyStall"/> und
/// <see cref="BrokerFailurePolicy.OnStatus"/> mit Zielstatus, Meldung, Wartezeit und Fehlversuchs-Zähler. Die
/// Verdrahtung im Worker halten <see cref="AnalysisJobWorkerTransitionTests"/> und <see cref="AnalysisJobWorkerBrokerTests"/> fest.
/// </summary>
public class AnalysisJobRunEndTests
{
    private const int Max = AnalysisJob.MaxFruitlessAttempts;
    private static readonly TimeSpan MinRuntime = TimeSpan.FromSeconds(60);

    private static RunFacts Depth(int reached, int target = 20, int atStart = 10, bool cancelled = false, bool live = false,
        double runtimeSec = 1, int fruitless = 0, bool gotData = true)
        => new(TargetNodes: null, TargetDepth: target, ReachedDepth: reached, DepthAtStart: atStart,
            NodeGoalReached: false, CurrentNodes: 0, EngineFinished: false, PreviousNodes: null,
            DirectEngine: true, CancelledByUs: cancelled, LiveOnEngine: live, GotData: gotData,
            RunTime: TimeSpan.FromSeconds(runtimeSec), FruitlessMinRuntime: MinRuntime, FruitlessAttempts: fruitless);

    private static RunFacts Nodes(long target, long current, bool goalReached = false, bool bestMove = false, bool direct = true,
        bool cancelled = false, bool live = false, long? previous = null, bool gotData = true, double runtimeSec = 1)
        => new(TargetNodes: target, TargetDepth: 30, ReachedDepth: 5, DepthAtStart: 0,
            NodeGoalReached: goalReached, CurrentNodes: current, EngineFinished: bestMove, PreviousNodes: previous,
            DirectEngine: direct, CancelledByUs: cancelled, LiveOnEngine: live, GotData: gotData,
            RunTime: TimeSpan.FromSeconds(runtimeSec), FruitlessMinRuntime: MinRuntime, FruitlessAttempts: 0);

    private static string Show(RunOutcome o)
        => $"{o.Kind}|{o.Error ?? "-"}|{(o.Backoff is { } b ? ((int)b.TotalSeconds).ToString() : "-")}|{o.Fruitless}";

    /// <summary>Fall → (Tatsachen, erwarteter Übergang). <see cref="RunFacts"/> ist intern, deshalb geht nur der Name
    /// durch die Theorie.</summary>
    private static readonly Dictionary<string, (RunFacts Facts, string Expected)> RunEndCases = new()
    {
        // Tiefenauftrag
        ["Tiefe erreicht"] = (Depth(reached: 20), "Done|-|-|Reset"),
        ["Tiefe erreicht trotz eigenem Abbruch"] = (Depth(reached: 22, cancelled: true), "Done|-|-|Reset"),
        ["eigener Abbruch"] = (Depth(reached: 15, cancelled: true, live: true), "Paused|-|-|Keep"),
        ["Fortschritt"] = (Depth(reached: 15, atStart: 10, live: true), "Paused|Stream vor der Zieltiefe beendet|30|Reset"),
        ["Live verdrängt"] = (Depth(reached: 10, live: true, runtimeSec: 600), "Paused|-|30|Keep"),
        ["lange ohne Fortschritt"] = (Depth(reached: 10, runtimeSec: 60), "Paused|Stream vorzeitig beendet — wird fortgesetzt|30|Keep"),
        ["Sackgasse, erster Versuch"] = (Depth(reached: 10, fruitless: 0), "Paused|Stream vor der Zieltiefe beendet|30|Increment"),
        ["Sackgasse, letzter Versuch"] = (Depth(reached: 10, fruitless: Max - 1),
            $"Failed|Engine lieferte in {Max} Läufen sofort keine Bewertung|-|Increment"),
        // Knotenauftrag
        ["Knotenziel im Stream gesehen"] = (Nodes(50_000, 50_000, goalReached: true), "Done|-|-|Reset"),
        ["Stream endet knapp unter dem Ziel"] = (Nodes(100_000, 98_000), "Done|-|-|Reset"),
        ["knapp unter dem Ziel, aber eigener Abbruch"] = (Nodes(100_000, 98_000, cancelled: true), "Paused|-|-|Keep"),
        ["knapp unter dem Ziel ohne Daten"] = (Nodes(100_000, 98_000, gotData: false), "Paused|Stream vor der Zieltiefe beendet|30|Reset"),
        ["direkte Engine beendet selbst (Smart Pruning)"] = (Nodes(50_000, 39_000, bestMove: true), "Done|-|-|Reset"),
        ["Lichess-Engine beendet selbst, erster Lauf"] = (Nodes(50_000, 39_000, bestMove: true, direct: false),
            "Paused|Stream vor der Zieltiefe beendet|30|Reset"),
        ["Lichess-Engine beendet selbst, zweiter Lauf an derselben Stelle"] =
            (Nodes(50_000, 39_000, bestMove: true, direct: false, previous: 39_367), "Done|-|-|Reset"),
        ["weit unter dem Ziel ohne bestmove"] = (Nodes(100_000, 40_000), "Paused|Stream vor der Zieltiefe beendet|30|Reset"),
        ["bestmove, aber Live auf der Engine"] = (Nodes(50_000, 39_000, bestMove: true, live: true),
            "Paused|Stream vor der Zieltiefe beendet|30|Reset"),
    };

    public static TheoryData<string> RunEndCaseNames() => new(RunEndCases.Keys);

    [Theory]
    [MemberData(nameof(RunEndCaseNames))]
    public void ClassifyRunEnd_golden(string fall)
    {
        var (facts, expected) = RunEndCases[fall];
        Assert.Equal(expected, Show(AnalysisJobRunEnd.ClassifyRunEnd(facts)));
    }

    [Fact]
    public void EndedOwnSearch_nurBeiKnotenauftragUnterDemZiel()
    {
        Assert.True(AnalysisJobRunEnd.EndedOwnSearch(Nodes(50_000, 39_000, bestMove: true)));
        Assert.False(AnalysisJobRunEnd.EndedOwnSearch(Nodes(50_000, 49_000, bestMove: true)));   // knapp darunter = Ziel
        Assert.False(AnalysisJobRunEnd.EndedOwnSearch(Nodes(50_000, 50_000, goalReached: true, bestMove: true)));
        Assert.False(AnalysisJobRunEnd.EndedOwnSearch(Depth(reached: 10)));
    }

    [Theory]
    [InlineData(false, 0, 0, "Paused|Engine lieferte keine Daten|300|Keep")]
    [InlineData(false, Max - 1, 0, "Paused|Engine lieferte keine Daten|300|Keep")]   // ohne Daten zählt nichts
    [InlineData(true, 0, 17, "Paused|Engine blieb bei Tiefe 17 stehen|60|Increment")]
    [InlineData(true, Max - 1, 17, "Failed|Engine blieb in 3 Läufen stehen|-|Increment")]
    public void ClassifyStall_golden(bool gotData, int fruitless, int depth, string expected)
        => Assert.Equal(expected, Show(AnalysisJobRunEnd.ClassifyStall(gotData, fruitless, depth)));

    [Theory]
    [InlineData(400, true, 0, "Failed|Stellung abgewiesen: kaputt|-|Keep", false)]
    [InlineData(400, true, Max - 1, "Failed|Stellung abgewiesen: kaputt|-|Keep", false)]
    [InlineData(400, false, 0, "Paused|Broker antwortete 400|120|Increment", false)]
    [InlineData(400, false, Max - 1, "Failed|Broker antwortete 400 in 3 Läufen|-|Increment", false)]
    [InlineData(503, false, 0, "Paused|Broker antwortete 503|120|Keep", true)]
    [InlineData(503, true, Max - 1, "Paused|Broker antwortete 503|120|Keep", true)]
    [InlineData(504, true, 0, "Paused|Broker antwortete 504|120|Keep", true)]
    [InlineData(500, true, 0, "Paused|Broker antwortete 500|120|Keep", false)]
    [InlineData(401, false, 0, "Paused|Broker antwortete 401|120|Keep", false)]
    public void BrokerFailurePolicy_OnStatus_golden(int code, bool local, int fruitless, string expected, bool tryOther)
    {
        var d = BrokerFailurePolicy.OnStatus(code, local, fruitless, "kaputt");
        Assert.Equal(expected, Show(d.Outcome));
        Assert.Equal(tryOther, d.TryOtherEngine);
    }

    [Fact]
    public void BrokerFailurePolicy_festeUebergaenge()
    {
        Assert.Equal("Paused|Lichess nicht erreichbar|60|Keep", Show(BrokerFailurePolicy.LichessUnreachable));
        Assert.Equal("Paused|Broker nicht erreichbar|60|Keep", Show(BrokerFailurePolicy.Unreachable));
        Assert.Equal("Paused|Broker antwortete nicht|300|Keep", Show(BrokerFailurePolicy.NoHeaders));
        Assert.Equal("Paused|Broker antwortete 503 — andere Engine|15|Keep",
            Show(BrokerFailurePolicy.SwitchedEngine(503, TimeSpan.FromSeconds(15))));
    }

    [Fact]
    public void MaxFruitlessAttempts_istDreiWieInDerTabelle() => Assert.Equal(3, Max);
}
