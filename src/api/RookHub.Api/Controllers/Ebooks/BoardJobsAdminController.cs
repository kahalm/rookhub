using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Authorization;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;

namespace RookHub.Api.Controllers.Ebooks;

/// <summary>Brett-Ausschneide-Aufträge verwalten (Admin, 0.874.0): anlegen (Opus-Schleife), Stand abfragen, zurückziehen.</summary>
[ApiController]
[Route("api/admin/board-jobs")]
[HasPermission(Permissions.EbooksManage)]
public class BoardJobsAdminController : BaseApiController
{
    private readonly BoardCutJobService _service;

    public BoardJobsAdminController(BoardCutJobService service) => _service = service;

    [HttpPost]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<ActionResult<BoardCutJobStatusDto>> Create([FromBody] BoardCutJobCreateDto dto, CancellationToken ct)
        => Ok(await _service.CreateAsync(dto, ct));

    [HttpGet]
    public async Task<ActionResult<List<BoardCutJobStatusDto>>> List([FromQuery] BoardCutState? state, [FromQuery] int take = 100, CancellationToken ct = default)
        => Ok(await _service.ListAsync(state, take, ct));

    [HttpGet("{jobId:guid}")]
    public async Task<ActionResult<BoardCutJobStatusDto>> Get(Guid jobId, CancellationToken ct)
        => Ok(await _service.GetAsync(jobId, ct));

    [HttpPost("{jobId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid jobId, CancellationToken ct)
    {
        await _service.CancelAsync(jobId, ct);
        return NoContent();
    }
}
