using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// Aufgabenblätter: die Zwischenablage (Sammelkorb für alles, was man „an ein Aufgabenblatt
/// schickt") und die benannten Blätter, die man später wieder bearbeitet und druckt.
/// </summary>
[ApiController]
[Route("api/worksheets")]
[Authorize]
public class WorksheetController : BaseApiController
{
    private readonly WorksheetService _worksheets;

    public WorksheetController(WorksheetService worksheets) => _worksheets = worksheets;

    /// <summary>Übersicht: Zwischenablage zuerst, dann die benannten Blätter (zuletzt geändert zuerst).</summary>
    [HttpGet]
    public async Task<ActionResult<List<WorksheetSummaryDto>>> List()
        => Ok(await _worksheets.ListAsync(GetUserId()));

    /// <summary>Die Zwischenablage samt Stellungen (wird beim ersten Zugriff angelegt).</summary>
    [HttpGet("clipboard")]
    public async Task<ActionResult<WorksheetDto>> Clipboard()
        => Ok(await _worksheets.GetClipboardAsync(GetUserId()));

    /// <summary>Ein Blatt samt Stellungen.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<WorksheetDto>> Get(int id)
    {
        var sheet = await _worksheets.GetAsync(GetUserId(), id);
        return sheet == null ? NotFound() : Ok(sheet);
    }

    /// <summary>Leeres benanntes Blatt anlegen.</summary>
    [HttpPost]
    public async Task<ActionResult<WorksheetDto>> Create([FromBody] CreateWorksheetDto dto)
        => Ok(await _worksheets.CreateAsync(GetUserId(), dto.Name, dto.PerPage));

    /// <summary>Die Zwischenablage unter einem Namen sichern — die Stellungen wandern mit, die Ablage
    /// ist danach leer. 400, wenn nichts in der Ablage liegt.</summary>
    [HttpPost("clipboard/save")]
    public async Task<ActionResult<WorksheetDto>> SaveClipboard([FromBody] CreateWorksheetDto dto)
    {
        var sheet = await _worksheets.SaveClipboardAsAsync(GetUserId(), dto.Name, dto.PerPage);
        return sheet == null ? BadRequest(new { message = "Clipboard is empty." }) : Ok(sheet);
    }

    /// <summary>Umbenennen / Dichte (Diagramme je Seite) ändern.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<WorksheetDto>> Update(int id, [FromBody] UpdateWorksheetDto dto)
    {
        var sheet = await _worksheets.UpdateAsync(GetUserId(), id, dto);
        return sheet == null ? NotFound() : Ok(sheet);
    }

    /// <summary>Blatt löschen; die Zwischenablage wird dabei nur geleert.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
        => await _worksheets.DeleteAsync(GetUserId(), id) ? NoContent() : NotFound();

    /// <summary>Alle Stellungen eines Blatts entfernen.</summary>
    [HttpDelete("{id:int}/items")]
    public async Task<ActionResult<WorksheetDto>> Clear(int id)
    {
        var sheet = await _worksheets.ClearAsync(GetUserId(), id);
        return sheet == null ? NotFound() : Ok(sheet);
    }

    /// <summary>„An Aufgabenblatt senden": Stellungen anhängen (<c>worksheetId</c> leer = Zwischenablage).</summary>
    [HttpPost("items")]
    public async Task<ActionResult<AddWorksheetItemsResultDto>> AddItems([FromBody] AddWorksheetItemsDto dto)
    {
        var result = await _worksheets.AddItemsAsync(GetUserId(), dto.WorksheetId, dto.Items ?? new());
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>Überschrift/Begleittext/Ausrichtung einer Aufgabe ändern.</summary>
    [HttpPut("{id:int}/items/{itemId:int}")]
    public async Task<ActionResult<WorksheetItemDto>> UpdateItem(int id, int itemId, [FromBody] UpdateWorksheetItemDto dto)
    {
        var item = await _worksheets.UpdateItemAsync(GetUserId(), id, itemId, dto);
        return item == null ? NotFound() : Ok(item);
    }

    /// <summary>Eine Aufgabe vom Blatt nehmen.</summary>
    [HttpDelete("{id:int}/items/{itemId:int}")]
    public async Task<IActionResult> DeleteItem(int id, int itemId)
        => await _worksheets.DeleteItemAsync(GetUserId(), id, itemId) ? NoContent() : NotFound();

    /// <summary>Reihenfolge setzen (Item-IDs in Wunsch-Abfolge).</summary>
    [HttpPut("{id:int}/order")]
    public async Task<ActionResult<WorksheetDto>> Reorder(int id, [FromBody] ReorderWorksheetDto dto)
    {
        var sheet = await _worksheets.ReorderAsync(GetUserId(), id, dto.ItemIds ?? new());
        return sheet == null ? NotFound() : Ok(sheet);
    }
}
