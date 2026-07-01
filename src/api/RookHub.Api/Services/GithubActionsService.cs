using System.Collections.Concurrent;
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
    private readonly IHttpClientFactory _httpFactory;
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

    /// <summary>Per Push gemeldete laufende Build-Infos je Repo (für Stacks, die rookhub nicht per HTTP
    /// erreichen kann — z. B. log-watcher in eigenem Docker-Netz). Prozessweit; ein neuer Report überschreibt.</summary>
    private static readonly ConcurrentDictionary<string, BuildInfo> _reportedBuilds = new();

    /// <summary>Meldet die laufende Build-SHA/Ref eines Repos (aufgerufen vom Build-Report-Endpoint).</summary>
    public void ReportBuild(string repo, string? sha, string? refName)
        => _reportedBuilds[repo] = new BuildInfo(sha, refName);

    private static BuildInfo? GetReportedBuild(string repo)
        => _reportedBuilds.TryGetValue(repo, out var b) ? b : null;

    private static IEnumerable<string> GetReportedRepos() => _reportedBuilds.Keys;

    public GithubActionsService(HttpClient http, IHttpClientFactory httpFactory, IConfiguration config, IMemoryCache cache, ILogger<GithubActionsService> logger)
    {
        _http = http;
        _httpFactory = httpFactory;
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
        var runsTask = Task.WhenAll(repos.Select(r => FetchRepoAsync(owner, r, token, ct)));
        // Parallel dazu: welche Build-SHA/Ref läuft aktuell je Stack? (crawler/piratechess/bot; best-effort)
        var buildInfoTask = ResolveRunningBuildsAsync(ct);
        await Task.WhenAll(runsTask, buildInfoTask);

        var running = buildInfoTask.Result;
        var results = new List<CiRepoDto>();
        foreach (var r in runsTask.Result)
        {
            var repoDto = r;
            if (running.TryGetValue(r.Repo, out var bi) && (bi.Sha is { Length: > 0 } || bi.Ref is { Length: > 0 }))
            {
                repoDto = r with { RunningSha = bi.Sha, RunningRef = bi.Ref };
                // Läuft der Build, ist aber NICHT unter den (Top-5-)Runs → gezielt per head_sha nachladen
                // und als zusätzliche (6.) Zeile anhängen, damit man immer sieht, was gerade läuft.
                if (repoDto.Error is null && bi.Sha is { Length: > 0 }
                    && !repoDto.Runs.Any(run => RunMatchesBuild(run, bi)))
                {
                    var extra = await FetchRunByShaAsync(owner, r.Repo, token, bi, ct);
                    if (extra != null)
                        repoDto = repoDto with { Runs = repoDto.Runs.Append(extra).ToList() };
                }
            }
            results.Add(repoDto);
        }
        return new CiOverviewDto(true, results, DateTime.UtcNow);
    }

    /// <summary>Passt ein Run zur laufenden Build-Info? SHA-Präfix-tolerant + Ref (falls gemeldet).</summary>
    private static bool RunMatchesBuild(CiRunDto run, BuildInfo bi)
    {
        if (bi.Sha is not { Length: > 0 } sha || run.HeadSha is not { Length: > 0 } head) return false;
        var shaMatch = sha == head || sha.StartsWith(head) || head.StartsWith(sha);
        if (!shaMatch) return false;
        return string.IsNullOrEmpty(bi.Ref) || run.Ref == bi.Ref;
    }

    /// <summary>Lädt den (einen) Workflow-Run zur laufenden SHA gezielt nach (für die „6. Zeile", wenn er
    /// aus den letzten 5 herausgefallen ist). Wählt bei mehreren den ref-passenden. Best-effort → null.</summary>
    private async Task<CiRunDto?> FetchRunByShaAsync(string owner, string repo, string token, BuildInfo bi, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"/repos/{owner}/{repo}/actions/runs?head_sha={bi.Sha}&per_page=10&exclude_pull_requests=true");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var payload = await resp.Content.ReadFromJsonAsync<RunsResponse>(GithubJson, ct);
            var items = payload?.WorkflowRuns ?? new List<RunItem>();
            if (items.Count == 0) return null;
            var tagBySha = await GetTagsByShaAsync(owner, repo, token, ct);
            var mapped = items.Select(i => MapRun(i, tagBySha)).ToList();
            // Ref-passenden bevorzugen (master-Push vs. gleichnamiger Tag teilen die SHA).
            return mapped.FirstOrDefault(m => string.IsNullOrEmpty(bi.Ref) || m.Ref == bi.Ref) ?? mapped[0];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Nachladen des laufenden Runs für {Owner}/{Repo} fehlgeschlagen", owner, repo);
            return null;
        }
    }

    private static CiRunDto MapRun(RunItem r, Dictionary<string, string> tagBySha)
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
    }

    /// <summary>Fragt bei den erreichbaren Stacks (crawler/piratechess/bot) deren build-info-Endpoint ab
    /// → welche Commit-SHA/Ref läuft dort GERADE. Rein best-effort: jeder Fehler/Timeout → kein Eintrag
    /// (die UI markiert dann nichts für dieses Repo). Das rookhub-Frontend meldet seine SHA selbst im Browser.</summary>
    private async Task<Dictionary<string, BuildInfo>> ResolveRunningBuildsAsync(CancellationToken ct)
    {
        var targets = new List<(string Repo, string? Url, string? HeaderName, string? HeaderValue)>();

        var crawlerBase = _config["Crawler:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(crawlerBase))
            targets.Add(("chessresults_crawler", Combine(crawlerBase, "api/health/build-info"), "X-Api-Key", _config["Crawler:ApiKey"]));

        var pirateBase = _config["Chessable:ApiUrl"];
        if (!string.IsNullOrWhiteSpace(pirateBase))
            targets.Add(("piratechess_docker", Combine(pirateBase, "api/chessable/direct/build-info"), "X-Service-Key", _config["Chessable:ServiceKey"]));

        var botWebhook = _config["SchachBot:WebhookUrl"];
        if (!string.IsNullOrWhiteSpace(botWebhook) && Uri.TryCreate(botWebhook, UriKind.Absolute, out var botUri))
            targets.Add(("schach-bot", $"{botUri.Scheme}://{botUri.Authority}/webhook/build-info", null, null));

        // rookhub selbst: das Frontend liefert /build-info.json (im internen Netz erreichbar) → so kennt
        // der Server auch die laufende rookhub-SHA und kann den Run ggf. als 6. Zeile nachladen.
        var selfUrl = _config["Frontend:BuildInfoUrl"];
        if (string.IsNullOrWhiteSpace(selfUrl)) selfUrl = "http://rookhub:8080/build-info.json";
        targets.Add(("rookhub", selfUrl, null, null));

        var pairs = await Task.WhenAll(targets.Select(async t =>
            (t.Repo, Info: await FetchBuildInfoAsync(t.Url, t.HeaderName, t.HeaderValue, ct))));

        var map = new Dictionary<string, BuildInfo>();
        // Zuerst per Push gemeldete Build-Infos (z. B. log-watcher, das rookhub nicht erreichen kann) —
        // ein direkt abgefragter Wert (unten) hat Vorrang und überschreibt.
        foreach (var repo in GetReportedRepos())
            if (GetReportedBuild(repo) is { } rep) map[repo] = rep;
        foreach (var (repo, info) in pairs)
            if (info is not null) map[repo] = info;
        return map;
    }

    private static string Combine(string baseUrl, string path) => $"{baseUrl.TrimEnd('/')}/{path}";

    private async Task<BuildInfo?> FetchBuildInfoAsync(string? url, string? headerName, string? headerValue, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            using var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(4);   // Stack-Ausfall darf die CI-Übersicht nicht hängen lassen
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(headerName) && !string.IsNullOrEmpty(headerValue))
                req.Headers.TryAddWithoutValidation(headerName, headerValue);
            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var bi = await resp.Content.ReadFromJsonAsync<BuildInfo>(GithubJson, ct);
            return bi;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "build-info-Abruf von {Url} fehlgeschlagen (Stack evtl. nicht erreichbar/altes Image)", url);
            return null;
        }
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
                .Select(r => MapRun(r, tagBySha))
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

    /// <summary>Antwort der stack-eigenen build-info-Endpoints: <c>{ "sha": …, "ref": … }</c>.</summary>
    private record BuildInfo(string? Sha, string? Ref);
}
