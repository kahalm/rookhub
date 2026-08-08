using System.Collections.Concurrent;
using System.Linq;
using System.Security.Claims;
using Serilog.Context;

namespace RookHub.Api.Middleware;

/// <summary>
/// Scope-Zaun für Personal-Access-Tokens (<c>rkh_…</c>): wer einen <c>scope</c>-Claim mitbringt
/// (= PAT, JWTs haben keinen), darf ausschließlich die Extension-Fläche benutzen — alles andere
/// endet mit 403, noch bevor Routing/Controller den Request sehen.
///
/// <para>WARUM: Ein PAT bekommt im <see cref="Services.ApiTokenAuthenticationHandler"/> dieselben
/// Identitäts-Claims wie ein JWT. Vorher war der Scope NUR im ExtensionController geprüft — damit
/// war ein „extension"-Token faktisch ein Voll-Account-Token: <c>PUT /api/profile</c> ändert die
/// E-Mail, danach „Passwort vergessen" → Kontoübernahme. Zentral gezogen sind neue Endpoints
/// automatisch gesperrt statt automatisch offen.</para>
///
/// <para>Der Zaun steht bewusst als eigene Klasse (nicht als Inline-Lambda in <c>Program.cs</c>):
/// so testet <c>PatScopeFenceTests</c> den ECHTEN Code samt <see cref="AllowedPrefixes"/> statt
/// einer Kopie, die still auseinanderlaufen kann. <c>Program.cs</c> verdrahtet ihn per
/// <see cref="PatScopeFenceExtensions.UsePatScopeFence"/> NACH <c>UseAuthentication()</c> —
/// davor wäre <c>HttpContext.User</c> noch anonym und der Zaun ein wirkungsloses No-op.</para>
/// </summary>
public sealed class PatScopeFenceMiddleware
{
    /// <summary>Einzige Quelle der Wahrheit für die Fläche, die ein PAT benutzen darf.
    /// Segment-Vergleich: „/api/extensionfoo" ist KEIN Treffer.</summary>
    public static readonly string[] AllowedPrefixes = ["/api/extension"];

    /// <summary>Maschinenlesbarer Fehlercode im 403-Body (das Frontend/RepCheck unterscheidet
    /// daran „Token hat den falschen Scope" von „Token ungültig").</summary>
    public const string ErrorCode = "api_token_scope";

    /// <summary>ECS-Domänen-Tag aus dem kanonischen Vokabular (log-watcher/schema/logging-schema.md):
    /// der Block ist ein Auth-Event und muss unter <c>tags: auth</c> auffindbar sein.</summary>
    internal const string LogTags = "auth";

    /// <summary>Pro Token-Inhaber höchstens ein <c>Warning</c> je Fenster, der Rest auf
    /// <c>Information</c> — ein hämmernder Client (oder ein gestohlener Token in einer Schleife)
    /// soll den <c>warn_spike</c> des log-watchers nicht dauerhaft auslösen. Jeder blockierte
    /// Request wird trotzdem geloggt, nur eben leiser.</summary>
    internal static readonly TimeSpan WarnThrottle = TimeSpan.FromMinutes(1);

    /// <summary>Harter Deckel für die Throttle-Tabelle. Sie ist ohnehin durch die Zahl echter
    /// Konten begrenzt (der Zaun greift nur bei erfolgreich authentifizierten Tokens); der Deckel
    /// wird in <c>Prune</c> auch dann eingehalten, wenn innerhalb EINES Fensters mehr Inhaber
    /// blockiert werden — dann fliegen die ältesten Einträge raus.</summary>
    internal const int MaxThrottleEntries = 500;

    private readonly RequestDelegate _next;
    private readonly ILogger<PatScopeFenceMiddleware> _logger;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastWarned = new();

    public PatScopeFenceMiddleware(RequestDelegate next, ILogger<PatScopeFenceMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Darf ein PAT diesen Pfad benutzen? Segment-basiert und case-insensitiv.</summary>
    public static bool IsAllowedPath(PathString path) =>
        AllowedPrefixes.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    public async Task InvokeAsync(HttpContext context)
    {
        // Der Zaun hängt am VORHANDENSEIN des scope-Claims, nicht an seinem Wert: ein künftiger
        // Scope öffnet damit nicht versehentlich die ganze API.
        var scope = context.User?.FindFirst("scope")?.Value;
        if (scope == null || IsAllowedPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        LogBlocked(context, scope);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            error = ErrorCode,
            detail = $"API tokens (scope '{scope}') may only be used on {string.Join(", ", AllowedPrefixes)}.",
        });
    }

