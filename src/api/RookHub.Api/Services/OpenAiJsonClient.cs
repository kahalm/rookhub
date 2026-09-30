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
/// Vorlagen-Schalter und hört stattdessen auf <c>reasoning_effort</c>: mit seiner Vorgabe (medium) brauchte ein
/// Drei-Satz-Absatz 73 s und 696 Tokens, mit <c>low</c> 12,7 s und 116 Tokens bei gleicher Übersetzung (gemessen
/// 29.09.2026). Darum gehen beide Schalter mit — welcher greift, entscheidet das Modell; der jeweils andere wird
/// ignoriert.</para>
/// <para>Gestreamt (<see cref="OpenAiChat.SendAsync"/>): vor dem Spark kappt ein Proxy jede Anfrage nach 90 s ohne
/// Antwort.</para>
/// <para>Höchstens <c>TextLlm:MaxConcurrent</c> (Vorgabe <see cref="DefaultMaxConcurrent"/>) Anfragen gleichzeitig — für
/// ALLE Zwecke zusammen (Erklärungen, Nacherzählung, Roast, Tipps, Übersetzungen, Zugvergleich). Der Client ist in der API
/// ein Singleton, die Grenze gilt also für den ganzen Prozess: vorher konnte ein Konto über „Fehler erklären lassen"
/// Tausende Anfragen gleichzeitig an die geteilte Spark schicken (A6-005), und alle anderen Aufträge standen dahinter.
/// Wer darüber liegt, wartet auf einen freien Platz (auch das Werkzeug <c>tools/LibraryImport</c> mit <c>--parallel</c>;
/// dort hebt <c>TextLlm__MaxConcurrent</c> die Grenze).</para>
/// </remarks>
public sealed class OpenAiJsonClient : IClaudeJsonClient
{
    /// <summary>So viele Anfragen gleichzeitig, wenn <c>TextLlm:MaxConcurrent</c> nichts sagt (vLLM rechnet bis rund 64
    /// Folgen gebündelt, der Durchsatz wächst über 5 parallel kaum noch — die Spark ist der Engpass).</summary>
    public const int DefaultMaxConcurrent = 8;

    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly string _baseUrl;
    private readonly string? _apiKey;
    private readonly string? _configuredModel;
    private readonly bool _thinking;
    private readonly SemaphoreSlim _gate;
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
        _gate = new SemaphoreSlim(Math.Clamp(config.GetValue("TextLlm:MaxConcurrent", DefaultMaxConcurrent), 1, 64));
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
        await _gate.WaitAsync(ct);
        try { return await AskGatedAsync(purpose, system, userPrompt, schema, maxTokens, ct); }
        finally { _gate.Release(); }
    }

    private async Task<string?> AskGatedAsync(string purpose, string system, string userPrompt, JsonNode schema, int maxTokens,
        CancellationToken ct)
    {
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
            if (!_thinking)
            {
                body["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false };
                body["reasoning_effort"] = "low";
            }
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
        return json == null ? null : PlainTypography(json);
    }

    /// <summary>
    /// Nimmt dem Modelltext die unsichtbare Typografie, die gpt-oss setzt und die keine Quelle je enthält: geschützte
    /// Bindestriche (U+2011, „h‑Bauer" — in 4 481 Prod-Texten gefunden), weiche Trennstriche (U+00AD, unsichtbar, brechen
    /// Suche und Kopieren), schmale und normale geschützte Leerzeichen (U+202F/U+00A0/U+2009 vor Einheiten) sowie
    /// Ziffernstrich und Minuszeichen (U+2012/U+2212 in Bewertungen). Gedankenstriche bleiben — die stehen auch in den
    /// Quellen. Läuft über die JSON-Antwort, bevor sie zerlegt wird: die Zeichen kommen nur in Textwerten vor.
    /// Denselben Tausch macht die Migration <c>NormalizeLlmTypography</c> einmal für den Bestand (0.597.3).
    /// </summary>
    public static string PlainTypography(string text)
    {
        if (text.AsSpan().IndexOfAny(TypographyChars) < 0) return text;
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\u2010': case '\u2011': case '\u2012': case '\u2212': sb.Append('-'); break;
                case '\u00AD': break;
                case '\u00A0': case '\u2009': case '\u202F': sb.Append(' '); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static readonly System.Buffers.SearchValues<char> TypographyChars =
        System.Buffers.SearchValues.Create("\u2010\u2011\u2012\u2212\u00AD\u00A0\u2009\u202F");

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
