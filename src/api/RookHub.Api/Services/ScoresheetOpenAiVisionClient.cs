using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services;

/// <summary>
/// Formular-Lesung über eine OpenAI-kompatible Chat-Schnittstelle (vLLM, SGLang, llama.cpp-Server) — gedacht für ein
/// Vision-Modell auf eigener Hardware, allen voran Qwen3-VL auf dem DGX Spark. Bisher benutzt NUR das Testwerkzeug
/// <c>tools/ScoresheetBench</c> diese Klasse; in der API ist sie nicht verdrahtet: erst am Testsatz messen, dann
/// entscheiden, ob sie Claude ersetzen oder ergänzen kann.
/// </summary>
/// <remarks>
/// Die Antwort wird über <c>response_format: json_schema</c> auf <see cref="ScoresheetPrompt.Schema"/> gezwungen (bei
/// vLLM über die Grammatik-Steuerung). Weist der Server das ab (ältere Fassung, anderer Server), wird EINMAL ohne
/// Schema wiederholt und ab dann darauf verzichtet; das JSON wird dann aus dem Text geschnitten (Denkblöcke und
/// Code-Zäune fallen weg).
/// </remarks>
public sealed class OpenAiScoresheetVisionClient : IScoresheetVisionClient
{
    /// <param name="BaseUrl">Bis einschließlich <c>/v1</c>, z. B. <c>http://spark:8000/v1</c>.</param>
    /// <param name="ApiKey">Optional — vLLM ohne <c>--api-key</c> braucht keinen.</param>
    /// <param name="SystemPrompt">Vorgabe <see cref="ScoresheetPrompt.TranscribeSystem"/> (nur abschreiben).</param>
    /// <param name="MaxTokensCap">Obergrenze der Antwort — sie hängt am Kontextfenster des Servers
    /// (<c>--max-model-len</c>), nicht an einem Budget: ein zu großer Wert ist dort ein 400.</param>
    public sealed record Settings(string BaseUrl, string Model, string? ApiKey = null,
        string SystemPrompt = ScoresheetPrompt.TranscribeSystem, bool UseJsonSchema = true, double Temperature = 0,
        int MaxTokensCap = 16384);

    private readonly HttpClient _http;
    private readonly Settings _settings;
    private readonly ILogger _logger;
    private bool _schemaRejected;

    public OpenAiScoresheetVisionClient(HttpClient http, Settings settings, ILogger logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.BaseUrl) && !string.IsNullOrWhiteSpace(_settings.Model);

    public string Model => _settings.Model;

    /// <summary>Die letzte Rohantwort (Text des Modells) — fürs Testwerkzeug, das sie zum Nachsehen ablegt.</summary>
    public string? LastRaw { get; private set; }

    public async Task<ScoresheetVisionResult> ReadAsync(byte[] jpeg, string instructions, int maxTokens,
        CancellationToken ct = default)
    {
        if (!IsConfigured) return new(null, "notConfigured");
        var useSchema = _settings.UseJsonSchema && !_schemaRejected;
        var reply = await OpenAiChat.SendAsync(_http, _settings.BaseUrl, _settings.ApiKey,
            Body(jpeg, instructions, maxTokens, useSchema), ct);
        if (useSchema && reply.Status == HttpStatusCode.BadRequest)
        {
            // Kein strukturiertes Ausgeben auf diesem Server: einmal ohne, und dabei bleibt es.
            _logger.LogWarning("Formular-Lesung: {Url} lehnt response_format ab ({Error}) — weiter ohne Schema.",
                _settings.BaseUrl, reply.Error);
            _schemaRejected = true;
            reply = await OpenAiChat.SendAsync(_http, _settings.BaseUrl, _settings.ApiKey,
                Body(jpeg, instructions, maxTokens, useSchema: false), ct);
        }
        LastRaw = reply.Content;
        if (reply.Error != null)
        {
            _logger.LogWarning("Formular-Lesung via {Model} fehlgeschlagen: {Error}", _settings.Model, reply.Error);
            return new(null, "failed", reply.InputTokens, reply.OutputTokens);
        }
        if (reply.FinishReason == "length")
            return new(null, "truncated", reply.InputTokens, reply.OutputTokens);
        var json = OpenAiChat.ExtractJsonObject(reply.Content);
        return json == null
            ? new(null, "failed", reply.InputTokens, reply.OutputTokens)
            : new(json, null, reply.InputTokens, reply.OutputTokens);
    }

