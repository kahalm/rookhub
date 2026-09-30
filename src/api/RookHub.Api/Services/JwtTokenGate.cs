using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Die beiden Ereignisse des JWT-Handlers, die entscheiden, ob ein formal gültiges Token noch etwas
/// wert ist — und die es SICHTBAR machen, wenn nicht.
///
/// <para><b>Warum eine eigene Klasse statt zweier Lambdas in Program.cs:</b> Am 2026-09-09 wurde
/// gemeldet, man müsse sich „immer wieder einloggen". Die Suche in den Logs lief ins Leere, weil eine
/// Token-Ablehnung nirgends stand: der Handler antwortete 401 und schwieg. Ob ein Stempel nicht mehr
/// passte, ein Konto gelöscht war oder die Datenbank gerade nicht antwortete, war nicht zu
/// unterscheiden. Jetzt hinterlässt jede Ablehnung eine Zeile mit Grund und Pfad (Logger
/// <c>RookHub.Api.JwtAuth</c>), und die Logik ist ohne HTTP-Pipeline testbar.</para>
///
/// <para><b>Fail-open bei Infrastrukturfehlern.</b> Die Prüfung gegen die Datenbank kann scheitern,
/// ohne dass das Token schlecht wäre (Verbindungsabbruch, Timeout beim Neustart von MariaDB). Vorher
/// wurde daraus ein 401, und der Client warf die Sitzung weg — der Nutzer war für einen
/// Server-Schluckauf ausgeloggt. Jetzt geht der Request durch und scheitert, wenn überhaupt, an der
/// eigentlichen Datenbankarbeit mit 500. Das Risiko: ein gelöschtes Konto könnte während eines
/// Datenbankausfalls einen Request absetzen — der dann an derselben Datenbank scheitert.</para>
/// </summary>
public static class JwtTokenGate
{
    public const string LoggerName = "RookHub.Api.JwtAuth";

    /// <summary>
    /// Warum ein Token abgelehnt wird; <c>null</c> = es bleibt gültig. Reine Funktion über
    /// <see cref="AuthUserValidation"/>, damit der Kern ohne HTTP-Kontext testbar ist.
    /// </summary>
    public static async Task<string?> RejectionReasonAsync(
        AppDbContext db, IMemoryCache cache, int userId, string? tokenStamp, ILogger logger, CancellationToken ct)
    {
        TokenRejection rejection;
        try
        {
            rejection = await AuthUserValidation.CheckTokenAsync(db, cache, userId, tokenStamp, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Kein Urteil über das Token möglich → durchlassen (siehe Klassenkommentar).
            logger.LogWarning(ex, "JwtAuth: Token-Prüfung für User {UserId} übersprungen — Datenbank nicht erreichbar", userId);
            return null;
        }

        return rejection switch
        {
            TokenRejection.None => null,
            TokenRejection.InactiveUser => "inactive-user",
            TokenRejection.StampMismatch => "stamp-mismatch",
            _ => "unknown",
        };
    }

    /// <summary>Signatur und Laufzeit sind geprüft — jetzt gegen den Kontostand.</summary>
    public static async Task OnTokenValidatedAsync(TokenValidatedContext ctx)
    {
        var idStr = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idStr, out var uid)) return;

        var services = ctx.HttpContext.RequestServices;
        var db = services.GetRequiredService<AppDbContext>();
        var cache = services.GetRequiredService<IMemoryCache>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerName);
        // Zusätzlich zum Gelöscht-Check den Security-Stamp prüfen: nach Passwort-Reset/-Änderung
        // passt der sstamp-Claim nicht mehr → Token wird abgelehnt (Alt-Token ohne Claim bleiben gültig).
        var stamp = ctx.Principal?.FindFirstValue("sstamp");

        var reason = await RejectionReasonAsync(db, cache, uid, stamp, logger, ctx.HttpContext.RequestAborted);
        if (reason is null) return;

        logger.LogWarning("JwtAuth: Token von User {UserId} abgelehnt ({Reason}) auf {Path}",
            uid, reason, ctx.HttpContext.Request.Path.Value);
        ctx.Fail("User account is deleted or the token has been invalidated.");
    }

    /// <summary>
    /// Drossel für <see cref="OnAuthenticationFailedAsync"/>: je aufgelöster Client-IP und Ausnahmetyp
    /// höchstens eine Zeile je Minute auf ihrem Level, der Rest auf Debug (unter dem Mindestlevel, landet
    /// also weder in der Konsole noch im ES). Die Authentifizierung läuft VOR dem Rate-Limiter: ein
    /// anonymer Client mit <c>Authorization: Bearer x</c> in einer Schleife schrieb sonst je Anfrage eine
    /// Warnung — auch für die Anfragen, die der Limiter danach mit 429 abweist (rund 86 Mio. Zeilen am Tag
    /// bei 1 000/s, dauerhafter warn_spike im log-watcher).
    /// </summary>
    internal static readonly LogThrottle FailureThrottle = new(TimeSpan.FromMinutes(1), maxEntries: 2000);

    /// <summary>Signatur/Laufzeit/Format haben NICHT gepasst. Ein abgelaufenes Token ist Alltag
    /// (der Client prüft <c>exp</c> selbst, aber Uhren gehen auseinander) und bleibt Information;
    /// ein kaputtes Format ebenso (Scanner, „Bearer x", „Bearer null" aus fehlerhaften Clients — nichts,
    /// was ein gültiges Token auch nur versucht). Falsche Signatur, fremder Aussteller u. Ä. ist eine
    /// Warnung. Alles gedrosselt über <see cref="FailureThrottle"/>, die Client-IP steht explizit im
    /// Event: der LogContext-Enricher mit <c>IpAddress</c> läuft erst NACH der Authentifizierung.</summary>
    public static Task OnAuthenticationFailedAsync(AuthenticationFailedContext ctx)
    {
        var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerName);
        LogAuthenticationFailure(logger, ctx.Exception, ctx.HttpContext.Request.Path.Value,
            ctx.HttpContext.Connection.RemoteIpAddress?.ToString(), FailureThrottle, DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    /// <summary>Kern von <see cref="OnAuthenticationFailedAsync"/> ohne HTTP-Kontext (testbar mit eigener Drossel).</summary>
    internal static void LogAuthenticationFailure(
        ILogger logger, Exception exception, string? path, string? ip, LogThrottle throttle, DateTimeOffset now)
    {
        var kind = exception.GetType().Name;
        var level = exception is SecurityTokenExpiredException or SecurityTokenMalformedException
            ? LogLevel.Information
            : LogLevel.Warning;
        if (!throttle.ShouldLog($"{ip}|{kind}", now))
            level = LogLevel.Debug;

        if (exception is SecurityTokenExpiredException)
            logger.Log(level, "JwtAuth: abgelaufenes Token auf {Path} ({ExceptionType}) von {IpAddress}", path, kind, ip);
        else
            logger.Log(level, "JwtAuth: Token nicht akzeptiert auf {Path} ({ExceptionType}: {Message}) von {IpAddress}",
                path, kind, exception.Message, ip);
    }
}
