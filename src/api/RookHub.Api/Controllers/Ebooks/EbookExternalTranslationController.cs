using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;

namespace RookHub.Api.Controllers.Ebooks;

/// <summary>
/// Einspeisepunkt für externe Übersetzer (<see cref="EbookExternalTranslationService"/>): ein Buch für eine Zielsprache
/// beanspruchen, die Texte holen, die sonst an die Spark gehen, Übersetzungen (auch in Teilen) zurückgeben und das EPUB
/// mit demselben Code wie der Worker bauen lassen. Wie die übrigen Übersetzungs-Routen <c>ebooks.manage</c>.
/// Werkzeug: <c>tools/EbookTranslateExternal/uebersetzen.py</c>.
/// </summary>
[ApiController]
[Route("api/admin/ebooks/translations/external")]
[HasPermission(Permissions.EbooksManage)]
public class EbookExternalTranslationController : BaseApiController
{
    private readonly EbookExternalTranslationService _service;

    public EbookExternalTranslationController(EbookExternalTranslationService service) => _service = service;

    /// <summary>Laufende externe Ansprüche (auch beim Zusammenbauen gescheiterte).</summary>
    [HttpGet]
    public async Task<ActionResult<List<ExternalTranslationInfoDto>>> List(CancellationToken ct)
        => Ok(await _service.ListAsync(ct));

    /// <summary>Buch + Zielsprache beanspruchen → Stand mit Übersetzungs-Id. Spark läuft gerade / schon fertig / anderer
    /// Übersetzer → 409; Buch in der Zielsprache, Scan, anonymer Upload → 400; unbekannt → 404.</summary>
    [HttpPost("claim")]
    public async Task<ActionResult<ExternalTranslationInfoDto>> Claim([FromBody] ExternalTranslationClaimDto dto, CancellationToken ct)
        => Ok(await _service.ClaimAsync(dto, ct));

    /// <summary>Stand, Auftrag der Spark (<c>systemPrompt</c>) und Anleitung.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ExternalTranslationInfoDto>> Get(int id, CancellationToken ct)
        => Ok(await _service.GetAsync(id, ct));

    /// <summary>Texte seitenweise (<c>limit</c> ≤ 500), nach Nummer; <c>onlyOpen</c> (Vorgabe) nur noch nicht übersetzte.</summary>
    [HttpGet("{id:int}/segments")]
    public async Task<ActionResult<ExternalTranslationSegmentPageDto>> Segments(int id, [FromQuery] int offset = 0, [FromQuery] int limit = 100,
        [FromQuery] bool onlyOpen = true, CancellationToken ct = default)
        => Ok(await _service.SegmentsAsync(id, offset, limit, onlyOpen, ct));

    /// <summary>Übersetzungen <c>[{ index, target }]</c> (≤ 500) — geprüft wie Spark-Antworten; Ablehnungen mit Grund.</summary>
    [HttpPut("{id:int}/segments")]
    public async Task<ActionResult<ExternalTranslationPutResultDto>> PutSegments(int id, [FromBody] List<ExternalTranslationTargetDto> items, CancellationToken ct)
        => Ok(await _service.PutAsync(id, items, ct));

    /// <summary>Übersetztes EPUB bauen → Done. Offene Texte ohne <c>allowMissing</c> → 409 (<c>details.missing</c>).</summary>
    [HttpPost("{id:int}/assemble")]
    public async Task<ActionResult<ExternalTranslationAssembleResultDto>> Assemble(int id, [FromBody] ExternalTranslationAssembleDto? dto, CancellationToken ct)
        => Ok(await _service.AssembleAsync(id, dto ?? new ExternalTranslationAssembleDto(), ct));

    /// <summary>Externen Anspruch zurücknehmen: Zeile wieder wartend (die Spark darf das Buch nehmen), Texte weg → 204.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Release(int id, CancellationToken ct)
    {
        await _service.ReleaseAsync(id, ct);
        return NoContent();
    }
}
