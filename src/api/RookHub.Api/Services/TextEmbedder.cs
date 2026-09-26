using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RookHub.Api.Services;

/// <summary>Texte → Vektoren (normiert auf Länge 1, <see cref="Models.CommentEmbedding.Dimensions"/> Werte).</summary>
public interface ITextEmbedder
{
    bool IsConfigured { get; }
    string Model { get; }

    /// <summary><c>null</c> bei Fehler. <paramref name="query"/> = Suchfrage (bekommt die Anweisung des Modells
    /// vorangestellt), sonst Dokument.</summary>
    Task<float[][]?> EmbedAsync(IReadOnlyList<string> texts, bool query, CancellationToken ct = default);
}

/// <summary>
/// Embedding-Modell über eine OpenAI-kompatible Schnittstelle (<c>POST /embeddings</c>, vLLM mit einem Pooling-Modell
/// auf dem DGX Spark, z. B. <c>Qwen/Qwen3-Embedding-0.6B</c>). Konfiguration <c>Embedding:BaseUrl</c> (bis <c>/v1</c>),
/// <c>Embedding:ApiKey</c>, <c>Embedding:Model</c> (leer = das erste Modell unter <c>/models</c>, dessen Name „embed"
/// enthält, sonst das erste). Fordert <see cref="Models.CommentEmbedding.Dimensions"/> Werte an; liefert der Server mehr,
/// wird gekürzt und neu normiert (bei Matryoshka-Modellen der vorgesehene Weg), liefert er weniger, ist das ein Fehler.
/// </summary>
/// <remarks>
/// <para><b>Lehnt der Server <c>dimensions</c> ab, geht es ohne weiter</b> (einmal nachgefragt, dann dabei geblieben):
/// vLLM nimmt den Parameter nur, wenn das Modell als Matryoshka-Modell gestartet wurde
/// (<c>--hf-overrides '{"is_matryoshka": true}'</c>), und antwortet sonst 400 — am 2026-09-26 auf dem Spark mit
/// <c>qwen3-embedding-4b</c> gesehen (2560 Werte). Das Kürzen hier ist dasselbe, was der Server täte.</para>
/// <para><b>Auf dem Spark hilft die Modellliste nicht</b>: der Proxy leitet <c>/embeddings</c> an den Embedding-Server,
/// <c>/models</c> an den Chat-Server — dort steht nur das Sprachmodell. <c>Embedding:Model</c> gehört also gesetzt.</para>
/// </remarks>
public sealed class OpenAiTextEmbedder : ITextEmbedder
{
    /// <summary>Qwen3-Embedding ist asymmetrisch: Suchfragen bekommen eine Anweisung, Dokumente nicht.</summary>
    public const string QueryInstruction =
        "Instruct: Given a question or topic about chess, retrieve annotations of chess games that discuss it\nQuery: ";

    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly string _baseUrl;
    private readonly string? _apiKey;
    private readonly string? _configuredModel;
    private string? _resolvedModel;
    private bool _dimensionsRejected;

    public OpenAiTextEmbedder(HttpClient http, IConfiguration config, ILogger logger)
    {
        _http = http;
        _logger = logger;
        _baseUrl = (config["Embedding:BaseUrl"] ?? "").Trim();
        _apiKey = string.IsNullOrWhiteSpace(config["Embedding:ApiKey"]) ? null : config["Embedding:ApiKey"]!.Trim();
        _configuredModel = string.IsNullOrWhiteSpace(config["Embedding:Model"]) ? null : config["Embedding:Model"]!.Trim();
    }

    public bool IsConfigured => _baseUrl.Length > 0;
    public string Model => _configuredModel ?? _resolvedModel ?? "embedding";

