using System.IO.Compression;
using System.Text;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

public class EbookChapterIndexTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rh-chapters-" + Guid.NewGuid().ToString("N"));
    public EbookChapterIndexTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string Epub(params (string Name, string Text)[] files)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".epub");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in files)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
            w.Write(text);
        }
        return path;
    }

    private const string Container = """<container><rootfiles><rootfile full-path="OEBPS/content.opf"/></rootfiles></container>""";

    [Fact]
    public void Pick_LastEntryAtOrBefore_TopLevelWhenItHasSeveral()
    {
        var entries = new List<EbookChapterIndex.Entry> { new(3, 0, "Kapitel 1"), new(5, 1, "1.1"), new(10, 0, "Kapitel 2") };
        Assert.Null(EbookChapterIndex.Pick(entries, 2));
        Assert.Equal("Kapitel 1", EbookChapterIndex.Pick(entries, 7));      // 1.1 ist Unterebene
        Assert.Equal("Kapitel 2", EbookChapterIndex.Pick(entries, 10));
        // Nur ein Eintrag oben (Buchtitel) → die Ebene darunter gliedert.
        var single = new List<EbookChapterIndex.Entry> { new(1, 0, "Buch"), new(2, 1, "Teil A"), new(8, 1, "Teil B") };
        Assert.Equal("Teil B", EbookChapterIndex.Pick(single, 9));
    }

    [Fact]
    public void FromEpub_NcxWithEncodedNames_MapsToSpineChapters()
    {
        var path = Epub(("META-INF/container.xml", Container),
            ("OEBPS/content.opf", """
                <package><manifest>
                  <item id="t" href="titlepage.xhtml" media-type="application/xhtml+xml"/>
                  <item id="c1" href="5_Chapter 1 (1)_converted.html" media-type="application/xhtml+xml"/>
                  <item id="c2" href="6_Chapter 1 (2)_converted.html" media-type="application/xhtml+xml"/>
                  <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
                </manifest><spine toc="ncx"><itemref idref="t"/><itemref idref="c1"/><itemref idref="c2"/></spine></package>
                """),
            ("OEBPS/toc.ncx", """
                <ncx><navMap>
                  <navPoint id="a"><navLabel><text>Title page</text></navLabel><content src="titlepage.xhtml"/></navPoint>
                  <navPoint id="b"><navLabel><text>Exercises 1-24</text></navLabel><content src="5_Chapter%201%20%281%29_converted.html"/></navPoint>
                  <navPoint id="c"><navLabel><text>Exercises 25-48</text></navLabel><content src="6_Chapter%201%20%282%29_converted.html#x"/></navPoint>
                </navMap></ncx>
                """));
        var entries = EbookChapterIndex.FromEpub(path);
        Assert.Equal(["Title page", "Exercises 1-24", "Exercises 25-48"], entries.Select(e => e.Label));
        Assert.Equal([1, 2, 3], entries.Select(e => e.Position));
        Assert.Equal("Exercises 25-48", EbookChapterIndex.Pick(entries, 3));
    }

    [Fact]
    public void FromEpub_Nav_NestedListsGiveLevels()
    {
        var path = Epub(("META-INF/container.xml", Container),
            ("OEBPS/content.opf", """
                <package><manifest>
                  <item id="nav" href="Text/nav.xhtml" properties="nav" media-type="application/xhtml+xml"/>
                  <item id="a" href="Text/a.xhtml" media-type="application/xhtml+xml"/>
                  <item id="b" href="Text/b.xhtml" media-type="application/xhtml+xml"/>
                </manifest><spine><itemref idref="a"/><itemref idref="b"/></spine></package>
                """),
            ("OEBPS/Text/nav.xhtml", """
                <html><body><nav epub:type="toc"><ol>
                  <li><a href="a.xhtml">Teil &amp; Eins</a><ol><li><a href="a.xhtml#s1">Abschnitt</a></li></ol></li>
                  <li><a href="b.xhtml"><span>Teil Zwei</span></a></li>
                </ol></nav></body></html>
                """));
        var entries = EbookChapterIndex.FromEpub(path);
        Assert.Equal([("Teil & Eins", 0), ("Abschnitt", 1), ("Teil Zwei", 0)], entries.Select(e => (e.Label, e.Level)));
        Assert.Equal("Teil Zwei", EbookChapterIndex.Pick(entries, 2));
    }

    [Fact]
    public void BuildPgn_WritesTheChapterIntoBlack()
    {
        var d = new EbookDiagram { Id = 7, Page = 3, Fen = "4k3/8/8/8/8/8/8/R3K3 w - - 0 1", SolutionSan = "Ra8+", FontFamily = "opus" };
        var r = EbookCourseBuilder.BuildPgn("Buch", [d], new Dictionary<int, string> { [7] = "Kapitel \"Matt\"" });
        Assert.Contains("[Black \"Kapitel \\\"Matt\\\"\"]", r.Text);
    }
}