    /// <summary>
    /// Schreibt den Block ins Log. Der Zaun kappt VOR dem Rate-Limiter, dem LogContext-Enricher und
    /// <c>UseSerilogRequestLogging</c> — ohne dieses Event taucht der 403 in KEINEM Log auf (weder
    /// für die Fehlersuche „warum antwortet die Extension mit 403" noch als Signal für einen
    /// gestohlenen Token, der die API abklopft). Deshalb werden User/IP/Pfad/Status hier explizit
    /// mitgegeben statt auf die späteren Enricher zu warten.
    ///
    /// <para>Bewusst NICHT geloggt: der rohe Token oder sein Anzeige-Präfix — ein Log-Leser darf
    /// daraus nie ein benutzbares Credential rekonstruieren. Der Besitzer ist über
    /// <c>UserId</c>/<c>UserName</c> eindeutig genug, um den Token zu widerrufen.</para>
    /// </summary>
    private void LogBlocked(HttpContext context, string scope)
    {
        var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userName = context.User?.Identity?.Name;
        var ip = context.Connection.RemoteIpAddress?.ToString();
        var level = ShouldWarn($"{userId}|{scope}", DateTimeOffset.UtcNow)
            ? LogLevel.Warning
            : LogLevel.Information;

        using (LogContext.PushProperty("LogTags", LogTags))
        using (LogContext.PushProperty("StatusCode", StatusCodes.Status403Forbidden))
        {
            // „Unauthorized" im Text ist kein Zufall: die Ingest-Pipeline setzt daran (bzw. an
            // StatusCode 403) zusätzlich automatisch das ECS-Tag `auth`.
            _logger.Log(level,
                "Unauthorized API token scope: token scope '{ApiTokenScope}' is fenced off {RequestMethod} {RequestPath} (user={UserId} name={UserName} ip={IpAddress})",
                scope, context.Request.Method, context.Request.Path.Value, userId, userName, ip);
        }
    }

    /// <summary>
    /// Throttle-Entscheidung: erster Block je <paramref name="key"/> (und danach je
    /// <see cref="WarnThrottle"/>) auf Warning, dazwischen leiser. Bei echter Gleichzeitigkeit auf
    /// demselben Key kann die Update-Factory mehrfach laufen und ein Warning zu viel durchlassen —
    /// für eine Rausch-Heuristik unkritisch, ein Lock wäre hier teurer als der Schaden.
    /// </summary>
    internal bool ShouldWarn(string key, DateTimeOffset now)
    {
        var warn = false;
        _lastWarned.AddOrUpdate(
            key,
            _ => { warn = true; return now; },
            (_, last) =>
            {
                if (now - last < WarnThrottle) return last;
                warn = true;
                return now;
            });

        if (_lastWarned.Count > MaxThrottleEntries) Prune(now);
        return warn;
    }

    /// <summary>Nur für Tests: aktuelle Größe der Throttle-Tabelle.</summary>
    internal int ThrottleEntryCount => _lastWarned.Count;

    private void Prune(DateTimeOffset now)
    {
        foreach (var (key, last) in _lastWarned)
            if (now - last >= WarnThrottle)
                _lastWarned.TryRemove(key, out _);

        // Abgelaufene wegzuräumen reicht NICHT: kommen viele verschiedene Token-Inhaber
        // innerhalb EINES Fensters, ist nichts abgelaufen und die Tabelle wächst über den
        // Deckel hinaus — der wäre dann bloß ein Kommentar. Deshalb zusätzlich die ältesten
        // Einträge wegwerfen. Kosten: für einen verworfenen Inhaber höchstens ein Warning
        // mehr; das ist genau die Rausch-Heuristik, um die es hier geht.
        if (_lastWarned.Count <= MaxThrottleEntries) return;
        foreach (var eintrag in _lastWarned.OrderBy(e => e.Value).Take(_lastWarned.Count - MaxThrottleEntries))
            _lastWarned.TryRemove(eintrag.Key, out _);
    }
}

/// <summary>Verdrahtung des <see cref="PatScopeFenceMiddleware"/> in die Pipeline.</summary>
public static class PatScopeFenceExtensions
{
    /// <summary>Muss NACH <c>UseAuthentication()</c> stehen (sonst ist <c>HttpContext.User</c>
    /// noch anonym) und VOR Routing/Controllern.</summary>
    public static IApplicationBuilder UsePatScopeFence(this IApplicationBuilder app) =>
        app.UseMiddleware<PatScopeFenceMiddleware>();
}
