using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

public class EbookDiagramExtractionTests
{
    [Theory]
    [InlineData("BGYQIG+Chess-Merida-Regular", "Marroquin")]
    [InlineData("Chess Alpha", "Marroquin")]
    [InlineData("ABCDEF+DiagramTTFritz", "ChessBase")]
    [InlineData("DiagramTTHabsburg", "ChessBase")]
    [InlineData("UDiagrammB", "ChessBase")]
    [InlineData("HastingsDiagramA", "Hastings")]
    [InlineData("XYZABC+LinaresDiagram", "Hastings")]
    [InlineData("CHESSBOARD", "Marroquin")]
    public void Detect_KnownFamilies(string font, string family) => Assert.Equal(family, ChessDiagramFonts.Detect(font)!.Name);

    [Theory]
    [InlineData("AGaramondPro-Regular")]
    [InlineData("LinaresFigurine")]   // Figurinen im Text, keine Diagramme
    [InlineData("CAChess")]
    [InlineData(null)]
    public void Detect_UnknownFontsAreNull(string? font) => Assert.Null(ChessDiagramFonts.Detect(font));

    [Fact]
    public void Marroquin_MapsLightDarkAndPrivateUseArea()
    {
        var f = ChessDiagramFonts.Detect("Chess-Merida")!;
        Assert.Equal('K', f.Map('k')!.Value.Piece);
        Assert.Equal('K', f.Map('K')!.Value.Piece);   // dunkles Feld
        Assert.Equal('q', f.Map('')!.Value.Piece);  // 'w' im Symbolbereich
        Assert.True(f.Map('+')!.Value.Empty);
        Assert.Null(f.Map('1'));
    }

    [Fact]
    public void ChessBase_UpperWhiteLowerBlack_OverlaysIgnored()
    {
        var f = ChessDiagramFonts.Detect("DiagramTTFritz")!;
        Assert.Equal('B', f.Map('L')!.Value.Piece);
        Assert.Equal('b', f.Map('l')!.Value.Piece);
        Assert.Equal('n', f.Map('n')!.Value.Piece);
        Assert.True(f.Map('-')!.Value.Empty);
        foreach (var overlay in "stvwzmxy8aA") Assert.Null(f.Map(overlay));
    }

    [Fact]
    public void Hastings_LightSquaresAreLetters_DarkSquaresOwnGlyphs()
    {
        var f = ChessDiagramFonts.Detect("HastingsDiagram")!;
        Assert.Equal("PNBRQKpnbrqk", string.Concat("PNBRQKpnbrqk".Select(c => f.Map(c)!.Value.Piece)));
        Assert.Equal("PNBRQKpnbrqk", string.Concat(")HG$!I0hg41i".Select(c => f.Map(c)!.Value.Piece)));
        foreach (var e in "WwDd") Assert.True(f.Map(e)!.Value.Empty);
        foreach (var frame in "{}uvlcCV") Assert.Null(f.Map(frame));
    }

    private static List<DiagramGlyph> Grid(string board, double x0, double y0, double cell = 16)
    {
        var glyphs = new List<DiagramGlyph>();
        var rows = board.Split('/');
        for (var r = 0; r < 8; r++)
        {
            var file = 0;
            foreach (var ch in rows[r])
            {
                if (char.IsDigit(ch))
                    for (var k = 0; k < ch - '0'; k++, file++)
                        glyphs.Add(new DiagramGlyph(x0 + file * cell, y0 + r * cell, cell, cell, ChessDiagramFonts.Square.EmptySquare, "T"));
                else
                    glyphs.Add(new DiagramGlyph(x0 + file++ * cell, y0 + r * cell, cell, cell, new ChessDiagramFonts.Square(false, ch), "T"));
            }
        }
        return glyphs;
    }

    [Fact]
    public void Builder_TwoBoardsOnAPage_WithNumberCaptionAndSideMarker()
    {
        const string a = "r6r/1pp3k1/1b6/p2P1p2/P1N1pn2/2P2PP1/BP5P/4RR1K";
        const string b = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR";
        var glyphs = Grid(a, 100, 100).Concat(Grid(b, 100, 400)).ToList();
        var words = new List<PageWord>
        {
            new(60, 92, 72, 104, "17", "Garamond"),                         // Nummer links der Oberkante (Kreis)
            new(100, 70, 200, 80, "Capablanca – Mieses", "Garamond"),       // Überschrift
            new(240, 200, 248, 210, "", "Wingdings3"),                  // Dreieck: Schwarz am Zug
            new(100, 360, 220, 372, "White to move", "Garamond"),
        };

        var result = DiagramGridBuilder.Build(glyphs, words);

        Assert.Equal(2, result.Count);
        Assert.Equal(a, result[0].Board);
        Assert.Equal("17", result[0].Label);
        Assert.Equal('b', result[0].Side);
        Assert.True(result[0].SideKnown);
        Assert.Contains("Capablanca", result[0].Caption);
        Assert.Equal(b, result[1].Board);
        Assert.Equal('w', result[1].Side);
        Assert.True(result[1].SideKnown);
    }

    [Fact]
    public void Builder_IgnoresGroupsThatDoNotSpanEightByEight()
    {
        // Nur 7 Reihen (die achte fehlt ganz) → kein Brett.
        var glyphs = Grid("8/8/8/8/8/8/8/8", 0, 0).Take(56).ToList();
        Assert.Empty(DiagramGridBuilder.Build(glyphs, []));
    }

