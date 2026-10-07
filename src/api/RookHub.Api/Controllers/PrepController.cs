using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Authorization;
using RookHub.Api.Models;
using RookHub.Api.Services;
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
    private readonly PermissionResolver? _permissions;

    public PrepController(PrepImportService import, PermissionResolver? permissions = null)
    {
        _import = import;
        _permissions = permissions;
    }

    /// <summary>Verwalter (<c>prep.manage</c>) — LIVE wie <c>[HasPermission]</c>. Nur sie sehen unsichere Online-Konten samt
    /// Kommentaren und können deren Partien in Baum und Profil nehmen: unsichere Konten sind Vermutungen über echte Personen
    /// mit internen Notizen, <c>prep.view</c> bekommt ein breiterer Kreis (Vorgabe des Betreuers 2026-10-02).</summary>
    private async Task<bool> CanManageAsync() =>
        User.IsInRole("Admin") || (_permissions != null
            ? (await _permissions.GetAsync(GetUserId())).Has(Permissions.PrepManage)
            : User.HasClaim(PermissionAuthorizationHandler.PermissionClaimType, Permissions.PrepManage));

    /// <summary>Filter aus der Adresse; <c>unsure=true</c> wirkt nur für Verwalter, sonst still wie <c>false</c>.</summary>
    private async Task<Services.League.LeagueProfileStore.TreeFilter> FilterAsync(string? source, string? speeds, int? years, bool? unsure) =>
        Services.League.LeagueProfileStore.TreeFilter.Parse(source, speeds, years, onlySure: !(unsure == true && await CanManageAsync()));

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

    /// <summary>Spielerkarte in der Form der Liga-Karte (Profil, Quellen, letzte Partien, Online-Konten — mit <c>prep.view</c> nur
    /// gesicherte ohne Kommentar wie über einen Teilen-Link, mit <c>prep.manage</c> wie für LeagueHub-Leser) plus <c>id</c>, <c>games</c>
    /// (im Bestand), <c>loaded</c>/<c>limited</c>/<c>limit</c>/<c>since</c> (Vorgabe: die jüngsten <see cref="PrepCardService.DefaultLimit"/>,
    /// <c>all=true</c>: bis <see cref="PrepCardService.DefaultMax"/>) und <c>twin</c> (Namens-Zwilling ohne FIDE-ID, nur mit <c>twin=true</c>
    /// dabei). Gilt für alle Unterseiten.</summary>
    [HttpGet("player/{id:int}")]
    [HasPermission(Permissions.PrepView)]
    public async Task<IActionResult> Player(int id, [FromQuery] bool? all, [FromQuery] bool? twin, [FromServices] PrepCardService cards,
        [FromServices] IConfiguration config, CancellationToken ct)
    {
        if (await cards.LoadAsync(id, all == true, twin == true, ct) is not { } l) return NotFound();
        var manage = await CanManageAsync();
        var card = await cards.CardAsync(l, manage, ct);
        // Phase 4: den Knopf „Online-Konten suchen" gibt es nur für Verwalter, mit Schalter und FIDE-ID.
        card["accountSearch"] = manage && PrepAccountSearch.IsEnabled(config) && l.Player.FideId is not null;
        return Ok(card);
    }

    /// <summary>Eröffnungsprofil über gefilterte Partien — Filter wie bei der Liga (<c>source</c>, <c>speeds</c>, <c>years</c>,
    /// Online nur gesicherter Konten außer <c>unsure=true</c>, das nur mit <c>prep.manage</c> wirkt).</summary>
    [HttpGet("player/{id:int}/profile")]
    [HasPermission(Permissions.PrepView)]
    public async Task<IActionResult> Profile(int id, [FromQuery] string? source, [FromQuery] string? speeds, [FromQuery] int? years,
        [FromQuery] bool? unsure, [FromQuery] bool? all, [FromQuery] bool? twin, [FromServices] PrepCardService cards, CancellationToken ct) =>
        await cards.LoadAsync(id, all == true, twin == true, ct) is { } l
            ? Ok(await cards.ProfileAsync(l, await FilterAsync(source, speeds, years, unsure), ct))
            : NotFound();

    /// <summary>Eröffnungsbaum: <c>color</c> w/s, <c>line</c> = Züge mit Leerzeichen; Filter wie das Profil.</summary>
    [HttpGet("player/{id:int}/tree")]
    [HasPermission(Permissions.PrepView)]
    public async Task<IActionResult> Tree(int id, [FromQuery] string? color, [FromQuery] string? line, [FromQuery] string? source,
        [FromQuery] string? speeds, [FromQuery] int? years, [FromQuery] bool? unsure, [FromQuery] bool? all, [FromQuery] bool? twin,
        [FromServices] PrepCardService cards, CancellationToken ct) =>
        await cards.LoadAsync(id, all == true, twin == true, ct) is { } l
            ? Ok(await cards.TreeAsync(l, color ?? "w", line, await FilterAsync(source, speeds, years, unsure), ct))
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

    /// <summary>Trainingslinien gegen diesen Spieler (<see cref="TrainingLinesService"/>): die Linien des eigenen Repertoires
    /// (<c>repertoire</c>, nur eigene mit „Für Extension und Vorbereitung verwenden"; fremd → 404) von wahrscheinlich nach unwahrscheinlich,
    /// gemessen an den Partien der Karte (Grenze/Zwilling wie dort) — <c>source</c> Vorgabe <c>both</c>, sonst Filter wie das Profil
    /// (<c>unsure=true</c> nur mit <c>prep.manage</c>). <c>color</c> w/b, <c>chapterColors</c> = eigene Farb-Festlegungen (JSON),
    /// <c>take</c> = so viele Linien.</summary>
    [HttpGet("player/{id:int}/training-lines")]
    [HasPermission(Permissions.PrepView)]
    public async Task<IActionResult> TrainingLines(int id, [FromQuery] int? repertoire, [FromQuery] string? color,
        [FromQuery] string? chapterColors, [FromQuery] int? take, [FromQuery] string? source, [FromQuery] string? speeds,
        [FromQuery] int? years, [FromQuery] bool? unsure, [FromQuery] bool? all, [FromQuery] bool? twin,
        [FromServices] PrepCardService cards, [FromServices] TrainingLinesService lines, CancellationToken ct)
    {
        if (await cards.LoadAsync(id, all == true, twin == true, ct) is not { } l) return NotFound();
        var filter = await FilterAsync(source ?? "both", speeds, years, unsure);
        var q = new TrainingLinesService.Query(repertoire, color, TrainingLinesService.ParseOverrides(chapterColors), take);
        var r = await lines.LinesAsync(GetUserId(), q, () => cards.TrainingGamesAsync(l, filter, ct), ct,
            () => lines.PrepEloAsync(l.Player.Id, l.Player.MaxElo, ct));
        return r is null ? NotFound(new { reason = "repertoire" }) : Ok(r);
    }

    /// <summary>„Show me lines to train": legt ein eigenes Repertoire „Prep: &lt;Spieler&gt; &lt;Jahr&gt;" mit den höchstens 50 wichtigsten
    /// Linien gegen diesen Spieler an (gleichnamiges wird ersetzt) → <c>{ id, name, lines, replaced }</c>. Rumpf wie die Abfrage
    /// (<see cref="TrainingLinesService.CreateRequest"/>); fremdes Repertoire 404, keine Linien 400.</summary>
    [HttpPost("player/{id:int}/training-repertoire")]
    [HasPermission(Permissions.PrepView)]
    public async Task<IActionResult> TrainingRepertoire(int id, [FromBody] TrainingLinesService.CreateRequest? req,
        [FromServices] PrepCardService cards, [FromServices] TrainingLinesService lines, CancellationToken ct)
    {
        req ??= new(null, null, null, null, null, null, null, null, null);
        if (await cards.LoadAsync(id, req.All == true, req.Twin == true, ct) is not { } l) return NotFound();
        var filter = await FilterAsync(req.Source ?? "both", req.Speeds, req.Years, req.Unsure);
        TrainingLinesService.Created? r;
        try
        {
            r = await lines.CreateRepertoireAsync(GetUserId(), l.Player.Name, req.ToQuery(), () => cards.TrainingGamesAsync(l, filter, ct), ct,
                () => lines.PrepEloAsync(l.Player.Id, l.Player.MaxElo, ct));
        }
        catch (TrainingLinesService.SameRepertoireException e) { return BadRequest(new { reason = "sameRepertoire", message = e.Message }); }
        return r is null ? NotFound(new { reason = "repertoire" }) : Ok(new { id = r.Id, name = r.Name, lines = r.Lines, replaced = r.Replaced });
    }

    // ---- Online-Konten suchen (Phase 4) -------------------------------------------------------------
    // Nur mit prep.manage UND dem Schalter Prep:AccountSearch (Vorgabe aus) — ohne Schalter 404 „disabled". Gesucht wird mit der
    // Konto-Suche von LeagueHub; Vorschläge eines Minderjährigen kommen hier nie heraus.

    /// <summary>Offene Vorschläge des Spielers → <c>{ items, perHour, remaining, accounts, leagueHub }</c> (<c>accounts</c>: seine
    /// eingetragenen Konten, die eines Minderjährigen nie; <c>leagueHub</c>: Ligaspieler — seine Konten pflegt LeagueHub).</summary>
    [HttpGet("player/{id:int}/suggestions")]
    [HasPermission(Permissions.PrepManage)]
    public async Task<IActionResult> Suggestions(int id, [FromServices] PrepAccountSearch search, CancellationToken ct)
    {
        if (!search.Enabled) return NotFound(new { reason = "disabled" });
        var (r, reason) = await search.SuggestionsAsync(id, GetUserId(), ct);
        return r is not null ? Ok(r) : reason == "noFide" ? BadRequest(new { reason }) : NotFound(new { reason });
    }

    /// <summary>Jetzt suchen → <c>{ items, found, perHour, remaining }</c>; 409 <c>busy</c> (eine Suche zur Zeit), 429 <c>limit</c>
    /// (Stunde aufgebraucht), 503 <c>rateLimited</c> (eine Seite bremst — die Suche endet ohne zweiten Versuch) / <c>unreachable</c>.</summary>
    [HttpPost("player/{id:int}/suggestions/scan")]
    [HasPermission(Permissions.PrepManage)]
    public async Task<IActionResult> ScanSuggestions(int id, [FromServices] PrepAccountSearch search, CancellationToken ct)
    {
        if (!search.Enabled) return NotFound(new { reason = "disabled" });
        var (r, reason) = await search.ScanAsync(id, GetUserId(), ct);
        if (r is not null) return Ok(r);
        return reason switch
        {
            "notFound" => NotFound(new { reason }),
            "noFide" => BadRequest(new { reason }),
            "busy" => Conflict(new { reason }),
            "limit" => StatusCode(StatusCodes.Status429TooManyRequests, new { reason, perHour = search.PerHour }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new { reason }),
        };
    }

    public sealed record AcceptRequest(bool Sure);

    /// <summary>Übernehmen <c>{ sure }</c> → das Konto; 404 fremd/erledigt/verborgen, 400 wie bei LeagueHub.</summary>
    [HttpPost("suggestions/{suggestionId:int}/accept")]
    [HasPermission(Permissions.PrepManage)]
    public async Task<IActionResult> AcceptSuggestion(int suggestionId, [FromBody] AcceptRequest? req, [FromServices] PrepAccountSearch search,
        CancellationToken ct)
    {
        if (!search.Enabled) return NotFound(new { reason = "disabled" });
        var (acc, reason) = await search.AcceptAsync(suggestionId, req?.Sure == true, User.Identity?.Name, ct);
        return acc is not null ? Ok(acc) : reason is "notFound" or "unknownPlayer" ? NotFound(new { reason }) : BadRequest(new { reason });
    }

    [HttpPost("suggestions/{suggestionId:int}/reject")]
    [HasPermission(Permissions.PrepManage)]
    public async Task<IActionResult> RejectSuggestion(int suggestionId, [FromServices] PrepAccountSearch search, CancellationToken ct)
    {
        if (!search.Enabled) return NotFound(new { reason = "disabled" });
        return await search.RejectAsync(suggestionId, ct) ? NoContent() : NotFound();
    }

    /// <summary>Prüfung (i) eines Vorschlags — wie bei LeagueHub, nie für Minderjährige; 409 <c>busy</c> (es läuft schon eine Suche oder
    /// Prüfung), 503 <c>rateLimited</c> (eine Seite hat gedrosselt).</summary>
    [HttpGet("suggestions/{suggestionId:int}/checks")]
    [HasPermission(Permissions.PrepManage)]
    public async Task<IActionResult> SuggestionChecks(int suggestionId, [FromServices] PrepAccountSearch search, CancellationToken ct)
    {
        if (!search.Enabled) return NotFound(new { reason = "disabled" });
        var (r, reason) = await search.ChecksAsync(suggestionId, ct);
        return r is not null ? Ok(r) : reason switch
        {
            "busy" => Conflict(new { reason }),
            "rateLimited" => StatusCode(StatusCodes.Status503ServiceUnavailable, new { reason }),
            _ => NotFound(new { reason }),
        };
    }

    public sealed record AccountUpdateRequest(bool? Sure, string? Comment);

    /// <summary>Ein eingetragenes Konto umstufen <c>{ sure, comment }</c> (fehlende Felder bleiben) → das Konto. Nur das eines Spielers,
    /// den LeagueHub nicht kennt: 409 <c>leagueHub</c> (die Pflege gehört dorthin), 404 fremd/verborgen (minderjährig), 400 wie bei
    /// LeagueHub (0.639.0).</summary>
    [HttpPut("accounts/{accountId:int}")]
    [HasPermission(Permissions.PrepManage)]
    public async Task<IActionResult> UpdateAccount(int accountId, [FromBody] AccountUpdateRequest? req, [FromServices] PrepAccountSearch search,
        CancellationToken ct)
    {
        if (!search.Enabled) return NotFound(new { reason = "disabled" });
        var (acc, reason) = await search.UpdateAccountAsync(accountId, req?.Sure, req?.Comment, ct);
        return acc is not null ? Ok(acc) : reason switch
        {
            "leagueHub" => Conflict(new { reason }),
            "notFound" => NotFound(new { reason }),
            _ => BadRequest(new { reason }),
        };
    }

    /// <summary>Ein eingetragenes Konto entfernen — samt seiner geholten Online-Partien; die Suche schlägt es nicht wieder vor. 409
    /// <c>leagueHub</c>, 404 wie beim Umstufen (0.639.0).</summary>
    [HttpDelete("accounts/{accountId:int}")]
    [HasPermission(Permissions.PrepManage)]
    public async Task<IActionResult> DeleteAccount(int accountId, [FromServices] PrepAccountSearch search, CancellationToken ct)
    {
        if (!search.Enabled) return NotFound(new { reason = "disabled" });
        return await search.DeleteAccountAsync(accountId, ct) switch
        {
            null => NoContent(),
            "leagueHub" => Conflict(new { reason = "leagueHub" }),
            var reason => NotFound(new { reason }),
        };
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
