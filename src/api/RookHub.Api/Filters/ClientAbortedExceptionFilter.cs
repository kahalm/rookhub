using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

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
        if (context.Exception is not OperationCanceledException) return;
        if (!context.HttpContext.RequestAborted.IsCancellationRequested) return;

        _logger.LogDebug("Request aborted by the client: {Method} {Path}",
            context.HttpContext.Request.Method, context.HttpContext.Request.Path.Value);
        context.Result = new StatusCodeResult(StatusClientClosedRequest);
        context.ExceptionHandled = true;
    }
}
