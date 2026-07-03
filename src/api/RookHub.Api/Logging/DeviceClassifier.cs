namespace RookHub.Api.Logging;

/// <summary>
/// Leitet aus dem <c>User-Agent</c>-Header eine grobe Geräteklasse ab
/// (<see cref="Mobile"/>/<see cref="Tablet"/>/<see cref="Desktop"/>/<see cref="Bot"/>/<see cref="Unknown"/>).
/// Landet pro Request als Log-Label <c>DeviceType</c> → in Kibana als Mobile-vs-PC-Anteil
/// (gesamt + je Bereich über <c>url.path</c>) auswertbar.
///
/// Heuristik über UA-Substrings, bewusst simpel gehalten (Anteils-Statistik, keine exakte
/// Geräteerkennung). Bekannte Grenzfälle: iPadOS-Safari meldet sich ab iPadOS 13 als „Macintosh"
/// (→ zählt als Desktop); Android-Tablets ohne „Mobile" im UA → Tablet. Die RookHub-Android-App
/// (TWA) trägt einen Chrome-Mobile-UA („Android … Mobile") → Mobile.
/// </summary>
public static class DeviceClassifier
{
    public const string Mobile = "mobile";
    public const string Tablet = "tablet";
    public const string Desktop = "desktop";
    public const string Bot = "bot";
    public const string Unknown = "unknown";

    private static readonly string[] BotMarkers =
    {
        "bot", "crawler", "spider", "slurp", "curl/", "wget", "python-requests",
        "httpclient", "headless", "phantomjs", "pingdom", "uptimerobot", "facebookexternalhit",
    };

    private static readonly string[] MobileMarkers =
    {
        "mobi", "iphone", "ipod", "android", "windows phone", "blackberry", "bb10",
        "opera mini", "iemobile", "webos",
    };

    private static readonly string[] TabletMarkers =
    {
        "ipad", "tablet", "kindle", "silk", "playbook",
    };

    public static string Classify(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return Unknown;
        var ua = userAgent.ToLowerInvariant();

        foreach (var m in BotMarkers)
            if (ua.Contains(m)) return Bot;

        // Tablet vor Mobile prüfen: iPad/Kindle sowie Android-Tablets (Android OHNE „mobile").
        foreach (var m in TabletMarkers)
            if (ua.Contains(m)) return Tablet;
        if (ua.Contains("android") && !ua.Contains("mobile")) return Tablet;

        foreach (var m in MobileMarkers)
            if (ua.Contains(m)) return Mobile;

        return Desktop;
    }
}
