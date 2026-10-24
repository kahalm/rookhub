using RookHub.Api.Services.Chessable;

namespace RookHub.Api.Tests.Chessable;

/// <summary>
/// Charakterisierung (Golden) der PGN-Block-Helfer des Chessable-Imports: Zerlegen in [Event]-Blöcke, oid lesen,
/// Zugtext-Signatur, oid nachtragen. Festgehalten VOR dem Herauslösen aus <c>ChessableImportService</c>
/// (Zerlegung Z3) — die Erwartungen sind das, was der alte Code lieferte. Die Blöcke folgen den realen Formen aus
/// den Import-Tests: heutiges piratechess-Format ([ChessableOid] + Leerzeichen-Zeile vor den Zügen), Alt-Import
/// ohne oid mit echter Leerzeile, CRLF-Zeilenenden mit Kommentar.
/// </summary>
public class ChessablePgnBlocksTests
{
    private const string BlockA =
        "[Event \"Ch1\"]\n[Round \"002.001\"]\n[White \"Line A\"]\n[Black \"T\"]\n[Result \"*\"]\n[ChessableOid \"501\"]\n                        \n1. e4 e5 2. Nf3 *\n";
    private const string BlockB =
        "[Event \"Ch1\"]\n[Round \"002.002\"]\n[White \"Line B\"]\n[Black \"T\"]\n[Result \"*\"]\n\n1. d4 d5 *\n";
    private const string BlockC =
        "[Event \"Ch2\"]\r\n[Round \"003.001\"]\r\n[ChessableOid \"777\"]\r\n\r\n1. c4 {Englisch} c5 *\r\n";

    private const string Pgn = "Vorspann\n" + BlockA + "\n\n" + BlockB + "\n" + BlockC;

    [Fact]
    public void Split_ThreeRealBlocks_TrimmedInOrder_PreambleDropped()
    {
        var blocks = ChessableImportService.SplitPgnGames(Pgn).ToList();
        Assert.Equal(new[] { BlockA.Trim(), BlockB.Trim(), BlockC.Trim() }, blocks);
        Assert.Equal(3, ChessableImportService.CountPgnGames(Pgn));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("1. e4 e5 *")]
    public void Split_NoEventHeader_Empty(string pgn)
    {
        Assert.Empty(ChessableImportService.SplitPgnGames(pgn));
        Assert.Equal(0, ChessableImportService.CountPgnGames(pgn));
    }

    [Fact]
    public void OidOf_ReadsHeaderOrNull()
    {
        var blocks = ChessableImportService.SplitPgnGames(Pgn).ToList();
        Assert.Equal("501", ChessableImportService.OidOf(blocks[0]));
        Assert.Null(ChessableImportService.OidOf(blocks[1]));
        Assert.Equal("777", ChessableImportService.OidOf(blocks[2]));
    }

    [Fact]
    public void MovetextOf_IgnoresHeadersAndSpaceLine()
    {
        var blocks = ChessableImportService.SplitPgnGames(Pgn).ToList();
        Assert.Equal("1. e4 e5 2. Nf3 *", ChessableImportService.MovetextOf(blocks[0]));
        Assert.Equal("1. d4 d5 *", ChessableImportService.MovetextOf(blocks[1]));
        Assert.Equal("1. c4 {Englisch} c5 *", ChessableImportService.MovetextOf(blocks[2]));
    }

    [Fact]
    public void InsertOids_BehindLastHeader_RestUntouched()
    {
        var result = ChessableImportService.InsertChessableOids(Pgn,
            new Dictionary<int, string> { [1] = "602", [2] = "778", [5] = "999" });

        var expected = "Vorspann\n" + BlockA + "\n\n"
            + BlockB.Replace("[Result \"*\"]\n\n", "[Result \"*\"]\n[ChessableOid \"602\"]\n\n") + "\n"
            + BlockC.Replace("[ChessableOid \"777\"]\r\n", "[ChessableOid \"777\"]\n[ChessableOid \"778\"]\r\n");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void InsertOids_NothingToDo_ReturnsInput()
    {
        Assert.Same(Pgn, ChessableImportService.InsertChessableOids(Pgn, new Dictionary<int, string>()));
        const string noEvent = "1. e4 e5 *";
        Assert.Same(noEvent, ChessableImportService.InsertChessableOids(noEvent, new Dictionary<int, string> { [0] = "1" }));
    }

    [Fact]
    public void HeaderEnd_PositionBeforeLineBreak_OrMinusOne()
    {
        Assert.Equal(BlockB.IndexOf("\n\n", StringComparison.Ordinal), ChessableImportService.HeaderEnd(BlockB, 0, BlockB.Length));
        Assert.Equal(BlockC.IndexOf("\r\n\r\n", StringComparison.Ordinal), ChessableImportService.HeaderEnd(BlockC, 0, BlockC.Length));
        const string noHeader = "1. e4 e5 *\n";
        Assert.Equal(-1, ChessableImportService.HeaderEnd(noHeader, 0, noHeader.Length));
    }
}