    [Fact]
    public void Builder_MissingLightSquaresCountAsEmpty()
    {
        // PDF-Bibliotheken unterschlagen Leerzeichen — fehlende Felder mitten im Brett sind leer, das Brett bleibt.
        const string board = "4k3/8/8/8/8/8/8/4K3";
        var glyphs = Grid(board, 0, 0).Where((g, i) => !(g.Square.Empty && i % 3 == 0)).ToList();
        var result = DiagramGridBuilder.Build(glyphs, []);
        Assert.Single(result);
        Assert.Equal(board, result[0].Board);
    }

    // Echte Bücher der Bibliothek (nur, wenn der Rechner sie hat — CI überspringt): Stellungen gegen den Chessable-Kurs.
    private const string Woodpecker = "/srv/media/sftp/kahalm2/Schach/schachbücher/Smith, Axel/The Woodpecker Method - Smith, Axel.pdf";
    private const string MartinKid = "/srv/media/sftp/kahalm2/Schach/schachbücher/Martin, Andrew/Andrew Martin Classical K.Pdf";

    [Fact]
    public void Woodpecker_FirstExercisesMatchTheCourse()
    {
        if (!File.Exists(Woodpecker)) return;
        var r = new EbookDiagramExtractor(NullLogger<EbookDiagramExtractor>.Instance).Extract(Woodpecker, 1, CancellationToken.None);
        Assert.True(r.FontFound);
        Assert.InRange(r.Diagrams.Count, 1140, 1150);
        var byLabel = r.Diagrams.Where(d => d.Label != null).GroupBy(d => d.Label!).ToDictionary(g => g.Key, g => g.First());
        // Brett + Zugrecht; die Zugnummer kommt seit 0.771.0 aus der Lösung („17...“ → Zug 17).
        static string Head(string fen) => string.Join(' ', fen.Split(' ').Take(2));
        Assert.Equal("r6r/1pp3k1/1b6/p2P1p2/P1N1pn2/2P2PP1/BP5P/4RR1K b", Head(byLabel["1"].Fen));
        Assert.Equal("rnb3kr/ppp4p/3b3B/3Pp2n/2BP4/3K1Rp1/PPP3q1/RN1Q4 w", Head(byLabel["2"].Fen));
        Assert.Equal("rn2rk2/5pp1/p7/2pb1qN1/7Q/2P5/5PPP/1R3RK1 w", Head(byLabel["1128"].Fen));
        // Lösungen aus dem Lösungsteil (gemessen 14.10.: 1 113 von 1 117 erste Züge = Chessable-Kurs).
        Assert.Equal("h8h2", byLabel["1"].SolutionUci!.Split(' ')[0]);
        Assert.StartsWith("d3a6", byLabel["44"].SolutionUci);
        Assert.True(r.Diagrams.Count(d => d.SolutionUci != null) > 1100);
        var d1 = byLabel["1"];
        Assert.InRange(d1.X, 0.0, 1.0); Assert.InRange(d1.W, 0.2, 0.5);
    }

    [Fact]
    public void ChessBaseFont_ReadsTheFirstKingsIndianDiagram()
    {
        if (!File.Exists(MartinKid)) return;
        var r = new EbookDiagramExtractor(NullLogger<EbookDiagramExtractor>.Instance).Extract(MartinKid, 1, CancellationToken.None);
        Assert.True(r.FontFound);
        Assert.StartsWith("rnbq1rk1/ppp1ppbp/3p1np1/8/2PPP3/2N2N2/PP3PPP/R1BQKB1R ", r.Diagrams[0].Fen);
    }

    private const string Baird = "/srv/media/sftp/kahalm2/Schach/schachbücher/Baird, W. J/700 Chess Problems [Baird, 1902].pdf";
    private const string Yusupov = "/srv/media/sftp/kahalm2/Schach/schachbücher/Yusupov, Artur/Artur Yusupov Chess Lesso.Pdf";
    private const string Taylor = "/srv/media/sftp/kahalm2/Schach/schachbücher/Taylor, Paul/J Paul Taylor Problemes .Pdf";

    [Theory]
    [InlineData(Baird, 11, "8/3R4/2n4B/2PBP3/K2kP3/2p3P1/2P5/6N1")]           // Hastings, Seitenbild geprüft
    [InlineData(Yusupov, 1, "2r4k/R1n2p1p/3p2pP/1ppPp3/4P2N/8/1PP2PP1/4K3")]   // UDiagramm (ChessBase-Belegung)
    [InlineData(Taylor, 13, "4R3/3p4/1N1pn1QN/1K1Pk3/1p2p2P/1P2n3/3BP3/8")]     // CHESSBOARD (Marroquin)
    public void RealBooks_FirstDiagramOnPage(string path, int page, string board)
    {
        if (!File.Exists(path)) return;
        var r = new EbookDiagramExtractor(NullLogger<EbookDiagramExtractor>.Instance).Extract(path, 1, CancellationToken.None);
        var first = r.Diagrams.Where(d => d.Page == page).OrderBy(d => d.X).ThenBy(d => d.Y).First();
        Assert.Equal(board, first.Fen.Split(' ')[0]);
    }
}
