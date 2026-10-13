using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

public class EbookCoverRendererTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    private EbookCoverRenderer Renderer => new(_kit.Storage, Microsoft.Extensions.Options.Options.Create(_kit.Options), NullLogger<EbookCoverRenderer>.Instance);

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Jpg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private static byte[] Zip(params (string Name, byte[] Bytes)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, bytes) in entries)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(bytes);
            }
        return ms.ToArray();
    }

    private async Task<Ebook> UploadedAsync(byte[] bytes, string name)
    {
        var user = await _kit.UserAsync("u" + Guid.NewGuid().ToString("N")[..6]);
        var dto = await _kit.UploadAsync(user.Id, true, bytes, name);
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstAsync(Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.Include(_kit.Db.Ebooks, b => b.Files), b => b.Id == dto.Id);
    }

    [Fact]
    public async Task Epub_CoverComesFromOpfManifest_Epub3PropertiesFirst()
    {
        var container = Encoding.UTF8.GetBytes("""<?xml version="1.0"?><container xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles></container>""");
        var opf = Encoding.UTF8.GetBytes("""<?xml version="1.0"?><package xmlns="http://www.idpf.org/2007/opf" version="3.0"><metadata><meta name="cover" content="old"/></metadata><manifest><item id="old" href="images/old.jpg" media-type="image/jpeg"/><item id="c" href="images/cover.png" media-type="image/png" properties="cover-image"/></manifest></package>""");
        var epub = Zip(("mimetype", Encoding.ASCII.GetBytes("application/epub+zip")), ("META-INF/container.xml", container),
            ("OEBPS/content.opf", opf), ("OEBPS/images/old.jpg", Jpg), ("OEBPS/images/cover.png", Png));
        var book = await UploadedAsync(epub, "b.epub");

        var r = await Renderer.RenderAsync(book, CancellationToken.None);

        Assert.True(r.Success, r.Error);
        Assert.Equal(Png, await File.ReadAllBytesAsync(_kit.Storage.CoverPath(book.Id)));
        Assert.Equal("image/png", RookHub.Api.Controllers.Ebooks.EbooksController.SniffImageType(_kit.Storage.CoverPath(book.Id)));
    }

    [Fact]
    public async Task Epub_FallsBackToMetaCover_ThenToNameThenFirstImage()
    {
        var container = Encoding.UTF8.GetBytes("""<?xml version="1.0"?><container xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="content.opf"/></rootfiles></container>""");
        var opf = Encoding.UTF8.GetBytes("""<?xml version="1.0"?><package xmlns="http://www.idpf.org/2007/opf" version="2.0"><metadata><meta name="cover" content="cv"/></metadata><manifest><item id="cv" href="img/titel.jpg" media-type="image/jpeg"/></manifest></package>""");
        var withMeta = Zip(("META-INF/container.xml", container), ("content.opf", opf), ("img/titel.jpg", Jpg), ("img/a.png", Png));
        var book = await UploadedAsync(withMeta, "meta.epub");
        Assert.True((await Renderer.RenderAsync(book, CancellationToken.None)).Success);
        Assert.Equal(Jpg, await File.ReadAllBytesAsync(_kit.Storage.CoverPath(book.Id)));

        var noManifest = Zip(("img/zz.png", Png), ("img/Cover-Front.jpg", Jpg));
        var book2 = await UploadedAsync(noManifest, "name.epub");
        Assert.True((await Renderer.RenderAsync(book2, CancellationToken.None)).Success);
        Assert.Equal(Jpg, await File.ReadAllBytesAsync(_kit.Storage.CoverPath(book2.Id)));

        var noImages = Zip(("text.html", Encoding.UTF8.GetBytes("<p>x</p>")));
        var book3 = await UploadedAsync(noImages, "none.epub");
        var r3 = await Renderer.RenderAsync(book3, CancellationToken.None);
        Assert.False(r3.Success);
    }

    [Fact]
    public async Task Cbz_TakesFirstImageAlphabetically()
    {
        var cbz = Zip(("p002.jpg", Jpg), ("p001.png", Png), ("info.txt", [1]));
        var book = await UploadedAsync(cbz, "comic.cbz");
        var r = await Renderer.RenderAsync(book, CancellationToken.None);
        Assert.True(r.Success, r.Error);
        Assert.Equal(Png, await File.ReadAllBytesAsync(_kit.Storage.CoverPath(book.Id)));
    }

    [Fact]
    public async Task Pdf_RendersWithPdftoppm_AndReadsPageCount_WhenToolsExist()
    {
        if (!ToolExists("pdftoppm") || !ToolExists("pdfinfo")) return;   // CI ohne poppler: nichts zu prüfen
        var book = await UploadedAsync(EbookTestKit.TinyPdf(), "tiny.pdf");
        var r = await Renderer.RenderAsync(book, CancellationToken.None);
        Assert.True(r.Success, r.Error);
        Assert.Equal(1, r.PageCount);
        Assert.Equal("image/jpeg", RookHub.Api.Controllers.Ebooks.EbooksController.SniffImageType(_kit.Storage.CoverPath(book.Id)));
    }

    [Fact]
    public async Task MissingTool_FailsSoftly_InsteadOfThrowing()
    {
        _kit.Options.PdftoppmPath = "/nonexistent/pdftoppm-" + Guid.NewGuid().ToString("N");
        var book = await UploadedAsync(EbookTestKit.TinyPdf(), "tiny.pdf");
        var r = await Renderer.RenderAsync(book, CancellationToken.None);
        Assert.False(r.Success);
        Assert.Contains("not available", r.Error);
        Assert.False(File.Exists(_kit.Storage.CoverPath(book.Id)));
    }

    [Fact]
    public void PickSource_PrefersPdf_AndParsePages()
    {
        var files = new[]
        {
            new EbookFile { Format = "djvu" }, new EbookFile { Format = "mobi" }, new EbookFile { Format = "epub" }, new EbookFile { Format = "pdf" },
        };
        Assert.Equal("pdf", EbookCoverRenderer.PickSource(files)!.Format);
        Assert.Equal("epub", EbookCoverRenderer.PickSource(files.Where(f => f.Format != "pdf"))!.Format);
        Assert.Null(EbookCoverRenderer.PickSource([new EbookFile { Format = "mobi" }]));
        Assert.Equal(312, EbookCoverRenderer.ParsePdfinfoPages("Title: x\nPages:          312\nEncrypted: no\n"));
        Assert.Null(EbookCoverRenderer.ParsePdfinfoPages("Syntax Error"));
    }

    private static bool ToolExists(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator)
            .Any(dir => dir.Length > 0 && File.Exists(Path.Combine(dir, name)));
}