    public async Task<float[][]?> EmbedAsync(IReadOnlyList<string> texts, bool query, CancellationToken ct = default)
    {
        if (!IsConfigured || texts.Count == 0) return texts.Count == 0 ? Array.Empty<float[]>() : null;
        var model = await ModelAsync(ct);
        if (model == null) return null;
        JsonObject Body(bool withDimensions)
        {
            var body = new JsonObject
            {
                ["model"] = model,
                ["input"] = new JsonArray(texts.Select(t => (JsonNode?)JsonValue.Create(query ? QueryInstruction + t : t)).ToArray()),
            };
            if (withDimensions) body["dimensions"] = Models.CommentEmbedding.Dimensions;
            return body;
        }
        try
        {
            var useDimensions = !_dimensionsRejected;
            var (status, text) = await PostAsync(Body(useDimensions), ct);
            if (useDimensions && status == System.Net.HttpStatusCode.BadRequest)
            {
                _logger.LogInformation("Embedding: {Model} nimmt kein dimensions ({Body}) — weiter ohne, gekürzt wird hier.",
                    model, OpenAiChat.Snippet(text));
                _dimensionsRejected = true;
                (status, text) = await PostAsync(Body(false), ct);
            }
            if ((int)status is < 200 or > 299)
            {
                _logger.LogWarning("Embedding: HTTP {Status}: {Body}", (int)status, OpenAiChat.Snippet(text));
                return null;
            }
            using var doc = JsonDocument.Parse(text);
            var data = doc.RootElement.GetProperty("data").EnumerateArray()
                .OrderBy(d => d.TryGetProperty("index", out var i) ? i.GetInt32() : 0)
                .Select(d => d.GetProperty("embedding").EnumerateArray().Select(x => x.GetSingle()).ToArray())
                .ToArray();
            if (data.Length != texts.Count) return null;
            var result = new float[data.Length][];
            for (var k = 0; k < data.Length; k++)
            {
                if (data[k].Length < Models.CommentEmbedding.Dimensions)
                {
                    _logger.LogWarning("Embedding: Modell liefert {Got} statt {Want} Werte", data[k].Length, Models.CommentEmbedding.Dimensions);
                    return null;
                }
                result[k] = VectorMath.Normalize(data[k].AsSpan(0, Models.CommentEmbedding.Dimensions).ToArray());
            }
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding von {Url} fehlgeschlagen", _baseUrl);
            return null;
        }
    }

    private async Task<(System.Net.HttpStatusCode Status, string Text)> PostAsync(JsonObject body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl.TrimEnd('/') + "/embeddings")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (_apiKey != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        using var response = await _http.SendAsync(request, ct);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(ct));
    }

    private async Task<string?> ModelAsync(CancellationToken ct)
    {
        if (_configuredModel != null) return _configuredModel;
        if (_resolvedModel != null) return _resolvedModel;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl.TrimEnd('/') + "/models");
            if (_apiKey != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var ids = doc.RootElement.GetProperty("data").EnumerateArray()
                .Select(m => m.GetProperty("id").GetString()).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
            _resolvedModel = ids.FirstOrDefault(id => id!.Contains("embed", StringComparison.OrdinalIgnoreCase)) ?? ids.FirstOrDefault();
            return _resolvedModel;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Embedding: Modellliste von {Url} nicht lesbar", _baseUrl);
            return null;
        }
    }
}

/// <summary>Vektoren ↔ Bytes (float32, little endian — das Format der MariaDB-Spalte <c>VECTOR</c>) und Kosinus.</summary>
public static class VectorMath
{
    public static float[] Normalize(float[] v)
    {
        double sum = 0;
        foreach (var x in v) sum += (double)x * x;
        var norm = Math.Sqrt(sum);
        if (norm <= 0) return v;
        for (var i = 0; i < v.Length; i++) v[i] = (float)(v[i] / norm);
        return v;
    }

    public static byte[] ToBytes(float[] v)
    {
        var bytes = new byte[v.Length * 4];
        for (var i = 0; i < v.Length; i++)
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), v[i]);
        return bytes;
    }

    public static float[] FromBytes(byte[] bytes)
    {
        var v = new float[bytes.Length / 4];
        for (var i = 0; i < v.Length; i++)
            v[i] = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4));
        return v;
    }

    /// <summary>Kosinus-ABSTAND wie <c>VEC_DISTANCE_COSINE</c> (0 = gleich, 2 = entgegengesetzt).</summary>
    public static double CosineDistance(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            dot += (double)a[i] * b[i];
            na += (double)a[i] * a[i];
            nb += (double)b[i] * b[i];
        }
        return na <= 0 || nb <= 0 ? 1 : 1 - dot / Math.Sqrt(na * nb);
    }
}
