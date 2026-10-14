using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

public class EbookSolutionFinderTests
{
    private const string Ex1 = "r6r/1pp3k1/1b6/p2P1p2/P1N1pn2/2P2PP1/BP5P/4RR1K b - - 0 1";

    [Fact]
    public void Normalize_MapsFigurinesAndSymbols()
    {
        Assert.Equal("34...Rxh4+! 35.Kg1 Q N B #", EbookSolutionFinder.Normalize("34...¦xh4†! 35.¢g1 £ ¤ \uF0A5 ‡"));
    }

    [Fact]
    public void Parse_BlackToMove_FollowsMainLine_SkipsProseAndSideLines()
    {
        // Woodpecker Nr. 1: 17...Th2+! 18.Kxh2 Th8+ … mit Prosa und einer Nebenvariante dazwischen.
        var s = EbookSolutionFinder.TryParse(Ex1, true,
            "Hamppe – Steinitz, Vienna 1860 17...¦xh2†! Black also wins after 17...¤xh2? no. 18.¢xh2 Or 18.¢g1 ¦h1 mate. 18...¦h8† and mates.", 230);
        Assert.NotNull(s);
        Assert.Equal('b', s!.Side);
        Assert.Equal(17, s.FirstMoveNumber);
        Assert.Equal("h8h2 h1h2 a8h8", string.Join(' ', s.Uci));
        Assert.Equal("Rxh2+", s.San[0]);
    }

    [Fact]
    public void Parse_MoveNumbersWithoutDot_DvoretskyStyle()
    {
        const string fen = "3k4/8/8/3K4/3P4/8/8/8 w - - 0 1";
        var w = EbookSolutionFinder.TryParse(fen, false, "With White to move, there is no win after 25 moves: 1 Kc5 Kc7; or 1 Ke5 Ke7.", 3);
        Assert.NotNull(w);
        Assert.Equal('w', w!.Side);
        Assert.Equal("Kc5 Kc7", string.Join(' ', w.San));
        var b = EbookSolutionFinder.TryParse("3k4/8/8/3K4/3P4/8/8/8 b - - 0 1", true, "Black to move loses: 1…Kc7 2 Ke6; or 1...Ke7 2 Kc6.", 3);
        Assert.NotNull(b);
        Assert.Equal("Kc7 Ke6", string.Join(' ', b!.San));
    }

    [Fact]
    public void Parse_DoubtedFirstMoveIsSkipped_AndOnlyTheSameMoveNumberCounts()
    {
        const string fen = "4k3/8/8/8/8/8/4P3/R3K3 w - - 0 1";
        var s = EbookSolutionFinder.TryParse(fen, true, "36.Ra8+?! looks good but after 36...Kd7 37.Ra7+ nothing. Instead 36.e4 wins.", 1);
        Assert.Equal("e2e4", s!.Uci[0]);
    }

    [Fact]
    public void Parse_IllegalFirstMove_GivesNothing()
    {
        Assert.Null(EbookSolutionFinder.TryParse(Ex1, true, "17...Qxh2+ 18.Kxh2", 1));
        Assert.Null(EbookSolutionFinder.TryParse(Ex1, true, "Black wins easily.", 1));
    }

    [Fact]
    public void NumberedEntries_StartWithNumberDotSpace_NotWithAMove()
    {
        var lines = new List<PageLine>
        {
            new(230, 0, 10, 20, 0, 400, "1. Hamppe – Steinitz, Vienna 1860"),
            new(230, 0, 22, 32, 0, 400, "17...Rxh2+! 18.Kxh2 Rh8+"),
            new(230, 0, 34, 44, 0, 400, "2. Steinitz – Wilson, London 1862"),
            new(230, 0, 46, 56, 0, 400, "21.Rf8+! Bxf8"),
        };
        var e = EbookSolutionFinder.NumberedEntries(lines);
        Assert.Equal([1, 2], e.Keys.Order());
        Assert.Contains("17...Rxh2+!", e[1][0].Text);
        Assert.DoesNotContain("21.Rf8", e[1][0].Text);
    }

    [Fact]
    public void Match_PrefersTheSolutionSectionOverTheDiagramPage_AndFallsBackToTextBelow()
    {
        var lines = new List<PageLine>
        {
            new(33, 0, 300, 310, 0, 200, "White to move"),
            new(40, 0, 100, 110, 0, 400, "1. Hamppe – Steinitz, Vienna 1860"),
            new(40, 0, 112, 122, 0, 400, "17...Rxh2+! 18.Kxh2 Rh8+ 19.Bh7 Rxh7 mate"),
            new(50, 1, 210, 220, 300, 500, "1.e4 and White wins"),
        };
        var diagrams = new List<(int, int, int, double, string, bool, string?)>
        {
            (0, 33, 0, 290, Ex1, true, "1"),
            (1, 50, 1, 200, "4k3/8/8/8/8/8/4P3/4K3 w - - 0 1", false, null),
        };
        var r = EbookSolutionFinder.Match(diagrams, lines);
        Assert.Equal(40, r[0].Page);
        Assert.Equal("h8h2", r[0].Uci[0]);
        Assert.Equal("e2e4", r[1].Uci[0]);
        Assert.Equal('w', r[1].Side);
    }
}
