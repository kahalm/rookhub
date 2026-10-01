using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RookHub.Api.Exceptions;
using RookHub.Api.Services;

namespace RookHub.Api.Filters;

/// <summary>
/// Übersetzt die Domänen-Ausnahmen (<see cref="NotFoundException"/>, <see cref="DomainValidationException"/>,
/// <see cref="ConflictException"/>, <see cref="ForbiddenException"/>) in dieselbe Antwort, die die Controller
/// bisher von Hand bauten: <c>{ message }</c> mit 404/400/409/403. Global registriert (Program.cs).
///
/// <para>Alles andere — auch die BCL-Basistypen selbst — bleibt unbehandelt und läuft in den globalen
/// Handler (500 + Error-Log mit Stacktrace). Genau das ist der Zweck: ein echter Fehler darf nicht mehr
/// als 4xx mit Framework-Text beim Client ankommen.</para>
///
/// <para><b>Warum ein MVC-Filter und kein <c>IExceptionHandler</c>:</b> der globale Handler sitzt VOR
/// <c>UseSerilogRequestLogging</c>. Eine Ausnahme, die bis dorthin durchläuft, schreibt das Request-Log
/// fest als „responded 500" auf Error — jede 404 „Session not found." sähe in Kibana und für den
/// log-watcher wie ein Serverfehler aus. Der Filter setzt das Ergebnis noch im Endpoint; das Request-Log
/// sieht den echten 4xx-Status wie bisher.</para>
///
/// <para>Kein Log hier: das sind erwartete Nutzerfehler, das Request-Log trägt den Status.</para>
/// </summary>
public sealed class DomainExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        int? status = context.Exception switch
        {
            NotFoundException => StatusCodes.Status404NotFound,
            DomainValidationException => StatusCodes.Status400BadRequest,
            ConflictException => StatusCodes.Status409Conflict,
            ForbiddenException => StatusCodes.Status403Forbidden,
            _ => null,
        };
        if (status is null) return;

        // { message } bzw. mit Fehlercode { message, code } (F5-019).
        context.Result = new ObjectResult(ApiErrorResponses.Body(context.Exception)) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
