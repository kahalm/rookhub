using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Controllers.Games;

/// <summary>
/// Reparatur der Partieanalysen nach dem Provider-Versatz (0.990.0, <see cref="GameAnalysisRepair"/>): Stellungen mit <c>[]</c>
/// oder zu wenigen Kandidaten wieder öffnen, seitenweise über den Cursor. Nur Admins, nie aus einer Impersonation.
/// </summary>
[ApiController]
[Route("api/admin/game-analysis-repair")]
[HasPermission(Permissions.AdminOnly)]
public class AdminGameAnalysisRepairController(GameAnalysisRepair repair) : BaseApiController
{
    /// <summary>Trockenlauf: ändert nichts. <c>?after=&lt;Stellungs-Id&gt;&amp;maxAnalyses=50&amp;maxScan=5000</c>.</summary>
    [HttpGet]
    [DenyWhileImpersonating]
    public async Task<IActionResult> DryRun([FromQuery] int after = 0, [FromQuery] int maxAnalyses = 50,
        [FromQuery] int maxScan = 5000, CancellationToken ct = default)
        => Ok(await repair.RunAsync(new GameAnalysisRepairRequest(false, after, maxAnalyses, maxScan), GetUserId(), ct));

    /// <summary>Funde wieder öffnen; Parameter wie der Trockenlauf, dazu <c>dropMistakeComparisons</c> (Vorgabe false).</summary>
    [HttpPost("apply")]
    [DenyWhileImpersonating]
    public async Task<IActionResult> Apply([FromQuery] int after = 0, [FromQuery] int maxAnalyses = 50,
        [FromQuery] int maxScan = 5000, [FromQuery] bool dropMistakeComparisons = false, CancellationToken ct = default)
        => Ok(await repair.RunAsync(new GameAnalysisRepairRequest(true, after, maxAnalyses, maxScan, dropMistakeComparisons),
            GetUserId(), ct));
}
