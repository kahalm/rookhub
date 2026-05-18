using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/extension")]
[Authorize]
public class ExtensionController : ControllerBase
{
    private readonly RepertoireService _repertoireService;

    public ExtensionController(RepertoireService repertoireService) => _repertoireService = repertoireService;

    private int GetUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("repertoires")]
    public async Task<ActionResult<List<ExtensionRepertoireDto>>> GetRepertoires()
    {
        return Ok(await _repertoireService.GetExtensionListAsync(GetUserId()));
    }

    [HttpGet("repertoires/{id}/pgn")]
    public async Task<IActionResult> GetPgn(int id)
    {
        try
        {
            var pgn = await _repertoireService.GetCombinedPgnAsync(id, GetUserId());
            return Content(pgn, "text/plain");
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
