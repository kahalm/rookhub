using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Memo-Feld der Kurs-Detailseite: hineinkopierte Stellungsliste → FENs. Bewusst tolerant bei der
/// Form (Nummerierung, Kommentare, fehlende hintere FEN-Felder) und bewusst OHNE Legalitätsprüfung.
/// </summary>
public class FenListParserTests
{
    private const string Fen1 = "r2q1r1k/1pp1bppb/p1np4/4p1Pp/2B1P2N/2PPB2P/PP1Q1P2/R3R1K1 w - - 0 18";
    private const string Fen2 = "r2qk2r/p3bppp/Q4n2/4p1N1/3n4/8/PPPP1PPP/RNB2RK1 b kq - 0 12";

    [Fact]
    public void Parse_NumberedList_TakesEveryPosition()
    {
        var text = $"1: {Fen1}\n2: {Fen2}\n";
        var result = FenListParser.Parse(text);
        Assert.Empty(result.Errors);
        Assert.Equal(new[] { Fen1, Fen2 }, result.Positions.Select(p => p.Fen));
        Assert.Equal(new[] { 1, 2 }, result.Positions.Select(p => p.LineNumber));
    }

    [Fact]
    public void Parse_AcceptsTheUsersExactPaste()
    {
        // Genau das Format aus der Anfrage (6 Zeilen, „N: FEN").
        var text = """
            1: r2q1r1k/1pp1bppb/p1np4/4p1Pp/2B1P2N/2PPB2P/PP1Q1P2/R3R1K1 w - - 0 18
            2: r2qk2r/p3bppp/Q4n2/4p1N1/3n4/8/PPPP1PPP/RNB2RK1 b kq - 0 12
            3: rnbqk2r/p4ppp/2p1p3/1p1nP3/P1pP4/2P2N2/3B1PPP/R2QKB1R w KQkq - 0 10
            4: 1k6/p1p2ppp/1p6/1Qn2q2/8/P2r1B2/1P4PP/K1R5 w - - 0 28
            5: 7r/3pn1k1/p3prp1/1pp3Q1/P2nB1RN/1PqP2P1/2P2PK1/5R2 b - - 4 30
            6: 2kr1b1r/pppq1pp1/2n1pn2/5b1p/2PP4/P3BN1P/1P1NBPP1/R2Q1RK1 b - - 0 11
            """;
        var result = FenListParser.Parse(text);
        Assert.Empty(result.Errors);
        Assert.Equal(6, result.Positions.Count);
        Assert.All(result.Positions, p => Assert.Equal(6, p.Fen.Split(' ').Length));
        Assert.Equal("1k6/p1p2ppp/1p6/1Qn2q2/8/P2r1B2/1P4PP/K1R5 w - - 0 28", result.Positions[3].Fen);
    }

    [Theory]
    [InlineData("1.")]
    [InlineData("1:")]
    [InlineData("1)")]
    [InlineData("1 -")]
    [InlineData("")]
    public void Parse_AcceptsDifferentNumberingStyles(string prefix)
    {
        var result = FenListParser.Parse($"{prefix} {Fen1}");
        Assert.Empty(result.Errors);
        Assert.Equal(Fen1, result.Positions.Single().Fen);
    }

    [Fact]
    public void Parse_FenStartingWithADigit_IsNotMistakenForNumbering()
    {
        var result = FenListParser.Parse("8/8/8/4k3/8/8/4K3/8 w - - 0 1");
        Assert.Empty(result.Errors);
        Assert.Equal("8/8/8/4k3/8/8/4K3/8 w - - 0 1", result.Positions.Single().Fen);
    }

    [Fact]
    public void Parse_SkipsBlankLines_AndKeepsOriginalLineNumbers()
    {
        var result = FenListParser.Parse($"\n{Fen1}\n\n   \n{Fen2}\n");
        Assert.Equal(2, result.Positions.Count);
        Assert.Equal(2, result.Positions[0].LineNumber);
        Assert.Equal(5, result.Positions[1].LineNumber);
    }

    [Fact]
    public void Parse_ReadsCommentAfterPipe()
    {
        var result = FenListParser.Parse($"1: {Fen1} | Wie bewertest du die Stellung?");
        var pos = result.Positions.Single();
        Assert.Equal(Fen1, pos.Fen);
        Assert.Equal("Wie bewertest du die Stellung?", pos.Comment);
    }

