using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

public class EbookScanDiagramTests
{
    private static float[,] Page(int h, int w)
    {
        var g = new float[h, w];
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) g[y, x] = 1f;
        return g;
    }

    /// <summary>Brett mit schraffierten dunklen Feldern (Diagonalen), Feld <paramref name="cell"/> px, optional ohne linke Rahmenlinie.</summary>
    private static void DrawBoard(float[,] g, int x0, int y0, int cell, bool leftFrame = true, int squares = 8)
    {
        var size = cell * squares;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dark = (y / cell + x / cell) % 2 == 1;
                // Ohne linken Rahmen beginnt die Schraffur der a-Linie wie im Scan erst ein Stück im Feld.
                if (!leftFrame && x < cell / 2) continue;
                if (dark && (x + y) % 4 == 0) g[y0 + y, x0 + x] = 0f;
            }
        for (var i = -1; i <= size; i++)
        {
            var fx = leftFrame ? Math.Max(0, i) : Math.Max(cell / 2, i);
            g[y0 - 1, x0 + fx] = 0f; g[y0 + size, x0 + fx] = 0f;
            g[y0 + Math.Max(0, i), x0 + size] = 0f;
            if (leftFrame) g[y0 + Math.Max(0, i), x0 - 1] = 0f;
        }
    }

    [Fact]
    public void FindBoards_FindsEveryBoardOnThePage_AndCompletesAMissingFrame()
    {
        var g = Page(900, 700);
        DrawBoard(g, 60, 60, 30);
        DrawBoard(g, 360, 500, 30, leftFrame: false);   // linke Spalte a: helle Felder hängen nicht an der Schraffur
        var boxes = EbookScanDiagramFinder(g);
        Assert.Equal(2, boxes.Count);
        Assert.InRange(boxes[0].X1 - boxes[0].X0, 238, 242);
        var b = boxes[1];
        Assert.InRange(b.X0, 358, 362);
        Assert.InRange(b.X1 - b.X0, 236, 244);
        Assert.All(boxes, x => Assert.True(EbookBoardImage.GridScore(EbookBoardImage.Cells(g, x)) >= EbookScanDiagramExtractor.MinGridScore));
    }

    private static List<(int X0, int Y0, int X1, int Y1)> EbookScanDiagramFinder(float[,] g) => EbookBoardImage.FindBoards(g);

    [Fact]
    public void GridScore_RejectsAThreeByThreeCutout()
    {
        var g = Page(400, 400);
        DrawBoard(g, 50, 50, 100, squares: 3);   // 300 px groß, aber nur 3 × 3 Felder
        var boxes = EbookBoardImage.FindBoards(g);
        Assert.Single(boxes);
        Assert.True(EbookBoardImage.GridScore(EbookBoardImage.Cells(g, boxes[0])) < EbookScanDiagramExtractor.MinGridScore);
    }

    [Fact]
    public void SideMarker_HolesMeanB_WideWithoutHolesMeansW()
    {
        // „B“: Rechteck 10 × 14 mit zwei weißen Innenräumen.
        var g = Page(400, 400);
        for (var y = 0; y < 14; y++) for (var x = 0; x < 10; x++) g[130 + y, 70 + x] = 0f;
        for (var y = 2; y < 6; y++) for (var x = 3; x < 7; x++) g[130 + y, 70 + x] = 1f;
        for (var y = 8; y < 12; y++) for (var x = 3; x < 7; x++) g[130 + y, 70 + x] = 1f;
        Assert.Equal('b', EbookBoardImage.SideMarker(g, (120, 100, 360, 340)));
        // „W“: zwei schräge Striche mit Lücke dazwischen, zusammen breit (16 × 12), ohne Innenraum.
        g = Page(400, 400);
        for (var y = 0; y < 12; y++) { g[130 + y, 70 + y / 2] = 0f; g[130 + y, 72 + y / 2] = 0f; g[130 + y, 85 - y / 2] = 0f; }
        Assert.Equal('w', EbookBoardImage.SideMarker(g, (120, 100, 360, 340)));
    }

    [Fact]
    public void SideMarker_TriangleAboveRight_HollowWhite_FilledBlack()
    {
        // Brett 120…360; Dreieck (Spitze oben, 14 px hoch) rechts über der oberen Kante.
        static float[,] Tri(bool filled)
        {
            var g = Page(400, 400);
            for (var y = 0; y < 14; y++)
                for (var x = -y / 2; x <= y / 2; x++)
                    if (filled || y == 13 || x == -y / 2 || x == y / 2) g[80 + y, 345 + x] = 0f;
            return g;
        }
        Assert.Equal('w', EbookBoardImage.SideMarker(Tri(filled: false), (120, 100, 360, 340)));
        Assert.Equal('b', EbookBoardImage.SideMarker(Tri(filled: true), (120, 100, 360, 340)));
    }

    [Fact]
    public void SideMarker_SquareBelowLeft_HollowWhite_FilledBlack()
    {
        // Brett 120…360 × 100…340; Kästchen 8 × 8 links neben der Unterkante (ragt über sie hinaus).
        static float[,] Sq(bool filled)
        {
            var g = Page(400, 400);
            for (var y = 0; y < 8; y++)
                for (var x = 0; x < 8; x++)
                    if (filled || y == 0 || y == 7 || x == 0 || x == 7) g[336 + y, 108 + x] = 0f;
            return g;
        }
        Assert.Equal('w', EbookBoardImage.SideMarker(Sq(filled: false), (120, 100, 360, 340)));
        Assert.Equal('b', EbookBoardImage.SideMarker(Sq(filled: true), (120, 100, 360, 340)));
    }

    [Fact]
    public void SideMarker_RankDigitsLeftOfTheBoard_AreNoMarker()
    {
        // Acht „8“-artige Ziffern mit Innenraum untereinander = Koordinaten, nicht „B“.
        var g = Page(400, 400);
        for (var r = 0; r < 8; r++)
        {
            int top = 105 + r * 30;
            for (var y = 0; y < 12; y++) for (var x = 0; x < 8; x++) g[top + y, 90 + x] = 0f;
            for (var y = 2; y < 5; y++) for (var x = 2; x < 6; x++) g[top + y, 90 + x] = 1f;
        }
        Assert.Null(EbookBoardImage.SideMarker(g, (120, 100, 360, 340)));
    }

    [Fact]
    public void RenderDpi_CapsGiantPages_ParsesPdfinfoSizes()
    {
        var sizes = EbookScanDiagramExtractor.ParsePageSizes("Page    1 size: 595.276 x 841.89 pts (A4)\nPage    3 size: 4419 x 3272.25 pts\n");
        Assert.Equal(2, sizes.Count);
        Assert.Equal(300, EbookScanDiagramExtractor.RenderDpi(sizes[1]));            // A4: normal
        var dpi = EbookScanDiagramExtractor.RenderDpi(sizes[3]);                      // 61 × 45 Zoll: gedrosselt
        Assert.True(dpi < 300 && 4419 / 72.0 * dpi <= EbookScanDiagramExtractor.MaxPagePixels);
        Assert.Equal(300, EbookScanDiagramExtractor.RenderDpi(default));               // unbekannt: wie bisher
    }

    [Fact]
    public void SideMarker_NothingLeftOfTheBoard_IsNull() => Assert.Null(EbookBoardImage.SideMarker(Page(400, 400), (120, 100, 360, 340)));

    [Fact]
    public void Choose_PerBookModel_WithFallbackPerBoard()
    {
        EbookScanDiagramExtractor.ScanCandidate C(string? a, string? b) => new(1, 0, 0, 0.3, 0.3, a, b, null);
        // Modell 2 liest mehr Bretter → es gilt fürs Buch; wo es nichts liest, springt Modell 1 ein.
        var boards = EbookScanDiagramExtractor.Choose([C("A1", "B1"), C(null, "B2"), C(null, "B3"), C("A4", null)], out var alt);
        Assert.True(alt);
        Assert.Equal(["B1", "B2", "B3", "A4"], boards.Select(b => b.Board).ToArray());
        boards = EbookScanDiagramExtractor.Choose([C("A1", "B1"), C("A2", null)], out alt);
        Assert.False(alt);
        Assert.Equal(["A1", "A2"], boards.Select(b => b.Board).ToArray());
    }

    private static char[] Grid(string boardFen)
    {
        var g = new List<char>();
        foreach (var ch in boardFen.Replace("/", "")) g.AddRange(char.IsDigit(ch) ? Enumerable.Repeat('.', ch - '0') : [ch]);
        return g.ToArray();
    }

    [Fact]
    public void PossibleMaterial_RejectsImpossibleArmies()
    {
        Assert.True(EbookBoardImage.PossibleMaterial(Grid("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR")));
        Assert.True(EbookBoardImage.PossibleMaterial(Grid("QQQQk3/8/8/8/8/8/PPPPP3/4K3")));   // 3 Umwandlungen bei 5 Bauern
        Assert.False(EbookBoardImage.PossibleMaterial(Grid("NNNNNk2/RRRR4/8/8/8/8/PPPPPP2/4K3")));   // 5 S + 4 T + 6 B
        Assert.False(EbookBoardImage.PossibleMaterial(Grid("4k3/pppppppp/p7/8/8/8/8/4K3")));   // 9 Bauern
    }

    [Fact]
    public void Overlap_RelativeToTheSmallerBox()
    {
        Assert.Equal(1.0, EbookBoardImage.Overlap((102, 409, 461, 768), (110, 421, 453, 764)), 3);   // Rahmen außen + Brett innen
        Assert.Equal(0.0, EbookBoardImage.Overlap((0, 0, 100, 100), (200, 0, 300, 100)));
    }

    [Fact]
    public void ReadPgm_ParsesBinaryGraymap_WithComment()
    {
        var head = System.Text.Encoding.ASCII.GetBytes("P5\n# pdftoppm\n3 2\n255\n");
        var g = EbookScanDiagramExtractor.ReadPgm([.. head, 0, 255, 51, 102, 204, 255])!;
        Assert.Equal(2, g.GetLength(0));
        Assert.Equal(3, g.GetLength(1));
        Assert.Equal(0f, g[0, 0]);
        Assert.Equal(1f, g[0, 1], 5);
        Assert.Equal(0.2f, g[0, 2], 5);
        Assert.Equal(0.8f, g[1, 1], 5);
        Assert.Null(EbookScanDiagramExtractor.ReadPgm(System.Text.Encoding.ASCII.GetBytes("P6 1 1 255\n...")));
    }

    [Theory]
    [InlineData("/tmp/x/p-007.pgm", 7)]
    [InlineData("p-123.png", 123)]
    [InlineData("cover.png", 0)]
    public void PageOf_ParsesPdftoppmNames(string file, int page) => Assert.Equal(page, EbookScanDiagramExtractor.PageOf(file));
}
