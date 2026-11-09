using System.Text;
using RookHub.Api.Services.Ebooks;
using UglyToad.PdfPig.Outline;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>G2-005: tief verschachtelte PDF-Lesezeichen dürfen den Prozess nicht per Stapelüberlauf beenden. Vor dem Fix
/// brach PdfPig ab etwa 20 000 Ebenen auf einem Pool-Thread mit „Stack overflow.“ ab (Testprozess tot), danach gibt es
/// eine gekappte oder gar keine Gliederung.</summary>
public class EbookPdfOutlineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rh-outline-" + Guid.NewGuid().ToString("N"));
    public EbookPdfOutlineTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>Ein-Seiten-PDF, dessen Gliederung eine Kette aus <paramref name="depth"/> Ebenen ist (je ein Kind).</summary>
    private string DeepOutlinePdf(int depth)
    {
        using var ms = new MemoryStream();
        void W(string s) => ms.Write(Encoding.ASCII.GetBytes(s));
        var count = 4 + depth;
        var offsets = new long[count + 1];
        void Obj(int i, string body) { offsets[i] = ms.Position; W($"{i} 0 obj\n{body}\nendobj\n"); }
        W("%PDF-1.4\n");
        Obj(1, "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R >>");
        Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Obj(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>");
        Obj(4, "<< /Type /Outlines /First 5 0 R /Last 5 0 R /Count 1 >>");
        for (var k = 0; k < depth; k++)
        {
            var i = 5 + k;
            var kids = k < depth - 1 ? $" /First {i + 1} 0 R /Last {i + 1} 0 R /Count 1" : "";
            Obj(i, $"<< /Title (L{k}) /Parent {(k == 0 ? 4 : i - 1)} 0 R /Dest [3 0 R /Fit]{kids} >>");
        }
        var xref = ms.Position;
        W($"xref\n0 {count + 1}\n0000000000 65535 f \n");
        for (var i = 1; i <= count; i++) W($"{offsets[i]:D10} 00000 n \n");
        W($"trailer\n<< /Size {count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        var path = Path.Combine(_dir, $"deep-{depth}.pdf");
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    private static BookmarkNode Chain(int depth)
    {
        BookmarkNode? node = null;
        for (var level = depth - 1; level >= 0; level--)
            node = new ContainerBookmarkNode($"L{level}", level, node == null ? [] : [node]);
        return node!;
    }

    [Fact]
    public void Flatten_ChainDeeperThanCap_KeepsOnlyCapLevelsInOrder()
    {
        var items = EbookPdfOutline.Flatten([Chain(100_000)]);
        Assert.Equal(EbookPdfOutline.MaxDepth, items.Count);
        Assert.Equal(Enumerable.Range(0, EbookPdfOutline.MaxDepth).Select(i => $"L{i}"), items.Select(i => i.Node.Title));
        Assert.Equal(Enumerable.Range(1, EbookPdfOutline.MaxDepth), items.Select(i => i.Depth));
    }

    [Fact]
    public void Flatten_PreOrderAndNodeCap()
    {
        var tree = new ContainerBookmarkNode("A", 0, [new ContainerBookmarkNode("A1", 1, []), new ContainerBookmarkNode("A2", 1, [])]);
        var b = new ContainerBookmarkNode("B", 0, []);
        Assert.Equal(["A", "A1", "A2", "B"], EbookPdfOutline.Flatten([tree, b]).Select(i => i.Node.Title));
        Assert.Equal(["A", "A1"], EbookPdfOutline.Flatten([tree, b], maxNodes: 2).Select(i => i.Node.Title));
    }

    [Fact]
    public void FromPdf_OutlineDeeperThanCap_ChaptersCappedAtMaxDepth()
    {
        var entries = EbookChapterIndex.FromPdf(DeepOutlinePdf(200));
        Assert.Equal(EbookPdfOutline.MaxDepth, entries.Count);
        Assert.Equal("L0", entries[0].Label);
        Assert.All(entries, e => Assert.Equal(1, e.Position));
    }

    [Fact]
    public void FromPdf_ExtremelyDeepOutline_NoCrashNoChapters()
    {
        // 50 000 Ebenen: vor dem Fix Stapelüberlauf in PdfPig (Prozessende), jetzt verwirft die Vorprüfung die Gliederung.
        var entries = EbookChapterIndex.FromPdf(DeepOutlinePdf(50_000));
        Assert.Empty(entries);
    }

    [Fact]
    public void ReflowRead_ExtremelyDeepOutline_PagesWithoutOutline()
    {
        var doc = EbookReflowPdfReader.Read(DeepOutlinePdf(50_000), CancellationToken.None);
        Assert.Single(doc.Pages);
        Assert.Empty(doc.Outline);
    }

    [Fact]
    public void FromPdf_NormalOutline_Unchanged()
    {
        var entries = EbookChapterIndex.FromPdf(DeepOutlinePdf(3));
        Assert.Equal(["L0", "L1", "L2"], entries.Select(e => e.Label));
    }
}
