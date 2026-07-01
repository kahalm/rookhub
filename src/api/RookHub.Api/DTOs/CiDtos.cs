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
    string? HeadSha,
    /// <summary>Anzeige-Ref: Branch-Name (z. B. "master") oder Tag-Name (z. B. "v0.232.0") bei Tag-Läufen.</summary>
    string? Ref,
    /// <summary>true = durch einen Tag ausgelöst (Ref ist ein Tag-Name), false = Branch-Push.</summary>
    bool IsTag);

/// <summary>Die letzten Läufe eines Repos (oder ein Fehler, wenn der Abruf scheiterte).</summary>
/// <param name="RunningSha">Commit-SHA des aktuell in diesem Stack LAUFENDEN Images (aus dessen
/// build-info-Endpoint). Für das rookhub-Frontend liefert der Browser das selbst (/build-info.json),
/// daher hier meist null; für crawler/piratechess/bot vom jeweiligen Dienst abgefragt. null = unbekannt.</param>
/// <param name="RunningRef">Ref des laufenden Images (Branch bei :dev, Tag bei :prod) — zusammen mit
/// <paramref name="RunningSha"/> markiert die UI genau den einen bauenden Run.</param>
public record CiRepoDto(string Repo, string? Error, List<CiRunDto> Runs,
    string? RunningSha = null, string? RunningRef = null);

/// <summary>Gesamtübersicht über alle beteiligten Repos. <see cref="Configured"/>=false, wenn kein
/// GitHub-Token hinterlegt ist (dann bleibt <see cref="Repos"/> leer und die UI zeigt einen Hinweis).</summary>
public record CiOverviewDto(bool Configured, List<CiRepoDto> Repos, DateTime FetchedAt);
