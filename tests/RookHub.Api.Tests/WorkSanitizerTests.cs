using RookHub.Api.Services.EngineBroker;

namespace RookHub.Api.Tests;

/// <summary>
/// Port von lila-engine <c>Work::sanitize</c>: was der Provider bekommt, ist geprüft und normalisiert.
/// Die illegalen Stellungen sind die, bei denen Stockfish 19 sich beendet (und den Provider mitnimmt).
/// </summary>
public class WorkSanitizerTests
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private static EngineWork Work(string fen = Start, int threads = 4, int hash = 256, int multiPv = 1,
        int? depth = 20, int? movetime = null, long? nodes = null, params string[] moves) =>
        new("sess", threads, hash, multiPv, fen, moves, depth, movetime, nodes);

    [Fact]
    public void ClampsThreadsAndHash_ToTheEngineMaxima()
    {
        var w = WorkSanitizer.Sanitize(Work(threads: 64, hash: 65536), maxThreads: 8, maxHash: 1024).Work;
        Assert.Equal(8, w.Threads);
        Assert.Equal(1024, w.Hash);
        var small = WorkSanitizer.Sanitize(Work(threads: 2, hash: 16), maxThreads: 8, maxHash: 1024).Work;
        Assert.Equal(2, small.Threads);
        Assert.Equal(16, small.Hash);
    }

    [Fact]
    public void Castling_IsNormalizedToKingTakesRook()
    {
        // Der Provider setzt UCI_Chess960 true: e1g1 hiesse dort „König nach g1", Stockfish verwürfe den Rest.
        var w = WorkSanitizer.Sanitize(Work(moves: ["e2e4", "e7e5", "g1f3", "b8c6", "f1c4", "g8f6", "e1g1", "f8c5"]), 8, 1024);
        Assert.Equal(["e2e4", "e7e5", "g1f3", "b8c6", "f1c4", "g8f6", "e1h1", "f8c5"], w.Work.Moves);
        // Schon König-schlägt-Turm geschrieben bleibt es.
        var w2 = WorkSanitizer.Sanitize(Work(moves: ["e2e4", "e7e5", "g1f3", "b8c6", "f1c4", "g8f6", "e1h1"]), 8, 1024);
        Assert.Equal("e1h1", w2.Work.Moves[^1]);
        Assert.Contains(" b ", w2.RootFen);
    }

    [Fact]
    public void RootFen_IsThePositionAfterTheMoves()
    {
        var w = WorkSanitizer.Sanitize(Work(moves: ["e2e4"]), 8, 1024);
        Assert.StartsWith("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b", w.RootFen);
        Assert.Equal(Start, w.Work.InitialFen);
    }

    [Theory]
    [InlineData("e2e5")]      // geht nicht
    [InlineData("e7e5")]      // falsche Seite
    [InlineData("e2e4x")]     // keine Figur
    [InlineData("0000")]      // Nullzug
    [InlineData("N@f3")]      // Einsetzen gibt es im Standardschach nicht
    public void IllegalMove_IsRejected(string move)
    {
        Assert.Throws<InvalidWorkException>(() => WorkSanitizer.Sanitize(Work(moves: [move]), 8, 1024));
    }

    [Fact]
    public void Promotion_NeedsThePiece()
    {
        const string fen = "4k3/1P6/8/8/8/8/8/4K3 w - - 0 1";
        Assert.Equal(["b7b8q"], WorkSanitizer.Sanitize(Work(fen, moves: ["b7b8Q"]), 8, 1024).Work.Moves);
        Assert.Throws<InvalidWorkException>(() => WorkSanitizer.Sanitize(Work(fen, moves: ["b7b8"]), 8, 1024));
    }

    [Fact]
    public void AtMost600Moves()
    {
        var shuffle = Enumerable.Range(0, 601).Select(i => (i % 4) switch { 0 => "g1f3", 1 => "g8f6", 2 => "f3g1", _ => "f6g8" }).ToArray();
        Assert.Throws<InvalidWorkException>(() => WorkSanitizer.Sanitize(Work(moves: shuffle), 8, 1024));
        Assert.Equal(600, WorkSanitizer.Sanitize(Work(moves: shuffle[..600]), 8, 1024).Work.Moves.Count);
    }

    [Theory]
    [InlineData("8/8/8/4p3/8/8/8/8 w - - 0 1")]                 // kein König (Chessable-Info-Diagramm) — Stockfish 19 beendet sich
    [InlineData("4k3/8/8/8/8/8/8/K3K3 w - - 0 1")]              // zwei weiße Könige
    [InlineData("4k3/4R3/8/8/8/8/8/4K3 w - - 0 1")]             // Schwarz steht im Schach, Weiß am Zug
    [InlineData("4k3/8/8/8/8/8/8/P3K3 w - - 0 1")]              // Bauer auf der Grundreihe
    [InlineData("4k3/8/8/8/8/8/8/4K3 w KQkq - 0 1")]            // Rochaderechte ohne Türme
    [InlineData("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e6 0 1")]   // ep-Feld auf der falschen Reihe
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq e3 0 1")]     // ep ohne gezogenen Bauern
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w HAha - 0 1")]      // Chess960-Rechte kennt RookHub nicht
    [InlineData("garbage")]
    [InlineData("")]
    public void IllegalInitialPosition_IsRejected(string fen)
    {
        Assert.Throws<InvalidWorkException>(() => WorkSanitizer.Sanitize(Work(fen), 8, 1024));
    }

    [Theory]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq -", "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR  w  KQkq - 3", "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 3 1")]
    [InlineData("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1")]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1")]
    public void LegalPositions_AreAccepted_CountersFilledIn(string fen, string expected)
    {
        Assert.Equal(expected, WorkSanitizer.Sanitize(Work(fen), 8, 1024).Work.InitialFen);
    }

    [Theory]
    [InlineData(null, null, null)]        // keines
    [InlineData(20, 5000, null)]          // zwei
    [InlineData(0, null, null)]           // nicht positiv
    public void ExactlyOneSearchLimit(int? depth, int? movetime, long? nodes)
    {
        Assert.Throws<InvalidWorkException>(() =>
            WorkSanitizer.Sanitize(new EngineWork("s", 1, 16, 1, Start, [], depth, movetime, nodes), 8, 1024));
    }

    [Fact]
    public void MultiPv_ZeroBecomesOne_AboveFiveIsInvalid()
    {
        Assert.Equal(1, WorkSanitizer.Sanitize(Work(multiPv: 0), 8, 1024).Work.MultiPv);
        Assert.Equal(5, WorkSanitizer.Sanitize(Work(multiPv: 5), 8, 1024).Work.MultiPv);
        Assert.Throws<InvalidWorkException>(() => WorkSanitizer.Sanitize(Work(multiPv: 6), 8, 1024));
    }

    [Fact]
    public void ToJson_HasTheFieldsTheProviderReads()
    {
        var w = WorkSanitizer.Sanitize(Work(threads: 2, hash: 64, multiPv: 3, depth: null, movetime: 1500, moves: ["e2e4"]), 8, 1024).Work;
        var json = w.ToJson().ToJsonString();
        Assert.Equal(
            "{\"sessionId\":\"sess\",\"threads\":2,\"hash\":64,\"movetime\":1500,\"multiPv\":3,\"variant\":\"chess\","
            + "\"initialFen\":\"" + Start + "\",\"moves\":[\"e2e4\"]}", json);
    }
}
