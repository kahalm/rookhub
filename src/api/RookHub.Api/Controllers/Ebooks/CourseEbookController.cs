using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Services.Ebooks;

namespace RookHub.Api.Controllers.Ebooks;

/// <summary>Kurslinie ↔ E-Book-Diagramm (Wunsch 2026-10-15, <see cref="CourseEbookLinker"/>).</summary>
[ApiController]
[Route("api/courses/{bookId:int}/ebook-refs")]
[Authorize]
public class CourseEbookController : BaseApiController
{
    private readonly CourseEbookLinker _linker;

    public CourseEbookController(CourseEbookLinker linker) => _linker = linker;

    /// <summary>Wo die Linien des Kurses im Buch stehen (nur Bücher, die der Aufrufer sehen darf). 404 ohne Kurszugang.</summary>
    [HttpGet]
    public async Task<ActionResult<List<CourseLineEbookRefDto>>> Get(int bookId, CancellationToken ct) =>
        Ok(await _linker.GetRefsAsync(GetUserId(), bookId, IsAdmin, ct));

    /// <summary>„Mit E-Book verknüpfen“ (Besitzer/Admin): Linien über die Stellung den Diagrammen zuordnen.
    /// Ohne <c>ebookId</c> sucht der Server das passendste Buch selbst.</summary>
    [HttpPost]
    public async Task<ActionResult<CourseEbookLinkResultDto>> Link(int bookId, [FromBody] LinkCourseEbookInputDto? dto, CancellationToken ct) =>
        Ok(await _linker.LinkAsync(GetUserId(), bookId, IsAdmin, dto?.EbookId, ct));
}
