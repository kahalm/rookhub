using RookHub.Api.DTOs.Courses;

namespace RookHub.Api.Services.Og;

/// <summary>
/// Karte eines geteilten Aufgabenblatts (<c>/w/{token}</c>): Raster aus bis zu sechs Mini-Diagrammen in der Ausrichtung der
/// Aufgabe, Name und Anzahl. Nur Stellungen — Begleittext und Lösung (die der Link mitliefert) stehen nicht im Bild.
/// </summary>
public partial class OgMetaService
{
    internal const int WorksheetBoards = 6;

    internal static OgCard WorksheetCard(SharedWorksheetDto w)
    {
        var n = w.Items.Count;
        var lines = new List<string> { n == 1 ? "1 position" : $"{n} positions" };
        if (n > WorksheetBoards) lines[0] += $" · first {WorksheetBoards} shown";
        lines.Add("Solve online — no account needed");
        return new OgCard
        {
            Boards = w.Items.Take(WorksheetBoards).Select(i => new OgDiagram(i.Fen, i.Orientation == "black")).ToList(),
            Kicker = "Worksheet",
            Title = string.IsNullOrWhiteSpace(w.Name) ? "Worksheet" : w.Name,
            Lines = lines,
        };
    }

    private async Task<SharedWorksheetDto?> SharedWorksheetAsync(string token) =>
        _worksheets is null ? null : await _worksheets.GetSharedAsync(token);

    private async Task<OgPage?> ResolveWorksheetPageAsync(string id, string img, string canonical)
    {
        if (await SharedWorksheetAsync(id) is not { } w) return null;
        var n = w.Items.Count;
        return new OgPage(string.IsNullOrWhiteSpace(w.Name) ? "Worksheet" : w.Name,
            $"{(n == 1 ? "1 chess position" : $"{n} chess positions")} to solve on RookHub — no account needed", $"{img}?c={CardVersion}", canonical);
    }

    private async Task<OgBoard?> ResolveWorksheetBoardAsync(string id)
    {
        if (await SharedWorksheetAsync(id) is not { } w) return null;
        var card = WorksheetCard(w);
        return new OgBoard(card.Boards.FirstOrDefault()?.Fen ?? StartFen, false, Card: card, MaxAge: EditableMaxAge);
    }
}
