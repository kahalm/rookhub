using System.Text.Json;
using RookHub.Api.Exceptions;

namespace RookHub.Api.Services;

public class CrawlerProxyService
{
    private readonly HttpClient _httpClient;

    public CrawlerProxyService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<JsonElement> GetAsync(string path, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync(path, ct);
        await EnsureSuccessOrThrowAsync(response);
        return await ReadJsonAsync(response);
    }

    public async Task<JsonElement> PostAsync(string path, JsonElement? body = null, CancellationToken ct = default)
    {
        HttpContent? content = body.HasValue
            ? new StringContent(body.Value.GetRawText(), System.Text.Encoding.UTF8, "application/json")
            : null;

        var response = await _httpClient.PostAsync(path, content, ct);
        await EnsureSuccessOrThrowAsync(response);
        return await ReadJsonAsync(response);
    }

    public async Task<JsonElement> PostJsonAsync<T>(string path, T body, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(path, content, ct);
        await EnsureSuccessOrThrowAsync(response);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json))
            return JsonSerializer.Deserialize<JsonElement>("{}");
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new CrawlerRequestException(response.StatusCode, body);
        }
    }
}
