using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.DTOs;

namespace RookHub.Api.Services;

/// <summary>
/// Liefert die letzten GitHub-Actions-Läufe der beteiligten Repos (kahalm/rookhub, -crawler, -bot, …)
/// für die Admin-CI-Übersicht. Ruft die GitHub-REST-API mit einem hinterlegten Token auf und cacht das
/// aggregierte Ergebnis kurz (<c>GitHub:CacheSeconds</c>, Default 4 s) — so bleiben viele 5-s-Polls
/// mehrerer Admins weit unter dem GitHub-Rate-Limit (ein Fetch je Cache-Fenster, nicht je Poll).
/// Ohne Token liefert der Service <see cref="CiOverviewDto.Configured"/>=false (die UI zeigt dann den
/// Hinweis, <c>GitHub__Token</c> zu setzen), damit das knappe unauthentifizierte Limit nicht verbrannt wird.
/// </summary>
public class GithubActionsService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GithubActionsService> _logger;

    private static readonly string[] DefaultRepos =
        { "rookhub", "chessresults_crawler", "schach-bot", "piratechess_docker", "log-watcher" };

    private static readonly JsonSerializerOptions GithubJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private const string CacheKey = "ci_actions_overview";

    public GithubActionsService(HttpClient http, IConfiguration config, IMemoryCache cache, ILogger<GithubActionsService> logger)
    {
        _http = http;
        _config = config;
        _cache = cache;
        _logger = logger;
    }

    public async Task<CiOverviewDto> GetOverviewAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out CiOverviewDto? cached) && cached is not null)
            return cached;

        var overview = await FetchAsync(ct);

        var ttl = TimeSpan.FromSeconds(Math.Clamp(_config.GetValue("GitHub:CacheSeconds", 4), 1, 60));
        _cache.Set(CacheKey, overview, ttl);
        return overview;
    }

    private async Task<CiOverviewDto> FetchAsync(CancellationToken ct)
    {
        var token = _config["GitHub:Token"];
        if (string.IsNullOrWhiteSpace(token))
            return new CiOverviewDto(false, new List<CiRepoDto>(), DateTime.UtcNow);

        var owner = _config["GitHub:Owner"];
        if (string.IsNullOrWhiteSpace(owner)) owner = "kahalm";
        var repos = _config.GetSection("GitHub:Repos").Get<string[]>() is { Length: > 0 } cfg ? cfg : DefaultRepos;

        // Alle Repos parallel abrufen; ein Repo-Fehler kippt nicht die ganze Übersicht.
        var tasks = repos.Select(r => FetchRepoAsync(owner, r, token, ct)).ToList();
        var results = await Task.WhenAll(tasks);
        return new CiOverviewDto(true, results.ToList(), DateTime.UtcNow);
    }

    private async Task<CiRepoDto> FetchRepoAsync(string owner, string repo, string token, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"/repos/{owner}/{repo}/actions/runs?per_page=5&exclude_pull_requests=true");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return new CiRepoDto(repo, $"HTTP {(int)resp.StatusCode}", new List<CiRunDto>());

            var payload = await resp.Content.ReadFromJsonAsync<RunsResponse>(GithubJson, ct);

            // Tags auflösen (Tag-Läufe haben kein head_branch → Ref = Tag-Name via sha-Map).
            var tagBySha = await GetTagsByShaAsync(owner, repo, token, ct);

            var runs = (payload?.WorkflowRuns ?? new List<RunItem>())
                .Take(5)
                .Select(r =>
                {
                    var branch = r.HeadBranch ?? "";
                    var isTag = string.IsNullOrEmpty(branch);
                    var refName = !isTag ? branch
                        : (r.HeadSha != null && tagBySha.TryGetValue(r.HeadSha, out var tag) ? tag : null);
                    return new CiRunDto(
                        r.Id, r.Name ?? "", string.IsNullOrWhiteSpace(r.DisplayTitle) ? (r.Name ?? "") : r.DisplayTitle,
                        branch, r.Event ?? "", r.Status ?? "", r.Conclusion,
                        r.RunNumber, r.CreatedAt, r.UpdatedAt, r.HtmlUrl ?? "", r.Actor?.Login, r.HeadSha,
                        refName, isTag && refName != null);
                })
                .ToList();
            return new CiRepoDto(repo, null, runs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GitHub-Actions-Abruf für {Owner}/{Repo} fehlgeschlagen", owner, repo);
            return new CiRepoDto(repo, "unreachable", new List<CiRunDto>());
        }
    }

    /// <summary>Neueste Tags des Repos als sha→Name-Map (für die Ref-Anzeige der Tag-Läufe).
    /// Fehler → leere Map (dann fällt die Anzeige auf den Branch/Short-SHA zurück).</summary>
    private async Task<Dictionary<string, string>> GetTagsByShaAsync(string owner, string repo, string token, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/repos/{owner}/{repo}/tags?per_page=50");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return new();
            var tags = await resp.Content.ReadFromJsonAsync<List<TagItem>>(GithubJson, ct) ?? new();
            var map = new Dictionary<string, string>();
            foreach (var t in tags)
                if (t.Commit?.Sha is { Length: > 0 } sha && !map.ContainsKey(sha))
                    map[sha] = t.Name;   // erster (neuester) Tag je Commit gewinnt
            return map;
        }
        catch { return new(); }
    }

    // --- GitHub-Rohschema (snake_case via NamingPolicy) ---
    private record RunsResponse(List<RunItem> WorkflowRuns);
    private record RunItem(
        long Id, string? Name, string? DisplayTitle, string? HeadBranch, string? Event,
        string? Status, string? Conclusion, int RunNumber, DateTime CreatedAt, DateTime UpdatedAt,
        string? HtmlUrl, ActorObj? Actor, string? HeadSha);
    private record ActorObj(string Login);
    private record TagItem(string Name, TagCommit? Commit);
    private record TagCommit(string Sha);
}
