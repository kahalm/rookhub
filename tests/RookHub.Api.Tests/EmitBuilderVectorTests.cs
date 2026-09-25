using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Tests;

/// <summary>
/// VEKTOR-Tests des emit.rs-Ports. Die erwarteten Zeilen stammen aus den ORIGINALEN Quelltexten von
/// lila-engine (<c>emit.rs</c>, <c>uci.rs</c>, Commit 60ea115c, shakmaty 0.30.1 laut deren Cargo.lock), im
/// Rust-Container ausgefuehrt — Eingaben in <c>tools/emit-reference/cases.json</c>, erzeugt mit
/// <c>bash tools/emit-reference/run.sh</c>. Sie stehen hier LITERAL, damit ein Fehler im Port nicht mit in
/// die Erwartung wandert.
///
/// <para>Harness-Semantik je Eingabezeile: <c>info</c> → der gesendete Emit oder <c>null</c> (kein
/// vollstaendiger Satz), andere Zeile → <c>IGNORED</c>, Steuerzeile → <c>CONTROL:…</c>, Parserfehler →
/// <c>ERR</c> (lila-engine bricht hier mit 400 ab, der Broker ueberspringt die Zeile — der Zustand bleibt
/// in beiden Faellen unberuehrt), <c>bestmove</c> → Abschluss-Emit; ohne <c>bestmove</c> am Ende
/// <c>EOF:</c> + Abschluss-Emit.</para>
///
/// <para>EINE bewusste Abweichung, in den Erwartungen nachgezogen: bei <c>bestmove (none)</c> (und am
/// Stream-Ende ohne bestmove) schreibt lila-engine <c>"bestmove":"(none)"</c>, der Broker laesst das Feld
/// weg (Plan 3.5). Kein Abnehmer liest es.</para>
/// </summary>
public class EmitBuilderVectorTests
{
    private static (IReadOnlyList<string> Moves, List<string?> Results) Run(string fen, string[] moves, string[] lines)
    {
        var sanitized = WorkSanitizer.Sanitize(new EngineWork("s", 1, 16, 1, fen, moves, Depth: 1), 1, 16);
        var emit = new EmitBuilder(sanitized.RootFen);
        var results = new List<string?>();
        var finished = false;
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith('{')) { results.Add("CONTROL:" + line); continue; }
            var parsed = UciLineParser.Parse(line);
            switch (parsed.Kind)
            {
                case UciLineKind.BestMove:
                    emit.Finish(parsed.BestMove!.Move, parsed.BestMove.Ponder);
                    results.Add(emit.ToJson());
                    finished = true;
                    break;
                case UciLineKind.Info:
                    emit.Update(parsed.Info!);
                    results.Add(emit.ShouldEmit ? emit.ToJson() : null);
                    break;
                case UciLineKind.Ignored:
                    results.Add("IGNORED");
                    break;
                default:
                    results.Add("ERR");
                    break;
            }
            if (finished) break;
        }
        if (!finished)
        {
            emit.Finish(null, null);
            results.Add("EOF:" + emit.ToJson());
        }
        return (sanitized.Work.Moves, results);
    }

    [Fact]
    public void SinglePvWhite()
    {
        var (moves, results) = Run(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            [],
            [
                "info depth 1 seldepth 1 multipv 1 score cp 18 nodes 20 nps 20000 hashfull 0 tbhits 0 time 1 pv e2e4",
                "info depth 2 seldepth 2 multipv 1 score cp 30 nodes 60 nps 30000 time 2 pv e2e4 e7e5",
                "info depth 3 seldepth 3 score cp 25 lowerbound nodes 90 time 3 pv d2d4",
                "info depth 3 seldepth 4 score cp 22 upperbound nodes 95 time 3 pv d2d4",
                "info depth 3 seldepth 4 multipv 1 score cp 21 nodes 120 time 4 pv d2d4 d7d5 c2c4",
                "bestmove d2d4 ponder d7d5",
            ]);
        Assert.Equal([], moves);
        Assert.Equal(new string?[]
        {
            "{\"time\":1,\"depth\":1,\"nodes\":20,\"pvs\":[{\"moves\":[\"e2e4\"],\"cp\":18,\"depth\":1}]}",
            "{\"time\":2,\"depth\":2,\"nodes\":60,\"pvs\":[{\"moves\":[\"e2e4\",\"e7e5\"],\"cp\":30,\"depth\":2}]}",
            null,
            null,
            "{\"time\":4,\"depth\":3,\"nodes\":120,\"pvs\":[{\"moves\":[\"d2d4\",\"d7d5\",\"c2c4\"],\"cp\":21,\"depth\":3}]}",
            "{\"time\":4,\"depth\":3,\"nodes\":120,\"pvs\":[{\"moves\":[\"d2d4\",\"d7d5\",\"c2c4\"],\"cp\":21,\"depth\":3}],\"bestmove\":\"d2d4\",\"ponder\":\"d7d5\"}",
        }, results);
    }

    [Fact]
    public void MultipvSets()
    {
        var (moves, results) = Run(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            ["e2e4"],
            [
                "info depth 1 seldepth 1 multipv 1 score cp -20 nodes 20 time 1 pv e7e5",
                "info depth 1 seldepth 1 multipv 2 score cp -25 nodes 40 time 1 pv c7c5",
                "info depth 1 seldepth 1 multipv 3 score cp -30 lowerbound nodes 60 time 2 pv e7e6",
                "info depth 2 seldepth 2 multipv 1 score cp -22 nodes 100 time 3 pv e7e5 g1f3",
                "info depth 2 seldepth 2 multipv 2 score cp -28 nodes 140 time 3 pv c7c5",
                "info depth 1 seldepth 2 multipv 3 score mate -3 nodes 150 time 4 pv e7e6 d2d4",
                "info depth 3 multipv 1 score cp -15 nodes 200 time 5 pv e7e5",
                "bestmove e7e5",
            ]);
        Assert.Equal(["e2e4"], moves);
        Assert.Equal(new string?[]
        {
            "{\"time\":1,\"depth\":1,\"nodes\":20,\"pvs\":[{\"moves\":[\"e7e5\"],\"cp\":20,\"depth\":1}]}",
            "{\"time\":1,\"depth\":1,\"nodes\":20,\"pvs\":[{\"moves\":[\"e7e5\"],\"cp\":20,\"depth\":1},{\"moves\":[\"c7c5\"],\"cp\":25,\"depth\":1}]}",
            "{\"time\":1,\"depth\":1,\"nodes\":20,\"pvs\":[{\"moves\":[\"e7e5\"],\"cp\":20,\"depth\":1},{\"moves\":[\"c7c5\"],\"cp\":25,\"depth\":1},{\"moves\":[\"e7e6\"],\"cp\":30,\"depth\":1}]}",
            null,
            null,
            "{\"time\":3,\"depth\":1,\"nodes\":100,\"pvs\":[{\"moves\":[\"e7e5\",\"g1f3\"],\"cp\":22,\"depth\":2},{\"moves\":[\"c7c5\"],\"cp\":28,\"depth\":2},{\"moves\":[\"e7e6\",\"d2d4\"],\"mate\":3,\"depth\":1}]}",
            null,
            "{\"time\":5,\"depth\":3,\"nodes\":200,\"pvs\":[{\"moves\":[\"e7e5\"],\"cp\":15,\"depth\":3}],\"bestmove\":\"e7e5\"}",
        }, results);
    }

    [Fact]
    public void BlackToMoveMateAndCastle()
    {
        var (moves, results) = Run(
            "r3k2r/pppq1ppp/2n1bn2/2bpp3/2BPP3/2N1BN2/PPPQ1PPP/R3K2R b KQkq - 0 8",
            [],
            [
                "info depth 5 seldepth 8 multipv 1 score cp 12 nodes 1000 time 10 pv e8g8 e1c1",
                "info depth 6 seldepth 9 multipv 1 score cp -40 nodes 2000 time 20 pv e8h8 e1a1 d7e7",
                "info depth 7 seldepth 9 multipv 1 score mate 2 nodes 3000 time 30 pv e8c8",
                "info depth 8 seldepth 9 multipv 1 score mate -1 nodes 3500 time 31 pv e8a8",
                "info depth 9 multipv 1 score mate 0 nodes 3600 time 32 pv",
                "bestmove e8g8 ponder e1c1",
            ]);
        Assert.Equal([], moves);
        Assert.Equal(new string?[]
        {
            "{\"time\":10,\"depth\":5,\"nodes\":1000,\"pvs\":[{\"moves\":[\"e8h8\",\"e1a1\"],\"cp\":-12,\"depth\":5}]}",
            "{\"time\":20,\"depth\":6,\"nodes\":2000,\"pvs\":[{\"moves\":[\"e8h8\",\"e1a1\",\"d7e7\"],\"cp\":40,\"depth\":6}]}",
            "{\"time\":30,\"depth\":7,\"nodes\":3000,\"pvs\":[{\"moves\":[\"e8a8\"],\"mate\":-2,\"depth\":7}]}",
            "{\"time\":31,\"depth\":8,\"nodes\":3500,\"pvs\":[{\"moves\":[\"e8a8\"],\"mate\":1,\"depth\":8}]}",
            "{\"time\":32,\"depth\":9,\"nodes\":3600,\"pvs\":[{\"moves\":[],\"mate\":0,\"depth\":9}]}",
            "{\"time\":32,\"depth\":9,\"nodes\":3600,\"pvs\":[{\"moves\":[],\"mate\":0,\"depth\":9}],\"bestmove\":\"e8g8\",\"ponder\":\"e1c1\"}",
        }, results);
    }

    [Fact]
    public void PvCutAndCap()
    {
        var (moves, results) = Run(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            [],
            [
                "info depth 10 multipv 1 score cp 5 nodes 10 time 1 pv e2e4 e7e5 e1e2 e8e7 e2e1 e7e8 e1e2 e8e7 e2e1 e7e8 e1e2 e8e7 e2e1 e7e8 e1e2 e8e7 e2e1 e7e8 e1e2 e8e7 e2e1 e7e8 e1e2 e8e7 e2e1 e7e8 e1e2 e8e7 e2e1 e7e8 e1e2 e8e7 e2e1 e7e8",
                "info depth 11 multipv 1 score cp 6 nodes 11 time 2 pv e2e4 e2e4 e7e5",
                "info depth 12 multipv 1 score cp 7 nodes 12 time 3 pv e2e5",
                "bestmove e2e4",
            ]);
        Assert.Equal([], moves);
        Assert.Equal(new string?[]
        {
            "{\"time\":1,\"depth\":10,\"nodes\":10,\"pvs\":[{\"moves\":[\"e2e4\",\"e7e5\",\"e1e2\",\"e8e7\",\"e2e1\",\"e7e8\",\"e1e2\",\"e8e7\",\"e2e1\",\"e7e8\",\"e1e2\",\"e8e7\",\"e2e1\",\"e7e8\",\"e1e2\",\"e8e7\",\"e2e1\",\"e7e8\",\"e1e2\",\"e8e7\",\"e2e1\",\"e7e8\",\"e1e2\",\"e8e7\",\"e2e1\",\"e7e8\",\"e1e2\",\"e8e7\",\"e2e1\",\"e7e8\"],\"cp\":5,\"depth\":10}]}",
            "{\"time\":2,\"depth\":11,\"nodes\":11,\"pvs\":[{\"moves\":[\"e2e4\"],\"cp\":6,\"depth\":11}]}",
            "{\"time\":3,\"depth\":12,\"nodes\":12,\"pvs\":[{\"moves\":[],\"cp\":7,\"depth\":12}]}",
            "{\"time\":3,\"depth\":12,\"nodes\":12,\"pvs\":[{\"moves\":[],\"cp\":7,\"depth\":12}],\"bestmove\":\"e2e4\"}",
        }, results);
    }

    [Fact]
    public void PromotionAndEp()
    {
        var (moves, results) = Run(
            "4k3/1P6/8/3pP3/8/8/8/4K3 w - d6 0 1",
            [],
            [
                "info depth 3 multipv 1 score cp 900 nodes 30 time 1 pv e5d6 e8d7 b7b8q",
                "info depth 4 multipv 1 score cp 800 nodes 40 time 2 pv b7b8n e8e7 b7b8",
                "info depth 5 multipv 1 score cp 700 nodes 50 time 3 pv b7b8",
                "bestmove b7b8q",
            ]);
        Assert.Equal([], moves);
        Assert.Equal(new string?[]
        {
            "{\"time\":1,\"depth\":3,\"nodes\":30,\"pvs\":[{\"moves\":[\"e5d6\",\"e8d7\",\"b7b8q\"],\"cp\":900,\"depth\":3}]}",
            "{\"time\":2,\"depth\":4,\"nodes\":40,\"pvs\":[{\"moves\":[\"b7b8n\",\"e8e7\"],\"cp\":800,\"depth\":4}]}",
            "{\"time\":3,\"depth\":5,\"nodes\":50,\"pvs\":[{\"moves\":[],\"cp\":700,\"depth\":5}]}",
            "{\"time\":3,\"depth\":5,\"nodes\":50,\"pvs\":[{\"moves\":[],\"cp\":700,\"depth\":5}],\"bestmove\":\"b7b8q\"}",
        }, results);
    }

    [Fact]
    public void IgnoredAndBroken()
    {
        var (moves, results) = Run(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            [],
            [
                "info string NNUE evaluation using nn-1111cefa1111.nnue enabled",
                "info depth 1 currmove e2e4 currmovenumber 1",
                "readyok",
                "",
                "info depth 2 multipv 1 score cp 10 nodes 5 time 1 wdl 500 400 100 pv e2e4",
                "info depth abc multipv 1 score cp 10 nodes 5 time 1 pv e2e4",
                "info depth 2 multipv 6 score cp 10 nodes 5 time 1 pv e2e4",
                "info depth 2 multipv 1 score xx 10 pv e2e4",
                "info depth 2 multipv 1 score cp",
                "info depth 2 multipv 0 score cp 11 nodes 6 time 2 pv e2e4",
                "info\tdepth 3\tmultipv 1  score cp 12 nodes 7   time 3 pv e2e4\te7e5",
                "info depth 4 multipv 1 score cp +13 nodes 8 time 4 pv g1f3 string hello world  ",
                "info depth 5 multipv 1 score cp 14 refutation e2e4 e7e5 currline 1 e2e4 nodes 9 time 5 pv c2c4",
                "{\"keepalive\":true}",
                "bestmove (none)",
            ]);
        Assert.Equal([], moves);
        Assert.Equal(new string?[]
        {
            null,
            null,
            "IGNORED",
            "IGNORED",
            "ERR",
            "ERR",
            "ERR",
            "ERR",
            "ERR",
            "{\"time\":2,\"depth\":2,\"nodes\":6,\"pvs\":[{\"moves\":[\"e2e4\"],\"cp\":11,\"depth\":2}]}",
            "{\"time\":3,\"depth\":3,\"nodes\":7,\"pvs\":[{\"moves\":[\"e2e4\",\"e7e5\"],\"cp\":12,\"depth\":3}]}",
            "{\"time\":4,\"depth\":4,\"nodes\":8,\"pvs\":[{\"moves\":[\"g1f3\"],\"cp\":13,\"depth\":4}]}",
            "{\"time\":5,\"depth\":5,\"nodes\":9,\"pvs\":[{\"moves\":[\"c2c4\"],\"cp\":14,\"depth\":5}]}",
            "CONTROL:{\"keepalive\":true}",
            "{\"time\":5,\"depth\":5,\"nodes\":9,\"pvs\":[{\"moves\":[\"c2c4\"],\"cp\":14,\"depth\":5}]}",
        }, results);
    }

    [Fact]
    public void ResetKeepsLength()
    {
        var (moves, results) = Run(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            [],
            [
                "info depth 1 multipv 1 score cp 1 nodes 1 time 1 pv e2e4",
                "info depth 1 multipv 2 score cp 2 nodes 2 time 1 pv d2d4",
                "info depth 2 multipv 1 score cp 3 nodes 3 time 2 pv e2e4",
                "info depth 2 score cp 4 nodes 4 time 3 pv e2e4",
                "info depth 2 multipv 2 score cp 5 nodes 5 time 3 pv d2d4",
            ]);
        Assert.Equal([], moves);
        Assert.Equal(new string?[]
        {
            "{\"time\":1,\"depth\":1,\"nodes\":1,\"pvs\":[{\"moves\":[\"e2e4\"],\"cp\":1,\"depth\":1}]}",
            "{\"time\":1,\"depth\":1,\"nodes\":1,\"pvs\":[{\"moves\":[\"e2e4\"],\"cp\":1,\"depth\":1},{\"moves\":[\"d2d4\"],\"cp\":2,\"depth\":1}]}",
            null,
            null,
            "{\"time\":3,\"depth\":2,\"nodes\":4,\"pvs\":[{\"moves\":[\"e2e4\"],\"cp\":4,\"depth\":2},{\"moves\":[\"d2d4\"],\"cp\":5,\"depth\":2}]}",
            "EOF:{\"time\":3,\"depth\":2,\"nodes\":4,\"pvs\":[{\"moves\":[\"e2e4\"],\"cp\":4,\"depth\":2},{\"moves\":[\"d2d4\"],\"cp\":5,\"depth\":2}]}",
        }, results);
    }

    [Fact]
    public void BestmoveVariants()
    {
        var (moves, results) = Run(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            ["g1f3", "g8f6", "g2g3", "g7g6", "f1g2", "f8g7"],
            [
                "info depth 3 multipv 1 score cp 20 nodes 3 time 1 pv e1g1 e8g8",
                "info depth 4 multipv 1 score cp 21 nodes 4 time 2 pv e1h1 e8h8 d2d4",
                "bestmove e1g1 ponder (none)",
            ]);
        Assert.Equal(["g1f3", "g8f6", "g2g3", "g7g6", "f1g2", "f8g7"], moves);
        Assert.Equal(new string?[]
        {
            "{\"time\":1,\"depth\":3,\"nodes\":3,\"pvs\":[{\"moves\":[\"e1h1\",\"e8h8\"],\"cp\":20,\"depth\":3}]}",
            "{\"time\":2,\"depth\":4,\"nodes\":4,\"pvs\":[{\"moves\":[\"e1h1\",\"e8h8\",\"d2d4\"],\"cp\":21,\"depth\":4}]}",
            "{\"time\":2,\"depth\":4,\"nodes\":4,\"pvs\":[{\"moves\":[\"e1h1\",\"e8h8\",\"d2d4\"],\"cp\":21,\"depth\":4}],\"bestmove\":\"e1g1\"}",
        }, results);
    }

    [Fact]
    public void BestmoveGarbage()
    {
        var (moves, results) = Run(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            [],
            [
                "info depth 1 multipv 1 score cp 1 nodes 1 time 1 pv e2e4",
                "bestmove e2e4 foo",
            ]);
        Assert.Equal([], moves);
        Assert.Equal(new string?[]
        {
            "{\"time\":1,\"depth\":1,\"nodes\":1,\"pvs\":[{\"moves\":[\"e2e4\"],\"cp\":1,\"depth\":1}]}",
            "ERR",
            "EOF:{\"time\":1,\"depth\":1,\"nodes\":1,\"pvs\":[{\"moves\":[\"e2e4\"],\"cp\":1,\"depth\":1}]}",
        }, results);
    }

    [Fact]
    public void EofWithoutBestmove()
    {
        var (moves, results) = Run(
            "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
            [],
            [
                "info depth 1 multipv 1 score cp 1 nodes 1 time 1 pv e2e4",
                "info depth 1 multipv 2 score cp 0 nodes 2 time 1 pv d2d4",
                "info depth 2 multipv 1 score cp 2 nodes 3 time 2 pv e2e4",
            ]);
        Assert.Equal([], moves);
        Assert.Equal(new string?[]
        {
            "{\"time\":1,\"depth\":1,\"nodes\":1,\"pvs\":[{\"moves\":[\"e2e4\"],\"cp\":1,\"depth\":1}]}",
            "{\"time\":1,\"depth\":1,\"nodes\":1,\"pvs\":[{\"moves\":[\"e2e4\"],\"cp\":1,\"depth\":1},{\"moves\":[\"d2d4\"],\"cp\":0,\"depth\":1}]}",
            null,
            "EOF:{\"time\":2,\"depth\":2,\"nodes\":3,\"pvs\":[{\"moves\":[\"e2e4\"],\"cp\":2,\"depth\":2}]}",
        }, results);
    }

    [Fact]
    public void MoveSyntax()
    {
        var (moves, results) = Run(
            "4k3/1P6/8/8/8/8/8/4K3 w - - 0 1",
            [],
            [
                "info depth 1 multipv 1 score cp 900 nodes 1 time 1 pv b7b8Q e8d7",
                "info depth 2 multipv 1 score cp 901 nodes 2 time 2 pv 0000 e8d7",
                "info depth 3 multipv 1 score cp 902 nodes 3 time 3 pv Q@b8 e8d7",
                "info depth 4 multipv 1 score cp 903 nodes 4 time 4 pv E1E2",
                "info depth 5 multipv 1 score cp 904 nodes 5 time 5 pv e1e2 e8e7 q@a1",
                "bestmove b7b8Q ponder e8d7",
            ]);
        Assert.Equal([], moves);
        Assert.Equal(new string?[]
        {
            "{\"time\":1,\"depth\":1,\"nodes\":1,\"pvs\":[{\"moves\":[\"b7b8q\",\"e8d7\"],\"cp\":900,\"depth\":1}]}",
            "{\"time\":2,\"depth\":2,\"nodes\":2,\"pvs\":[{\"moves\":[],\"cp\":901,\"depth\":2}]}",
            "{\"time\":3,\"depth\":3,\"nodes\":3,\"pvs\":[{\"moves\":[],\"cp\":902,\"depth\":3}]}",
            "ERR",
            "{\"time\":5,\"depth\":5,\"nodes\":5,\"pvs\":[{\"moves\":[\"e1e2\",\"e8e7\"],\"cp\":904,\"depth\":5}]}",
            "{\"time\":5,\"depth\":5,\"nodes\":5,\"pvs\":[{\"moves\":[\"e1e2\",\"e8e7\"],\"cp\":904,\"depth\":5}],\"bestmove\":\"b7b8q\",\"ponder\":\"e8d7\"}",
        }, results);
    }

}
