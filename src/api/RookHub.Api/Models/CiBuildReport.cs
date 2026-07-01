namespace RookHub.Api.Models;

/// <summary>
/// Zuletzt per Push gemeldete laufende Build-SHA/Ref eines Stacks, den rookhub nicht selbst per HTTP
/// erreichen kann (z. B. log-watcher in eigenem Docker-Netz; meldet an <c>POST /api/ci/build-report</c>).
/// PERSISTENT (statt nur In-Memory), damit die Admin-CI die laufende Version eines Stacks auch nach einem
/// rookhub-api-Neustart/Deploy sofort kennt — ohne auf den nächsten Push des Stacks warten zu müssen.
/// Ein Eintrag je Repo (Upsert).
/// </summary>
public class CiBuildReport
{
    /// <summary>Repo-Name (Primärschlüssel), z. B. "log-watcher".</summary>
    public string Repo { get; set; } = string.Empty;

    public string? Sha { get; set; }
    public string? Ref { get; set; }

    /// <summary>Zeitpunkt des letzten Reports (UTC).</summary>
    public DateTime ReportedAt { get; set; }
}
