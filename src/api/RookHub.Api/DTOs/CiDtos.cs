namespace RookHub.Api.DTOs;

/// <summary>Ein einzelner GitHub-Actions-Workflow-Lauf (reduziert auf das, was die Admin-Anzeige braucht).</summary>
public record CiRunDto(
    long Id,
    string Name,
    string Title,
    string Branch,
    string Event,
    string Status,          // queued | in_progress | completed
    string? Conclusion,     // success | failure | cancelled | … (nur wenn completed)
    int RunNumber,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string HtmlUrl,
    string? Actor,
    string? HeadSha);

/// <summary>Die letzten Läufe eines Repos (oder ein Fehler, wenn der Abruf scheiterte).</summary>
public record CiRepoDto(string Repo, string? Error, List<CiRunDto> Runs);

/// <summary>Gesamtübersicht über alle beteiligten Repos. <see cref="Configured"/>=false, wenn kein
/// GitHub-Token hinterlegt ist (dann bleibt <see cref="Repos"/> leer und die UI zeigt einen Hinweis).</summary>
public record CiOverviewDto(bool Configured, List<CiRepoDto> Repos, DateTime FetchedAt);
