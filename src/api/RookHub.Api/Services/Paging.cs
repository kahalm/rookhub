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

    /// <summary>Klemmt Seite/Seitengröße auf gültige Werte. Die Seite ist auch nach OBEN gedeckelt
    /// (<c>int.MaxValue / maxPageSize</c>), damit <c>Skip((page - 1) * pageSize)</c> der Aufrufer nicht
    /// überläuft: <c>?page=2147483647&amp;pageSize=100</c> ergab unchecked -200 → OFFSET -200 →
    /// MariaDB-Syntaxfehler → 500. Eine so große Seite liefert jetzt einfach eine leere Liste.</summary>
    public static (int Page, int PageSize) Normalize(int page, int pageSize, int maxPageSize = DefaultMaxPageSize)
        => (Math.Clamp(page, 1, int.MaxValue / maxPageSize), Math.Clamp(pageSize, 1, maxPageSize));
}
