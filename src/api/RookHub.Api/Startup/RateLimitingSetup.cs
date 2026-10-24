using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using RookHub.Api.Services;

namespace RookHub.Api.Startup;

/// <summary>Rate-Limiting: globaler Deckel je IP und die benannten Policies der Controller ([EnableRateLimiting]).
/// Schlüssel und Deckel der meisten Policies stehen in <see cref="RateLimitPartitions"/>; <c>AuthRateLimitTests</c>
/// prüft, dass jede in einem Controller benutzte Policy hier registriert ist.</summary>
public static class RateLimitingSetup
{
    public static IServiceCollection AddRookHubRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        // M-11: Rate limiting for auth endpoints
        // S-6: Global rate limiting for all endpoints
        // Faktor auf ALLE Deckel unten; setzt nur der E2E-Stack (siehe RateLimitScale).
        var permitScale = RookHub.Api.Services.RateLimitScale.FromConfig(configuration);
        services.AddSingleton<RateLimitRejectionHandler>();
        services.AddRateLimiter(options =>
        {
            // 100/min je IP — außer für die Kinderseiten-Lesezugriffe („kids-read"), deren eigene Policy die Obergrenze je
            // Adresse ist (siehe RateLimitPartitions).
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RookHub.Api.Services.RateLimitPartitions.Global(context, permitScale));
            AddLoginAndAnonymousPolicies(options, permitScale);
            AddPerAccountPolicies(options, permitScale);
            AddCrawlerAndScanPolicies(options, permitScale);
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Retry-After + Rumpf, und eine gedrosselte Warnung je (IP, Policy): vorher hinterließ eine Absage keine Spur —
            // UseRateLimiter steht vor dem Request-Log, und die Middleware meldet sie nur auf Debug (Codereview A10-010).
            options.OnRejected = RateLimitRejectionHandler.OnRejectedAsync;
        });
        return services;
    }

    // Die benannten Limiter MÜSSEN pro Client-IP partitionieren (wie der GlobalLimiter), sonst
    // wäre jeder ein EINZIGER globaler Bucket: ein Angreifer könnte mit 10 Logins/min das Fenster
    // site-weit für alle Nutzer ausschöpfen (Login/Register/Forgot-DoS) UND Brute-Force wäre nicht
    // pro-IP gedrosselt. Partition-Key = aufgelöste Client-IP (UseForwardedHeaders läuft davor).
    private static RateLimitPartition<string> PerIpFixedWindow(HttpContext ctx, int permit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });

    /// <summary>Anmeldung (je IP) und die offenen Endpunkte nach Zweck.</summary>
    private static void AddLoginAndAnonymousPolicies(RateLimiterOptions options, int permitScale)
    {
        options.AddPolicy("auth", ctx => PerIpFixedWindow(ctx, 10 * permitScale));
        // Offene Endpunkte nach ZWECK getrennt (vorher teilten sich 21 davon „anonymous-puzzle", 30/min je IP — eine
        // Schulklasse hinter einer NAT-Adresse sah in KidHub ab dem 16. Kind 429). Schlüssel und Deckel in
        // RateLimitPartitions: Kinderseite je IP mit hohem Deckel und ohne den globalen Topf; anonyme Stände und
        // Lesezugriffe je Konto bzw. je IP + X-Visitor-Id; der Rest (Client-Log, Bot-Statistik, Token-Test,
        // Bestandssuche, Extension-Senke) bleibt „anonymous-puzzle", angemeldet jetzt je Konto statt je IP.
        options.AddPolicy("anonymous-puzzle", ctx => RookHub.Api.Services.RateLimitPartitions.AnonymousMisc(ctx, permitScale));
        options.AddPolicy("kids-read", ctx => RookHub.Api.Services.RateLimitPartitions.KidsRead(ctx, permitScale));
        options.AddPolicy("ebook-entries", ctx => RookHub.Api.Services.RateLimitPartitions.EbookEntries(ctx, permitScale));
        options.AddPolicy("ebook-file", ctx => RookHub.Api.Services.RateLimitPartitions.EbookFile(ctx, permitScale));
        options.AddPolicy("anonymous-read", ctx => RookHub.Api.Services.RateLimitPartitions.AnonymousRead(ctx, permitScale));
        options.AddPolicy("anonymous-write", ctx => RookHub.Api.Services.RateLimitPartitions.AnonymousWrite(ctx, permitScale));
    }

    /// <summary>Policies je Konto (Fallback IP): Flags, Nachrichten, Spielzeit, Suche, Rechte, geteilte Anmeldung, Extension-Senken.</summary>
    private static void AddPerAccountPolicies(RateLimiterOptions options, int permitScale)
    {
        // Community-Review-Flags (flag-hints): jeder eingeloggte User darf setzen/aufheben — aber
        // gedrosselt PRO USER, damit niemand per Id-Iteration den Admin-Review-Bestand des ganzen
        // Katalogs umflaggt (der globale 100/min-IP-Limiter erlaubte ~6000 Flags/h).
        options.AddPolicy("user-flag", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10 * permitScale,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        // Nachricht an das Admin-Team (POST /api/messages/reply): offen für jedes Konto, und jede Nachricht legte bei
        // ALLEN Admins Glocke + Web-Push an — nur der globale 100/min-IP-Deckel bremste (~6000 Nachrichten/h). Je KONTO;
        // die Glocke entprellt zusätzlich AdminMessageService (Serie ungelesener Nachrichten = eine Glocke je Admin).
        // Derselbe Topf gilt für Meldung und Quellen-Hinweis im Turnierverzeichnis — beide schreiben in diesen Kanal.
        options.AddPolicy("user-message", ctx => RookHub.Api.Services.RateLimitPartitions.UserMessage(ctx, permitScale));
        // „Spielzeit aktualisieren" (POST /api/training-goals/sync-play): jeder Aufruf fragte von der Server-IP bei Lichess
        // und chess.com — je KONTO gedrosselt (die Sperrfrist je Plattform setzt PlayTimeService.ManualSyncCooldown).
        options.AddPolicy("sync-play", ctx => RookHub.Api.Services.RateLimitPartitions.SyncPlay(ctx, permitScale));
        // Nutzer-Suche (Autocomplete in der Freundesliste): eigene Partition PRO USER. Vorher lief sie
        // auf der „auth"-Policy — und Endpoint-Limiter partitionieren nach (Policy-Name, Key), also
        // teilten sich Suche und Login/Registrierung/Reset EIN 10/min-Fenster je IP: zehn
        // Tastenanschläge sperrten hinter einem NAT (Schule, Verein) die Anmeldung für eine Minute.
        options.AddPolicy("user-search", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30 * permitScale,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        // Rechte-Abfrage (GET /api/auth/permissions): eigene Partition PRO USER. Sie lief über das Klassen-Attribut
        // auf „auth" — jede sichtbare Oberfläche (RookHub, Turnierseite, LeagueHub) fragt beim Start, bei der Rückkehr
        // in den Tab und alle 2 min, und hinter einem NAT erschöpfte das das 10/min-Login-Fenster der ganzen IP.
        options.AddPolicy("auth-permissions", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30 * permitScale,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
        // Geteilte Anmeldung (POST /api/auth/rh-session, rh-session/end): JEDER App-Start ohne Anmeldung fragt hier (auf allen
        // vier Oberflächen) — eigene IP-Partition mit höherem Deckel statt „auth": eine Schulklasse, die KidHub öffnet,
        // sind 25 Starts in einer Minute und sperrte sonst Anmeldung, Registrierung und „Passwort vergessen".
        options.AddPolicy("auth-session", ctx => PerIpFixedWindow(ctx, 60 * permitScale));
        // Chessable-Roh-Senken der Extension (problem-moves, session-moves, review-lines): eigene Partition PRO
        // USER, zusätzlich zum globalen Deckel je IP. Vorher durfte ein Konto die vollen 100 Batches/min je IP
        // schreiben (~1,3 GB/min). RepCheck flusht je Senke und Kurs höchstens alle 15 s — 60/min für alle drei
        // zusammen lässt auch mehreren offenen Kursen reichlich Luft. Die Menge je Konto deckelt ChessableSinkBytes.
        options.AddPolicy("extension-sink", ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 60 * permitScale,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));
    }

    /// <summary>Turnier-Proxy, Crawler-Aufträge, Verzeichnis, rechenintensive Suchen und Soziales.</summary>
    private static void AddCrawlerAndScanPolicies(RateLimiterOptions options, int permitScale)
    {
        // Anonyme Turnier-Proxy-GETs (oeffentliche Turnierseite / Teilen-Feature):
        // bewusst ohne Login erreichbar, aber gedrosselt, damit der dahinterliegende
        // Crawler (chess-results.com) nicht ungebremst missbraucht werden kann.
        options.AddPolicy("anonymous-tournament", ctx => PerIpFixedWindow(ctx, 60 * permitScale));
        // Nutzer-ausgeloeste Crawler-Auftraege (crawl, crawl/player-details, {id}/clubs, Monitor einschalten): je KONTO,
        // zusaetzlich zum globalen Deckel je IP — die Crawler-Warteschlange teilen sich alle (RateLimitPartitions).
        options.AddPolicy("user-crawl", ctx => RookHub.Api.Services.RateLimitPartitions.CrawlerRequest(ctx, permitScale));
        // Turnierkalender lesen — auch ohne Anmeldung (Turnierseite voll benutzbar ohne Konto): je Konto bzw. Adresse.
        options.AddPolicy("directory-read", ctx => RookHub.Api.Services.RateLimitPartitions.DirectoryRead(ctx, permitScale));
        // Lückensuche einer Rekonstruktion (gap, gap/propose): reine CPU im Request-Thread — je KONTO gedrosselt, die
        // Gleichzeitigkeit über alle Konten deckelt GapSearchGate (RateLimitPartitions, Codereview N5-001).
        options.AddPolicy("reconstruction-gap", ctx => RookHub.Api.Services.RateLimitPartitions.ReconstructionGap(ctx, permitScale));
        // Repertoire-Analyse der Erweiterung (analyze-game): je KONTO — ein Neuaufbau des Positions-Sets liest alle
        // markierten PGNs und spielt jeden Halbzug nach (RateLimitPartitions, Codereview N8-005).
        options.AddPolicy("extension-analyze", ctx => RookHub.Api.Services.RateLimitPartitions.ExtensionAnalyze(ctx, permitScale));
        // Baummodus und Ähnlichkeitssuche im Repertoire (position-tree, similar-positions): je KONTO ein Fenster plus
        // höchstens zwei gleichzeitige Scans — jede Anfrage spielt alle lesbaren Linien nach, bis zum Zeitbudget der
        // Dienste (RateLimitPartitions, WindowAndConcurrencyLimiter, Codereview N7-001).
        options.AddPolicy("repertoire-scan", ctx => RookHub.Api.Services.RateLimitPartitions.RepertoireScan(ctx, permitScale));
        // Freundschaftsanfrage und Challenge (friends/request, challenges): je KONTO, ein gemeinsames Fenster — jede legte
        // beim Empfänger Glocke + Web-Push an, Senden–Zurückziehen–Senden flutete beliebige Konten (RateLimitPartitions,
        // Codereview N9-003). Die Glocke der Freundschaftsanfrage entprellt zusätzlich FriendService.
        // Zugfolgen-Suche im lokalen Explorer (explorer/paths): je KONTO — bis zu 400 Abfragen und 20 s je Aufruf.
        options.AddPolicy("explorer-paths", ctx => RookHub.Api.Services.RateLimitPartitions.ExplorerPaths(ctx, permitScale));
        options.AddPolicy("user-social", ctx => RookHub.Api.Services.RateLimitPartitions.UserSocial(ctx, permitScale));
    }
}
