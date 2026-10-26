using System.Text.RegularExpressions;

namespace RookHub.Api.Logging;

/// <summary>
/// Ermittelt die VisitorId fuer das Request-Logging (Kibana „Unique Visits"):
/// eingeloggte Besucher ueber den Username (Prefix "u:"), anonyme ueber die vom Client
/// gelieferte Session-Id aus dem X-Visitor-Id-Header (Prefix "a:") — geloggt nur als gekürzter SHA-256 (16 Hex, F1-012):
/// dieselbe Id schützt anonyme Puzzle-/Endlos-/E-Book-Stände und darf nicht im Klartext in jedem Log stehen. Der Header
/// selbst bleibt die rohe Id (E-Book-Kennung ohne Konto, Rate-Limit-Partition), daher wird hier und nicht im Client gehasht.
/// Der Header wird streng validiert (gleiche Form wie die Anon-Session-Id), damit weder
/// Log-Injection noch unbegrenzte Cardinality moeglich ist.
/// </summary>
public static class VisitorIdResolver
{
    public const string HeaderName = "X-Visitor-Id";

    private static readonly Regex SessionIdPattern = new(ValidationConstants.SessionIdPattern, RegexOptions.Compiled);

    /// <summary>Gekürzter SHA-256 (16 Hex) der Session-Id, Schreibweise egal.</summary>
    public static string Pseudonym(string sessionId) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(sessionId.ToLowerInvariant())))[..16];

    /// <returns>
    /// "u:&lt;username&gt;" wenn authentifiziert; sonst "a:&lt;pseudonym&gt;" wenn der Header
    /// eine gueltige Session-Id ist; sonst null (dann wird keine VisitorId geloggt).
    /// </returns>
    public static string? Resolve(bool isAuthenticated, string? userName, string? visitorHeader)
    {
        if (isAuthenticated && !string.IsNullOrEmpty(userName))
            return "u:" + userName;

        if (!string.IsNullOrEmpty(visitorHeader) && SessionIdPattern.IsMatch(visitorHeader))
            return "a:" + Pseudonym(visitorHeader);

        return null;
    }
}
