using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace RookHub.Api.Services.League;

/// <summary>
/// PGN einer ÖFFENTLICHEN Lichess-Studie holen (Import in die Vereins-Datenbank, Wunsch 2026-09-28: „Import soll auch
/// eine öffentliche Lichess-Studien-URL akzeptieren"). Nur Adressen der Form <c>lichess.org/study/{id}</c> bzw.
/// <c>…/study/{id}/{kapitel}</c> — der Server ruft ausschließlich die feste Lichess-API auf (kein freier Abruf beliebiger
/// Adressen). Private oder fehlende Studien → <c>lichessNotFound</c>.
/// </summary>
public sealed partial class LichessStudySource
{
    public const string ClientName = "LichessStudy";

    /// <summary>Vorgabe für <c>League:StudyFetchPerLinkPerDay</c>: so viele Studienabrufe je Teilen-Link und UTC-Tag.</summary>
    public const int DefaultPerLinkPerDay = 10;

    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache? _cache;
    private readonly int _perLinkPerDay;

    public LichessStudySource(IHttpClientFactory http, IMemoryCache? cache = null, IConfiguration? configuration = null)
    {
        _http = http;
        _cache = cache;
        _perLinkPerDay = Math.Max(0, configuration?.GetValue("League:StudyFetchPerLinkPerDay", DefaultPerLinkPerDay) ?? DefaultPerLinkPerDay);
    }

    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?lichess\.org/study/([A-Za-z0-9]{8})(?:/([A-Za-z0-9]{8}))?(?:[/?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex StudyUrl();

    /// <summary>Studie + optional Kapitel aus einer Adresse; <c>null</c> = keine Lichess-Studien-Adresse.</summary>
    public static (string Study, string? Chapter)? Parse(string? url)
    {
        var m = StudyUrl().Match((url ?? "").Trim());
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value : null) : null;
    }

    public static string ApiPath((string Study, string? Chapter) s) =>
        s.Chapter is null ? $"/api/study/{s.Study}.pgn" : $"/api/study/{s.Study}/{s.Chapter}.pgn";

    /// <summary>PGN oder Grund (<c>invalidUrl</c>, <c>lichessNotFound</c>, <c>lichessFailed</c>, <c>tooLarge</c>).</summary>
    public async Task<(string? Pgn, string? Reason)> FetchAsync(string? url, CancellationToken ct)
    {
        if (Parse(url) is not { } s) return (null, "invalidUrl");
        HttpResponseMessage res;
        try
        {
            res = await _http.CreateClient(ClientName).GetAsync(ApiPath(s), HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return (null, "lichessFailed"); }
        using (res)
        {
            if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                return (null, "lichessNotFound");
            if (!res.IsSuccessStatusCode) return (null, "lichessFailed");
            // Gestreamt und bei MaxImportChars + 1 abgebrochen (N6-002) — vorher wurde die ganze Antwort gepuffert, erst
            // dann griff der Deckel.
            var text = await ReadCappedAsync(res, LeagueClubService.MaxImportChars, ct);
            if (text is null) return (null, "tooLarge");
            return string.IsNullOrWhiteSpace(text) ? (null, "lichessNotFound") : (text, null);
        }
    }

    /// <summary>
    /// Wie <see cref="FetchAsync"/>, aber über einen anonymen Teilen-Link: höchstens <c>League:StudyFetchPerLinkPerDay</c>
    /// (Vorgabe 10) Abrufe je Link und UTC-Tag, sonst <c>studyLimit</c> ohne Abruf (N6-002) — jeder Abruf geht von der
    /// Server-IP an Lichess. Zähler im Arbeitsspeicher (ein Neustart setzt ihn zurück); ungültige Adressen zählen nicht.
    /// </summary>
    public async Task<(string? Pgn, string? Reason)> FetchForShareAsync(string linkToken, string? url, CancellationToken ct)
    {
        if (Parse(url) is null) return (null, "invalidUrl");
        if (_cache is not null)
        {
            var key = $"lichess-study-share:{linkToken.ToLowerInvariant()}:{DateTime.UtcNow:yyyy-MM-dd}";
            var counter = _cache.GetOrCreate(key, e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);
                return new StrongBox<int>();
            })!;
            if (Interlocked.Increment(ref counter.Value) > _perLinkPerDay) return (null, "studyLimit");
        }
        return await FetchAsync(url, ct);
    }

    /// <summary>Text der Antwort, höchstens <paramref name="maxChars"/> Zeichen; <c>null</c>, sobald es mehr wären.</summary>
    private static async Task<string?> ReadCappedAsync(HttpResponseMessage res, int maxChars, CancellationToken ct)
    {
        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var sb = new StringBuilder();
        var buffer = new char[81920];
        int n;
        while ((n = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            if (sb.Length + n > maxChars) return null;
            sb.Append(buffer, 0, n);
        }
        return sb.ToString();
    }
}
