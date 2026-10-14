using System.Globalization;
using System.Text;
using SkiaSharp;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Schreibt ein PDF aus JPEG-Seiten (0.775.4, Lesefassung übergroßer Scans): je Seite ein Bild-Objekt mit
/// <c>DCTDecode</c> — das JPEG wird unverändert eingebettet, nichts neu kodiert. Seiten werden nacheinander in den Strom
/// geschrieben (kein ganzes Buch im Speicher); Katalog, Seitenbaum und Querverweis kommen am Ende.
/// </summary>
public sealed class ImagePdfWriter : IDisposable
{
    private readonly Stream _out;
    private readonly List<long> _offsets = [0, 0, 0];   // Objekt 0 frei, 1 = Katalog, 2 = Seitenbaum (am Ende)
    private readonly List<int> _pages = [];
    private long _pos;

    public ImagePdfWriter(Stream output)
    {
        _out = output;
        WriteRaw("%PDF-1.4\n%âãÏÓ\n");
    }

    public int PageCount => _pages.Count;

    /// <summary>Eine Seite aus einem JPEG; Seitengröße aus Pixeln und <paramref name="dpi"/>.</summary>
    public void AddJpegPage(byte[] jpeg, int dpi)
    {
        using var codec = SKCodec.Create(new MemoryStream(jpeg)) ?? throw new InvalidDataException("kein lesbares JPEG");
        if (codec.EncodedFormat != SKEncodedImageFormat.Jpeg) throw new InvalidDataException("kein JPEG");
        int w = codec.Info.Width, h = codec.Info.Height;
        var gray = codec.Info.ColorType == SKColorType.Gray8;
        var image = NewObject();
        WriteRaw($"{image} 0 obj\n<</Type/XObject/Subtype/Image/Width {w}/Height {h}/ColorSpace/{(gray ? "DeviceGray" : "DeviceRGB")}/BitsPerComponent 8/Filter/DCTDecode/Length {jpeg.Length}>>\nstream\n");
        WriteBytes(jpeg);
        WriteRaw("\nendstream\nendobj\n");
        var pw = w * 72.0 / dpi; var ph = h * 72.0 / dpi;
        var draw = Encoding.ASCII.GetBytes($"q {F(pw)} 0 0 {F(ph)} 0 0 cm /Im0 Do Q");
        var content = NewObject();
        WriteRaw($"{content} 0 obj\n<</Length {draw.Length}>>\nstream\n");
        WriteBytes(draw);
        WriteRaw("\nendstream\nendobj\n");
        var page = NewObject();
        WriteRaw($"{page} 0 obj\n<</Type/Page/Parent 2 0 R/MediaBox[0 0 {F(pw)} {F(ph)}]/Resources<</XObject<</Im0 {image} 0 R>>>>/Contents {content} 0 R>>\nendobj\n");
        _pages.Add(page);
    }

    /// <summary>Seitenbaum, Katalog, Querverweis, Trailer.</summary>
    public void Finish()
    {
        if (_pages.Count == 0) throw new InvalidOperationException("PDF ohne Seiten");
        _offsets[2] = _pos;
        WriteRaw($"2 0 obj\n<</Type/Pages/Kids[{string.Join(' ', _pages.Select(p => $"{p} 0 R"))}]/Count {_pages.Count}>>\nendobj\n");
        _offsets[1] = _pos;
        WriteRaw("1 0 obj\n<</Type/Catalog/Pages 2 0 R>>\nendobj\n");
        var xref = _pos;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"xref\n0 {_offsets.Count}\n0000000000 65535 f \n");
        for (var i = 1; i < _offsets.Count; i++) sb.Append(CultureInfo.InvariantCulture, $"{_offsets[i]:D10} 00000 n \n");
        sb.Append(CultureInfo.InvariantCulture, $"trailer\n<</Size {_offsets.Count}/Root 1 0 R>>\nstartxref\n{xref}\n%%EOF\n");
        WriteRaw(sb.ToString());
        _out.Flush();
    }

    private int NewObject()
    {
        _offsets.Add(_pos);
        return _offsets.Count - 1;
    }

    private void WriteRaw(string s) => WriteBytes(Encoding.Latin1.GetBytes(s));

    private void WriteBytes(byte[] b)
    {
        _out.Write(b);
        _pos += b.Length;
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    public void Dispose() => _out.Dispose();
}
