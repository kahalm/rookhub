using System.Text.RegularExpressions;

namespace RookHub.Api.Logging;

/// <summary>
/// Ermittelt die VisitorId fuer das Request-Logging (Kibana „Unique Visits"):
/// eingeloggte Besucher ueber den Username (Prefix "u:"), anonyme ueber die vom Client
/// gelieferte Session-Id aus dem X-Visitor-Id-Header (Prefix "a:").
/// Der Header wird streng validiert (gleiche Form wie die Anon-Session-Id), damit weder
/// Log-Injection noch unbegrenzte Cardinality moeglich ist.
/// </summary>
public static class VisitorIdResolver
{
    public const string HeaderName = "X-Visitor-Id";

    private static readonly Regex SessionIdPattern = new(ValidationConstants.SessionIdPattern, RegexOptions.Compiled);

    /// <returns>
    /// "u:&lt;username&gt;" wenn authentifiziert; sonst "a:&lt;sessionId&gt;" wenn der Header
    /// eine gueltige Session-Id ist; sonst null (dann wird keine VisitorId geloggt).
    /// </returns>
    public static string? Resolve(bool isAuthenticated, string? userName, string? visitorHeader)
    {
        if (isAuthenticated && !string.IsNullOrEmpty(userName))
            return "u:" + userName;

        if (!string.IsNullOrEmpty(visitorHeader) && SessionIdPattern.IsMatch(visitorHeader))
            return "a:" + visitorHeader;

        return null;
    }
}
