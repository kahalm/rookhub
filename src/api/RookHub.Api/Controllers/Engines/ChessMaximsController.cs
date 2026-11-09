using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Engines;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Ebooks;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Controllers.Engines;

/// <summary>Die kuratierten Merksätze mit ihren Buchstellen (0.917.0) — für das (i) hinter dem Merksatz einer Erklärung und die
/// Seite „Merksätze“. Offen: die Liste selbst ist nichts Geheimes; je Buchstelle sagt <c>accessible</c>, ob der Aufrufer das Buch
/// öffnen darf (dieselbe Regel wie die Bibliothek, <see cref="EbookAccess.Visible"/>) — nur dann zeigt die App einen Link.</summary>
[ApiController]
[Route("api/maxims")]
[AllowAnonymous]
public class ChessMaximsController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly PermissionResolver? _permissions;

    public ChessMaximsController(AppDbContext db, PermissionResolver? permissions = null)
    {
        _db = db;
        _permissions = permissions;
    }

    [HttpGet]
    public async Task<ActionResult<List<ChessMaximDto>>> List(CancellationToken ct)
    {
        var signedIn = User?.Identity?.IsAuthenticated == true;
        EbookCaller caller = signedIn ? GetUserId() : EbookCaller.Anonymous(Request?.Headers["X-Visitor-Id"].FirstOrDefault());
        var manageAll = signedIn && await HasPermissionAsync(_permissions, Permissions.EbooksManage);
        var ids = ChessMaxims.All.SelectMany(m => m.Sources).Select(s => s.EbookId).Distinct().ToList();
        var visible = (await EbookAccess.Visible(_db, caller, manageAll).Where(b => ids.Contains(b.Id)).Select(b => b.Id)
            .ToListAsync(ct)).ToHashSet();
        return Ok(ChessMaxims.All.Select(m => new ChessMaximDto(m.Id, m.Text, m.Category, m.When, m.TextEn, m.CategoryEn, m.WhenEn,
            m.Sources.Select(s => new ChessMaximSourceDto(s.EbookId, s.Author, s.Title, s.Year, s.Location, s.Page, s.Percent,
                s.Quote, visible.Contains(s.EbookId))).ToList())).ToList());
    }
}
