using System.Text.Json;

namespace RookHub.Api.Services;

public class CrawlerProxyService
{
    private readonly HttpClient _httpClient;

    public CrawlerProxyService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<JsonElement> GetAsync(string path)
    {
        var response = await _httpClient.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    public async Task<JsonElement> PostAsync(string path, JsonElement? body = null)
    {
        HttpContent? content = body.HasValue
            ? new StringContent(body.Value.GetRawText(), System.Text.Encoding.UTF8, "application/json")
            : null;

        var response = await _httpClient.PostAsync(path, content);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    public async Task<JsonElement> PostJsonAsync<T>(string path, T body)
    {
        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(path, content);
        response.EnsureSuccessStatusCode();
        var responseJson = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(responseJson);
    }
}
