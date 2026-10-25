using System.Globalization;
using System.Security.Claims;

namespace RookHub.Api.Services.Accounts;

/// <summary>
/// Absolute Obergrenze einer Anmeldung (Codereview A1-007/P-14, entschieden 25.10.2026): Jede Anmeldung trägt ihren
/// Anmeldezeitpunkt als <c>auth_time</c>-Claim (Unix-Sekunden, wie OIDC) — im JWT UND im geteilten Cookie
/// <c>rh_session</c>. Vorher schrieb jedes Einlösen des Cookies ein frisches 30-Tage-Cookie samt neuem Token: eine
/// einmal abgegriffene Sitzung verlängerte sich unbegrenzt. Jetzt wird beim Einlösen und bei der Übergabe der
/// ursprüngliche Zeitpunkt weitergereicht, kein Token und kein Cookie läuft länger als <c>auth_time</c> +
/// <see cref="MaxAge"/> (<c>Auth:SessionMaxDays</c>, Vorgabe 90), und ein älteres Cookie wird nicht mehr eingelöst.
/// Danach läuft das JWT ab → 401 <c>invalid_token</c> → die Oberfläche meldet ab (Interceptor/SessionStore).
/// <para><b>Altbestand ohne Claim</b> (vor der Einführung ausgegeben): gilt als „jetzt angemeldet" — kein
/// Massen-Abmelden beim Deploy; ab dem nächsten Einlösen trägt die Sitzung den Claim und die Frist läuft.</para>
/// </summary>
public static class SessionLifetime
{
    /// <summary>Name im Token (OIDC <c>auth_time</c>).</summary>
    public const string AuthTimeClaim = "auth_time";

    public const int DefaultMaxDays = 90;

    /// <summary>Höchstalter einer Anmeldung (<c>Auth:SessionMaxDays</c>, ungesetzt/ungültig = 90 Tage).</summary>
    public static TimeSpan MaxAge(IConfiguration config) =>
        TimeSpan.FromDays(int.TryParse(config["Auth:SessionMaxDays"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) && d > 0
            ? d : DefaultMaxDays);

    /// <summary>Gewünschtes Ablaufdatum, gekappt auf <paramref name="authTime"/> + <see cref="MaxAge"/>.</summary>
    public static DateTime CapExpiry(IConfiguration config, DateTime authTime, DateTime wanted)
    {
        var limit = authTime + MaxAge(config);
        return wanted < limit ? wanted : limit;
    }

    /// <summary>Ist die Anmeldung von <paramref name="authTime"/> zum Zeitpunkt <paramref name="now"/> zu alt?</summary>
    public static bool IsTooOld(IConfiguration config, DateTime authTime, DateTime now) => authTime + MaxAge(config) <= now;

    public static Claim ToClaim(DateTime authTime) =>
        new(AuthTimeClaim, new DateTimeOffset(DateTime.SpecifyKind(authTime, DateTimeKind.Utc)).ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64);

    /// <summary>Anmeldezeitpunkt aus einem geprüften Token, <c>null</c> ohne Claim (Altbestand). Der JWT-Leser bildet
    /// <c>auth_time</c> beim Einlesen auf <see cref="ClaimTypes.AuthenticationInstant"/> ab — beide Namen gelten.</summary>
    public static DateTime? Read(ClaimsPrincipal? principal)
    {
        var value = principal?.FindFirstValue(AuthTimeClaim) ?? principal?.FindFirstValue(ClaimTypes.AuthenticationInstant);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var secs)
            ? DateTimeOffset.FromUnixTimeSeconds(secs).UtcDateTime
            : null;
    }
}
