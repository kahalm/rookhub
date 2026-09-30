using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Logging;

namespace RookHub.Api.Services;

/// <summary>
/// Partitionen des Rate-Limiters, die nach ZWECK getrennt sind (Program.cs registriert sie unter ihrem Namen).
///
/// <para><b>Warum:</b> Vorher hingen 21 anonyme Endpunkte in 11 Controllern an EINER Policy „anonymous-puzzle"
/// (30 Anfragen/min je IP). Benannte Limiter partitionieren nach (Policy, Schlüssel) — also teilten sich KidHub, die
/// anonymen Endless-/Puzzle-Stände, die Punktepartie und der Client-Log EIN Fenster je Adresse. Eine Schulklasse oder
/// Vereinsgruppe hinter EINER NAT-Adresse, die KidHub öffnet (levels + courses je Kind), sah ab dem 16. Kind das
/// Fehlerbild; ein zweiter anonymer Endless-Spieler im selben Netz verlor still seine Server-Stände.</para>
///
/// <para><b>Wie:</b> <see cref="KidsRead"/> (die für alle gleichen Lese-Endpunkte der Kinderseite) je IP mit deutlich
/// höherem Deckel und aus dem globalen 100/min-Topf genommen — die Policy ist dort selbst die Obergrenze je Adresse.
/// <see cref="AnonymousRead"/>/<see cref="AnonymousWrite"/> zählen je Konto, sonst je Adresse + gültiger
/// <c>X-Visitor-Id</c> (die RookHub-App schickt sie auf jedem /api-Aufruf); die Obergrenze je Adresse bleibt der globale
/// Limiter, denn die Visitor-Id ist frei wählbar. KidHub schickt bewusst KEINE Visitor-Id und zählt deshalb je Adresse.</para>
/// </summary>
public static class RateLimitPartitions
{
    /// <summary>Policy-Name der Kinderseiten-Lesezugriffe; der globale Limiter nimmt ihn aus (siehe <see cref="Global"/>).</summary>
    public const string KidsReadPolicy = "kids-read";

    public const int GlobalPermitPerMinute = 100;
    /// <summary>Eine Klasse von 30 Kindern braucht beim Start rund 90 (levels, courses, ggf. language-hint), danach je
    /// Stufenstart eine bis zwei — 240 lässt auch zwei Gruppen hinter derselben Adresse Luft.</summary>
    public const int KidsReadPermitPerMinute = 240;
    public const int AnonymousReadPermitPerMinute = 60;
    /// <summary>Unverändert zum früheren Topf — jetzt aber je Spieler statt je Adresse (Endless speichert alle 3 s).</summary>
    public const int AnonymousWritePermitPerMinute = 30;
    /// <summary>Die übrigen offenen Endpunkte (Client-Log, Bot-Statistik, Token-Test, Bestandssuche, Extension-Senke).</summary>
    public const int AnonymousMiscPermitPerMinute = 30;

    /// <summary>Policy-Name der nutzer-ausgeloesten Crawler-Auftraege (Crawl, Spieler-Details, Vereine nachtragen,
    /// Runden-Monitor einschalten).</summary>
    public const string CrawlerRequestPolicy = "user-crawl";
    /// <summary>Die Crawler-Warteschlange (500 Plaetze) und der chess-results-Takt gehoeren allen — auch den
    /// Hintergrunddiensten (Runden-Monitor, Abo-Abgleich, Turnierverlauf). Vorher galt fuer diese Endpunkte nur der
    /// globale Deckel von 100/min je Adresse: ein einziges Konto fuellte die Warteschlange in rund fuenf Minuten
    /// (Codereview A5-004). Ein Turnier oeffnen, aktualisieren, Vereine nachtragen, Monitor einschalten sind je ein
    /// Auftrag — 10 je Minute bleibt weit darueber.</summary>
    public const int CrawlerRequestPermitPerMinute = 10;

