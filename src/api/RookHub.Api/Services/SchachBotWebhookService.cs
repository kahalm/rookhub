using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RookHub.Api.DTOs;

namespace RookHub.Api.Services;

/// <summary>
/// Schickt Solver-Stand-Updates an den schach-bot, damit dieser den Tagespuzzle-Post live
/// aktualisieren kann. Wird vom <see cref="BookPuzzleService"/> nach einem aufgezeichneten
/// Lösungsversuch via <see cref="IBackgroundTaskQueue"/> fire-and-forget angestoßen.
///
/// Konfiguration (appsettings / env):
/// - <c>SchachBot:WebhookUrl</c> z.B. <c>http://schach-bot:9000/webhook/puzzle-attempt</c>
/// - <c>SchachBot:WebhookSecret</c> identisch zum Bot-<c>WEBHOOK_SECRET</c>.
///
/// Beide leer = Webhook deaktiviert (no-op).
/// </summary>
public class SchachBotWebhookService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<SchachBotWebhookService> _logger;

    public SchachBotWebhookService(HttpClient http, IConfiguration config, ILogger<SchachBotWebhookService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    /// <summary>True wenn URL + Secret konfiguriert sind.</summary>
    public bool IsEnabled =>
        !string.IsNullOrEmpty(_config["SchachBot:WebhookUrl"]) &&
        !string.IsNullOrEmpty(_config["SchachBot:WebhookSecret"]);

    /// <summary>
    /// Schickt den aktuellen Solver-Stand fuer ein Buch-Puzzle an den Bot. Schlucht alle
    /// Fehler (Logging only) — der Bot ist aus API-Sicht best-effort.
    /// </summary>
    public async Task NotifyAttemptAsync(int puzzleId, BookPuzzleResultsDto results, CancellationToken ct = default)
    {
        var url = _config["SchachBot:WebhookUrl"];
        var secret = _config["SchachBot:WebhookSecret"];
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(secret))
        {
            return;
        }

        // Derselbe DTO wie GET /api/book-puzzles/{id}/results (gleiche JSON-Form, siehe WireJson): der Bot
        // liest beide mit demselben Parser. Eine handgebaute Projektion verlor schon einmal ein Feld
        // (hintsUsed — Daily-Löser bekamen nie das 💡), über den Catch-up-Pfad kam es an.
        var payload = new { puzzleId, results };

        await PostSignedAsync(url, secret, payload, new WebhookLog(
            ex => _logger.LogWarning(ex, "SchachBot-Webhook: Payload konnte nicht serialisiert werden (puzzleId={PuzzleId})", puzzleId),
            status => _logger.LogWarning("SchachBot-Webhook: HTTP {Status} (puzzleId={PuzzleId})", status, puzzleId),
            () => _logger.LogDebug("SchachBot-Webhook abgebrochen (puzzleId={PuzzleId})", puzzleId),
            ex => _logger.LogWarning(ex, "SchachBot-Webhook fehlgeschlagen (puzzleId={PuzzleId})", puzzleId)), ct);
    }

    /// <summary>
    /// Schickt den aggregierten Wochenpost-Stand an den Bot (live-Update des Ankündigungs-Threads).
    /// Ziel-URL wird aus <c>SchachBot:WebhookUrl</c> abgeleitet (letztes Pfadsegment → <c>weekly-progress</c>),
    /// gleiches Secret. Schluckt alle Fehler (best-effort).
    /// </summary>
    public async Task NotifyWeeklyAsync(int weeklyPostId, WeeklyPostResultsDto results, CancellationToken ct = default)
    {
        var baseUrl = _config["SchachBot:WebhookUrl"];
        var secret = _config["SchachBot:WebhookSecret"];
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(secret))
            return;
        // ".../webhook/puzzle-attempt" → ".../webhook/weekly-progress"
        var url = SiblingWebhookUrl(baseUrl, "weekly-progress");

        // Derselbe DTO wie GET /api/weekly-posts/{id}/results (Begründung wie beim Solver-Webhook).
        var payload = new { weeklyPostId, results };

        await PostSignedAsync(url, secret, payload, new WebhookLog(
            ex => _logger.LogWarning(ex, "SchachBot-Weekly-Webhook: Payload nicht serialisierbar (weeklyPostId={Id})", weeklyPostId),
            status => _logger.LogWarning("SchachBot-Weekly-Webhook: HTTP {Status} (weeklyPostId={Id})", status, weeklyPostId),
            () => _logger.LogDebug("SchachBot-Weekly-Webhook abgebrochen (weeklyPostId={Id})", weeklyPostId),
            ex => _logger.LogWarning(ex, "SchachBot-Weekly-Webhook fehlgeschlagen (weeklyPostId={Id})", weeklyPostId)), ct);
    }

    /// <summary>
    /// Benachrichtigt den Bot, dass das Tagespuzzle für <paramref name="date"/> neu generiert wurde.
    /// Der Bot postet das neue Puzzle in den Channel und archiviert den alten Thread.
    /// Schluckt alle Fehler (best-effort, fire-and-forget).
    /// </summary>
    public async Task NotifyDailyRegeneratedAsync(DateOnly date, int newPuzzleId, CancellationToken ct = default)
    {
        var baseUrl = _config["SchachBot:WebhookUrl"];
        var secret = _config["SchachBot:WebhookSecret"];
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(secret))
            return;

        var url = SiblingWebhookUrl(baseUrl, "daily-regenerate");

        var payload = new { date = date.ToString("yyyy-MM-dd"), puzzleId = newPuzzleId };
        await PostSignedAsync(url, secret, payload, new WebhookLog(
            ex => _logger.LogWarning(ex, "SchachBot-DailyRegenerate-Webhook: Payload nicht serialisierbar (date={Date})", date),
            status => _logger.LogWarning("SchachBot-DailyRegenerate-Webhook: HTTP {Status} (date={Date})", status, date),
            () => _logger.LogDebug("SchachBot-DailyRegenerate-Webhook abgebrochen (date={Date})", date),
            ex => _logger.LogWarning(ex, "SchachBot-DailyRegenerate-Webhook fehlgeschlagen (date={Date})", date)), ct);
    }

    /// <summary>
    /// JSON-Form der Webhook-Rümpfe = die der MVC-Antworten (<c>AddJsonOptions</c> in Program.cs: Web-Vorgaben,
    /// also camelCase, plus Enums als Text). So trägt der Live-Webhook genau die Felder, die der Bot über
    /// GET /results ebenfalls bekommt — eine Form derselben Daten statt zwei.
    /// </summary>
    internal static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>Log-Zeilen eines Webhooks. Die Templates bleiben je Webhook eigen (Kibana-Suchen), der
    /// Versand selbst steht EINMAL in <see cref="PostSignedAsync"/>.</summary>
    private readonly record struct WebhookLog(
        Action<Exception> SerializeFailed,
        Action<int> HttpError,
        Action Cancelled,
        Action<Exception> Failed);

    /// <summary>
    /// Der eine Versandweg aller Bot-Webhooks: serialisieren, mit dem aktuellen Zeitstempel signieren
    /// (<see cref="BuildSignedRequest"/>), POSTen. Schluckt alle Fehler (best-effort) und meldet sie
    /// nur über <paramref name="log"/>.
    /// </summary>
    private async Task PostSignedAsync(string url, string secret, object payload, WebhookLog log, CancellationToken ct)
    {
        string body;
        try { body = JsonSerializer.Serialize(payload, WireJson); }
        catch (Exception ex)
        {
            log.SerializeFailed(ex);
            return;
        }

        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            using var req = BuildSignedRequest(url, secret, body, ts);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
                log.HttpError((int)resp.StatusCode);
        }
        catch (TaskCanceledException) { log.Cancelled(); }
        catch (Exception ex) { log.Failed(ex); }
    }

    /// <summary>
    /// Signatur-Vertrag mit dem Bot (<c>core/webhook_server.py</c>, <c>_verify_signature</c>), an EINER Stelle:
    /// <c>X-Webhook-Timestamp</c> = <paramref name="ts"/> (Unix-Sekunden), <c>X-Webhook-Signature</c> =
    /// <c>"sha256=" + hex(HMAC_SHA256(secret, "&lt;ts&gt;.&lt;body&gt;"))</c>, Body als <c>application/json</c>.
    /// </summary>
    internal static HttpRequestMessage BuildSignedRequest(string url, string secret, string body, string ts)
    {
        var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        req.Headers.TryAddWithoutValidation("X-Webhook-Signature", "sha256=" + ComputeHmacHex(secret, ts + "." + body));
        req.Headers.TryAddWithoutValidation("X-Webhook-Timestamp", ts);
        return req;
    }

    /// <summary>
    /// Leitet aus <c>SchachBot:WebhookUrl</c> die Schwester-Webhook-URL ab, indem das LETZTE
    /// Pfadsegment ersetzt wird (".../webhook/puzzle-attempt" → ".../webhook/{sibling}").
    /// Robust gegen Konfigurations-Varianten, an denen das frühere naive
    /// <c>LastIndexOf('/')</c>-Schneiden still kaputtging:
    /// - Trailing-Slash (".../puzzle-attempt/") → würde sonst ".../puzzle-attempt/{sibling}" bauen;
    /// - URL OHNE Pfad ("http://schach-bot:9000") → schnitt sonst am "//" des Schemas und
    ///   erzeugte "http://{sibling}" (falscher HOST!); jetzt wird der dokumentierte Bot-Pfad
    ///   "/webhook/{sibling}" angehängt.
    /// </summary>
    internal static string SiblingWebhookUrl(string baseUrl, string sibling)
    {
        var trimmed = baseUrl.TrimEnd('/');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.AbsolutePath.Length > 1)
        {
            var slash = trimmed.LastIndexOf('/');
            return trimmed[..slash] + "/" + sibling;
        }
        return trimmed + "/webhook/" + sibling;
    }

    /// <summary>HMAC-SHA256 ueber <paramref name="body"/> mit <paramref name="secret"/>, als lowercase-hex.</summary>
    public static string ComputeHmacHex(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
