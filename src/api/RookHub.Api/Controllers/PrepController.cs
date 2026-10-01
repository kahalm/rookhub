using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Authorization;
using RookHub.Api.Models;
using RookHub.Api.Services.Prep;

namespace RookHub.Api.Controllers;

/// <summary>
/// Spielervorbereitung („Prep", 2026-10-01): einen beliebigen Spieler im Partiebestand aus Megabase und Lumbra suchen
/// und vorbereiten. Lesen hinter <see cref="Permissions.PrepView"/>, Einspielen hinter <see cref="Permissions.PrepManage"/>
/// — ohne Recht ist nichts sichtbar. Der Bestand hat keine anonyme Route und keinen Massen-Download.
/// </summary>
[ApiController]
[Route("api/prep")]
[Authorize]
public class PrepController : BaseApiController
{
    /// <summary>Wie die generische <c>/api/</c>-Regel im nginx des Frontends (<c>client_max_body_size 15M</c>).</summary>
    public const int MaxBodyBytes = 15 * 1024 * 1024;
    /// <summary>Entpackt höchstens so viel — ein Paket mit 5 000 Partien hat rund 3 MB.</summary>
    public const int MaxPgnBytes = 64 * 1024 * 1024;

    private readonly PrepImportService _import;

    public PrepController(PrepImportService import) => _import = import;

    // ---- Lesen (Phase 2) -----------------------------------------------------------------------------

    /// <summary>Spieler suchen: Namens-Präfix („Carlsen", „Carlsen, M", „Magnus Carlsen", Umlaute in beiden Schreibweisen) oder
    /// FIDE-ID → <c>{ items[{ id, name, fide, games, firstYear, lastYear, maxElo }] }</c>, meistgespielte zuerst.</summary>
    [HttpGet("players")]
    [HasPermission(Permissions.PrepView)]
    public async Task<IActionResult> Players([FromQuery] string? q, [FromQuery] int? take, [FromServices] PrepPlayerSearch search,
        CancellationToken ct)
    {
        var hits = await search.SearchAsync(q, take ?? PrepPlayerSearch.DefaultTake, ct);
        return Ok(new
        {
            items = hits.Select(h => new
            {
                id = h.Id, name = h.Name, fide = h.FideId, games = h.Games, firstYear = h.FirstYear, lastYear = h.LastYear, maxElo = h.MaxElo,
            }),
        });
    }

    /// <summary>Spielerkarte in der Form der Liga-Karte (Profil, Quellen, letzte Partien, Online-Konten) plus <c>id</c>, <c>games</c>
    /// (im Bestand), <c>loaded</c>/<c>limited</c>/<c>limit</c>/<c>since</c> (Vorgabe: die jüngsten <see cref="PrepCardService.DefaultLimit"/>,
    /// <c>all=true</c>: bis <see cref="PrepCardService.DefaultMax"/>) und <c>twin</c> (Namens-Zwilling ohne FIDE-ID, nur mit <c>twin=true</c>
    /// dabei). Gilt für alle Unterseiten.</summary>
    [HttpGet("player/{id:int}")]
    [HasPermission(Permissions.PrepView)]
    public async Task<IActionResult> Player(int id, [FromQuery] bool? all, [FromQuery] bool? twin, [FromServices] PrepCardService cards,
        CancellationToken ct) =>
        await cards.LoadAsync(id, all == true, twin == true, ct) is { } l ? Ok(await cards.CardAsync(l, ct)) : NotFound();

    /// <summary>Eröffnungsprofil über gefilterte Partien — Filter wie bei der Liga (<c>source</c>, <c>speeds</c>, <c>years</c>,
    /// Online nur gesicherter Konten außer <c>unsure=true</c>).</summary>
    [HttpGet("player/{id:int}/profile")]
    [HasPermission(Permissions.PrepView)]
    public async Task<IActionResult> Profile(int id, [FromQuery] string? source, [FromQuery] string? speeds, [FromQuery] int? years,
        [FromQuery] bool? unsure, [FromQuery] bool? all, [FromQuery] bool? twin, [FromServices] PrepCardService cards, CancellationToken ct) =>
        await cards.LoadAsync(id, all == true, twin == true, ct) is { } l
            ? Ok(await cards.ProfileAsync(l, Services.League.LeagueProfileStore.TreeFilter.Parse(source, speeds, years, onlySure: unsure != true), ct))
            : NotFound();

    /// <summary>Eröffnungsbaum: <c>color</c> w/s, <c>line</c> = Züge mit Leerzeichen; Filter wie das Profil.</summary>
    [HttpGet("player/{id:int}/tree")]
    [HasPermission(Permissions.PrepView)]
    public async Task<IActionResult> Tree(int id, [FromQuery] string? color, [FromQuery] string? line, [FromQuery] string? source,
        [FromQuery] string? speeds, [FromQuery] int? years, [FromQuery] bool? unsure, [FromQuery] bool? all, [FromQuery] bool? twin,
        [FromServices] PrepCardService cards, CancellationToken ct) =>
        await cards.LoadAsync(id, all == true, twin == true, ct) is { } l
            ? Ok(await cards.TreeAsync(l, color ?? "w", line,
                Services.League.LeagueProfileStore.TreeFilter.Parse(source, speeds, years, onlySure: unsure != true), ct))
            : NotFound();

