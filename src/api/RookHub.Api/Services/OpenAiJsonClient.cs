using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RookHub.Api.Services;

/// <summary>
/// Tipps und Übersetzungen über ein Sprachmodell auf EIGENER Hardware (OpenAI-kompatible Schnittstelle, vLLM auf dem
/// DGX Spark) statt über Claude — kostenlos je Aufruf. Aktiv, sobald <c>TextLlm:BaseUrl</c> gesetzt ist; dann gewinnt
/// es gegen den Claude-Schlüssel <c>Anthropic:TextApiKey</c> (<see cref="TextJsonClients.Create"/>).
/// </summary>
/// <remarks>
/// <para><c>TextLlm:Model</c> leer = das erste Modell, das der Server unter <c>/models</c> meldet — auf dem Spark
/// wechselt das Modell (gpt-oss-120b, Qwen3.5-122B), und eine fest eingetragene Id wäre nach jedem Wechsel ein 404.</para>
/// <para>Nachdenken ist AUS (<c>TextLlm:Thinking=true</c> schaltet es ein): Qwen3/Qwen3.5 denken sonst über die
/// Chat-Vorlage, und auf dem Spark kostet das Minuten je Tipp — bei tausenden Puzzles zu viel. gpt-oss ignoriert den
/// Schalter und denkt mit seiner Vorgabe.</para>
/// <para>Gestreamt (<see cref="OpenAiChat.SendAsync"/>): vor dem Spark kappt ein Proxy jede Anfrage nach 90 s ohne
/// Antwort.</para>
/// </remarks>
public sealed class OpenAiJsonClient : IClaudeJsonClient
{
    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly string _baseUrl;
    private readonly string? _apiKey;
    private readonly string? _configuredModel;
    private readonly bool _thinking;
    private string? _resolvedModel;
    private bool _schemaRejected;

    public OpenAiJsonClient(HttpClient http, IConfiguration config, ILogger logger)
    {
        _http = http;
        _logger = logger;
        _baseUrl = (config["TextLlm:BaseUrl"] ?? "").Trim();
        _apiKey = string.IsNullOrWhiteSpace(config["TextLlm:ApiKey"]) ? null : config["TextLlm:ApiKey"]!.Trim();
        _configuredModel = string.IsNullOrWhiteSpace(config["TextLlm:Model"]) ? null : config["TextLlm:Model"]!.Trim();
        _thinking = bool.TryParse(config["TextLlm:Thinking"], out var t) && t;
    }

    public bool IsConfigured => _baseUrl.Length > 0;

    /// <summary>Das Modell, das zuletzt geantwortet hat (bzw. das eingestellte) — gehört an den gespeicherten Satz.</summary>
    public string TranslationModel => _configuredModel ?? _resolvedModel ?? "local";

    public bool IsLocal => true;

    public Task<string?> CompleteJsonAsync(string purpose, string system, string userPrompt, JsonNode schema,
        int maxTokens, CancellationToken ct = default)
        => AskAsync(purpose, system, userPrompt, schema, maxTokens, ct);

    public Task<string?> GenerateHintsJsonAsync(string system, string userPrompt, CancellationToken ct = default)
        => AskAsync("hints", system, userPrompt, HintSchema(), maxTokens: 4096, ct);

    public Task<string?> TranslateCommentsJsonAsync(string system, string userPrompt, CancellationToken ct = default)
        => AskAsync("translation", system, userPrompt, TranslationSchema(), maxTokens: 16000, ct);

