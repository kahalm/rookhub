using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Services;
using RookHub.Api.Authorization;
using RookHub.Api.Models;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Scoresheet;

namespace RookHub.Api.Controllers.League;

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
    /// Statt <c>fileIds</c> geht <c>pages</c> (0.754.0): je Seite <c>{fileId, rotate, crop, views}</c> — das Bild wird um
    /// <c>rotate</c> (0/90/180/270) im Uhrzeigersinn gedreht und auf <c>crop</c> ([links, oben, rechts, unten] als Anteile
    /// 0…1 des GEDREHTEN Bilds) zugeschnitten; das Ergebnis ist das gespeicherte Foto, auf das sich die Kästen beziehen.
    /// <c>views</c> (gleiche Form) werden weitere Ansichten derselben Seite — für Fotos mit zwei Exemplaren nebeneinander
    /// (Weiß + Schwarz), dieselbe <c>fileId</c> darf dabei mehrfach vorkommen.
    /// 400 <c>reason</c> ∈ noFile/tooManyPages/tooManyViews/invalidLayout/invalidTransform/unsupportedImage/
    /// invalidTranscription/noMoves/clubGameNotFound/unknownUser/mixedClubs.
    /// Die Einlesung gehört dem Verein des Stapels (Mandanten-Schritt 2026-10-07).
    /// </summary>
    [HttpPost("manual-scan")]
    public async Task<IActionResult> ManualScan([FromBody] ManualScanRequest req, [FromServices] AppDbContext db,
        [FromServices] ScoresheetScanService scans, CancellationToken ct)
    {
        var sheets = req.Pages is { Count: > 0 } given ? given
            : req.FileIds is { Count: > 0 } plain ? plain.Select(id => new ManualScanPage(id)).ToList()
            : null;
        if (sheets == null) return BadRequest(new { reason = "noFile" });
        if (!await db.AppUsers.AnyAsync(u => u.Id == req.UserId, ct)) return BadRequest(new { reason = "unknownUser" });
        var ids = sheets.SelectMany(p => (p.Views ?? []).Select(v => v.FileId).Prepend(p.FileId)).Distinct().ToList();
        var files = await db.LeagueBatchUploadFiles.AsNoTracking().Where(f => ids.Contains(f.Id))
            .Select(f => new { f.Id, f.Data, f.ContentType, f.FileName, f.Batch.ClubId }).ToListAsync(ct);
        if (files.Count != ids.Count) return BadRequest(new { reason = "noFile" });
        // Die Einlesung gehört dem Verein des Stapels (Mandanten-Schritt 2026-10-07) — Bilder aus Stapeln zweier Vereine
        // ergeben keine Einlesung.
        if (files.Select(f => f.ClubId).Distinct().Count() != 1) return BadRequest(new { reason = "mixedClubs" });
        var byId = files.ToDictionary(f => f.Id);
        var withLayout = req.Pages is { Count: > 0 } && sheets.Any(p => p.Views is { Count: > 0 });
        var pages = new List<ScoresheetUpload>();
        for (var i = 0; i < sheets.Count; i++)
            foreach (var part in (sheets[i].Views ?? []).Select(v => new ManualScanPage(v.FileId, v.Rotate, v.Crop)).Prepend(sheets[i]))
            {
                var f = byId[part.FileId];
                if (ManualTransform(f.Data, part) is not { } data) return BadRequest(new { reason = "invalidTransform" });
                var changed = !ReferenceEquals(data, f.Data);
                pages.Add(new ScoresheetUpload(data, changed ? "image/jpeg" : f.ContentType, f.FileName, withLayout ? i + 1 : 0));
            }
        var (scan, reason) = await scans.CreateManualAsync(req.UserId, pages, req.Transcription.GetRawText(), req.ClubGameId, ct,
            files[0].ClubId);
        return scan == null ? BadRequest(new { reason }) : Ok(scan);
    }

    /// <summary>Das Bild wie verlangt gedreht/zugeschnitten; unverändert (dieselbe Instanz), wenn nichts verlangt ist,
    /// <c>null</c> bei ungültigem Winkel/Ausschnitt.</summary>
    internal static byte[]? ManualTransform(byte[] data, ManualScanPage part)
    {
        var rotate = part.Rotate ?? 0;
        if (rotate == 0 && part.Crop == null) return data;
        if (part.Crop is { } c && c.Count != 4) return null;
        var crop = part.Crop is { } r ? (r[0], r[1], r[2], r[3]) : ((double, double, double, double)?)null;
        return ScoresheetImage.Transform(data, rotate, crop);
    }

    public sealed record ManualScanRequest(int UserId, List<int>? FileIds, System.Text.Json.JsonElement Transcription, int? ClubGameId,
        List<ManualScanPage>? Pages = null);

    public sealed record ManualScanPage(int FileId, int? Rotate = null, List<double>? Crop = null, List<ManualScanView>? Views = null);

    public sealed record ManualScanView(int FileId, int? Rotate = null, List<double>? Crop = null);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) => await batches.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
