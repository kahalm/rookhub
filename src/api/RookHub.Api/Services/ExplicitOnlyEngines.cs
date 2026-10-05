namespace RookHub.Api.Services;

/// <summary>
/// Engines, die NUR rechnen, wenn ein Auftrag sie ausdrücklich nennt (<c>AnalysisJobs:ExplicitOnlyEngineIds</c>, Liste;
/// leer = alle Hintergrund-Engines sind gleichberechtigt, wie bisher). Gedacht für eine Engine mit anderem Maßstab als
/// Stockfish — Lc0 erreicht Tiefe 22 praktisch nie —, die trotzdem in der Hintergrund-Liste stehen muss, damit ein Auftrag
/// sie über <c>EngineId</c> anfordern kann. Ohne diese Liste fiele sie in die automatische Wahl (kürzeste Schlange), in den
/// Failover reihum und in die Platzzahl der Meisterpartien-Analyse.
/// </summary>
public static class ExplicitOnlyEngines
{
    public const string ConfigKey = "AnalysisJobs:ExplicitOnlyEngineIds";

    public static IReadOnlySet<string> From(Microsoft.Extensions.Configuration.IConfiguration? config) =>
        (config?.GetSection(ConfigKey).Get<string[]>() ?? [])
            .Select(s => s?.Trim()).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToHashSet(StringComparer.Ordinal);

    /// <summary>Die Engines, auf die automatisch verteilt werden darf (Reihenfolge bleibt).</summary>
    public static IReadOnlyList<string> Automatic(IReadOnlyList<string> engines, IReadOnlySet<string> explicitOnly) =>
        explicitOnly.Count == 0 ? engines : engines.Where(e => !explicitOnly.Contains(e)).ToList();
}
