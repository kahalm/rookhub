using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

public class EbookDjvuConverterTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    private static bool Tool(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Any(d => d.Length > 0 && File.Exists(Path.Combine(d, name)));

    private EbookDjvuConverter Converter()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_kit.Db);
        services.AddSingleton<IOptions<EbookOptions>>(Options.Create(_kit.Options));
        var sp = services.BuildServiceProvider();
        return new EbookDjvuConverter(sp.GetRequiredService<IServiceScopeFactory>(), _kit.Storage, NullLogger<EbookDjvuConverter>.Instance);
    }

    [Fact]
    public void NeedsPdfConversion_OnlyDjvuWithoutPdf()
    {
        Assert.True(EbookFormats.NeedsPdfConversion([new EbookFile { Format = "djvu" }]));
        Assert.False(EbookFormats.NeedsPdfConversion([new EbookFile { Format = "djvu" }, new EbookFile { Format = "pdf" }]));
        Assert.False(EbookFormats.NeedsPdfConversion([new EbookFile { Format = "epub" }]));
    }

    [Fact]
    public void HeavyPdf_NeedsAReadingPdf_AndThePrimaryFileIsTheDerivedOne()
    {
        var heavy = new EbookFile { Format = "pdf", SizeBytes = 300_000_000 };
        Assert.True(EbookFormats.IsHeavyPdf(heavy, 68));                           // 4,4 MB je Seite
        Assert.False(EbookFormats.IsHeavyPdf(heavy, 400));                         // 0,75 MB je Seite
        Assert.False(EbookFormats.IsHeavyPdf(heavy, null));                        // Seitenzahl noch unbekannt
        Assert.True(EbookFormats.NeedsReadingPdf([heavy], 68));
        var derived = new EbookFile { Format = "pdf", Derived = true, SizeBytes = 10_000_000 };
        Assert.False(EbookFormats.NeedsReadingPdf([heavy, derived], 68));
        Assert.Same(derived, EbookFormats.Primary([heavy, derived]));
        Assert.Same(derived, EbookFormats.Primary([derived, heavy]));
        Assert.Same(derived, EbookFormats.ReadingPdf([heavy, derived]));
    }

    [Fact]
    public void ImagePdfWriter_WritesAReadablePdfFromJpegs()
    {
        static byte[] Jpeg(int w, int h)
        {
            using var bmp = new SkiaSharp.SKBitmap(w, h);
            bmp.Erase(SkiaSharp.SKColors.DarkGray);
            using var data = bmp.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80);
            return data.ToArray();
        }
        var path = Path.Combine(_kit.Root, "img.pdf");
        using (var w = new ImagePdfWriter(File.Create(path)))
        {
            w.AddJpegPage(Jpeg(300, 450), 150);
            w.AddJpegPage(Jpeg(450, 300), 150);
            w.Finish();
        }
        using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
        Assert.Equal(2, doc.NumberOfPages);
        Assert.Equal(144, doc.GetPage(1).Width, 1);    // 300 px bei 150 dpi = 2 Zoll = 144 pt
        Assert.Equal(216, doc.GetPage(1).Height, 1);
        Assert.Single(doc.GetPage(2).GetImages());
    }

    [Fact]
    public async Task HeavyPdf_GetsARasterizedReadingPdf_WhenToolsExist()
    {
        if (!Tool("pdftoppm")) return;   // CI ohne poppler
        var dir = Path.Combine(_kit.LibraryRoot, "Autor");
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, "Scan.pdf"), EbookTestKit.TinyPdf());
        var admin = await _kit.UserAsync("admin", admin: true);
        var book = new Ebook { Title = "Scan", OwnerUserId = admin.Id, Source = EbookSource.Library, ConversionState = EbookConversionState.Pending,
            CoverState = EbookCoverState.Done, DiagramState = EbookDiagramState.Unsupported, PageCount = 1 };
        // Größe aus der Datenbank entscheidet: so tun, als hätte die eine Seite 5 MB.
        book.Files.Add(new EbookFile { Format = "pdf", RelativePath = "Autor/Scan.pdf", FileName = "Scan.pdf", SizeBytes = 5_000_000 });
        _kit.Db.Ebooks.Add(book);
        await _kit.Db.SaveChangesAsync();

        Assert.True(await Converter().RunStepAsync(CancellationToken.None));

        var reloaded = await _kit.Db.Ebooks.Include(b => b.Files).SingleAsync();
        Assert.Equal(EbookConversionState.Done, reloaded.ConversionState);
        var derived = reloaded.Files.Single(f => f.Derived);
        Assert.Same(derived, EbookFormats.Primary(reloaded.Files));
        using var doc = UglyToad.PdfPig.PdfDocument.Open(_kit.Storage.ResolveFile(reloaded, derived));
        Assert.Equal(1, doc.NumberOfPages);
    }

    /// <summary>0.872.0: liefert das Rendern ein kaputtes Seitenbild, wird das Buch „fehlgeschlagen“ und der nächste Durchlauf
    /// nimmt das nächste Buch — vorher blieb es „ausstehend“ und blockierte die Warteschlange (Endlosschleife, OOM 29.10.).</summary>
    [Fact]
    public async Task BrokenPageImage_MarksTheBookFailed_AndTheQueueMovesOn()
    {
        if (OperatingSystem.IsWindows()) return;
        // Ersatz für pdftoppm: legt eine „JPEG“-Datei ohne Bilddaten ab und meldet Erfolg.
        var fake = Path.Combine(_kit.Root, "fake-pdftoppm.sh");
        await File.WriteAllTextAsync(fake, "#!/bin/sh\nfor a; do last=$a; done\nprintf 'kaputt' > \"$last-01.jpg\"\n");
        File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _kit.Options.PdftoppmPath = fake;
        var dir = Path.Combine(_kit.LibraryRoot, "Autor");
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, "Riesen.pdf"), EbookTestKit.TinyPdf());
        var admin = await _kit.UserAsync("admin", admin: true);
        Ebook Heavy(string title) => new()
        {
            Title = title, OwnerUserId = admin.Id, Source = EbookSource.Library, ConversionState = EbookConversionState.Pending,
            CoverState = EbookCoverState.Done, DiagramState = EbookDiagramState.Unsupported, PageCount = 1,
            Files = { new EbookFile { Format = "pdf", RelativePath = "Autor/Riesen.pdf", FileName = "Riesen.pdf", SizeBytes = 5_000_000 } },
        };
        var first = Heavy("Riesen");
        var second = Heavy("Danach");
        _kit.Db.Ebooks.AddRange(first, second);
        await _kit.Db.SaveChangesAsync();

        await Converter().RunStepAsync(CancellationToken.None);
        await Converter().RunStepAsync(CancellationToken.None);

        Assert.Equal(EbookConversionState.Failed, (await _kit.Db.Ebooks.SingleAsync(b => b.Id == first.Id)).ConversionState);
        Assert.Equal(EbookConversionState.Failed, (await _kit.Db.Ebooks.SingleAsync(b => b.Id == second.Id)).ConversionState);
        Assert.Empty(Directory.GetFiles(_kit.Root, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public void DerivedFiles_LiveUnderTheWritableRoot_EvenForLibraryBooks()
    {
        var book = new Ebook { Source = EbookSource.Library };
        var path = _kit.Storage.ResolveFile(book, new EbookFile { RelativePath = "converted/7.pdf", Derived = true });
        Assert.StartsWith(_kit.Root, path);
        var lib = _kit.Storage.ResolveFile(book, new EbookFile { RelativePath = "A/b.djvu" });
        Assert.StartsWith(_kit.LibraryRoot, lib);
    }

    [Fact]
    public async Task Converts_LibraryDjvu_ToDerivedPdf_AndQueuesCoverAndDiagrams()
    {
        if (!Tool("c44") || !Tool("ddjvu")) return;   // CI ohne djvulibre: nichts zu prüfen
        // Eine kleine DJVU-Seite aus einem Graustufenbild bauen (c44).
        var dir = Path.Combine(_kit.LibraryRoot, "Autor");
        Directory.CreateDirectory(dir);
        var pgm = Path.Combine(dir, "seite.pgm");
        var header = System.Text.Encoding.ASCII.GetBytes("P5 64 64 255\n");
        await File.WriteAllBytesAsync(pgm, header.Concat(Enumerable.Range(0, 64 * 64).Select(i => (byte)(i % 255))).ToArray());
        var djvu = Path.Combine(dir, "Buch.djvu");
        using (var p = Process.Start(new ProcessStartInfo("c44", $"\"{pgm}\" \"{djvu}\"") { RedirectStandardError = true }))
            await p!.WaitForExitAsync();
        Assert.True(File.Exists(djvu));

        var admin = await _kit.UserAsync("admin", admin: true);
        var book = new Ebook { Title = "Buch", OwnerUserId = admin.Id, Source = EbookSource.Library, ConversionState = EbookConversionState.Pending,
            CoverState = EbookCoverState.Failed, DiagramState = EbookDiagramState.Unsupported };
        book.Files.Add(new EbookFile { Format = "djvu", RelativePath = "Autor/Buch.djvu", FileName = "Buch.djvu", SizeBytes = 1 });
        _kit.Db.Ebooks.Add(book);
        await _kit.Db.SaveChangesAsync();

        Assert.True(await Converter().RunStepAsync(CancellationToken.None));

        var reloaded = await _kit.Db.Ebooks.Include(b => b.Files).SingleAsync();
        Assert.Equal(EbookConversionState.Done, reloaded.ConversionState);
        var pdf = reloaded.Files.Single(f => f.Format == "pdf");
        Assert.True(pdf.Derived);
        Assert.Equal("converted/" + book.Id + ".pdf", pdf.RelativePath);
        Assert.True(File.Exists(_kit.Storage.ResolveFile(reloaded, pdf)));
        Assert.Equal(EbookCoverState.Pending, reloaded.CoverState);
        Assert.Equal(EbookDiagramState.Pending, reloaded.DiagramState);
        Assert.Equal("pdf", EbookFormats.Primary(reloaded.Files)!.Format);

        // Löschen nimmt die erzeugte PDF mit, die Bibliotheksdatei bleibt.
        await _kit.Service.DeleteAsync(admin.Id, true, book.Id);
        Assert.False(File.Exists(Path.Combine(_kit.Root, "converted", book.Id + ".pdf")));
        Assert.True(File.Exists(djvu));
    }

    [Fact]
    public async Task ExistingDjvuReadingPdf_IsRegeneratedInPlace_WithoutTouchingCoverOrDiagrams()
    {
        if (!Tool("c44") || !Tool("ddjvu")) return;   // CI ohne djvulibre
        var dir = Path.Combine(_kit.LibraryRoot, "Autor");
        Directory.CreateDirectory(dir);
        var pgm = Path.Combine(dir, "seite.pgm");
        await File.WriteAllBytesAsync(pgm, System.Text.Encoding.ASCII.GetBytes("P5 64 64 255\n").Concat(Enumerable.Repeat((byte)128, 64 * 64)).ToArray());
        using (var p = Process.Start(new ProcessStartInfo("c44", $"\"{pgm}\" \"{Path.Combine(dir, "Buch.djvu")}\"") { RedirectStandardError = true }))
            await p!.WaitForExitAsync();

        var admin = await _kit.UserAsync("admin", admin: true);
        var book = new Ebook { Title = "Buch", OwnerUserId = admin.Id, Source = EbookSource.Library, ConversionState = EbookConversionState.Pending,
            CoverState = EbookCoverState.Done, DiagramState = EbookDiagramState.Done, PageCount = 1 };
        book.Files.Add(new EbookFile { Format = "djvu", RelativePath = "Autor/Buch.djvu", FileName = "Buch.djvu", SizeBytes = 1 });
        _kit.Db.Ebooks.Add(book);
        await _kit.Db.SaveChangesAsync();
        // alte, übergroße Lesefassung wie vor 0.837.5
        var old = Path.Combine(_kit.Root, "converted", book.Id + ".pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        await File.WriteAllBytesAsync(old, new byte[500_000]);
        book.Files.Add(new EbookFile { Format = "pdf", RelativePath = "converted/" + book.Id + ".pdf", FileName = "Buch.pdf", Derived = true, SizeBytes = 500_000 });
        await _kit.Db.SaveChangesAsync();

        Assert.True(await Converter().RunStepAsync(CancellationToken.None));

        var reloaded = await _kit.Db.Ebooks.Include(b => b.Files).SingleAsync();
        Assert.Equal(EbookConversionState.Done, reloaded.ConversionState);
        var pdf = Assert.Single(reloaded.Files, f => f.Format == "pdf");   // keine zweite Lesefassung
        Assert.Equal(new FileInfo(old).Length, pdf.SizeBytes);
        Assert.True(pdf.SizeBytes < 500_000);
        Assert.Equal(EbookCoverState.Done, reloaded.CoverState);
        Assert.Equal(EbookDiagramState.Done, reloaded.DiagramState);
        using var doc = UglyToad.PdfPig.PdfDocument.Open(old);
        Assert.Equal(1, doc.NumberOfPages);
    }

    [Fact]
    public async Task Upload_OfDjvu_IsQueuedForConversion()
    {
        var user = await _kit.UserAsync("u");
        await _kit.UploadAsync(user.Id, false, [0x41, 0x54, 0x26, 0x54], "x.djvu");
        Assert.Equal(EbookConversionState.Pending, (await _kit.Db.Ebooks.SingleAsync()).ConversionState);
    }
}
