using System.Text;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.DTOs.TrainingVideos;
using RookHub.Api.Models;
using RookHub.Api.Services.TrainingVideos;

namespace RookHub.Api.Controllers.TrainingVideos;

/// <summary>
/// Admin: Trainingsstunden einem Konto zuordnen und einlesen. <c>dryRun</c> ist VORGABE (<c>true</c>) — geschrieben wird
/// nur mit <c>dryRun=false</c>. Idempotent je Konto und Datum (Re-Import ersetzt). Zwei Wege: eine Datei als Rumpf
/// (Skript <c>tools/trainingvideos/import.py</c>) oder alle Dateien des Daten-Ordners (<c>TrainingVideos__DataRoot</c>).
/// </summary>
[ApiController]
[Route("api/admin/training-videos")]
[HasPermission(Permissions.AdminOnly)]
public class TrainingVideosAdminController(TrainingVideoImporter importer) : BaseApiController
{
    /// <summary>Eine Stunde (JSON im Schema 1) für <c>?user=</c> (Id oder Benutzername) einlesen.</summary>
    [HttpPost("import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<TrainingImportReportDto>> Import([FromQuery] string? user, [FromQuery] bool dryRun = true,
        CancellationToken ct = default)
    {
        var owner = await importer.ResolveOwnerAsync(user, ct);
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var json = await reader.ReadToEndAsync(ct);
        return Ok(await importer.ImportAsync(owner.Id, json, dryRun, ct));
    }

    /// <summary>Alle <c>*.json</c> des Daten-Ordners für <c>?user=</c> einlesen.</summary>
    [HttpPost("import-folder")]
    public async Task<ActionResult<TrainingFolderImportDto>> ImportFolder([FromQuery] string? user, [FromQuery] bool dryRun = true,
        CancellationToken ct = default)
    {
        var owner = await importer.ResolveOwnerAsync(user, ct);
        return Ok(new TrainingFolderImportDto(dryRun, owner.Username, await importer.ImportFolderAsync(owner.Id, dryRun, ct)));
    }
}
