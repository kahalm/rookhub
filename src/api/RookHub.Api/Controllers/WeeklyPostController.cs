using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Controllers;

/// <summary>
/// „Wochenpost": öffentlich sichtbare, terminierte PGN-Posts (Abbildung der schach-bot-Wochenposts).
/// Lesen ist anonym; Upload/Bearbeiten/Löschen nur für Admins. Termin-Vorschlag (letzter + 7 Tage,
/// gleiche Uhrzeit, Standard 19:00) macht das Frontend aus der Liste.
/// </summary>
[ApiController]
[Route("api/weekly-posts")]
public class WeeklyPostController : BaseApiController
{
    private readonly AppDbContext _db;
    private readonly WeeklyPostService _progress;
    public WeeklyPostController(AppDbContext db, WeeklyPostService progress)
    {
        _db = db;
        _progress = progress;
    }

    private bool IsAdmin() => User?.IsInRole("Admin") ?? false;
    private static bool IsPublished(WeeklyPost w) => w.ScheduledAt <= DateTime.UtcNow;

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var admin = IsAdmin();
        var posts = await _db.WeeklyPosts
            .Where(w => admin || w.ScheduledAt <= DateTime.UtcNow)
            .OrderByDescending(w => w.ScheduledAt)
            .Select(w => new WeeklyPostDto
            {
                Id = w.Id,
                Title = w.Title,
                FileName = w.FileName,
                FileSize = w.FileSize,
                ScheduledAt = w.ScheduledAt,
                CreatedAt = w.CreatedAt,
                UpdatedAt = w.UpdatedAt,
            })
            .ToListAsync();
        return Ok(posts);
    }

    [AllowAnonymous]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var w = await _db.WeeklyPosts.FindAsync(id);
        if (w == null || (!IsAdmin() && !IsPublished(w)))
            return NotFound(new { message = "Weekly post not found." });
        return Ok(new WeeklyPostDetailDto
        {
            Id = w.Id,
            Title = w.Title,
            FileName = w.FileName,
            FileSize = w.FileSize,
            ScheduledAt = w.ScheduledAt,
            CreatedAt = w.CreatedAt,
            UpdatedAt = w.UpdatedAt,
            PgnContent = w.PgnContent,
        });
    }

    /// <summary>
    /// Puzzles des Wochenposts zum Durchspielen (sequenziell). Das gespeicherte PGN wird on-the-fly
    /// in Puzzles geparst (gleiche Logik wie Bücher); keine Fortschritts-Speicherung, beliebige Retrys.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("{id}/puzzles")]
    public async Task<IActionResult> GetPuzzles(int id)
    {
        var w = await _db.WeeklyPosts.FindAsync(id);
        if (w == null || (!IsAdmin() && !IsPublished(w)))
            return NotFound(new { message = "Weekly post not found." });

        var parsed = PgnImportService.ParsePgn(w.FileName, w.PgnContent).Puzzles;
        var puzzles = parsed.Select((p, i) => new BookPuzzleDto
        {
            Id = i,                       // lokaler Index (kein DB-Datensatz)
            LineId = p.LineId,
            BookFileName = w.FileName,
            Round = p.Round,
            Fen = p.Fen,
            Moves = p.Moves,
            StartPly = p.StartPly,
            Title = p.Title,
            Chapter = p.Chapter,
            Comment = p.Comment,
            MoveComments = p.MoveComments,
        }).ToList();

        return Ok(new WeeklyPlayDto { Id = w.Id, Title = w.Title, Puzzles = puzzles });
    }

    /// <summary>Zeichnet ein gespieltes Puzzle des Wochenposts auf (gelöst oder nicht) und gibt den Fortschritt zurück.</summary>
    [Authorize]
    [HttpPost("{id}/attempt")]
    public async Task<ActionResult<WeeklyPostProgressDto>> RecordAttempt(int id, [FromBody] RecordWeeklyAttemptDto dto)
    {
        var w = await _db.WeeklyPosts.FindAsync(id);
        if (w == null || (!IsAdmin() && !IsPublished(w)))
            return NotFound(new { message = "Weekly post not found." });
        try
        {
            return Ok(await _progress.RecordAttemptAsync(id, GetUserId(), dto));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Fortschritt des eingeloggten Users über alle Wochenposts (für die Übersicht) — nur Posts mit
    /// Versuchen. Literal-Route „progress" hat Vorrang vor „{id}".
    /// </summary>
    [Authorize]
    [HttpGet("progress")]
    public async Task<ActionResult<List<WeeklyPostProgressDto>>> GetAllProgress()
        => Ok(await _progress.GetAllProgressAsync(GetUserId()));

    /// <summary>Aggregierte Ergebnisse eines Wochenposts (wer wie weit + Gesamtzeit) — für die Discord-Anzeige.</summary>
    [AllowAnonymous]
    [HttpGet("{id}/results")]
    public async Task<ActionResult<WeeklyPostResultsDto>> GetResults(int id)
    {
        var w = await _db.WeeklyPosts.FindAsync(id);
        if (w == null || (!IsAdmin() && !IsPublished(w)))
            return NotFound(new { message = "Weekly post not found." });
        try
        {
            return Ok(await _progress.GetResultsAsync(id));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>Fortschritt des eingeloggten Users für diesen Wochenpost.</summary>
    [Authorize]
    [HttpGet("{id}/progress")]
    public async Task<ActionResult<WeeklyPostProgressDto>> GetProgress(int id)
    {
        var w = await _db.WeeklyPosts.FindAsync(id);
        if (w == null || (!IsAdmin() && !IsPublished(w)))
            return NotFound(new { message = "Weekly post not found." });
        try
        {
            return Ok(await _progress.GetProgressAsync(id, GetUserId()));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("/api/admin/weekly-posts")]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(RepertoireService.MaxFileSize)]
    public async Task<IActionResult> Create(IFormFile file, [FromForm] DateTime scheduledAt, [FromForm] string? title, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided." });
        if (file.Length > RepertoireService.MaxFileSize)
            return BadRequest(new { message = $"File exceeds {RepertoireService.MaxFileSize / 1024 / 1024} MB limit." });

        string content;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
            content = await reader.ReadToEndAsync(ct);

        if (!RepertoireService.LooksLikePgn(content))
            return BadRequest(new { message = "File does not appear to be valid PGN content." });

        var safeName = SanitizeFileName(file.FileName);
        var finalTitle = string.IsNullOrWhiteSpace(title)
            ? Regex.Replace(safeName, @"\.pgn$", "", RegexOptions.IgnoreCase).Replace('_', ' ').Trim()
            : title.Trim();
        if (finalTitle.Length == 0) finalTitle = safeName;
        if (finalTitle.Length > 300) finalTitle = finalTitle[..300];

        var now = DateTime.UtcNow;
        var post = new WeeklyPost
        {
            Title = finalTitle,
            FileName = safeName,
            PgnContent = content,
            FileSize = file.Length,
            // Puzzle-Anzahl einmal beim Upload berechnen (PGN ist danach unveränderlich) → spart den
            // teuren LONGTEXT-Parse auf den Lese-/Aufzeichnungspfaden.
            PuzzleCount = PgnImportService.ParsePgn(safeName, content).Puzzles.Count,
            ScheduledAt = scheduledAt == default ? now : scheduledAt,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.WeeklyPosts.Add(post);
        await _db.SaveChangesAsync();

        return Ok(ToDto(post));
    }

    [HttpPut("/api/admin/weekly-posts/{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateWeeklyPostDto dto)
    {
        var post = await _db.WeeklyPosts.FindAsync(id);
        if (post == null)
            return NotFound(new { message = "Weekly post not found." });

        if (dto.Title != null)
        {
            var t = dto.Title.Trim();
            if (t.Length > 0) post.Title = t.Length > 300 ? t[..300] : t;
        }
        if (dto.ScheduledAt.HasValue && dto.ScheduledAt.Value != default)
            post.ScheduledAt = dto.ScheduledAt.Value;

        post.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(ToDto(post));
    }

    [HttpDelete("/api/admin/weekly-posts/{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var post = await _db.WeeklyPosts.FindAsync(id);
        if (post == null)
            return NotFound(new { message = "Weekly post not found." });
        _db.WeeklyPosts.Remove(post);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static string SanitizeFileName(string fileName)
    {
        var safe = Regex.Replace(Path.GetFileName(fileName ?? string.Empty), @"[^a-zA-Z0-9_.-]", "_");
        if (string.IsNullOrWhiteSpace(safe)) safe = "upload.pgn";
        return safe.Length > 255 ? safe[..255] : safe;
    }

    private static WeeklyPostDto ToDto(WeeklyPost w) => new()
    {
        Id = w.Id,
        Title = w.Title,
        FileName = w.FileName,
        FileSize = w.FileSize,
        ScheduledAt = w.ScheduledAt,
        CreatedAt = w.CreatedAt,
        UpdatedAt = w.UpdatedAt,
    };
}
