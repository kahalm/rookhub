using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using SkiaSharp;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>R3-009: Deckel der EPUB-Verarbeitung — Kapitel/Gesamt in <see cref="EbookEpubContent.Read"/>, Spine-Duplikate,
/// Pixeldeckel vor dem Dekodieren (<see cref="EbookBoardImage.DecodeGray(byte[], int)"/>), Versuchszähler im Worker.</summary>
public class EbookProcessingCapsTests : IDisposable
{
    private readonly EbookTestKit _kit = new();
    public void Dispose() => _kit.Dispose();

    // ── EPUB-Text ──────────────────────────────────────────────────────────────────────────────

    /// <summary>EPUB mit Spine aus <paramref name="spine"/> (idrefs), Kapiteln c1…cN; <paramref name="bigChapterBytes"/> &gt; 0
    /// hängt an c2 so viele Leerzeichen an (gestreamt geschrieben, packt sich auf ~1/1000).</summary>
    private static ZipArchive Epub(string[] spine, int chapters, long bigChapterBytes = 0)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string path, string text) { using var s = zip.CreateEntry(path).Open(); s.Write(Encoding.UTF8.GetBytes(text)); }
            Add("META-INF/container.xml", "<?xml version=\"1.0\"?><container xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">" +
                "<rootfiles><rootfile full-path=\"OPS/content.opf\"/></rootfiles></container>");
            var items = string.Concat(Enumerable.Range(1, chapters).Select(i => $"<item id=\"c{i}\" href=\"c{i}.xhtml\"/>"));
            var refs = string.Concat(spine.Select(id => $"<itemref idref=\"{id}\"/>"));
            Add("OPS/content.opf", $"<package><manifest>{items}</manifest><spine>{refs}</spine></package>");
            for (var i = 1; i <= chapters; i++)
            {
                using var s = zip.CreateEntry($"OPS/c{i}.xhtml", CompressionLevel.SmallestSize).Open();
                s.Write(Encoding.UTF8.GetBytes($"<html><body><p>Kapitel {i}</p>"));
                if (i == 2 && bigChapterBytes > 0)
                {
                    var spaces = new byte[1 << 20];
                    Array.Fill(spaces, (byte)' ');
                    for (long left = bigChapterBytes; left > 0; left -= spaces.Length) s.Write(spaces, 0, (int)Math.Min(left, spaces.Length));
                }
                s.Write(Encoding.UTF8.GetBytes("</body></html>"));
            }
        }
        ms.Position = 0;
        return new ZipArchive(ms, ZipArchiveMode.Read);
    }

    [Fact]
    public void Read_RepeatedSpineEntry_IsReadOnce_AndNumberingStays()
    {
        // 1 000-fach wiederholtes itemref: vorher 1 000 Mal gelesen und geparst.
        var spine = new[] { "c1" }.Concat(Enumerable.Repeat("c2", 1000)).Append("c3").ToArray();
        using var zip = Epub(spine, 3);
        var blocks = EbookEpubContent.Read(zip);
        Assert.Equal(["Kapitel 1", "Kapitel 2", "Kapitel 3"], blocks.Select(b => b.Text!).ToArray());
        Assert.Equal([1, 2, 1002], blocks.Select(b => b.Spine).ToArray());   // Spine-Nummer wie in der Spine (epub.js zählt gleich)
    }

    [Fact]
    public void Read_ChapterOverTheCap_IsSkipped_WithoutMaterializingIt()
    {
        using var zip = Epub(["c1", "c2", "c3"], 3, bigChapterBytes: 50L * 1024 * 1024);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var blocks = EbookEpubContent.Read(zip);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(["Kapitel 1", "Kapitel 3"], blocks.Select(b => b.Text!).ToArray());
        Assert.True(allocated < 8L * 1024 * 1024, $"Read hat {allocated / 1024 / 1024} MB angelegt");
    }

    [Fact]
    public void Read_StopsAtTheTotalCap()
    {
        using var zip = Epub(["c1", "c2", "c3"], 3, bigChapterBytes: 2L * 1024 * 1024);
        // Gesamtdeckel 1 MB: Kapitel 1 passt, Kapitel 2 (2 MB) sprengt den Rest, danach ist Schluss.
        var blocks = EbookEpubContent.Read(zip, maxTotalBytes: 1024 * 1024);
        Assert.Equal(["Kapitel 1"], blocks.Select(b => b.Text!).ToArray());
        // Ohne Deckel-Argument (Vorgabe 128 MB) kommen alle drei.
        using var zip2 = Epub(["c1", "c2", "c3"], 3, bigChapterBytes: 2L * 1024 * 1024);
        Assert.Equal(3, EbookEpubContent.Read(zip2).Count);
    }

    [Fact]
    public void ReadCapped_CountsTheRealBytes()
    {
        using var zip = Epub(["c1", "c2"], 2, bigChapterBytes: 100_000);
        var entry = zip.GetEntry("OPS/c2.xhtml")!;
        Assert.Null(EbookEpubContent.ReadCapped(entry, 50_000));
        Assert.True(EbookEpubContent.ReadCapped(zip.GetEntry("OPS/c2.xhtml")!, 200_000)!.Length > 100_000);
    }

    // ── Bilder ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>PNG-Kopf (Signatur, IHDR Graustufen 8 bit, ein winziges IDAT, IEND) mit beliebigen Maßen — wenige Bytes,
    /// voll dekodiert wären es Width × Height Bytes.</summary>
    internal static byte[] PngHeader(int width, int height)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        void Chunk(string type, byte[] data)
        {
            Span<byte> len = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
            ms.Write(len);
            var td = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            ms.Write(td);
            Span<byte> crc = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(td));
            ms.Write(crc);
        }
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; ihdr[9] = 0;   // 8 bit, Graustufen
        Chunk("IHDR", ihdr);
        using (var z = new MemoryStream())
        {
            using (var zl = new ZLibStream(z, CompressionLevel.SmallestSize, true)) zl.Write(new byte[width + 1]);   // eine Zeile
            Chunk("IDAT", z.ToArray());
        }
        Chunk("IEND", []);
        return ms.ToArray();
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }

    [Fact]
    public void PngHeader_IsReadableByTheCodec()
    {
        using var codec = SKCodec.Create(new SKMemoryStream(PngHeader(20_000, 20_000)));
        Assert.NotNull(codec);
        Assert.Equal(20_000, codec.Info.Width);
    }

    [Fact]
    public void DecodeGray_HugePixelCount_IsRejectedFromTheHeader()
    {
        // 20 000 × 20 000 px (400 MP) in ~ 100 Bytes: vorher voll dekodiert (SKBitmap.Decode), jetzt nur der Kopf gelesen.
        Assert.Null(EbookBoardImage.DecodeGray(PngHeader(20_000, 20_000)));
        // Über dem Dekodier-Deckel, aber PNG lässt sich nicht verkleinert dekodieren → ebenfalls abgelehnt.
        Assert.Null(EbookBoardImage.DecodeGray(PngHeader(2_000, 2_000), 1600, maxPixels: 10_000_000, maxDecodePixels: 1_000_000));
    }

    [Fact]
    public void DecodeGray_LargeJpeg_IsDecodedScaledDown_SmallOnesAsBefore()
    {
        using var bmp = new SKBitmap(2_000, 2_000);
        using (var canvas = new SKCanvas(bmp)) canvas.Clear(SKColors.Gray);
        using var jpeg = SKImage.FromBitmap(bmp).Encode(SKEncodedImageFormat.Jpeg, 80);
        var data = jpeg.ToArray();

        var scaled = EbookBoardImage.DecodeGray(data, 1600, maxPixels: 10_000_000, maxDecodePixels: 1_000_000);
        Assert.NotNull(scaled);
        Assert.True((long)scaled.GetLength(0) * scaled.GetLength(1) <= 1_000_000);

        var full = EbookBoardImage.DecodeGray(data);   // 4 MP: unter dem Deckel, wie bisher voll dekodiert und auf 1600 verkleinert
        Assert.NotNull(full);
        Assert.Equal(1600, full.GetLength(0));
    }

    // ── Versuchszähler ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Attempts_CountOnlyRunsWithoutOrderlyEnd()
    {
        var a = new EbookDiagramAttempts(_kit.Root);
        Assert.True(a.TryBegin(7)); a.End(7);
        Assert.Equal(0, a.Interrupted(7));
        // Drei harte Abbrüche (Marke bleibt stehen) → der vierte Versuch wird verweigert.
        Assert.True(a.TryBegin(7));
        Assert.True(a.TryBegin(7));
        Assert.True(a.TryBegin(7));
        Assert.Equal(3, a.Interrupted(7));
        Assert.False(a.TryBegin(7));
        Assert.Equal(0, a.Interrupted(8));
        Assert.True(new EbookDiagramAttempts(null).TryBegin(7));   // ohne Upload-Wurzel zählt nichts
    }

    private EbookDiagramWorker Worker()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_kit.Db);
        services.AddSingleton(_kit.Storage);
        services.AddSingleton(new EbookDiagramExtractor(NullLogger<EbookDiagramExtractor>.Instance));
        services.AddSingleton(new EbookEpubDiagramExtractor(NullLogger<EbookEpubDiagramExtractor>.Instance));
        var sp = services.BuildServiceProvider();
        return new EbookDiagramWorker(sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<EbookDiagramWorker>.Instance);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public async Task Worker_BookThatCrashedTheProcessThreeTimes_IsFailedInsteadOfRetaken(int interrupted, bool failed)
    {
        var owner = await _kit.UserAsync("owner");
        var dto = await _kit.UploadAsync(owner.Id, false, EbookTestKit.TinyPdf(), "a.pdf");
        var book = await _kit.Db.Ebooks.SingleAsync(b => b.Id == dto.Id);
        book.DiagramState = EbookDiagramState.Pending;
        await _kit.Db.SaveChangesAsync();
        var marker = Path.Combine(_kit.Root, "diagram-attempts", dto.Id.ToString());
        if (interrupted > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, interrupted.ToString());
        }

        await Worker().RunStepAsync(CancellationToken.None);

        var state = (await _kit.Db.Ebooks.AsNoTracking().SingleAsync(b => b.Id == dto.Id)).DiagramState;
        if (failed) Assert.Equal(EbookDiagramState.Failed, state);
        else Assert.NotEqual(EbookDiagramState.Failed, state);
        Assert.NotEqual(EbookDiagramState.Pending, state);
        Assert.False(File.Exists(marker));   // geordnetes Ende: Marke weg
    }
}
