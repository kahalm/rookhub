using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Authorization;
using RookHub.Api.DTOs.Tournaments;
using RookHub.Api.Filters;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Tournaments.Live;
using RookHub.Api.Validation;

namespace RookHub.Api.Controllers.Tournaments;

/// <summary>
/// Live-Bretter der Turnierseite (1.014.0, <c>docs/api/live-bretter.md</c>): Verknüpfung mit einer Lichess-Übertragung und
/// der Live-Strom einer Runde als Server-Sent Events. Lesen und Zuschauen ohne Anmeldung (wie die übrigen Turnier-Lesewege),
/// Verknüpfen mit Konto, Lösen nur mit <see cref="Permissions.TournamentsManage"/>. Gestreamt werden NUR Runden einer
/// verknüpften Übertragung — eine Lichess-Kennung von außen nimmt kein Endpunkt an. Eigener Pfad (nicht unter
/// <c>/api/tournaments</c>), damit nginx ihn ungepuffert durchreichen kann (<c>location ^~ /api/live-boards/</c>).
/// </summary>
[ApiController]
[Route("api/live-boards")]
[Authorize]
[TypeFilter(typeof(CrawlerExceptionFilter))]
public class LiveBoardsController : BaseApiController
{
    /// <summary>So oft geht ein Lebenszeichen (SSE-Kommentar) hinaus — hält nginx/NPM (60 s) und Mobilfunk-NATs offen.</summary>
    public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(20);

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TournamentLiveService _live;

    public LiveBoardsController(TournamentLiveService live) => _live = live;

    private CancellationToken RequestCt => HttpContext?.RequestAborted ?? default;

