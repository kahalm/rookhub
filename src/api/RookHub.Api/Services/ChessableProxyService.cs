using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RookHub.Api.DTOs;

namespace RookHub.Api.Services;

/// <summary>
/// Fehler aus dem piratechess-Backend. <see cref="Status"/> ist der Original-
/// Statuscode, <see cref="Message"/> die vom Backend gelieferte Fehlermeldung
/// (oder eine generische, falls keine geliefert wurde).
/// </summary>
public class ChessableProxyException : Exception
{
    public HttpStatusCode Status { get; }
    public ChessableProxyException(HttpStatusCode status, string message) : base(message)
    {
        Status = status;
    }
}

/// <summary>
/// Typed HttpClient zur piratechess-API. Reicht den User-Bearer pro Request
/// durch (stateless aus piratechess-Sicht). Authentifiziert sich mit dem
/// <c>X-Service-Key</c>-Header (siehe <c>Chessable:ServiceKey</c>).
/// </summary>
public class ChessableProxyService
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ChessableProxyService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ChessableTestResultDto> TestAsync(string bearer, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/chessable/direct/test", new { Bearer = bearer }, ct);
        await EnsureSuccessOrThrowAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<ChessableTestResultDto>(JsonOpts, ct))!;
    }

    public async Task<List<ChessableCourseDto>> GetCoursesAsync(string bearer, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/chessable/direct/courses", new { Bearer = bearer }, ct);
        await EnsureSuccessOrThrowAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<List<ChessableCourseDto>>(JsonOpts, ct)) ?? new();
    }

    /// <summary>
    /// Tiefer Kurs-Abruf: holt die komplette Kursstruktur als ein PGN. <paramref name="mode"/>
    /// steuert die Trainingsannotation: "None" = Repertoire, "FirstKeyMove" = Buch (erster Key
    /// trainierbar), "AllKeyMoves". Kann je nach Kursgröße lange dauern (langer Client-Timeout).
    /// </summary>
    public async Task<ChessableCourseDataDto> FetchCourseAsync(string bearer, string bid, string mode, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/chessable/direct/course", new { Bearer = bearer, Bid = bid, Mode = mode }, ct);
        await EnsureSuccessOrThrowAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<ChessableCourseDataDto>(JsonOpts, ct))!;
    }

    /// <summary>Startet den tiefen Kurs-Abruf asynchron und liefert die JobId für das Polling.</summary>
    public async Task<ChessableCourseStartDto> StartCourseFetchAsync(string bearer, string bid, string mode, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/chessable/direct/course/start", new { Bearer = bearer, Bid = bid, Mode = mode }, ct);
        await EnsureSuccessOrThrowAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<ChessableCourseStartDto>(JsonOpts, ct))!;
    }

    /// <summary>Pollt den Fortschritt eines Kurs-Abruf-Jobs. <c>null</c> = Job unbekannt/weg (piratechess-Neustart).</summary>
    public async Task<ChessableCourseProgressDto?> GetCourseProgressAsync(string jobId, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/api/chessable/direct/course/{jobId}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessOrThrowAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<ChessableCourseProgressDto>(JsonOpts, ct);
    }

    /// <summary>True, wenn piratechess die Rohdaten des Kurses schon gecacht hat (Import braucht
    /// dann keinen Chessable-Abruf). Fehler/unerreichbar → false (dann normal über die Queue).</summary>
    public async Task<bool> IsCourseCachedAsync(string bid, CancellationToken ct = default)
    {
        try
        {
            var dto = await _httpClient.GetFromJsonAsync<CachedDto>($"/api/chessable/direct/course/{Uri.EscapeDataString(bid)}/cached", JsonOpts, ct);
            return dto?.Cached ?? false;
        }
        catch
        {
            return false;
        }
    }

    private record CachedDto(bool Cached);

    private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        // piratechess gibt { "message": "..." } zurueck — herausziehen falls vorhanden.
        var message = body;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                message = msg.GetString() ?? body;
        }
        catch (JsonException) { /* not JSON — keep raw */ }

        throw new ChessableProxyException(response.StatusCode, message);
    }
}
