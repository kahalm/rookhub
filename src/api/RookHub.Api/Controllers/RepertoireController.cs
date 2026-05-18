using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

[ApiController]
[Route("api/repertoires")]
[Authorize]
public class RepertoireController : BaseApiController
{
    private readonly RepertoireService _repertoireService;

    public RepertoireController(RepertoireService repertoireService) => _repertoireService = repertoireService;

    [HttpGet]
    public async Task<ActionResult<List<RepertoireDto>>> GetAll()
    {
        return Ok(await _repertoireService.GetAllAsync(GetUserId()));
    }

    [HttpPost]
    public async Task<ActionResult<RepertoireDto>> Create([FromBody] CreateRepertoireDto dto)
    {
        var result = await _repertoireService.CreateAsync(GetUserId(), dto);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RepertoireDetailDto>> GetById(int id)
    {
        try
        {
            return Ok(await _repertoireService.GetByIdAsync(id, GetUserId()));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<RepertoireDto>> Update(int id, [FromBody] UpdateRepertoireDto dto)
    {
        try
        {
            return Ok(await _repertoireService.UpdateAsync(id, GetUserId(), dto));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _repertoireService.DeleteAsync(id, GetUserId());
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB

    [HttpPost("{id}/files")]
    public async Task<ActionResult<RepertoireFileDto>> UploadFile(int id, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided." });

        if (!Path.GetExtension(file.FileName).Equals(".pgn", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Only .pgn files are allowed." });

        if (file.Length > MaxFileSize)
            return BadRequest(new { message = $"File size exceeds maximum of {MaxFileSize / 1024 / 1024} MB." });

        try
        {
            using var stream = file.OpenReadStream();
            var result = await _repertoireService.UploadFileAsync(id, GetUserId(), file.FileName, stream);
            return Ok(result);
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("{id}/files/{fileId}")]
    public async Task<IActionResult> DownloadFile(int id, int fileId)
    {
        try
        {
            var (fileName, content) = await _repertoireService.DownloadFileAsync(id, fileId, GetUserId());
            return File(System.Text.Encoding.UTF8.GetBytes(content), "application/x-chess-pgn", fileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpDelete("{id}/files/{fileId}")]
    public async Task<IActionResult> DeleteFile(int id, int fileId)
    {
        try
        {
            await _repertoireService.DeleteFileAsync(id, fileId, GetUserId());
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("{id}/pgn")]
    public async Task<IActionResult> GetCombinedPgn(int id)
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
