namespace RookHub.Api.Logging;

/// <summary>
/// Klassifiziert HTTP-Requests als „System-Call" (automatisch, NICHT vom Nutzer ausgelöst) vs.
/// echten Nutzer-Request. System-Calls sind Infra-/Health-Checks und periodisches Client-Polling
/// (Glocken-/Badge-Zähler, Import-Status-Polls, Menü-Sichtbarkeit, Client-Diagnose/Heartbeat).
/// Das Ergebnis landet im Log als <c>RequestKind</c> (<see cref="System"/>/<see cref="User"/>) →
/// in Kibana filterbar, damit man den automatischen „Grundrauschen"-Traffic vom echten
/// Nutzer-Verhalten trennen (bzw. ausblenden) kann.
/// </summary>
public static class SystemCallClassifier
{
    public const string System = "system";
    public const string User = "user";

    public static string Classify(string? path) => IsSystemCall(path) ? System : User;

    public static bool IsSystemCall(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var p = path.TrimEnd('/');
        if (p.Length == 0) return false;

        // Infrastruktur / Health / API-Doku
        if (StartsWith(p, "/health") || StartsWith(p, "/swagger")) return true;

        // Client-Diagnose + Client-Heartbeat (ClientLogService, gedrosselt/periodisch)
        if (Eq(p, "/api/client-log")) return true;

        // Menü-Sichtbarkeit — beim Laden/periodisch geprüft, nicht nutzer-initiiert
        if (Eq(p, "/api/menu")) return true;

        // Badge-/Zähler-Polls (Navbar-Glocke 60s etc.): /count, /unread-count, /pending-counts
        if (EndsWith(p, "/count") || EndsWith(p, "-count")
            || EndsWith(p, "/counts") || EndsWith(p, "-counts")) return true;

        // Chessable-Import-Status-Polls (Dashboard-Widget / Kursseite / Chessable-Tab)
        if (Eq(p, "/api/chessable/admin/active") || Eq(p, "/api/chessable/admin/imports")) return true;

        return false;
    }

    private static bool Eq(string p, string v) => string.Equals(p, v, StringComparison.OrdinalIgnoreCase);
    private static bool StartsWith(string p, string v) => p.StartsWith(v, StringComparison.OrdinalIgnoreCase);
    private static bool EndsWith(string p, string v) => p.EndsWith(v, StringComparison.OrdinalIgnoreCase);
}
