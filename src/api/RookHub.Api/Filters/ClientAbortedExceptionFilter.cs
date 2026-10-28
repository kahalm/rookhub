using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RookHub.Api.Services;

namespace RookHub.Api.Filters;

/// <summary>
/// Hat der BROWSER die Anfrage abgebrochen (<c>HttpContext.RequestAborted</c>: Stellung gewechselt, Seite neu geladen,
/// Fortsetzungsrunde der Zugfolgen-Suche verworfen), ist die <see cref="OperationCanceledException"/>, die daraus im
/// Endpoint entsteht, kein Serverfehler: niemand liest die Antwort mehr. Der Filter macht daraus 499 („Client Closed
/// Request", wie nginx) ohne Rumpf und loggt nur auf Debug. Global registriert (Program.cs).
///
/// <para>Prod 09.10.2026: <c>GET /api/explorer/paths responded 500</c> mit <c>TaskCanceledException</c> aus
/// <c>LocalExplorerClient.GetAsync</c> — der Abbruch lief bis in den globalen Handler (500 + Error-Log mit Stacktrace,
/// log-watcher-Rauschen). Dasselbe galt für <c>/api/explorer/position</c>, <c>/games</c> und den Lochfinder
/// (<c>/api/repertoire/{id}/explorer-analysis</c>), die das Request-Token alle bis zum Explorer bzw. zur DB durchreichen.</para>
///
/// <para>Nur bei abgebrochenem <c>RequestAborted</c>: ein Abbruch aus einer EIGENEN Frist (Budget, HttpClient-Timeout)
/// bleibt, was er war — der Dienst fängt ihn selbst (z. B. <c>truncated</c>) oder er ist ein echter Fehler (500).
/// Warum ein MVC-Filter: siehe <see cref="DomainExceptionFilter"/> (der globale Handler schriebe das Request-Log als 500
/// auf Error). Ein Controller-Filter wie der <see cref="CrawlerExceptionFilter"/> läuft vorher und behält seine Antwort.</para>
///
/// <para><b>Abgebrochener/kaputter Rumpf (<see cref="BadHttpRequestException"/>, auch als InnerException):</b> Dev
/// 28.10.2026 <c>POST /api/puzzles/random-batch responded 500</c> mit „Unexpected end of request content." aus dem
/// Model-Binding (<c>SystemTextJsonInputFormatter</c>) — der Browser schloss die Seite mitten im Upload. Die Ausnahme
/// lief an den Filtern vorbei bis in den globalen Handler; der schrieb zwar 400, das Request-Log sah aber die Ausnahme
/// und stand fest auf „500"/Error. Jetzt hier: Client weg (<c>RequestAborted</c>) → 499, sonst Kestrels eigener
/// Status (400/413 …) mit <c>{ message }</c>; geloggt nur auf Debug.</para>
/// </summary>
public sealed class ClientAbortedExceptionFilter : IExceptionFilter
{
    /// <summary>nginx' „Client Closed Request" — kein offizieller Status, aber in den Zugriffslogs bekannt.</summary>
    public const int StatusClientClosedRequest = 499;

    private readonly ILogger<ClientAbortedExceptionFilter> _logger;

    public ClientAbortedExceptionFilter(ILogger<ClientAbortedExceptionFilter> logger) => _logger = logger;

    public void OnException(ExceptionContext context)
    {
        if (context.ExceptionHandled) return;

        if (FindBadRequest(context.Exception) is { } bad)
        {
            HandleBadRequest(context, bad);
            return;
        }

        if (context.Exception is not OperationCanceledException) return;
        if (!context.HttpContext.RequestAborted.IsCancellationRequested) return;

        _logger.LogDebug("Request aborted by the client: {Method} {Path}",
            context.HttpContext.Request.Method, context.HttpContext.Request.Path.Value);
        context.Result = new StatusCodeResult(StatusClientClosedRequest);
        context.ExceptionHandled = true;
    }

    /// <summary>Kestrels <see cref="BadHttpRequestException"/> selbst oder als (verschachtelte) InnerException.</summary>
    internal static BadHttpRequestException? FindBadRequest(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is BadHttpRequestException bad) return bad;
        return null;
    }

    private void HandleBadRequest(ExceptionContext context, BadHttpRequestException bad)
    {
        var http = context.HttpContext;
        if (http.RequestAborted.IsCancellationRequested)
        {
            _logger.LogDebug("Request aborted by the client while reading the body: {Method} {Path}",
                http.Request.Method, http.Request.Path.Value);
            context.Result = new StatusCodeResult(StatusClientClosedRequest);
        }
        else
        {
            var status = bad.StatusCode is >= 400 and < 500 ? bad.StatusCode : StatusCodes.Status400BadRequest;
            _logger.LogDebug("Malformed request body ({Status}): {Method} {Path} — {Reason}",
                status, http.Request.Method, http.Request.Path.Value, bad.Message);
            context.Result = new ObjectResult(ApiErrorResponses.Body(ApiErrorResponses.BadRequestTitle(status), null))
            {
                StatusCode = status,
            };
        }
        context.ExceptionHandled = true;
    }
}
