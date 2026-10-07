using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Services;
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

    /// <summary>
    /// Bilder eines Stapels OHNE Modell als Liga-Einlesung anlegen (0.684.0, Skill <c>/formulare</c>): die Lesung kommt im
    /// Rumpf (Form der Modell-Antwort), <c>fileIds</c> in Seitenreihenfolge. Die Einlesung gehört <c>userId</c> und steht
    /// dort in LeagueHub zum Prüfen offen; mit <c>clubGameId</c> wird sie gleich an diese Vereinspartie gehängt.
    /// 400 <c>reason</c> ∈ noFile/tooManyPages/unsupportedImage/invalidTranscription/noMoves/clubGameNotFound/unknownUser/mixedClubs.
    /// Die Einlesung gehört dem Verein des Stapels (Mandanten-Schritt 2026-10-07).
    /// </summary>
    [HttpPost("manual-scan")]
    public async Task<IActionResult> ManualScan([FromBody] ManualScanRequest req, [FromServices] AppDbContext db,
        [FromServices] ScoresheetScanService scans, CancellationToken ct)
    {
        if (req.FileIds is not { Count: > 0 } ids) return BadRequest(new { reason = "noFile" });
        if (!await db.AppUsers.AnyAsync(u => u.Id == req.UserId, ct)) return BadRequest(new { reason = "unknownUser" });
        var files = await db.LeagueBatchUploadFiles.AsNoTracking().Where(f => ids.Contains(f.Id))
            .Select(f => new { f.Id, f.Data, f.ContentType, f.FileName, f.Batch.ClubId }).ToListAsync(ct);
        if (files.Count != ids.Distinct().Count()) return BadRequest(new { reason = "noFile" });
        // Die Einlesung gehört dem Verein des Stapels (Mandanten-Schritt 2026-10-07) — Bilder aus Stapeln zweier Vereine
        // ergeben keine Einlesung.
        if (files.Select(f => f.ClubId).Distinct().Count() != 1) return BadRequest(new { reason = "mixedClubs" });
        var pages = ids.Select(id => files.First(f => f.Id == id))
            .Select(f => new ScoresheetUpload(f.Data, f.ContentType, f.FileName)).ToList();
        var (scan, reason) = await scans.CreateManualAsync(req.UserId, pages, req.Transcription.GetRawText(), req.ClubGameId, ct,
            files[0].ClubId);
        return scan == null ? BadRequest(new { reason }) : Ok(scan);
    }

    public sealed record ManualScanRequest(int UserId, List<int>? FileIds, System.Text.Json.JsonElement Transcription, int? ClubGameId);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) => await batches.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