    private async Task<string?> AskAsync(string purpose, string system, string userPrompt, JsonNode schema, int maxTokens,
        CancellationToken ct)
    {
        if (!IsConfigured) return null;
        var model = await ModelAsync(ct);
        if (model == null) return null;

        JsonObject Body(bool withSchema)
        {
            var body = new JsonObject
            {
                ["model"] = model,
                ["max_tokens"] = maxTokens,
                ["temperature"] = 0.2,
                ["messages"] = new JsonArray
                {
                    new JsonObject { ["role"] = "system", ["content"] = system },
                    new JsonObject { ["role"] = "user", ["content"] = userPrompt },
                },
            };
            if (!_thinking) body["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false };
            if (withSchema)
                body["response_format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject { ["name"] = purpose, ["strict"] = true, ["schema"] = schema.DeepClone() },
                };
            return body;
        }

        var useSchema = !_schemaRejected;
        var reply = await OpenAiChat.SendAsync(_http, _baseUrl, _apiKey, Body(useSchema), ct);
        if (useSchema && reply.Status == System.Net.HttpStatusCode.BadRequest)
        {
            _logger.LogWarning("{Purpose} via {Model}: response_format abgelehnt ({Error}) — weiter ohne Schema.",
                purpose, model, reply.Error);
            _schemaRejected = true;
            reply = await OpenAiChat.SendAsync(_http, _baseUrl, _apiKey, Body(false), ct);
        }
        if (reply.Error != null)
        {
            _logger.LogWarning("{Purpose} via {Model} fehlgeschlagen: {Error}", purpose, model, reply.Error);
            return null;
        }
        if (reply.FinishReason == "length")
        {
            _logger.LogWarning("{Purpose} via {Model} am Token-Deckel abgeschnitten.", purpose, model);
            return null;
        }
        var json = OpenAiChat.ExtractJsonObject(reply.Content);
        if (json == null) _logger.LogWarning("{Purpose} via {Model}: keine JSON-Antwort.", purpose, model);
        return json;
    }

    /// <summary>Eingestelltes Modell, sonst das erste unter <c>/models</c> (einmal gefragt, dann gemerkt).</summary>
    private async Task<string?> ModelAsync(CancellationToken ct)
    {
        if (_configuredModel != null) return _configuredModel;
        if (_resolvedModel != null) return _resolvedModel;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl.TrimEnd('/') + "/models");
            if (_apiKey != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            using var response = await _http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TextLlm: {Url}/models antwortet {Status}", _baseUrl, (int)response.StatusCode);
                return null;
            }
            using var doc = JsonDocument.Parse(text);
            _resolvedModel = doc.RootElement.GetProperty("data").EnumerateArray()
                .Select(m => m.GetProperty("id").GetString()).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
            if (_resolvedModel != null) _logger.LogInformation("TextLlm: benutze Modell {Model} von {Url}", _resolvedModel, _baseUrl);
            return _resolvedModel;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TextLlm: Modellliste von {Url} nicht lesbar", _baseUrl);
            return null;
        }
    }

    /// <summary>Dasselbe Schema wie <see cref="ClaudeJsonClient"/> — die Aufrufer parsen beide Antworten gleich.</summary>
    internal static JsonNode HintSchema() => JsonNode.Parse("""
        {"type":"object","properties":{"hint1":{"type":"string"},"hint2":{"type":"string"},"hint3":{"type":"string"}},
         "required":["hint1","hint2","hint3"],"additionalProperties":false}
        """)!;

    internal static JsonNode TranslationSchema() => JsonNode.Parse("""
        {"type":"object","properties":{"items":{"type":"array","items":{"type":"object",
          "properties":{"ply":{"type":"integer"},"text":{"type":"string"}},"required":["ply","text"],"additionalProperties":false}}},
         "required":["items"],"additionalProperties":false}
        """)!;
}

/// <summary>Welcher Client Tipps und Übersetzungen macht — EINE Regel für die API und <c>tools/LibraryImport</c>.</summary>
public static class TextJsonClients
{
    /// <summary>Eigene Hardware (<c>TextLlm:BaseUrl</c>) vor Claude (<c>Anthropic:TextApiKey</c>); ist keins von beiden
    /// gesetzt, ein Claude-Client ohne Schlüssel (<see cref="IClaudeJsonClient.IsConfigured"/> = false, alles aus).</summary>
    public static IClaudeJsonClient Create(IConfiguration config, ILoggerFactory loggers, HttpClient? http = null)
    {
        if (!string.IsNullOrWhiteSpace(config["TextLlm:BaseUrl"]))
            return new OpenAiJsonClient(http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) }, config,
                loggers.CreateLogger<OpenAiJsonClient>());
        return new ClaudeJsonClient(config, loggers.CreateLogger<ClaudeJsonClient>());
    }
}
