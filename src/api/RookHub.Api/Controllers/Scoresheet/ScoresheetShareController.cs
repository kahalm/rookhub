using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Services.Scoresheet;

namespace RookHub.Api.Controllers.Scoresheet;

/// <summary>
/// „Teilen → RookHub“ für Partieformular-Fotos (Web Share Target, 0.883.0). Android schickt das geteilte Bild an
/// <c>POST /api/scoresheets/share</c> (Ziel aus <c>share_target</c> im Web-Manifest) — ohne Anmeldung, also landet es
/// in der <see cref="ScoresheetShareInbox"/>, und der Browser wird auf die Einlese-Seite mit dem Abholschlüssel
/// weitergeleitet. Die Seite holt die Fotos angemeldet ab und legt sie in die Auswahl, als hätte man sie gewählt.
/// </summary>
[ApiController]
[Route("api/scoresheets/share")]
[Authorize]
public class ScoresheetShareController : BaseApiController
{
    /// <summary>Hier landet der Browser nach dem Teilen (Route der Einlese-Seite).</summary>
    public const string UploadPage = "/games/scoresheet";

    private readonly ScoresheetShareInbox _inbox;

    public ScoresheetShareController(ScoresheetShareInbox inbox) => _inbox = inbox;

    /// <summary>Geteilte Fotos annehmen (multipart, Teile <c>file</c>). Antwortet immer mit 303 auf die Einlese-Seite:
    /// <c>?share=&lt;schlüssel&gt;</c>, oder <c>?shareError=&lt;grund&gt;</c> (<c>noFile</c>, <c>tooManyPages</c>,
    /// <c>tooLarge</c>, <c>unsupportedImage</c>, <c>busy</c>) — der Nutzer kommt aus dem Teilen-Menü und soll eine Seite
    /// sehen, keinen JSON-Fehler.</summary>
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("anonymous-write")]
    [RequestSizeLimit(ScoresheetScanService.MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ScoresheetScanService.MaxUploadRequestBytes)]
    public async Task<IActionResult> Share([FromForm] List<IFormFile>? file)
    {
        var files = (file ?? new()).Where(f => f.Length > 0).ToList();
        string? reason = null;
        if (files.Count == 0) reason = "noFile";
        else if (files.Count > ScoresheetScanService.MaxPages) reason = "tooManyPages";
        else if (files.Any(f => f.Length > ScoresheetScanService.MaxUploadBytes)) reason = "tooLarge";
        else if (files.Any(f => !ScoresheetImage.AcceptedTypes.Contains(f.ContentType ?? ""))) reason = "unsupportedImage";
        if (reason != null) return SeeOther($"{UploadPage}?shareError={reason}");

        var pages = new List<ScoresheetUpload>();
        foreach (var f in files)
        {
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms);
            pages.Add(new ScoresheetUpload(ms.ToArray(), f.ContentType, f.FileName));
        }
        var token = _inbox.Put(pages);
        return SeeOther(token == null ? $"{UploadPage}?shareError=busy" : $"{UploadPage}?share={token}");
    }

    /// <summary>Wie viele Fotos unter dem Schlüssel liegen; 404 unbekannt oder abgelaufen.</summary>
    [HttpGet("{token}")]
    public ActionResult<object> Info(string token)
        => _inbox.Get(token) is { } pages ? Ok(new { count = pages.Count }) : NotFound();

    /// <summary>Ein geteiltes Foto (Bilddaten); 404 unbekannt, abgelaufen oder Index daneben.</summary>
    [HttpGet("{token}/{index:int}")]
    public IActionResult Photo(string token, int index)
    {
        if (_inbox.Get(token) is not { } pages || index < 0 || index >= pages.Count) return NotFound();
        var p = pages[index];
        return File(p.Data, p.ContentType ?? "application/octet-stream", p.FileName);
    }

    /// <summary>Abgeholt: Fotos aus der Ablage nehmen. 204, auch wenn sie schon weg waren.</summary>
    [HttpDelete("{token}")]
    public IActionResult Remove(string token)
    {
        _inbox.Remove(token);
        return NoContent();
    }

    private StatusCodeResult SeeOther(string location)
    {
        Response.Headers.Location = location;
        return StatusCode(StatusCodes.Status303SeeOther);
    }
}
