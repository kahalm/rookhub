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
    public async Task Upload_OfDjvu_IsQueuedForConversion()
    {
        var user = await _kit.UserAsync("u");
        await _kit.UploadAsync(user.Id, false, [0x41, 0x54, 0x26, 0x54], "x.djvu");
        Assert.Equal(EbookConversionState.Pending, (await _kit.Db.Ebooks.SingleAsync()).ConversionState);
    }
}
