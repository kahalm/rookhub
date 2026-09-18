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
    private readonly ILogger<ChessableProxyService> _logger;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // logger optional, damit bestehende Test-Konstruktionen ohne Änderung kompilieren.
    public ChessableProxyService(HttpClient httpClient, ILogger<ChessableProxyService>? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ChessableProxyService>.Instance;
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

    /// <summary>
    /// Fetch-freier Parse: schickt bereits (vom Browser über die RepCheck-Extension) erfasstes rohes
    /// Chessable-JSON — je Kapitel die getList-Antwort + geordnete getGame-Antworten — an piratechess und
    /// bekommt dasselbe PGN wie der Live-Abruf zurück, OHNE dass piratechess Chessable kontaktiert (kein
    /// Bearer/VPN). Für den Browser-Import („Über meinen Browser holen"). <paramref name="mode"/> wie bei
    /// <see cref="FetchCourseAsync"/> ("None"=Repertoire, "FirstKeyMove"=Buch).
    /// </summary>
    /// <param name="courseJson">Echte getCourse-Antwort — nur zusammen mit <paramref name="complete"/> relevant.</param>
    /// <param name="complete">Die Extension hat den Kurs vollständig geholt → piratechess darf ihn als Ganzes cachen.</param>
    public async Task<ChessableCourseDataDto> ParseCourseAsync(
        string bid, string mode, IEnumerable<ChessableIngestChapter> chapters,
        string? courseJson = null, bool complete = false, CancellationToken ct = default)
    {
        var payload = new
        {
            Bid = bid,
            Mode = mode,
            Chapters = chapters.Select(c => new { c.ChapterJson, c.Lines, c.LineOids }).ToList(),
            CourseJson = courseJson,
            Complete = complete,
        };
        var response = await _httpClient.PostAsJsonAsync("/api/chessable/direct/course/parse", payload, ct);
        await EnsureSuccessOrThrowAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<ChessableCourseDataDto>(JsonOpts, ct))!;
    }

    /// <summary>
    /// „Wahrheit" je oid für die Repertoire-Bereinigung: das PGN, das piratechess aus dem geteilten Linien-Cache für
    /// genau diese Linie erzeugt (Vorgabe: Repertoire-Modus "None", siehe <paramref name="mode"/>). Geht über den fetch-freien Parse — ohne mitgeschickte Inhalte
    /// schreibt der nichts in den Cache. Nicht gecachte oids fehlen im Ergebnis. Verbindungsfehler WERFEN, damit der
    /// Aufrufer es später erneut versucht, statt „nicht gecacht" anzunehmen.
    /// </summary>
    /// <param name="mode">Trainings-Modus des erzeugten PGN: <c>"None"</c> (Repertoire-Stil, ohne
    /// Marker) ist die Vorgabe; <c>"FirstKeyMove"</c> setzt ein <c>[%tqu]</c> am ersten Schlüsselzug der
    /// Solverfarbe — daran liest <see cref="ChessableTrainingStart"/> die Farbe einer Linie ab.</param>
    public async Task<Dictionary<string, string>> GetCachedLinePgnsAsync(IEnumerable<string> oids,
        string mode = "None", CancellationToken ct = default)
    {
        var list = oids
            .Where(o => int.TryParse(o, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (list.Count == 0) return result;
        var entries = string.Join(",", list.Select(o => "{\"id\":" + o + ",\"name\":\"x\"}"));
        var chapter = new ChessableIngestChapter("{\"list\":{\"name\":\"x\",\"title\":\"x\",\"data\":[" + entries + "]}}",
            list.Select(_ => (string)null!).ToList(), list);
        var parsed = await ParseCourseAsync("1", mode, new[] { chapter }, ct: ct);
        foreach (var block in System.Text.RegularExpressions.Regex.Split(parsed.Pgn ?? string.Empty, @"(?=\[Event )"))
        {
            var m = System.Text.RegularExpressions.Regex.Match(block, "\\[ChessableOid \"([^\"]+)\"\\]");
            if (m.Success) result[m.Groups[1].Value] = block.Trim();
        }
        return result;
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

    /// <summary>Leichte Vorab-Schätzung: Gesamt-Linienzahl eines Kurses (für die „~N Linien · ~M min"-
    /// Anzeige in der Admin-Kursliste). Gecacht → ohne Chessable-Abruf; sonst EIN getCourse-Call.</summary>
    public async Task<ChessableCourseInfoDto?> GetCourseInfoAsync(string bearer, string bid, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/chessable/direct/course/info", new { Bearer = bearer, Bid = bid }, ct);
        await EnsureSuccessOrThrowAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<ChessableCourseInfoDto>(JsonOpts, ct);
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // Shutdown/Timeout des Aufrufers nicht als „nicht gecacht" maskieren
        }
        catch (Exception ex)
        {
            // Bewusst weich (false = normaler Download-Queue-Weg), aber SICHTBAR: ein down/
            // fehlkonfigurierter piratechess ließ sonst still ALLE Kurse ungecacht erscheinen —
            // die Fast-Lane starb undiagnostizierbar, ohne eine einzige Log-Zeile.
            _logger.LogWarning(ex, "Chessable-Proxy: Cache-Check für bid {Bid} fehlgeschlagen — als ungecacht behandelt.", bid);
            return false;
        }
    }

    /// <summary>Welche der oids liegen im geteilten Linien-Cache von piratechess (nur Existenz). Weich wie der
    /// Kurs-Cache-Check: Fehler/unerreichbar → leere Menge (dann holt die Extension alle Linien selbst), aber
    /// SICHTBAR geloggt.</summary>
    public async Task<HashSet<string>> GetCachedLineOidsAsync(IReadOnlyCollection<string> oids, CancellationToken ct = default)
    {
        if (oids.Count == 0) return new HashSet<string>();
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/chessable/direct/lines/cached", new { Oids = oids }, ct);
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<CachedLinesDto>(JsonOpts, ct);
            return new HashSet<string>(dto?.Oids ?? new List<string>());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chessable-Proxy: Linien-Cache-Abfrage für {Count} oids fehlgeschlagen — alle Linien werden geholt.", oids.Count);
            return new HashSet<string>();
        }
    }

    private sealed record CachedLinesDto(List<string>? Oids);

    /// <summary>Alle gecachten Kurs-Bids auf einmal (für die Kurslisten-Anreicherung mit „gecacht"-Flag).
    /// Fehler/unerreichbar → leeres Set (dann eben keine Flags, kein harter Fehler).</summary>
    public async Task<HashSet<string>> GetCachedBidsAsync(CancellationToken ct = default)
    {
        try
        {
            var dto = await _httpClient.GetFromJsonAsync<CachedBidsDto>("/api/chessable/direct/courses/cached", JsonOpts, ct);
            return dto?.Bids is { Count: > 0 } ? new HashSet<string>(dto.Bids) : new HashSet<string>();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chessable-Proxy: Batch-Cache-Abruf fehlgeschlagen — behandle alle Kurse als ungecacht.");
            return new HashSet<string>();
        }
    }

    private record CachedDto(bool Cached);
    private record CachedBidsDto(List<string> Bids);

    private async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        // piratechess gibt { "message": "..." } zurueck — herausziehen falls vorhanden.
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                message = msg.GetString();
        }
        catch (JsonException) { /* nicht JSON — siehe unten */ }

        // Ohne erkennbare Proxy-Meldung NICHT den Roh-Body durchreichen: die Controller geben
        // `ex.Message` unverändert an den Browser, und bei einer Störung ist das die HTML-Fehlerseite
        // von nginx/Kestrel — inklusive Server-Version und Hostnamen, in Dev auch mal ein Stacktrace.
        // Der Crawler-Pfad macht es über seinen Exception-Filter genauso. Der Roh-Body bleibt im Log.
        if (string.IsNullOrWhiteSpace(message))
        {
            _logger?.LogWarning("Chessable-Proxy antwortete {Status}; Body (gekürzt): {Body}",
                (int)response.StatusCode, body.Length > 500 ? body[..500] : body);
            message = "Chessable-Dienst nicht erreichbar (bitte später erneut versuchen).";
        }

        throw new ChessableProxyException(response.StatusCode, message);
    }
}
