namespace RookHub.Api.Services;

/// <summary>
/// Welche der Kindersprachen (de/en/hr/hu — nur die sind auf KidHub vollständig) passt zu einem Land?
/// Gebraucht, wenn die Browsersprache keine davon ist: erst das Land der IP, dann Deutsch.
/// <para>Länder mit einer dieser vier als (Haupt-)Sprache bekommen sie; jedes ANDERE bekannte Land bekommt
/// Englisch (Wunsch des Nutzers, 2026-09-27: „Frankreich → Englisch" — für ein Kind aus Frankreich ist
/// Englisch verständlicher als Deutsch). Deutsch bleibt nur, wenn das Land UNBEKANNT ist (LAN-Adresse,
/// keine Länderliste, Ausfall) — dann liefert der Hinweis nichts, und KidHub nimmt seine Vorgabe.</para>
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

    /// <summary>Englisch für jedes bekannte Land ohne eigene Kindersprache.</summary>
    public const string OtherCountries = "en";

    public static string? ForCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country)) return null;
        return ByCountry.TryGetValue(country, out var lang) ? lang : OtherCountries;
    }
}
