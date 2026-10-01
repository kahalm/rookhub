namespace RookHub.Api.Middleware;

/// <summary>
/// Merkt die ROHE <c>X-Forwarded-For</c>-Kette, wie sie ankam, fürs Log-Feld <c>ForwardedFor</c> — BEVOR
/// <c>UseForwardedHeaders</c> sie verbraucht (Codereview A10-008).
///
/// <para><b>Warum nicht <c>X-Original-For</c>:</b> Dort legt die ForwardedHeadersMiddleware nur den ursprünglichen
/// Socket-Peer samt Port ab (im Stack: frontend-nginx, „[::ffff:172.26.0.7]:41234"), nicht die Kette. Die verbrauchten
/// Einträge sind danach weg, und nur die unverbrauchten bleiben in <c>X-Forwarded-For</c>. Das Feld trug so auf praktisch
/// jedem Request eine Proxy-Adresse, und ein vom Client vorangestellter, gefälschter Eintrag — genau der Fall, den das
/// Feld sichtbar machen soll — war im Log nicht zu finden.</para>
///
/// <para>Gekappt auf <see cref="MaxLength"/> Zeichen: der Kopf kommt vom Client und ist frei wählbar.</para>
/// </summary>
public static class ForwardedChainCapture
{
    internal const string ItemKey = "RookHub.RawForwardedFor";

    /// <summary>Obergrenze fürs Log-Feld (zwei Proxys + Client sind rund 50 Zeichen).</summary>
    public const int MaxLength = 256;

    /// <summary>Kette aus dem Kopf in <see cref="HttpContext.Items"/> legen (gekappt); ohne Kopf passiert nichts.</summary>
    public static void Capture(HttpContext context)
    {
        var raw = context.Request.Headers["X-Forwarded-For"].ToString();
        if (string.IsNullOrEmpty(raw)) return;
        context.Items[ItemKey] = raw.Length > MaxLength ? raw[..MaxLength] : raw;
    }

    /// <summary>Die gemerkte Kette, <c>null</c> ohne Kopf.</summary>
    public static string? Get(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as string : null;

    /// <summary>Muss VOR <c>UseForwardedHeaders()</c> stehen.</summary>
    public static IApplicationBuilder UseForwardedChainCapture(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            Capture(context);
            return next(context);
        });
}
