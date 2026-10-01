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

    /// <summary>Policy-Name der Lückensuche einer Rekonstruktion (<c>…/gap</c>, <c>…/gap/propose</c>).</summary>
    public const string ReconstructionGapPolicy = "reconstruction-gap";
    /// <summary>Lückensuchen je Konto und Minute. Jede rechnet bis zum Budget des <see cref="GapSolver"/> im Request-Thread;
    /// vorher galt nur der globale Deckel von 100/min je Adresse — ein frei registriertes Konto hielt so rund hundert
    /// Suchen gleichzeitig am Laufen (Codereview 2026-09-29, N5-001). Ein Mensch klickt „Lücke schließen" einmal je Lücke;
    /// die Gleichzeitigkeit über alle Konten deckelt zusätzlich <see cref="GapSearchGate"/>.</summary>
    public const int ReconstructionGapPermitPerMinute = 6;

    /// <summary>Policy-Name der Repertoire-Analyse der Erweiterung (<c>POST /api/extension/analyze-game</c>).</summary>
    public const string ExtensionAnalyzePolicy = "extension-analyze";
    /// <summary>Analysen je Konto und Minute. RepCheck fragt einmal je angesehener Partie (und beim Knopf
    /// „Aktualisieren"); vorher galt nur der globale Deckel von 100/min je Adresse, und jede Anfrage durfte das
    /// Positions-Set des Kontos neu bauen lassen (Codereview 2026-09-29, N8-005).</summary>
    public const int ExtensionAnalyzePermitPerMinute = 30;

    /// <summary>Policy-Name des Baummodus und der Ähnlichkeitssuche im Repertoire (<c>position-tree</c>,
    /// <c>similar-positions</c>).</summary>
    public const string RepertoireScanPolicy = "repertoire-scan";
    /// <summary>Anfragen je Konto und Minute (beide Endpunkte teilen EIN Fenster). Jede spielt alle lesbaren
    /// Repertoire-Linien nach (bis zum Zeitbudget der Dienste); vorher galt nur der globale Deckel von 100/min je
    /// Adresse (Codereview 2026-09-29, N7-001). Bewusst ÜBER diesem Deckel: das Panel lädt im Baum- und im
    /// Ähnlich-Modus bei JEDEM Schritt durch eine Partie neu (Pfeiltasten in Analyse, PGN-Viewer, geteilter
    /// Partie) — 30/min zeigte beim gemächlichen Durchklicken schon nach einer halben Minute Fehler. Die Rechenzeit
    /// deckelt <see cref="RepertoireScanConcurrentPerAccount"/>, nicht das Fenster. Die Stellungssuche
    /// (<c>position-lookup</c>) liest aus dem gecachten Index und bleibt ganz draußen.</summary>
    public const int RepertoireScanPermitPerMinute = 120;
    /// <summary>Gleichzeitig laufende Scans je Konto. Begrenzt die CPU besser als jedes Fenster: höchstens zwei
    /// Durchläufe à Zeitbudget (8 s) je Konto statt bis zu „Fenster × 8 s" parallel.</summary>
    public const int RepertoireScanConcurrentPerAccount = 2;
    /// <summary>Wartende Scans je Konto, wenn beide Plätze belegt sind. Es wartet nur der NEUESTE
    /// (<see cref="QueueProcessingOrder.NewestFirst"/>): kommt ein weiterer, bekommt der ältere Wartende 429 — das
    /// Panel hat dessen Antwort ohnehin schon verworfen (Durchklicken), die jüngste Stellung kommt aber durch.</summary>
    public const int RepertoireScanQueuePerAccount = 1;

    /// <summary>Policy-Name der sozialen Glocken-Auslöser: Freundschaftsanfrage und Challenge
    /// (<c>POST /api/friends/request/{userId}</c>, <c>POST /api/challenges</c>).</summary>
    public const string UserSocialPolicy = "user-social";
    /// <summary>Anfragen je Konto und Minute (beide Endpunkte teilen EIN Fenster). Jede legte beim Empfänger eine Glocke
    /// samt Web-Push-Auftrag an; vorher galt nur der globale Deckel von 100/min je Adresse — Senden, Zurückziehen, Senden
    /// ergab rund 50 Glocken je Minute gegen ein beliebiges Konto (Codereview 2026-09-29, N9-003). Ein Mensch schickt
    /// nach dem Lösen ein Puzzle weiter oder stellt eine Anfrage — 10 je Minute bleibt weit darüber.</summary>
    public const int UserSocialPermitPerMinute = 10;

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

    public static RateLimitPartition<string> ReconstructionGap(HttpContext ctx, int scale) =>
        FixedWindow(UserOrIp(ctx), ReconstructionGapPermitPerMinute * scale);

    public static RateLimitPartition<string> ExtensionAnalyze(HttpContext ctx, int scale) =>
        FixedWindow(UserOrIp(ctx), ExtensionAnalyzePermitPerMinute * scale);

    /// <summary>Fenster UND Gleichzeitigkeit je Konto (<see cref="WindowAndConcurrencyLimiter"/>).</summary>
    public static RateLimitPartition<string> RepertoireScan(HttpContext ctx, int scale) =>
        RateLimitPartition.Get(UserOrIp(ctx), _ => new WindowAndConcurrencyLimiter(
            RepertoireScanPermitPerMinute * scale, TimeSpan.FromMinutes(1),
            RepertoireScanConcurrentPerAccount * scale, RepertoireScanQueuePerAccount));

    public static RateLimitPartition<string> UserSocial(HttpContext ctx, int scale) =>
        FixedWindow(UserOrIp(ctx), UserSocialPermitPerMinute * scale);

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
