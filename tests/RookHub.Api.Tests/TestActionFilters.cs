using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace RookHub.Api.Tests;

/// <summary>Für Controller-Tests, die Actions direkt aufrufen (ohne MVC-Pipeline): Sperren wie
/// <c>[DenyWhileImpersonating]</c> oder <c>[RequireTokenScope]</c> sind Filter-Attribute und liefen sonst nicht mit.</summary>
internal static class TestActionFilters
{
    /// <summary>Führt die als Attribut deklarierten <see cref="IActionFilter"/> der Klasse und der Action vorher aus — in
    /// der Reihenfolge der Pipeline (Controller vor Action) — und ruft die Action nur, wenn keiner kurzschließt.</summary>
    public static async Task<IActionResult> InvokeAsync(ControllerBase controller, string actionName, Func<Task<IActionResult>> action)
    {
        var method = controller.GetType().GetMethod(actionName, BindingFlags.Public | BindingFlags.Instance)!;
        var filters = controller.GetType().GetCustomAttributes(true).Concat(method.GetCustomAttributes(true)).OfType<IActionFilter>();
        var ctx = new ActionExecutingContext(new ActionContext(controller.HttpContext, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller);
        foreach (var filter in filters)
        {
            filter.OnActionExecuting(ctx);
            if (ctx.Result is not null) return ctx.Result;
        }
        return await action();
    }
}
