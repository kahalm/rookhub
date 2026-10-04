using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Controllers;

/// <summary>Admin-Tab „Uploads" (0.651.0): die LeagueHub-Stapel-Uploads auflisten, als ZIP holen, löschen
/// (<see cref="LeagueBatchUploadService"/>). Dieselben Leute, die die Meldung bekommen (messages.admin).</summary>
[ApiController]
[Route("api/admin/league-uploads")]
[HasPermission(Permissions.MessagesAdmin)]
public class AdminLeagueUploadController(LeagueBatchUploadService batches) : BaseApiController
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await batches.ListAsync(ct));

    /// <summary>ZIP aller Bilder eines Stapels. Erst in eine Zwischendatei (ZipArchive schreibt das Verzeichnis am Ende
    /// synchron, das nimmt Kestrel nicht), die beim Schließen verschwindet.</summary>
    [HttpGet("{id:int}/zip")]
    public async Task<IActionResult> Zip(int id, CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), $"league-batch-{id}-{Guid.NewGuid():N}.zip");
        var fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            if (!await batches.WriteZipAsync(id, fs, ct)) { await fs.DisposeAsync(); return NotFound(); }
            fs.Position = 0;
            return File(fs, "application/zip", $"leaguehub-stapel-{id}.zip");
        }
        catch
        {
            await fs.DisposeAsync();
            throw;
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) => await batches.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
