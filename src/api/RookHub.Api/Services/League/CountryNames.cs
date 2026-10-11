using System.Text.RegularExpressions;

namespace RookHub.Api.Services.League;

/// <summary>
/// Deutsche Ländernamen zu ISO-3166-Kürzeln (UI-Sweep 2026-10-11, r2-l-land-codes): die Hinweise der Konto-Suche schrieben
/// nur das Land der Region aus („Land Deutschland", „Land Österreich") und jedes andere als rohes Kürzel („Land SK" —
/// Slowakei, nicht Schachklub). Bewusst eine feste Tabelle statt <c>RegionInfo</c>: die Anzeigenamen hingen sonst an der
/// Kultur des Containers. Unbekannte Kürzel bleiben, wie sie sind.
/// </summary>
public static partial class CountryNames
{
    private static readonly Dictionary<string, string> German = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AD"] = "Andorra", ["AE"] = "Vereinigte Arabische Emirate", ["AL"] = "Albanien", ["AM"] = "Armenien", ["AR"] = "Argentinien",
        ["AT"] = "Österreich", ["AU"] = "Australien", ["AZ"] = "Aserbaidschan", ["BA"] = "Bosnien und Herzegowina", ["BD"] = "Bangladesch",
        ["BE"] = "Belgien", ["BG"] = "Bulgarien", ["BR"] = "Brasilien", ["BY"] = "Belarus", ["CA"] = "Kanada", ["CH"] = "Schweiz",
        ["CL"] = "Chile", ["CN"] = "China", ["CO"] = "Kolumbien", ["CU"] = "Kuba", ["CY"] = "Zypern", ["CZ"] = "Tschechien",
        ["DE"] = "Deutschland", ["DK"] = "Dänemark", ["DZ"] = "Algerien", ["EC"] = "Ecuador", ["EE"] = "Estland", ["EG"] = "Ägypten",
        ["ES"] = "Spanien", ["FI"] = "Finnland", ["FO"] = "Färöer", ["FR"] = "Frankreich", ["GB"] = "Vereinigtes Königreich",
        ["GE"] = "Georgien", ["GR"] = "Griechenland", ["HR"] = "Kroatien", ["HU"] = "Ungarn", ["ID"] = "Indonesien", ["IE"] = "Irland",
        ["IL"] = "Israel", ["IN"] = "Indien", ["IQ"] = "Irak", ["IR"] = "Iran", ["IS"] = "Island", ["IT"] = "Italien", ["JP"] = "Japan",
        ["KG"] = "Kirgisistan", ["KR"] = "Südkorea", ["KZ"] = "Kasachstan", ["LI"] = "Liechtenstein", ["LT"] = "Litauen",
        ["LU"] = "Luxemburg", ["LV"] = "Lettland", ["MA"] = "Marokko", ["MC"] = "Monaco", ["MD"] = "Moldau", ["ME"] = "Montenegro",
        ["MK"] = "Nordmazedonien", ["MN"] = "Mongolei", ["MT"] = "Malta", ["MX"] = "Mexiko", ["MY"] = "Malaysia", ["NL"] = "Niederlande",
        ["NO"] = "Norwegen", ["NZ"] = "Neuseeland", ["PE"] = "Peru", ["PH"] = "Philippinen", ["PK"] = "Pakistan", ["PL"] = "Polen",
        ["PT"] = "Portugal", ["PY"] = "Paraguay", ["RO"] = "Rumänien", ["RS"] = "Serbien", ["RU"] = "Russland", ["SE"] = "Schweden",
        ["SG"] = "Singapur", ["SI"] = "Slowenien", ["SK"] = "Slowakei", ["SM"] = "San Marino", ["SY"] = "Syrien", ["TH"] = "Thailand",
        ["TJ"] = "Tadschikistan", ["TM"] = "Turkmenistan", ["TN"] = "Tunesien", ["TR"] = "Türkei", ["UA"] = "Ukraine",
        ["US"] = "Vereinigte Staaten", ["UY"] = "Uruguay", ["UZ"] = "Usbekistan", ["VE"] = "Venezuela", ["VN"] = "Vietnam",
        ["XK"] = "Kosovo", ["ZA"] = "Südafrika",
    };

    /// <summary>Deutscher Name zu einem Kürzel, sonst das Kürzel selbst (in Großbuchstaben).</summary>
    public static string GermanName(string code) =>
        German.TryGetValue(code.Trim(), out var name) ? name : code.Trim().ToUpperInvariant();

    [GeneratedRegex(@"\bLand ([A-Z]{2})\b")]
    private static partial Regex LandCode();

    /// <summary>„Land SK" → „Land Slowakei" in einem Hinweistext — auch für Vorschläge, die vor dieser Regel gespeichert wurden.</summary>
    public static string? ExpandLand(string? text) =>
        string.IsNullOrEmpty(text) ? text : LandCode().Replace(text, m => "Land " + GermanName(m.Groups[1].Value));
}
