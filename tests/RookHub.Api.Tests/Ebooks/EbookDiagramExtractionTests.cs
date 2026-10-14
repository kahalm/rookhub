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
    public void Detect_KnownFamilies(string font, string family) => Assert.Equal(family, ChessDiagramFonts.Detect(font)!.Name);

    [Theory]
    [InlineData("AGaramondPro-Regular")]
    [InlineData("HastingsDiagramA")]   // Belegung noch nicht verifiziert → bewusst unbekannt
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
        Assert.Equal("r6r/1pp3k1/1b6/p2P1p2/P1N1pn2/2P2PP1/BP5P/4RR1K b - - 0 1", byLabel["1"].Fen);
        Assert.Equal("rnb3kr/ppp4p/3b3B/3Pp2n/2BP4/3K1Rp1/PPP3q1/RN1Q4 w - - 0 1", byLabel["2"].Fen);
        Assert.Equal("rn2rk2/5pp1/p7/2pb1qN1/7Q/2P5/5PPP/1R3RK1 w - - 0 1", byLabel["1128"].Fen);
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
}