    /// <summary>Die letzten Partien samt PGN zum Nachspielen; <c>color</c> w/s = nur mit dieser Farbe.</summary>
    [HttpGet("player/{id:int}/recent")]
    [HasPermission(Permissions.PrepView)]
    public async Task<IActionResult> Recent(int id, [FromQuery] string? color, [FromQuery] bool? all, [FromQuery] bool? twin,
        [FromServices] PrepCardService cards, CancellationToken ct) =>
        await cards.LoadAsync(id, all == true, twin == true, ct) is { } l ? Ok(await cards.RecentAsync(l, color, ct)) : NotFound();

    /// <summary>Die geladenen Partien als PGN-Datei (Grenze und Zwilling wie die Karte) — der einzige Download, je Spieler.</summary>
    [HttpGet("player/{id:int}/pgn")]
    [HasPermission(Permissions.PrepView)]
    public async Task<IActionResult> Pgn(int id, [FromQuery] bool? all, [FromQuery] bool? twin, [FromServices] PrepCardService cards,
        CancellationToken ct)
    {
        if (await cards.LoadAsync(id, all == true, twin == true, ct) is not { } l) return NotFound();
        return LeagueController.PgnFile(l.Player.FideId ?? $"p{l.Player.Id}", l.Player.Name, await cards.PgnAsync(l, ct));
    }

    // ---- Einspielen (Phase 1) ------------------------------------------------------------------------

    /// <summary>
    /// Ein Paket des Bestands einspielen (PGN, gern gzip mit <c>Content-Encoding: gzip</c>) → Zähler des Pakets.
    /// <c>source</c> = <c>Mega</c> | <c>Lumbra</c>, <c>chunk</c> = Paketnummer, <c>first</c> = Nummer der ersten Partie
    /// des Pakets in der Quelle. Ein schon eingespieltes Paket liefert seine Zähler mit <c>already: true</c> und ändert
    /// nichts; 409 <c>chunkMismatch</c>, wenn es dort mit einer anderen ersten Partie steht (andere Paketgröße).
    /// </summary>
    [HttpPost("admin/games")]
    [HasPermission(Permissions.PrepManage)]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> ImportGames([FromQuery] string? source, [FromQuery] int? chunk, [FromQuery] long? first,
        CancellationToken ct)
    {
        var bit = PrepSources.Parse(source);
        if (bit == 0) return BadRequest(new { reason = "invalidSource" });
        if (chunk is not >= 0 || first is not >= 0) return BadRequest(new { reason = "invalidChunk" });
        var pgn = await ReadBodyAsync(ct);
        if (pgn is null) return StatusCode(StatusCodes.Status413PayloadTooLarge, new { reason = "tooLarge" });
        try
        {
            var r = await _import.ImportChunkAsync(bit, chunk.Value, first.Value, pgn, ct);
            return Ok(Json(r));
        }
        catch (PrepImportService.ChunkMismatchException e)
        {
            return Conflict(new { reason = "chunkMismatch", message = e.Message });
        }
        catch (PrepImportService.ChunkTooLargeException e)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { reason = "tooManyGames", message = e.Message });
        }
        catch (DbUpdateException) when (!ct.IsCancellationRequested)
        {
            // Dasselbe Paket lief gleichzeitig ein zweites Mal (eindeutiger Index auf Quelle + Paket) — der andere gewinnt.
            return Conflict(new { reason = "busy" });
        }
    }

    /// <summary>Die eingespielten Pakete einer Quelle samt Summen — das Skript setzt danach fort.</summary>
    [HttpGet("admin/imports")]
    [HasPermission(Permissions.PrepManage)]
    public async Task<IActionResult> Imports([FromQuery] string? source, CancellationToken ct)
    {
        var bit = PrepSources.Parse(source);
        if (bit == 0) return BadRequest(new { reason = "invalidSource" });
        var rows = await _import.ImportsAsync(bit, ct);
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in rows.Where(r => !string.IsNullOrEmpty(r.DiscardReasons)))
            foreach (var (k, v) in JsonSerializer.Deserialize<Dictionary<string, int>>(r.DiscardReasons!) ?? new())
                reasons[k] = reasons.GetValueOrDefault(k) + v;
        return Ok(new
        {
            source = PrepSources.Name(bit),
            chunks = rows.Select(r => new { chunk = r.Chunk, first = r.FirstGame, read = r.Read, added = r.Added, duplicates = r.Duplicates,
                discarded = r.Discarded, millis = r.Millis, createdAt = r.CreatedAt }),
            totals = new { read = rows.Sum(r => (long)r.Read), added = rows.Sum(r => (long)r.Added),
                duplicates = rows.Sum(r => (long)r.Duplicates), discarded = rows.Sum(r => (long)r.Discarded), reasons },
        });
    }

    private static object Json(PrepImportService.ChunkResult r) => new
    {
        source = r.Source, chunk = r.Chunk, first = r.FirstGame, read = r.Read, added = r.Added, duplicates = r.Duplicates,
        discarded = r.Discarded, reasons = r.Reasons, millis = r.Millis, already = r.Already,
    };

    /// <summary>Den Rumpf lesen, gzip entpackt — <c>null</c>, wenn er entpackt größer als <see cref="MaxPgnBytes"/> ist.</summary>
    private async Task<string?> ReadBodyAsync(CancellationToken ct)
    {
        Stream body = Request.Body;
        if (Request.Headers.ContentEncoding.ToString().Contains("gzip", StringComparison.OrdinalIgnoreCase))
            body = new GZipStream(Request.Body, CompressionMode.Decompress);
        await using (body)
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int n;
            while ((n = await body.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + n > MaxPgnBytes) return null;
                buffer.Write(chunk, 0, n);
            }
            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
    }
}