    /// <summary>Nachricht an das Admin-Team je Konto (POST /api/messages/reply, dazu Meldung und Quellen-Hinweis im
    /// Turnierverzeichnis — gleicher Kanal, gemeinsamer Topf) — jede klingelte bei ALLEN Admins.</summary>
    public const int UserMessagePermitPerMinute = 10;
    /// <summary>„Spielzeit aktualisieren" (POST /api/training-goals/sync-play) je Konto. Nacheinander bremst schon die
    /// Sperrfrist in PlayTimeService; das hier fängt parallele Anfragen ab, die alle vor dem ersten gespeicherten Abruf
    /// ankommen.</summary>
    public const int SyncPlayPermitPerMinute = 3;

    public static string ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>Globaler Deckel je Adresse — außer für Endpunkte, deren eigene Policy schon die Obergrenze je Adresse
    /// ist und höher liegt (<see cref="KidsReadPolicy"/>); sonst schnitte der globale Topf sie doch wieder bei 100 ab.</summary>
    public static RateLimitPartition<string> Global(HttpContext ctx, int scale) =>
        HasOwnIpCeiling(ctx)
            ? RateLimitPartition.GetNoLimiter(KidsReadPolicy)
            : FixedWindow(ClientIp(ctx), GlobalPermitPerMinute * scale);

    /// <summary>Trägt der angesteuerte Endpunkt die Policy <see cref="KidsReadPolicy"/>? (Das Routing läuft vor dem
    /// Rate-Limiter, der Endpunkt ist hier also schon bekannt.)</summary>
    public static bool HasOwnIpCeiling(HttpContext ctx) =>
        ctx.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == KidsReadPolicy;

    public static RateLimitPartition<string> KidsRead(HttpContext ctx, int scale) =>
        FixedWindow("ip:" + ClientIp(ctx), KidsReadPermitPerMinute * scale);

    public static RateLimitPartition<string> AnonymousRead(HttpContext ctx, int scale) =>
        FixedWindow(UserVisitorOrIp(ctx), AnonymousReadPermitPerMinute * scale);

    public static RateLimitPartition<string> AnonymousWrite(HttpContext ctx, int scale) =>
        FixedWindow(UserVisitorOrIp(ctx), AnonymousWritePermitPerMinute * scale);

    public static RateLimitPartition<string> AnonymousMisc(HttpContext ctx, int scale) =>
        FixedWindow(UserOrIp(ctx), AnonymousMiscPermitPerMinute * scale);

    /// <summary>Crawler-Auftraege je Konto (alle Endpunkte mit <see cref="CrawlerRequestPolicy"/> teilen EIN Fenster);
    /// ohne Anmeldung — die Endpunkte verlangen sie — je Adresse.</summary>
    public static RateLimitPartition<string> CrawlerRequest(HttpContext ctx, int scale) =>
        FixedWindow(UserOrIp(ctx), CrawlerRequestPermitPerMinute * scale);

    public static RateLimitPartition<string> UserMessage(HttpContext ctx, int scale) =>
        FixedWindow(UserOrIp(ctx), UserMessagePermitPerMinute * scale);

    public static RateLimitPartition<string> SyncPlay(HttpContext ctx, int scale) =>
        FixedWindow(UserOrIp(ctx), SyncPlayPermitPerMinute * scale);

    /// <summary>Angemeldet: je Konto. Sonst je Adresse — ohne Visitor-Id, weil diese Endpunkte teuer sind
    /// (semantische Suche) oder keine Visitor-Id kennen (Bot, Extension, Provider-Preflight).</summary>
    public static string UserOrIp(HttpContext ctx) =>
        UserKey(ctx) ?? "ip:" + ClientIp(ctx);

    /// <summary>Angemeldet: je Konto. Sonst je Adresse + gültiger <c>X-Visitor-Id</c> (gleiche strenge Form wie die
    /// Anon-Session-Id, sonst ignoriert), ohne Header je Adresse.</summary>
    public static string UserVisitorOrIp(HttpContext ctx)
    {
        if (UserKey(ctx) is { } user) return user;
        var ip = "ip:" + ClientIp(ctx);
        var visitor = VisitorIdResolver.Resolve(false, null,
            ctx.Request.Headers[VisitorIdResolver.HeaderName].FirstOrDefault());
        return visitor is null ? ip : ip + "|" + visitor;
    }

    private static string? UserKey(HttpContext ctx) =>
        ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value is { Length: > 0 } id ? "u:" + id : null;

    public static RateLimitPartition<string> FixedWindow(string key, int permit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: key,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
}
