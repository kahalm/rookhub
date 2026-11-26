using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services.Tournaments.Live;

namespace RookHub.Api.Tests.Tournaments.Live;

/// <summary>
/// „Nur ein Zug“ (1.033.0, Wunsch 2026-11-26: „markieren, wenn es nur einen Zug gibt, der gut ist, und alle anderen z. B. 1 Punkt
/// schlechter“; Präzisierung: „Wenn der 2.-beste Zug noch +3 ist, dann ist alles wurst.“). Werte immer aus Weiß-Sicht wie von
/// Broker und Cloud — bei Schwarz am Zug muss die Regel selbst drehen.
/// </summary>
public class LiveOnlyMoveTests
{
    private static readonly LiveOnlyMoveOptions O = new();

    private static LiveEval Ev(int? cp, int? mate, int? secondCp, int? secondMate, int depth = 30, string source = "engine") =>
        new(cp, mate, source, depth, "e2e4", secondCp, secondMate);

    /// <summary>Werte aus Sicht des Ziehenden → für Schwarz am Zug in Weiß-Sicht umgedreht.</summary>
    private static bool Rule(bool whiteToMove, int bestForMover, int secondForMover) =>
        whiteToMove
            ? LiveOnlyMove.Applies(Ev(bestForMover, null, secondForMover, null), true, O)
            : LiveOnlyMove.Applies(Ev(-bestForMover, null, -secondForMover, null), false, O);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UsersCases_SecondStillPlus3_IsIrrelevant_Plus2VsPlus08_Marked(bool whiteToMove)
    {
        Assert.False(Rule(whiteToMove, 450, 310));   // „+4,5 / +3,1“: zweitbester noch ≥ +3 → egal
        Assert.True(Rule(whiteToMove, 200, 80));     // „+2,0 / +0,8“: 1,2 Bauern Abstand → nur ein Zug
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Gap_ExactlyOnePawn_Marked_JustBelow_NotMarked(bool whiteToMove)
    {
        Assert.True(Rule(whiteToMove, 30, -70));
        Assert.False(Rule(whiteToMove, 30, -69));
        Assert.True(Rule(whiteToMove, -380, -490));  // Rettungszug in schlechter Stellung (bester > −5)
        Assert.False(Rule(whiteToMove, -500, -900)); // bester schon ≤ −5 → ohnehin verloren
        Assert.True(Rule(whiteToMove, 299, 150 - 300)); // zweitbester −1,5 < +3 → zählt
    }

    [Fact]
    public void BlackToMove_SignFromWhitePov()
    {
        // Schwarz am Zug, Weiß-Sicht: bester −1,5 (Schwarz +1,5), zweiter 0 → Schwarz verliert 1,5 → markiert.
        Assert.True(LiveOnlyMove.Applies(Ev(-150, null, 0, null), false, O));
        // Dieselben Zahlen mit Weiß am Zug wären Unsinn-Reihenfolge (zweiter besser): die Regel ordnet selbst, Abstand 1,5 → markiert.
        Assert.True(LiveOnlyMove.Applies(Ev(-150, null, 0, null), true, O));
        // Schwarz am Zug, Weiß-Sicht −400 / −320: für Schwarz +4 / +3,2 → zweitbester ≥ +3 → egal.
        Assert.False(LiveOnlyMove.Applies(Ev(-400, null, -320, null), false, O));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Mates(bool whiteToMove)
    {
        var s = whiteToMove ? 1 : -1;
        Assert.True(LiveOnlyMove.Applies(Ev(null, s * 3, s * 50, null), whiteToMove, O));       // Matt in 3, sonst +0,5 → nur ein Zug
        Assert.False(LiveOnlyMove.Applies(Ev(null, s * 3, s * 320, null), whiteToMove, O));     // sonst noch +3,2 → egal
        Assert.False(LiveOnlyMove.Applies(Ev(null, s * 2, null, s * 9), whiteToMove, O));       // zweiter setzt auch matt → egal
        Assert.True(LiveOnlyMove.Applies(Ev(s * 20, null, null, -s * 4), whiteToMove, O));      // nur ein Zug verhindert das Matt
        Assert.False(LiveOnlyMove.Applies(Ev(null, -s * 5, null, -s * 2), whiteToMove, O));     // beide Male matt → verloren
    }

    [Fact]
    public void Unknown_NoSecondLine_Shallow_Broadcast_NoMarker()
    {
        Assert.False(LiveOnlyMove.Applies(Ev(200, null, null, null), true, O));                  // nur eine Linie
        Assert.False(LiveOnlyMove.Applies(Ev(200, null, 0, null, depth: 19), true, O));          // unter MinShowDepth 20
        Assert.True(LiveOnlyMove.Applies(Ev(200, null, 0, null, depth: 20), true, O));
        Assert.False(LiveOnlyMove.Applies(Ev(200, null, 0, null, source: "broadcast"), true, O));
        Assert.Null(LiveOnlyMove.Mark(Ev(200, null, 150, null) with { OnlyMove = true }, true, O).OnlyMove);
        Assert.True(LiveOnlyMove.Mark(Ev(200, null, 50, null), true, O).OnlyMove);
    }

    [Fact]
    public void Options_FromConfig_DefaultsAndOverrides()
    {
        var empty = LiveOnlyMoveOptions.From(new ConfigurationBuilder().Build(), new LiveEngineOptions());
        Assert.Equal((1.0, 3.0, 5.0, 20), (empty.GapPawns, empty.IrrelevantPawns, empty.LostPawns, empty.MinDepth));
        var c = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LiveBoards:OnlyMoveGapPawns"] = "1.5", ["LiveBoards:OnlyMoveIrrelevantPawns"] = "2", ["LiveBoards:OnlyMoveLostPawns"] = "4",
        }).Build();
        var o = LiveOnlyMoveOptions.From(c, new LiveEngineOptions { MinShowDepth = 18 });
        Assert.Equal((1.5, 2.0, 4.0, 18), (o.GapPawns, o.IrrelevantPawns, o.LostPawns, o.MinDepth));
        Assert.False(LiveOnlyMove.Applies(Ev(200, null, 80, null), true, o));                    // 1,2 < 1,5
    }

