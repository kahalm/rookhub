using System.IO.Compression;
using System.Text;

namespace RookHub.Api.Services;

/// <summary>
/// Rümpfe der Import-Wege lesen, bei <c>Content-Encoding: gzip</c> entpackt und begrenzt (<see cref="LimitedReadStream"/>) —
/// EINE Stelle für Liga-Admin-Importe (<c>LeagueController.AdminBody</c>) und Spielervorbereitung (<c>PrepController</c>),
/// vorher zwei Umsetzungen derselben Grenze (R2-006). <c>RequestSizeLimit</c> zählt nur die komprimierten Bytes.
/// </summary>
public static class RequestBodies
{
    /// <summary>Ist der Rumpf gzip-gepackt?</summary>
    public static bool IsGzip(HttpRequest request) =>
        request.Headers.ContentEncoding.ToString().Contains("gzip", StringComparison.OrdinalIgnoreCase);

    /// <summary>Der Rumpf, gzip entpackt und dann höchstens <paramref name="limit"/> Bytes (darüber wirft das Lesen
    /// <see cref="LimitedReadStream.LimitExceededException"/>); ungepackt der Rumpf selbst, oder mit
    /// <paramref name="limitPlain"/> auch der begrenzt.</summary>
    public static Stream Unpacked(HttpRequest request, long limit, bool limitPlain = false) =>
        IsGzip(request) ? new LimitedReadStream(new GZipStream(request.Body, CompressionMode.Decompress), limit)
            : limitPlain ? new LimitedReadStream(request.Body, limit) : request.Body;

    /// <summary>Den ganzen Rumpf als UTF-8-Text (Bytes wie gesendet, ohne BOM-Erkennung) — gepackt oder nicht höchstens
    /// <paramref name="limit"/> Bytes; <c>null</c>, wenn er größer ist.</summary>
    public static async Task<string?> ReadTextAsync(HttpRequest request, long limit, CancellationToken ct)
    {
        await using var body = Unpacked(request, limit, limitPlain: true);
        using var reader = new StreamReader(body, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
        try { return await reader.ReadToEndAsync(ct); }
        catch (LimitedReadStream.LimitExceededException) { return null; }
    }
}
