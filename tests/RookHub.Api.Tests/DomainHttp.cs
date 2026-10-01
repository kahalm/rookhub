using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using RookHub.Api.Filters;

namespace RookHub.Api.Tests;

/// <summary>
/// Controller-Tests für umgestellte Controller (fangen nichts selbst, Codereview A10-005/A7-011): die Antwort,
/// die der Client sieht — das Ergebnis der Aktion oder, bei einer Domänen-Ausnahme, das, was der global
/// registrierte <see cref="DomainExceptionFilter"/> daraus macht. Jede andere Ausnahme läuft durch (im Betrieb 500).
/// </summary>
internal static class DomainHttp
{
    public static async Task<IActionResult> ResultAsync(Func<Task<IActionResult?>> action)
    {
        try
        {
            return Assert.IsAssignableFrom<IActionResult>(await action());
        }
        catch (Exception ex)
        {
            var ctx = new ExceptionContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>()) { Exception = ex };
            new DomainExceptionFilter().OnException(ctx);
            if (!ctx.ExceptionHandled) throw;
            return ctx.Result!;
        }
    }

    /// <summary>Status der Antwort; mit <paramref name="message"/> zusätzlich der Rumpf genau <c>{ message }</c>.</summary>
    public static void AssertError(IActionResult result, int status, string? message = null)
    {
        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeActionResult>(result).StatusCode);
        var body = Assert.IsAssignableFrom<ObjectResult>(result).Value;   // jede Fehlerantwort trägt einen Rumpf
        var json = System.Text.Json.JsonSerializer.Serialize(body);
        if (message is null) Assert.Contains("\"message\":", json);
        else Assert.Equal(System.Text.Json.JsonSerializer.Serialize(new { message }), json);
    }
}
