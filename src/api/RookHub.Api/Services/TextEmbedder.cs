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
/// <c>Embedding:ApiKey</c>, <c>Embedding:Model</c> (leer = erstes unter <c>/models</c>). Fordert
/// <see cref="Models.CommentEmbedding.Dimensions"/> Werte an; liefert der Server mehr, wird gekürzt und neu normiert
/// (bei Matryoshka-Modellen der vorgesehene Weg), liefert er weniger, ist das ein Fehler.
/// </summary>
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
        var body = new JsonObject
        {
            ["model"] = model,
            ["input"] = new JsonArray(texts.Select(t => (JsonNode?)JsonValue.Create(query ? QueryInstruction + t : t)).ToArray()),
            ["dimensions"] = Models.CommentEmbedding.Dimensions,
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl.TrimEnd('/') + "/embeddings")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (_apiKey != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        try
        {
            using var response = await _http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Embedding: HTTP {Status}: {Body}", (int)response.StatusCode, OpenAiChat.Snippet(text));
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
            _resolvedModel = doc.RootElement.GetProperty("data").EnumerateArray()
                .Select(m => m.GetProperty("id").GetString()).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
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