    [Fact]
    public void BrokerLine_MultiPv2_SecondLine_WhitePov_NotFlipped()
    {
        var ev = BrokerLiveEngineEvaluator.ParseLine(
            """{"depth":24,"pvs":[{"moves":["c7c5"],"cp":-35,"depth":24},{"moves":["e7e5"],"mate":4,"depth":24}]}""", whiteToMove: false)!;
        Assert.Equal((-35, (int?)null, (int?)null, (int?)4, 24), (ev.Cp, ev.Mate, ev.SecondCp, ev.SecondMate, ev.Depth));
        var one = BrokerLiveEngineEvaluator.ParseLine("""{"depth":24,"pvs":[{"moves":["c7c5"],"cp":-35,"depth":24}]}""", false)!;
        Assert.False(one.HasSecond);
        var nullSlot = BrokerLiveEngineEvaluator.ParseLine("""{"depth":24,"pvs":[{"moves":["c7c5"],"cp":-35,"depth":24},null]}""", false)!;
        Assert.False(nullSlot.HasSecond);
    }

    [Fact]
    public void CloudParse_MultiPv2_SecondLine_SinglePv_Unknown()
    {
        var ev = LichessCloudEval.Parse("""{"depth":60,"pvs":[{"moves":"e7e5 g1f3","cp":22},{"moves":"c7c6 d2d4","cp":27}]}""")!;
        Assert.Equal((22, (int?)27, (int?)null, "e7e5"), (ev.Cp, ev.SecondCp, ev.SecondMate, ev.Best));
        var mate = LichessCloudEval.Parse("""{"depth":40,"pvs":[{"moves":"d1h5","mate":2},{"moves":"d2d4","mate":-7}]}""")!;
        Assert.Equal((2, (int?)-7), (mate.Mate, mate.SecondMate));
        Assert.False(LichessCloudEval.Parse("""{"depth":40,"pvs":[{"moves":"d2d4","cp":20}]}""")!.HasSecond);
    }

    [Fact]
    public void Json_KeepsPayloadSmall_OmitsUnknownFields()
    {
        var json = JsonSerializer.Serialize(new LiveEval(20, null, "cloud", 30, "d2d4"), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("second", json);
        Assert.DoesNotContain("onlyMove", json);
        Assert.DoesNotContain("hasSecond", json);
        var marked = JsonSerializer.Serialize(new LiveEval(20, null, "cloud", 30, "d2d4", -120, null, true), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"secondCp\":-120", marked);
        Assert.Contains("\"onlyMove\":true", marked);
        Assert.DoesNotContain("secondMate", marked);
    }

    [Fact]
    public async Task Hub_CloudTwoLines_BlackToMove_MarksOnlyMove()
    {
        const string round = "R9900077";
        var src = new FakeLichessLiveSource();
        var game = LivePgn.Game("g1", "A", "B", "1. e4");                   // Schwarz am Zug
        src.RoundPgn[round] = game;
        // Weiß-Sicht: bester −1,5 (für Schwarz +1,5), zweitbester 0 → Schwarz verliert mit allem anderen 1,5.
        src.CloudEvals[LiveGameParser.Parse(game)!.Fen] = """{"depth":40,"pvs":[{"moves":"c7c5","cp":-150},{"moves":"e7e5","cp":0}]}""";
        var cloud = new LichessCloudEval(src, new MemoryCache(new MemoryCacheOptions()), NullLogger<LichessCloudEval>.Instance) { Spacing = TimeSpan.Zero };
        await using var hub = new BroadcastLiveHub(src, cloud, NullLogger<BroadcastLiveHub>.Instance);
        var sub = hub.Subscribe(round, false, wantsEval: true);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        LiveEval? got = null;
        while (got is null)
        {
            var e = await sub.Events.ReadAsync(cts.Token);
            if (e is LiveGameChanged { Game.Eval: { } ev }) got = ev;
        }
        Assert.True(got.OnlyMove);
        Assert.Equal((-150, (int?)0), (got.Cp, got.SecondCp));
        await sub.DisposeAsync();
    }
}
