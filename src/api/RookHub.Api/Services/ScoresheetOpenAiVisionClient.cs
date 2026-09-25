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
        CancellationToken ct = default, ScoresheetReadMode mode = ScoresheetReadMode.Full)
    {
        if (!IsConfigured) return new(null, "notConfigured");
        var useSchema = _settings.UseJsonSchema && !_schemaRejected;
        var noThinking = mode == ScoresheetReadMode.Transcribe;
        var system = noThinking ? ScoresheetPrompt.TranscribeSystem : _settings.SystemPrompt;
        var reply = await OpenAiChat.SendAsync(_http, _settings.BaseUrl, _settings.ApiKey,
            Body(jpeg, instructions, maxTokens, useSchema, system, noThinking), ct);
        if (useSchema && reply.Status == HttpStatusCode.BadRequest)
        {
            // Kein strukturiertes Ausgeben auf diesem Server: einmal ohne, und dabei bleibt es.
            _logger.LogWarning("Formular-Lesung: {Url} lehnt response_format ab ({Error}) — weiter ohne Schema.",
                _settings.BaseUrl, reply.Error);
            _schemaRejected = true;
            reply = await OpenAiChat.SendAsync(_http, _settings.BaseUrl, _settings.ApiKey,
                Body(jpeg, instructions, maxTokens, useSchema: false, system, noThinking), ct);
        }
        LastRaw = reply.Content ?? reply.Error; // im Fehlerfall die Meldung — fürs Testwerkzeug
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

    private JsonObject Body(byte[] jpeg, string instructions, int maxTokens, bool useSchema, string system, bool noThinking)
    {
        var body = new JsonObject
        {
            ["model"] = _settings.Model,
            ["max_tokens"] = Math.Clamp(maxTokens, 256, Math.Max(256, _settings.MaxTokensCap)),
            ["temperature"] = _settings.Temperature,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = system },
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
        // Qwen3/Qwen3.5 denken über die Chat-Vorlage nach, solange man es nicht abschaltet — beim Abschreiben (Rückfall
        // bzw. Scoresheet:Thinking=false) aus. Vorlagen ohne den Schalter ignorieren ihn.
        if (noThinking)
            body["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false };
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

    /// <summary>
    /// Ein <c>chat/completions</c>-Aufruf, GESTREAMT (<c>stream: true</c> + <c>include_usage</c>). Gebraucht, weil vor
    /// dem Spark ein Reverse-Proxy (openresty) jede Anfrage nach 90 Sekunden ohne Antwort mit 504 abbricht — eine ganze
    /// Formular-Lesung dauert dort Minuten. Solange Tokens fließen, bleibt die Verbindung offen. Antwortet ein Server
    /// trotzdem mit einem ganzen JSON-Objekt (Stream ignoriert), wird das gelesen.
    /// </summary>
    internal static async Task<Reply> SendAsync(HttpClient http, string baseUrl, string? apiKey, JsonObject body,
        CancellationToken ct)
    {
        body["stream"] = true;
        body["stream_options"] = new JsonObject { ["include_usage"] = true };
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
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
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                return new(response.StatusCode, null, null, 0, 0, $"HTTP {(int)response.StatusCode}: {Snippet(error)}");
            }
            if (response.Content.Headers.ContentType?.MediaType != "text/event-stream")
                return ParseCompletion(response.StatusCode, await response.Content.ReadAsStringAsync(ct));
            try
            {
                return await ReadStreamAsync(response, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) // Verbindung mitten im Strom abgerissen
            {
                return new(response.StatusCode, null, null, 0, 0, "stream: " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }

    /// <summary>Server-Sent Events: <c>data: {…}</c>-Zeilen bis <c>data: [DONE]</c>. Gesammelt wird nur
    /// <c>delta.content</c> (Denken steht bei vLLM in <c>reasoning</c>/<c>reasoning_content</c> und bleibt draußen),
    /// dazu der Stoppgrund und die Tokens aus dem letzten Stück.</summary>
    private static async Task<Reply> ReadStreamAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var content = new StringBuilder();
        string? finish = null;
        int input = 0, output = 0;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") break;
            if (data.Length == 0) continue;
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
                return new(response.StatusCode, null, null, input, output, "stream error: " + Snippet(err.ToString()));
            ReadUsage(root, ref input, ref output);
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array) continue;
            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("delta", out var delta) && ContentOf(delta) is { } part) content.Append(part);
                if (choice.TryGetProperty("finish_reason", out var f) && f.ValueKind == JsonValueKind.String) finish = f.GetString();
            }
        }
        return new(response.StatusCode, content.ToString(), finish, input, output, null);
    }

    private static void ReadUsage(JsonElement root, ref int input, ref int output)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return;
        if (usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pi)) input = pi;
        if (usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out var ci)) output = ci;
    }

    /// <summary>Eine ganze (nicht gestreamte) Antwort.</summary>
    private static Reply ParseCompletion(HttpStatusCode status, string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            int input = 0, output = 0;
            ReadUsage(root, ref input, ref output);
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
                return new(status, null, null, input, output, "no choices: " + Snippet(text));
            var choice = choices[0];
            var finish = choice.TryGetProperty("finish_reason", out var f) && f.ValueKind == JsonValueKind.String
                ? f.GetString() : null;
            var content = choice.TryGetProperty("message", out var message) ? ContentOf(message) : null;
            return new(status, content, finish, input, output, null);
        }
        catch (JsonException)
        {
            return new(status, null, null, 0, 0, "no JSON: " + Snippet(text));
        }
    }

    /// <summary><c>content</c> einer Nachricht oder eines Stream-Stücks als Text — ein String, oder (manche Server)
    /// eine Liste von Text-Teilen.</summary>
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
