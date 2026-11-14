using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Controllers.Engines;

/// <summary>
/// Admin: EINEN fertigen Fehler-Vergleich einer Partie neu erklären lassen (0.971.0) — der Bestand wird nach einer Änderung am
/// Auftrag nie von selbst neu erklärt (jeder Lauf kostet Fable-Aufrufe), einzelne Vergleiche auf Zuruf schon.
/// Siehe <see cref="MoveComparisonService.ReexplainAsync"/>.
/// </summary>
[ApiController]
[Route("api/admin/move-comparisons")]
[HasPermission(Permissions.AdminOnly)]
public class AdminMoveComparisonsController(MoveComparisonService comparisons) : BaseApiController
{
    /// <summary>Zurück auf „wird erklärt“: die Pumpe rechnet fehlende andere Verteidigungen nach und lässt neu begründen; die alte
    /// Begründung bleibt, bis die neue da ist. 202 <c>{ id, status }</c>; 404 unbekannt oder kein Vergleich einer Partie-Analyse;
    /// 409 <c>code</c> <c>not-done</c>, solange er noch rechnet oder gerade erklärt wird.</summary>
    [HttpPost("{id:int}/re-explain")]
    public async Task<IActionResult> Reexplain(int id, CancellationToken ct) => await comparisons.ReexplainAsync(id, ct) switch
    {
        null => NotFound(),
        false => Conflict(new { message = "Comparison is not finished yet", code = "not-done" }),
        _ => Accepted(new { id, status = "explaining" }),
    };
}
