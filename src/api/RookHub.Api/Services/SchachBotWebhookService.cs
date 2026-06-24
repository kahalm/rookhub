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

        var payload = new
        {
            puzzleId,
            results = new
            {
                solvedCount = results.SolvedCount,
                anonymousSolvedCount = results.AnonymousSolvedCount,
                attemptCount = results.AttemptCount,
                solvers = results.Solvers.Select(s => new
                {
                    name = s.Name,
                    discordId = s.DiscordId,
                    discordUsername = s.DiscordUsername,
                    timeSeconds = s.TimeSeconds,
                }),
            },
        };

        string body;
        try
        {
            body = JsonSerializer.Serialize(payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SchachBot-Webhook: Payload konnte nicht serialisiert werden (puzzleId={PuzzleId})", puzzleId);
            return;
        }

        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signature = ComputeHmacHex(secret, ts + "." + body);

        try
        {
            using var content = new StringContent(body, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            req.Headers.TryAddWithoutValidation("X-Webhook-Signature", "sha256=" + signature);
            req.Headers.TryAddWithoutValidation("X-Webhook-Timestamp", ts);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("SchachBot-Webhook: HTTP {Status} (puzzleId={PuzzleId})", (int)resp.StatusCode, puzzleId);
            }
        }
        catch (TaskCanceledException)
        {
            _logger.LogDebug("SchachBot-Webhook abgebrochen (puzzleId={PuzzleId})", puzzleId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SchachBot-Webhook fehlgeschlagen (puzzleId={PuzzleId})", puzzleId);
        }
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
        var slash = baseUrl.LastIndexOf('/');
        var url = slash > 0 ? baseUrl[..slash] + "/weekly-progress" : baseUrl;

        var payload = new
        {
            weeklyPostId,
            results = new
            {
                total = results.Total,
                completedCount = results.CompletedCount,
                players = results.Players.Select(p => new
                {
                    name = p.Name,
                    discordId = p.DiscordId,
                    discordUsername = p.DiscordUsername,
                    playedCount = p.PlayedCount,
                    solvedCount = p.SolvedCount,
                    totalSeconds = p.TotalSeconds,
                    hintsUsed = p.HintsUsed,
                    completed = p.Completed,
                }),
            },
        };

        string body;
        try { body = JsonSerializer.Serialize(payload); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SchachBot-Weekly-Webhook: Payload nicht serialisierbar (weeklyPostId={Id})", weeklyPostId);
            return;
        }

        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signature = ComputeHmacHex(secret, ts + "." + body);
        try
        {
            using var content = new StringContent(body, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            req.Headers.TryAddWithoutValidation("X-Webhook-Signature", "sha256=" + signature);
            req.Headers.TryAddWithoutValidation("X-Webhook-Timestamp", ts);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("SchachBot-Weekly-Webhook: HTTP {Status} (weeklyPostId={Id})", (int)resp.StatusCode, weeklyPostId);
        }
        catch (TaskCanceledException) { _logger.LogDebug("SchachBot-Weekly-Webhook abgebrochen (weeklyPostId={Id})", weeklyPostId); }
        catch (Exception ex) { _logger.LogWarning(ex, "SchachBot-Weekly-Webhook fehlgeschlagen (weeklyPostId={Id})", weeklyPostId); }
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

        var slash = baseUrl.LastIndexOf('/');
        var url = slash > 0 ? baseUrl[..slash] + "/daily-regenerate" : baseUrl;

        var payload = new { date = date.ToString("yyyy-MM-dd"), puzzleId = newPuzzleId };
        string body;
        try { body = JsonSerializer.Serialize(payload); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SchachBot-DailyRegenerate-Webhook: Payload nicht serialisierbar (date={Date})", date);
            return;
        }

        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signature = ComputeHmacHex(secret, ts + "." + body);
        try
        {
            using var content = new StringContent(body, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            req.Headers.TryAddWithoutValidation("X-Webhook-Signature", "sha256=" + signature);
            req.Headers.TryAddWithoutValidation("X-Webhook-Timestamp", ts);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("SchachBot-DailyRegenerate-Webhook: HTTP {Status} (date={Date})", (int)resp.StatusCode, date);
        }
        catch (TaskCanceledException) { _logger.LogDebug("SchachBot-DailyRegenerate-Webhook abgebrochen (date={Date})", date); }
        catch (Exception ex) { _logger.LogWarning(ex, "SchachBot-DailyRegenerate-Webhook fehlgeschlagen (date={Date})", date); }
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
