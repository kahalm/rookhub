using System.Text.Json;
using RookHub.Api.DTOs;

namespace RookHub.Api.Services;

public class PlayerSearchService
{
    private readonly CrawlerProxyService _crawlerProxy;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PlayerSearchService> _logger;

    public PlayerSearchService(CrawlerProxyService crawlerProxy, IHttpClientFactory httpClientFactory,
        ILogger<PlayerSearchService> logger)
    {
        _crawlerProxy = crawlerProxy;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<PlayerSearchResultDto> SearchAsync(string lastName, string? firstName)
    {
        var crTask = SearchChessResultsAsync(lastName, firstName);
        var fideTask = SearchFideAsync(lastName, firstName);

        await Task.WhenAll(crTask, fideTask);

        return new PlayerSearchResultDto
        {
            ChessResultsResults = await crTask,
            FideResults = await fideTask
        };
    }

    private async Task<List<PlayerSearchItemDto>> SearchChessResultsAsync(string lastName, string? firstName)
    {
        try
        {
            var path = $"/api/players/search?lastName={Uri.EscapeDataString(lastName)}";
            if (!string.IsNullOrWhiteSpace(firstName))
                path += $"&firstName={Uri.EscapeDataString(firstName)}";

            var json = await _crawlerProxy.GetAsync(path);
            var items = new List<PlayerSearchItemDto>();

            if (json.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in json.EnumerateArray())
                {
                    items.Add(new PlayerSearchItemDto
                    {
                        Name = el.GetProperty("name").GetString() ?? "",
                        FideId = el.TryGetProperty("fideId", out var fid) ? fid.GetString() : null,
                        ChessResultsId = el.TryGetProperty("chessResultsId", out var crid) ? crid.GetString() : null,
                        Elo = el.TryGetProperty("elo", out var elo) && elo.ValueKind == JsonValueKind.Number ? elo.GetInt32() : null,
                        Country = el.TryGetProperty("country", out var c) ? c.GetString() : null,
                        Title = el.TryGetProperty("title", out var t) ? t.GetString() : null
                    });
                }
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ChessResults player search failed");
            return [];
        }
    }

    private async Task<List<PlayerSearchItemDto>> SearchFideAsync(string lastName, string? firstName)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("FideSearch");
            var query = string.IsNullOrWhiteSpace(firstName) ? lastName : $"{lastName}, {firstName}";
            var url = $"/ratinglist/search?query={Uri.EscapeDataString(query)}&list_type=fide";

            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonSerializer.Deserialize<JsonElement>(json);

            var items = new List<PlayerSearchItemDto>();

            if (doc.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.EnumerateArray())
                {
                    items.Add(new PlayerSearchItemDto
                    {
                        Name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        FideId = el.TryGetProperty("fideid", out var fid) ? fid.ToString() : null,
                        Elo = el.TryGetProperty("rating", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : null,
                        Country = el.TryGetProperty("country", out var c) ? c.GetString() : null,
                        Title = el.TryGetProperty("title", out var t) ? t.GetString() : null
                    });
                }
            }

            return items.Take(50).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FIDE player search failed");
            return [];
        }
    }
}