    private JsonObject Body(byte[] jpeg, string instructions, int maxTokens, bool useSchema)
    {
        var body = new JsonObject
        {
            ["model"] = _settings.Model,
            ["max_tokens"] = Math.Clamp(maxTokens, 256, Math.Max(256, _settings.MaxTokensCap)),
            ["temperature"] = _settings.Temperature,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = _settings.SystemPrompt },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        OpenAiChat.ImagePart(jpeg),
                        new JsonObject { ["type"] = "text", ["text"] = instructions },
                    },
                },
            },
        };
        if (useSchema)
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = "scoresheet",
                    ["strict"] = true,
                    ["schema"] = JsonSerializer.SerializeToNode(ScoresheetPrompt.Schema()),
                },
            };
        return body;
    }
}

/// <summary>Der gemeinsame HTTP-Teil der OpenAI-kompatiblen Leser (Qwen3-VL, dots.ocr).</summary>
internal static class OpenAiChat
{
    /// <summary>Antwort eines <c>chat/completions</c>-Aufrufs. <see cref="Error"/> gesetzt = kein brauchbarer Inhalt.</summary>
    internal sealed record Reply(HttpStatusCode Status, string? Content, string? FinishReason, int InputTokens,
        int OutputTokens, string? Error);

    internal static JsonObject ImagePart(byte[] jpeg) => new()
    {
        ["type"] = "image_url",
        ["image_url"] = new JsonObject { ["url"] = "data:image/jpeg;base64," + Convert.ToBase64String(jpeg) },
    };

    internal static async Task<Reply> SendAsync(HttpClient http, string baseUrl, string? apiKey, JsonObject body,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) // Netz weg, Zeitüberschreitung des HttpClient
        {
            return new(0, null, null, 0, 0, ex.GetType().Name + ": " + ex.Message);
        }
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return new(response.StatusCode, null, null, 0, 0, $"HTTP {(int)response.StatusCode}: {Snippet(text)}");
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                int input = 0, output = 0;
                if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    input = usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pi) ? pi : 0;
                    output = usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out var ci) ? ci : 0;
                }
                if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array
                    || choices.GetArrayLength() == 0)
                    return new(response.StatusCode, null, null, input, output, "no choices: " + Snippet(text));
                var choice = choices[0];
                var finish = choice.TryGetProperty("finish_reason", out var f) && f.ValueKind == JsonValueKind.String
                    ? f.GetString() : null;
                var content = choice.TryGetProperty("message", out var message) ? ContentOf(message) : null;
                return new(response.StatusCode, content, finish, input, output, null);
            }
            catch (JsonException)
            {
                return new(response.StatusCode, null, null, 0, 0, "no JSON: " + Snippet(text));
            }
        }
    }

    /// <summary><c>message.content</c> als Text — ein String, oder (manche Server) eine Liste von Text-Teilen.</summary>
    private static string? ContentOf(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content)) return null;
        if (content.ValueKind == JsonValueKind.String) return content.GetString();
        if (content.ValueKind != JsonValueKind.Array) return null;
        var sb = new StringBuilder();
        foreach (var part in content.EnumerateArray())
            if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                sb.Append(t.GetString());
        return sb.ToString();
    }

    private static readonly Regex ThinkBlock = new(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Das JSON-Objekt aus einer Modellantwort: Denkblöcke (<c>&lt;think&gt;…&lt;/think&gt;</c>) und Code-Zäune fallen
    /// weg, genommen wird vom ersten <c>{</c> bis zum letzten <c>}</c> — aber nur, wenn das auch wirklich ein
    /// gültiges Objekt ist. Sonst <c>null</c>.
    /// </summary>
    internal static string? ExtractJsonObject(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var text = ThinkBlock.Replace(content, "");
        var open = text.IndexOf("<think>", StringComparison.Ordinal);
        if (open >= 0) text = text[..open]; // Denkblock ohne Ende: dahinter steht keine Antwort
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        var candidate = text[start..(end + 1)];
        try
        {
            using var doc = JsonDocument.Parse(candidate);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? candidate : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string Snippet(string? text)
    {
        var t = (text ?? "").ReplaceLineEndings(" ").Trim();
        return t.Length <= 300 ? t : t[..300] + "…";
    }
}
