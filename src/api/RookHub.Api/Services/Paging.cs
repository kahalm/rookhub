namespace RookHub.Api.Services;

/// <summary>
/// Geteilte Seiten-Normalisierung für paginierte Listen-Endpoints: <c>page ≥ 1</c>,
/// <c>pageSize</c> in <c>[1, max]</c> (Default-Obergrenze 100). Vorher lag dieselbe Klemm-Logik
/// in fünf Service-Kopien mit stilistischer Drift (Math.Max/Clamp vs. if-Ketten) — eine
/// Policy-Änderung (Obergrenze, Off-by-one im Skip) hätte überall einzeln nachgezogen werden müssen.
/// </summary>
public static class Paging
{
    public const int DefaultMaxPageSize = 100;

    /// <summary>Klemmt Seite/Seitengröße auf gültige Werte.</summary>
    public static (int Page, int PageSize) Normalize(int page, int pageSize, int maxPageSize = DefaultMaxPageSize)
        => (Math.Max(1, page), Math.Clamp(pageSize, 1, maxPageSize));
}
