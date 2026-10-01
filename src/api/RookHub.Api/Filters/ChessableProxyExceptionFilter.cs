using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RookHub.Api.Services;

namespace RookHub.Api.Filters;

/// <summary>
/// EINE Abbildung der piratechess-Fehler auf HTTP (Codereview 2026-09-29, A3-015) — vorher bildete der
/// ExtensionController sie auf 400/502 ab, Chessable- und Chessable-Admin-Controller pauschal auf 400, und ein
/// nicht erreichbares piratechess (<see cref="HttpRequestException"/>) fing niemand: nackter 500.
///
/// <para><b>Regel:</b> piratechess-400 → 400 mit dessen Meldung (Eingabe bzw. Chessable lehnt ab — daran hängt
/// auch die Breaker-Logik der Controller). Alles andere → 502: 5xx/503 (Neustart), aber auch 401/403
/// (Dienst-Schlüssel zwischen rookhub und piratechess falsch), 404 (Route fehlt = Versionsstand) und 429 (das
/// Fenster gehört allen rookhub-Importen) sind KEIN Fehler des Aufrufers. Ein 401 darf ohnehin nie durchgereicht
/// werden: RepCheck liest jeden 401 als „RookHub-Token ungültig" und trennt die Verbindung.</para>
///
/// <para>Die Controller fangen <see cref="ChessableProxyException"/> weiter selbst, wo sie dabei loggen oder den
/// Bearer-Breaker auslösen, und antworten über <see cref="ResultFor"/>; der Filter (per <c>TypeFilter</c> an den
/// Chessable-Controllern bzw. den Ingest-Actions der Extension) übernimmt Transportfehler und jede nicht
/// gefangene Proxy-Ausnahme mit derselben Regel.</para>
/// </summary>
public sealed class ChessableProxyExceptionFilter : IExceptionFilter
{
    private readonly ILogger<ChessableProxyExceptionFilter> _logger;

    public ChessableProxyExceptionFilter(ILogger<ChessableProxyExceptionFilter> logger) => _logger = logger;

    /// <summary>Antwort-Status für einen piratechess-Status.</summary>
    public static int StatusFor(HttpStatusCode upstream)
        => upstream == HttpStatusCode.BadRequest ? StatusCodes.Status400BadRequest : StatusCodes.Status502BadGateway;

    /// <summary>Die Antwort auf eine <see cref="ChessableProxyException"/>: <c>{ message }</c> mit <see cref="StatusFor"/>.</summary>
    public static ObjectResult ResultFor(ChessableProxyException ex)
    {
        var body = new { message = ex.Message };
        return StatusFor(ex.Status) == StatusCodes.Status400BadRequest
            ? new BadRequestObjectResult(body)
            : new ObjectResult(body) { StatusCode = StatusCodes.Status502BadGateway };
    }

    public void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            case ChessableProxyException proxy:
                context.Result = ResultFor(proxy);
                context.ExceptionHandled = true;
                break;
            case HttpRequestException transport:
                _logger.LogWarning(transport, "Chessable-Proxy nicht erreichbar ({Path})", context.HttpContext.Request.Path.Value);
                context.Result = new ObjectResult(new { message = ChessableProxyException.UnreachableMessage })
                {
                    StatusCode = StatusCodes.Status502BadGateway,
                };
                context.ExceptionHandled = true;
                break;
        }
    }
}
