using System.Security.Claims;
using Serilog.Context;

namespace RookHub.Api.Middleware;

/// <summary>
/// Reichert JEDES Log-Event innerhalb eines Requests mit UserId/UserName/IpAddress an
/// (sofern vorhanden) — nicht nur die Request-Summary. Greift via Enrich.FromLogContext().
/// Steht in der Pipeline nach UseAuthentication, damit HttpContext.User bereits gesetzt ist (früher Inline-Lambda in
/// Program.cs, Zerlegung Z2).
/// </summary>
public sealed class RequestLogContextMiddleware
{
    private readonly RequestDelegate _next;

    public RequestLogContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var scopes = new List<IDisposable>(6);
        var ip = context.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrEmpty(ip))
            scopes.Add(LogContext.PushProperty("IpAddress", ip));
        // IpAddress oben ist die VERTRAUENSWUERDIGE, aufgeloeste Client-IP (Basis fuer
        // Rate-Limiter). Zusaetzlich die ROHE Weiterleitungs-Kette, wie sie ankam — alle Hops
        // inkl. vom Client vorangestellter Eintraege, die nicht als Client-IP gewaehlt wurden.
        // Gemerkt VOR UseForwardedHeaders (ForwardedChainCapture, auf 256 Zeichen gekappt):
        // X-Original-For traegt nur den Socket-Peer (frontend-nginx mit Port), nicht die Kette.
        // X-Real-IP wird nicht mehr geloggt — frontend-nginx ueberschreibt es mit $remote_addr,
        // dort stand also immer das Docker-Gateway (Codereview A10-008).
        var fwd = ForwardedChainCapture.Get(context);
        if (!string.IsNullOrEmpty(fwd))
            scopes.Add(LogContext.PushProperty("ForwardedFor", fwd));
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
                scopes.Add(LogContext.PushProperty("UserId", userId));
            if (!string.IsNullOrEmpty(context.User.Identity.Name))
                scopes.Add(LogContext.PushProperty("UserName", context.User.Identity.Name));
            // Admin-Impersonation: das Token trägt dann den `imp`-Claim (Id des impersonierenden Admins).
            // Ohne Marker sehen impersonierte Requests im Log identisch zum echten User aus → hier als
            // ImpersonatorId mitschreiben, damit jeder impersonierte Request in Kibana filterbar ist.
            var imp = context.User.FindFirst("imp")?.Value;
            if (!string.IsNullOrEmpty(imp))
                scopes.Add(LogContext.PushProperty("ImpersonatorId", imp));
        }
        // VisitorId fuer Kibana „Unique Visits": Username (eingeloggt) sonst validierte Anon-Session-Id
        // aus dem X-Visitor-Id-Header. So zaehlen eingeloggte UND anonyme Besucher in EINEM Feld.
        var visitorId = RookHub.Api.Logging.VisitorIdResolver.Resolve(
            context.User?.Identity?.IsAuthenticated == true,
            context.User?.Identity?.Name,
            context.Request.Headers[RookHub.Api.Logging.VisitorIdResolver.HeaderName].FirstOrDefault());
        if (!string.IsNullOrEmpty(visitorId))
            scopes.Add(LogContext.PushProperty("VisitorId", visitorId));
        // Systemcall-Flag: automatische, nicht nutzer-initiierte Requests (Health/Swagger, Client-
        // Heartbeat/-Diagnose, Menü-Check, Badge-/Zähler-Polls, Import-Status-Polls) als
        // RequestKind="system" markieren — echter Nutzer-Traffic bekommt "user". In Kibana filterbar,
        // um das automatische Grundrauschen vom echten Nutzerverhalten zu trennen.
        scopes.Add(LogContext.PushProperty(
            "RequestKind",
            RookHub.Api.Logging.SystemCallClassifier.Classify(context.Request.Path.Value)));
        // Geräteklasse (mobile/tablet/desktop/bot/unknown) aus dem User-Agent → in Kibana als
        // Mobile-vs-PC-Anteil (gesamt + je Bereich über url.path) auswertbar. Roh-UA gekürzt
        // mitschreiben (Diagnose), DeviceType immer (auch "unknown", damit die Terms-Agg vollständig ist).
        var ua = context.Request.Headers.UserAgent.ToString();
        if (!string.IsNullOrEmpty(ua))
            scopes.Add(LogContext.PushProperty("UserAgent", ua.Length > 512 ? ua[..512] : ua));
        scopes.Add(LogContext.PushProperty(
            "DeviceType", RookHub.Api.Logging.DeviceClassifier.Classify(ua)));
        try { await _next(context); }
        finally { for (var i = scopes.Count - 1; i >= 0; i--) scopes[i].Dispose(); }
    }
}

public static class RequestLogContextExtensions
{
    /// <summary>Hängt <see cref="RequestLogContextMiddleware"/> ein.</summary>
    public static IApplicationBuilder UseRequestLogContext(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestLogContextMiddleware>();
}
