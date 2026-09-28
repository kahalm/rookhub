using System.Net;
using System.Text.RegularExpressions;

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
    private readonly IHttpClientFactory _http;

    public LichessStudySource(IHttpClientFactory http) => _http = http;

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
            var text = await res.Content.ReadAsStringAsync(ct);
            if (text.Length > LeagueClubService.MaxImportChars) return (null, "tooLarge");
            return string.IsNullOrWhiteSpace(text) ? (null, "lichessNotFound") : (text, null);
        }
    }
}
