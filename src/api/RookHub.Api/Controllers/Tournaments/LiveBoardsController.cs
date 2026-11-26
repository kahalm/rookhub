using System.Text;
using System.Text.Json;
using System.Threading.Channels;
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
/// verknüpften Übertragung; fremde Runden (zusätzliche Bretter, 1.025.0) nur, wenn Lichess sie kennt und unter den Grenzen
/// von <see cref="ExternalRoundRegistry"/>, gebündelt in den Strom der eigenen Runde (EINE Browser-Verbindung). Eigener Pfad (nicht unter
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

    /// <summary>
    /// „Brett hinzufügen“ aus einer FREMDEN Übertragung (1.025.0): Lichess-Link (Partie, Runde oder Turnier) → Runde und ihre
    /// Partien zur Auswahl. 400 <c>live_invalid_url</c> (kein lichess.org/broadcast-Link mit gültigen Kennungen), 404
    /// <c>live_broadcast_not_found</c> / <c>live_ext_game_not_found</c>. Literal-Route vor <c>{id}</c>.
    /// </summary>
    [HttpGet("external")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPartitions.DirectoryReadPolicy)]
    public async Task<IActionResult> External([FromQuery] string? url, [FromServices] ExternalRoundRegistry ext, [FromServices] BroadcastLiveHub hub)
    {
        ExternalRoundView? view;
        string? reason;
        try { (view, reason) = await ext.LookupAsync(url, hub, RequestCt); }
        catch (LichessRateLimitedException) { return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Lichess is busy.", code = "live_lichess_busy" }); }
        return reason switch
        {
            null => Ok(view),
            "invalidUrl" => BadRequest(new { message = "Not a Lichess broadcast link.", code = "live_invalid_url" }),
            "gameNotFound" => NotFound(new { message = "Game not found in this round.", code = "live_ext_game_not_found" }),
            _ => NotFound(new { message = "Broadcast not found.", code = "live_broadcast_not_found" }),
        };
    }

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
    /// Verbindungswechseln zu Lichess; alle 20 s ein Kommentar als Lebenszeichen. <c>eval=true</c> = Engine-Balken an.
    /// <c>extra=roundId.gameId,…</c> (1.025.0) = zusätzliche Bretter aus fremden Übertragungen im SELBEN Strom: je Runde
    /// <c>ext-snapshot</c> / <c>ext-game</c> / <c>ext-state</c> mit <c>round</c> = Lichess-Runde, nur die gewählten Partien;
    /// <c>ext-error</c> <c>{ round, code }</c> bei unbekannter Runde (<c>live_ext_not_found</c>) oder voller Serverliste
    /// (<c>live_ext_limit</c>). Höchstens 4 Runden / 12 Bretter je Verbindung (<see cref="ExternalRoundRegistry"/>).
    /// </summary>
    [HttpGet("{id}/rounds/{round:int}/stream")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPartitions.DirectoryReadPolicy)]
    public async Task Stream(string id, int round, [FromQuery] bool eval, [FromServices] BroadcastLiveHub hub,
        [FromServices] SseConnectionLimiter limiter, [FromServices] ILiveTournamentData data,
        [FromServices] ExternalRoundRegistry? ext = null, [FromQuery] string? extra = null)
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

        // Fremde Runden: prüfen (Lichess kennt sie), Platz belegen, im Hub mitschauen — nur die gewählten Partien.
        var extras = new List<ExternalStream>();
        var errors = new List<(string Round, string Code)>();
        var leases = new List<IDisposable>();
        try
        {
            if (ext is not null)
                foreach (var group in ExternalRoundRegistry.ParseRefs(extra).Where(r => r.RoundId != info.LichessRoundId).GroupBy(r => r.RoundId))
                {
                    var x = await ext.ResolveAsync(group.Key, ct);
                    if (x is null) { errors.Add((group.Key, "live_ext_not_found")); continue; }
                    if (ext.TryWatch(group.Key) is not { } watch) { errors.Add((group.Key, "live_ext_limit")); continue; }
                    leases.Add(watch);
                    extras.Add(new ExternalStream(x, hub.Subscribe(x.RoundId, x.Finished, eval,
                        group.Select(r => r.GameId).ToHashSet(StringComparer.Ordinal))));
                }

            await using var sub = hub.Subscribe(info.LichessRoundId, info.Finished, eval);
            await RunStreamAsync(Response.Body, sub, crRound, round, ct, extras: extras, errors: errors);
        }
        finally
        {
            foreach (var e in extras) await e.Sub.DisposeAsync();
            foreach (var l in leases) l.Dispose();
        }
    }

    /// <summary>Eine fremde Runde im Strom: was Lichess über sie sagt + das Abo am Hub (gefiltert auf die gewählten Partien).</summary>
    internal sealed record ExternalStream(ExternalRoundInfo Info, BroadcastLiveHub.RoundSubscription Sub);

    /// <summary>Die Ereignisschleife (eigene Methode, damit Tests sie ohne HTTP-Pipeline treiben).</summary>
    internal static async Task RunStreamAsync(Stream body, BroadcastLiveHub.RoundSubscription sub, CrRound crRound, int round,
        CancellationToken ct, TimeSpan? heartbeat = null, IReadOnlyList<ExternalStream>? extras = null,
        IReadOnlyList<(string Round, string Code)>? errors = null)
    {
        var games = new Dictionary<string, LiveGame>(StringComparer.Ordinal);
        LiveRoundLayout? layout = null;
        var known = new HashSet<string>(StringComparer.Ordinal);
        extras ??= [];
        // Alle Abos in EINEN Kanal (Quelle -1 = eigene Runde). Endet die eigene Runde, endet der Strom wie bisher.
        var merged = Channel.CreateBounded<(int Source, LiveEvent Event)>(new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true,
        });
        using var pumpStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pumps = new List<Task> { Pump(sub, -1, merged.Writer, pumpStop.Token, completeWhenDone: true) };
        for (var i = 0; i < extras.Count; i++) pumps.Add(Pump(extras[i].Sub, i, merged.Writer, pumpStop.Token, completeWhenDone: false));
        await WriteAsync(body, null, "retry: 5000\n", ct);
        try
        {
            foreach (var (r, code) in errors ?? []) await WriteAsync(body, "ext-error", new { round = r, code }, ct);
            while (!ct.IsCancellationRequested)
            {
                using (var wait = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    wait.CancelAfter(heartbeat ?? Heartbeat);
                    try
                    {
                        if (!await merged.Reader.WaitToReadAsync(wait.Token)) break;
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        await WriteAsync(body, null, ": ping\n\n", ct);
                        continue;
                    }
                }
                while (merged.Reader.TryRead(out var item))
                {
                    var e = item.Event;
                    if (item.Source >= 0)
                    {
                        await WriteExternalAsync(body, extras[item.Source].Info, e, ct);
                        continue;
                    }
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
        finally
        {
            await pumpStop.CancelAsync();
            await Task.WhenAll(pumps);
        }
    }

    private static async Task Pump(BroadcastLiveHub.RoundSubscription sub, int source, ChannelWriter<(int, LiveEvent)> to,
        CancellationToken ct, bool completeWhenDone)
    {
        try
        {
            await foreach (var e in sub.Events.ReadAllAsync(ct)) to.TryWrite((source, e));
        }
        catch (OperationCanceledException) { /* Strom zu */ }
        if (completeWhenDone) to.TryComplete();
    }

    /// <summary>Ereignis einer fremden Runde: mit <c>round</c> = Lichess-Runde; der Vollstand trägt Turnier/Runde für die Überschrift.</summary>
    private static Task WriteExternalAsync(Stream body, ExternalRoundInfo info, LiveEvent e, CancellationToken ct) => e switch
    {
        LiveSnapshot s => WriteAsync(body, "ext-snapshot", new
        {
            round = info.RoundId, tourName = info.TourName, roundName = info.RoundName, url = info.Url, state = s.State, games = s.Games,
        }, ct),
        LiveGameChanged c => WriteAsync(body, "ext-game", new { round = info.RoundId, game = c.Game }, ct),
        LiveStateChanged st => WriteAsync(body, "ext-state", new { round = info.RoundId, state = st.State }, ct),
        _ => Task.CompletedTask,
    };

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
