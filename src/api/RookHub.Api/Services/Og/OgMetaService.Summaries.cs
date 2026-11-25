namespace RookHub.Api.Services.Og;

/// <summary>
/// Roter Faden der Partie-Zusammenfassung als Beschreibung der Link-Vorschau (Wunsch 2026-11-25: „der rote Faden … in der Vorschau vom
/// Link"). Ausgezogen wird er an EINER Stelle (<see cref="SummaryRedThread"/>), gelesen immer über denselben Dienst wie die Seite des
/// Links — also schon gefiltert, wo der Link anonymisiert (<c>/cg/</c>, <c>/ga/</c>, anonymisierte Partie hinter <c>/s/</c>).
/// Welche Routen ihn tragen und warum die übrigen nicht: <c>docs/api/link-vorschau.md</c>, „Roter Faden als Beschreibung".
/// </summary>
public partial class OgMetaService
{
    /// <summary>Übliche Länge einer Vorschau-Beschreibung (Messenger kürzen meist ab ~200 Zeichen).</summary>
    internal const int DescriptionMax = 200;

    /// <summary>Pfad der Zusammenfassung (<c>/…/summary</c>, ohne Query).</summary>
    internal static bool IsSummaryPath(string? path) =>
        path is not null && path.Split('?')[0].TrimEnd('/').EndsWith("/summary", StringComparison.Ordinal);

    /// <summary>Der rote Faden als schlichter, gekürzter Text — oder <c>null</c>.</summary>
    internal static string? RedThreadDescription(string? redThread) =>
        string.IsNullOrWhiteSpace(redThread) ? null : SummaryRedThread.Plain(redThread, DescriptionMax);
}
