namespace RookHub.Api.Services;

/// <summary>
/// Welche der Kindersprachen (de/en/hr/hu — nur die sind auf KidHub vollständig) passt zu einem Land?
/// Gebraucht, wenn die Browsersprache keine davon ist: erst das Land der IP, dann Deutsch.
/// Nur Länder, deren (Haupt-)Sprache eine der vier ist; alle anderen bleiben ohne Hinweis.
/// </summary>
public static class KidsLanguageHint
{
    private static readonly Dictionary<string, string> ByCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AT"] = "de", ["DE"] = "de", ["CH"] = "de", ["LI"] = "de", ["LU"] = "de",
        ["HR"] = "hr", ["BA"] = "hr",
        ["HU"] = "hu",
        ["GB"] = "en", ["IE"] = "en", ["US"] = "en", ["CA"] = "en", ["AU"] = "en", ["NZ"] = "en",
    };

    public static string? ForCountry(string? country) =>
        country is not null && ByCountry.TryGetValue(country, out var lang) ? lang : null;
}