    /// <summary>Ist eine Übertragung verknüpft (sonst wird einmal gesucht), welche Runden, welche läuft.</summary>
    [HttpGet("{id}")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPartitions.DirectoryReadPolicy)]
    public async Task<IActionResult> Status(string id)
    {
        if (!TournamentIdValidator.IsValid(id)) return BadRequest(new { message = "Invalid tournament ID." });
        var status = await _live.GetStatusAsync(id, RequestCt);
        return status is null ? NotFound(new { message = "Tournament not found." }) : Ok(status);
    }

    /// <summary>Übertragung von Hand verknüpfen (Lichess-Link). 400 <c>live_invalid_url</c>, 404 <c>live_broadcast_not_found</c>/<c>live_tournament_not_found</c>.</summary>
    [HttpPut("{id}/link")]
    [EnableRateLimiting(RateLimitPartitions.CrawlerRequestPolicy)]
    public async Task<IActionResult> Link(string id, [FromBody] LiveBoardLinkRequest? req)
    {
        if (!TournamentIdValidator.IsValid(id)) return BadRequest(new { message = "Invalid tournament ID." });
        var (status, reason) = await _live.LinkAsync(id, req?.Url, RequestCt);
        return reason switch
        {
            null => Ok(status),
            "invalidUrl" => BadRequest(new { message = "Not a Lichess broadcast link.", code = "live_invalid_url" }),
            "tournamentNotFound" => NotFound(new { message = "Tournament not found.", code = "live_tournament_not_found" }),
            _ => NotFound(new { message = "Broadcast not found.", code = "live_broadcast_not_found" }),
        };
    }

    /// <summary>Verknüpfung lösen (falsch zugeordnet). Danach sucht das nächste Lesen wieder automatisch.</summary>
    [HttpDelete("{id}/link")]
    [HasPermission(Permissions.TournamentsManage)]
    public async Task<IActionResult> Unlink(string id)
    {
        if (!TournamentIdValidator.IsValid(id)) return BadRequest(new { message = "Invalid tournament ID." });
        return await _live.UnlinkAsync(id, RequestCt) ? NoContent() : NotFound();
    }

    /// <summary>
    /// Live-Strom einer Runde (<c>text/event-stream</c>): zuerst <c>snapshot</c> (Zustand, Bretter-Zuordnung, alle Partien),
    /// dann <c>game</c> je geänderter Partie, <c>layout</c> wenn eine neue Partie die Zuordnung ändert, <c>state</c> bei
    /// Verbindungswechseln zu Lichess; alle 20 s ein Kommentar als Lebenszeichen. <c>eval=1</c> = Engine-Balken an.
    /// </summary>
    [HttpGet("{id}/rounds/{round:int}/stream")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPartitions.DirectoryReadPolicy)]
    public async Task Stream(string id, int round, [FromQuery] bool eval, [FromServices] BroadcastLiveHub hub,
        [FromServices] SseConnectionLimiter limiter, [FromServices] ILiveTournamentData data)
    {
        var ct = RequestCt;
        if (!TournamentIdValidator.IsValid(id) || round is < 1 or > 999)
        {
            await WriteErrorAsync(StatusCodes.Status400BadRequest, "Invalid tournament or round.");
            return;
        }
        var info = await _live.ResolveRoundAsync(id, round, ct);
        if (info is null)
        {
            await WriteErrorAsync(StatusCodes.Status404NotFound, "No linked broadcast for this round.", "live_not_linked");
            return;
        }
        using var lease = limiter.TryAcquire(RateLimitPartitions.ClientIp(HttpContext));
        if (lease is null)
        {
            await WriteErrorAsync(StatusCodes.Status429TooManyRequests, "Too many live connections.", "live_too_many_streams");
            return;
        }
        CrRound crRound;
        try { crRound = await data.GetRoundAsync(id, round, ct); }
        catch (RookHub.Api.Exceptions.CrawlerRequestException) { crRound = new CrRound(false, [], [], []); }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.Headers.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        await using var sub = hub.Subscribe(info.LichessRoundId, info.Finished, eval);
        await RunStreamAsync(Response.Body, sub, crRound, round, ct);
    }

    /// <summary>Die Ereignisschleife (eigene Methode, damit Tests sie ohne HTTP-Pipeline treiben).</summary>
    internal static async Task RunStreamAsync(Stream body, BroadcastLiveHub.RoundSubscription sub, CrRound crRound, int round,
        CancellationToken ct, TimeSpan? heartbeat = null)
    {
        var games = new Dictionary<string, LiveGame>(StringComparer.Ordinal);
        LiveRoundLayout? layout = null;
        var known = new HashSet<string>(StringComparer.Ordinal);
        await WriteAsync(body, null, "retry: 5000\n", ct);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using (var wait = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    wait.CancelAfter(heartbeat ?? Heartbeat);
                    try
                    {
                        if (!await sub.Events.WaitToReadAsync(wait.Token)) break;
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        await WriteAsync(body, null, ": ping\n\n", ct);
                        continue;
                    }
                }
                while (sub.Events.TryRead(out var e))
                {
                    switch (e)
                    {
                        case LiveSnapshot s:
                            games = s.Games.ToDictionary(g => g.Id, StringComparer.Ordinal);
                            layout = LiveBoardAssembler.Assemble(crRound, s.Games);
                            known = games.Keys.ToHashSet(StringComparer.Ordinal);
                            await WriteAsync(body, "snapshot", new { state = s.State, round, layout, games = s.Games }, ct);
                            break;
                        case LiveGameChanged c:
                            games[c.Game.Id] = c.Game;
                            if (known.Add(c.Game.Id))
                            {
                                layout = LiveBoardAssembler.Assemble(crRound, games.Values.ToList());
                                await WriteAsync(body, "layout", layout, ct);
                            }
                            await WriteAsync(body, "game", c.Game, ct);
                            break;
                        case LiveStateChanged st:
                            await WriteAsync(body, "state", new { state = st.State }, ct);
                            break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* Zuschauer weg */ }
        catch (IOException) { /* Verbindung abgerissen */ }
    }

    private static async Task WriteAsync(Stream body, string? evt, object data, CancellationToken ct)
    {
        var text = data is string raw ? raw
            : (evt is null ? "" : $"event: {evt}\n") + "data: " + JsonSerializer.Serialize(data, Json) + "\n\n";
        await body.WriteAsync(Encoding.UTF8.GetBytes(text), ct);
        await body.FlushAsync(ct);
    }

    private async Task WriteErrorAsync(int status, string message, string? code = null)
    {
        Response.StatusCode = status;
        await Response.WriteAsJsonAsync(code is null ? new { message } : (object)new { message, code }, RequestCt);
    }
}
