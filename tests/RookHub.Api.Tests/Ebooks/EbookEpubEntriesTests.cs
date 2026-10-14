using System.IO.Compression;
using System.Text;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

public class EbookEpubEntriesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rh-epub-entries-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* egal */ } }

    /// <summary>Ein kleines EPUB-ZIP (Container, OPF, Kapitel, Bild, Ordner-Eintrag); <paramref name="extra"/> = weitere Einträge.</summary>
    internal static byte[] TinyEpub(params (string Path, string Text)[] extra)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string path, byte[] bytes) { using var s = zip.CreateEntry(path).Open(); s.Write(bytes); }
            Add("mimetype", Encoding.ASCII.GetBytes("application/epub+zip"));
            Add("META-INF/container.xml", Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">" +
                "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>"));
            Add("OEBPS/content.opf", Encoding.UTF8.GetBytes("<package/>"));
            Add("OEBPS/Text/ch1.xhtml", Encoding.UTF8.GetBytes("<html><body><img src=\"../Images/Board 1.png\"/></body></html>"));
            Add("OEBPS/Images/Board 1.png", [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);
            zip.CreateEntry("OEBPS/Empty/");
            foreach (var (p, t) in extra) Add(p, Encoding.UTF8.GetBytes(t));
        }
        return ms.ToArray();
    }

    private string Write(byte[] bytes)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".epub");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public async Task ReadAsync_FindsEntries_WithContentTypeByExtension()
    {
        var file = Write(TinyEpub());
        var container = await EbookEpubEntries.ReadAsync(file, "META-INF/container.xml");
        Assert.NotNull(container);
        Assert.Equal("application/xml", container.ContentType);
        Assert.Contains("OEBPS/content.opf", Encoding.UTF8.GetString(container.Bytes));

        Assert.Equal("application/xhtml+xml", (await EbookEpubEntries.ReadAsync(file, "OEBPS/Text/ch1.xhtml"))!.ContentType);
        Assert.Equal("application/oebps-package+xml", (await EbookEpubEntries.ReadAsync(file, "OEBPS/content.opf"))!.ContentType);
        var img = await EbookEpubEntries.ReadAsync(file, "OEBPS/Images/Board 1.png");   // Leerzeichen: Route liefert es entschlüsselt
        Assert.Equal("image/png", img!.ContentType);
        Assert.Equal(7, img.Bytes.Length);
        // „./“ und doppelte Schrägstriche sind harmlos.
        Assert.NotNull(await EbookEpubEntries.ReadAsync(file, "OEBPS/./Text//ch1.xhtml"));
    }

    [Fact]
    public async Task ReadAsync_MissingDirectoryWrongCaseOrNotAZip_IsNull()
    {
        var file = Write(TinyEpub());
        Assert.Null(await EbookEpubEntries.ReadAsync(file, "OEBPS/Text/ch2.xhtml"));
        Assert.Null(await EbookEpubEntries.ReadAsync(file, "oebps/text/CH1.xhtml"));   // Schreibweise wie im ZIP
        Assert.Null(await EbookEpubEntries.ReadAsync(file, "OEBPS/Empty/"));
        Assert.Null(await EbookEpubEntries.ReadAsync(file, ""));
        Assert.Null(await EbookEpubEntries.ReadAsync(file, null));
        Assert.Null(await EbookEpubEntries.ReadAsync(Write(Encoding.ASCII.GetBytes("kein zip")), "META-INF/container.xml"));
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("OEBPS/../../etc/passwd")]
    [InlineData("OEBPS/../META-INF/container.xml")]
    [InlineData("/etc/passwd")]
    [InlineData("OEBPS\\Text\\ch1.xhtml")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("OEBPS/Text/ch1.xhtml\0")]
    public async Task ReadAsync_RejectsTraversalAbsoluteAndOddPaths(string path)
    {
        // Auch ein Eintrag, der im ZIP selbst „../“ trägt, ist nicht erreichbar.
        var file = Write(TinyEpub(("../secret.txt", "geheim")));
        Assert.Null(EbookEpubEntries.NormalizePath(path));
        Assert.Null(await EbookEpubEntries.ReadAsync(file, path));
    }

    [Theory]
    [InlineData("a/b.XHTML", "application/xhtml+xml")]
    [InlineData("a/b.html", "text/html; charset=utf-8")]
    [InlineData("s.css", "text/css; charset=utf-8")]
    [InlineData("toc.ncx", "application/x-dtbncx+xml")]
    [InlineData("i.JPG", "image/jpeg")]
    [InlineData("i.jpeg", "image/jpeg")]
    [InlineData("i.gif", "image/gif")]
    [InlineData("i.svg", "image/svg+xml")]
    [InlineData("f.woff", "font/woff")]
    [InlineData("f.woff2", "font/woff2")]
    [InlineData("f.otf", "font/otf")]
    [InlineData("f.ttf", "font/ttf")]
    [InlineData("mimetype", "application/octet-stream")]
    [InlineData("dir.v2/file", "application/octet-stream")]
    public void ContentType_ByExtension(string path, string expected) => Assert.Equal(expected, EbookEpubEntries.ContentType(path));
}