    [Fact]
    public void Parse_ReadsCommentInBraces()
    {
        var result = FenListParser.Parse($"{Fen2} {{Turmendspiel, Schlüsselstellung}}");
        var pos = result.Positions.Single();
        Assert.Equal(Fen2, pos.Fen);
        Assert.Equal("Turmendspiel, Schlüsselstellung", pos.Comment);
    }

    [Fact]
    public void Parse_EmptyComment_StaysNull()
    {
        Assert.Null(FenListParser.Parse($"{Fen1} |   ").Positions.Single().Comment);
        Assert.Null(FenListParser.Parse($"{Fen1} {{}}").Positions.Single().Comment);
    }

    [Fact]
    public void Parse_FillsMissingTrailingFields()
    {
        var result = FenListParser.Parse("8/8/8/4k3/8/8/4K3/8 w");
        Assert.Equal("8/8/8/4k3/8/8/4K3/8 w - - 0 1", result.Positions.Single().Fen);
    }

    [Fact]
    public void Parse_KeepsIllegalButStructurallyValidDiagrams()
    {
        // Chessable-Muster-Diagramm ohne König — bewusst erlaubt.
        var result = FenListParser.Parse("8/8/3p4/4P3/8/8/8/8 w - - 0 1");
        Assert.Empty(result.Errors);
        Assert.Single(result.Positions);
    }

    [Theory]
    [InlineData("völliger Unsinn")]                                  // kein FEN
    [InlineData("8/8/8/8/8/8/8 w - - 0 1")]                          // nur 7 Reihen
    [InlineData("8/8/8/8/8/8/8/9 w - - 0 1")]                        // Reihe mit 9 Feldern
    [InlineData("8/8/8/8/8/8/8/ppp w - - 0 1")]                      // Reihe zu kurz
    [InlineData("8/8/8/8/8/8/8/8 x - - 0 1")]                        // Seite am Zug ungültig
    [InlineData("8/8/8/8/8/8/8/8")]                                  // Seite am Zug fehlt
    [InlineData("8/8/8/8/8/8/8/xyz w - - 0 1")]                      // ungültige Figuren
    [InlineData("8/8/8/8/8/8/8/8 w - z9 0 1")]                       // e.p.-Feld ungültig
    public void Parse_ReportsUnusableLines(string line)
    {
        var result = FenListParser.Parse(line);
        Assert.Empty(result.Positions);
        var error = Assert.Single(result.Errors);
        Assert.Equal("invalid_fen", error.Reason);
        Assert.Equal(1, error.LineNumber);
    }

    [Fact]
    public void Parse_MixesGoodAndBadLines()
    {
        var result = FenListParser.Parse($"{Fen1}\nkaputt\n{Fen2}");
        Assert.Equal(2, result.Positions.Count);
        var error = Assert.Single(result.Errors);
        Assert.Equal(2, error.LineNumber);
    }

    [Fact]
    public void Parse_StopsAtTheLineLimit()
    {
        var text = string.Join('\n', Enumerable.Repeat(Fen1, FenListParser.MaxLines + 20));
        var result = FenListParser.Parse(text);
        Assert.Equal(FenListParser.MaxLines, result.Positions.Count);
        Assert.Equal("too_many", Assert.Single(result.Errors).Reason);
    }

    [Fact]
    public void Parse_EmptyInput_YieldsNothing()
    {
        foreach (var text in new[] { null, "", "   \n\n" })
        {
            var result = FenListParser.Parse(text);
            Assert.Empty(result.Positions);
            Assert.Empty(result.Errors);
        }
    }

    [Fact]
    public void NormalizeFen_TrimsAndRepairs()
    {
        Assert.Equal("8/8/8/4k3/8/8/4K3/8 b - - 0 1", FenListParser.NormalizeFen("8/8/8/4k3/8/8/4K3/8   b"));
        Assert.Equal("8/8/8/4k3/8/8/4K3/8 w - - 0 1", FenListParser.NormalizeFen("8/8/8/4k3/8/8/4K3/8 w - - -3 0"));
        Assert.Null(FenListParser.NormalizeFen(null));
        Assert.Null(FenListParser.NormalizeFen("   "));
    }
}
