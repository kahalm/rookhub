namespace RookHub.Api.Services;

/// <summary>
/// chess-results fuehrt Foederationen unter FIDE-/IOC-Codes (AUT, GER, SUI), der GeoNames-Gazetteer
/// unter ISO-3166-1-alpha-2 (AT, DE, CH). Die Tabelle deckt Europa vollstaendig ab plus die grossen
/// Verbaende ausserhalb - also genau die Laender, fuer die ein Postleitzahl-Datensatz ueberhaupt
/// importiert wird.
///
/// Fehlt ein Code, wird bewusst NICHT geraten: die Ortssuche laeuft dann ohne Landfilter weiter
/// (der Einwohnerzahl-Tiebreak faengt das meiste ab) und die Zeile bekommt keine PLZ-Aufloesung.
/// Ein falsch geratenes Land waere schlimmer als gar keins - es setzt den Pin verlaesslich falsch.
///
/// Mehrere FIDE-Codes duerfen auf dasselbe Land zeigen (ENG/SCO/WLS -> GB); das ist gewollt.
/// </summary>
public static class FideCountryCodes
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // Europa
        ["ALB"] = "AL", ["AND"] = "AD", ["ARM"] = "AM", ["AUT"] = "AT", ["AZE"] = "AZ",
        ["BLR"] = "BY", ["BEL"] = "BE", ["BIH"] = "BA", ["BUL"] = "BG", ["CRO"] = "HR",
        ["CYP"] = "CY", ["CZE"] = "CZ", ["DEN"] = "DK", ["ENG"] = "GB", ["EST"] = "EE",
        ["FAI"] = "FO", ["FIN"] = "FI", ["FRA"] = "FR", ["GEO"] = "GE", ["GER"] = "DE",
        ["GCI"] = "GG", ["GRE"] = "GR", ["HUN"] = "HU", ["ISL"] = "IS", ["IRL"] = "IE",
        ["IOM"] = "IM", ["IMN"] = "IM", ["ISR"] = "IL", ["ITA"] = "IT", ["JCI"] = "JE",
        ["KAZ"] = "KZ", ["KOS"] = "XK", ["LAT"] = "LV", ["LIE"] = "LI", ["LTU"] = "LT",
        ["LUX"] = "LU", ["MKD"] = "MK", ["FRM"] = "MK", ["MLT"] = "MT", ["MDA"] = "MD",
        ["MNC"] = "MC", ["MNE"] = "ME", ["NED"] = "NL", ["NOR"] = "NO", ["POL"] = "PL",
        ["POR"] = "PT", ["ROU"] = "RO", ["RUS"] = "RU", ["SMR"] = "SM", ["SCO"] = "GB",
        ["SRB"] = "RS", ["SVK"] = "SK", ["SLO"] = "SI", ["ESP"] = "ES", ["SWE"] = "SE",
        ["SUI"] = "CH", ["TUR"] = "TR", ["UKR"] = "UA", ["WLS"] = "GB",
        // Grosse Verbaende ausserhalb Europas
        ["ARG"] = "AR", ["AUS"] = "AU", ["BRA"] = "BR", ["CAN"] = "CA", ["CHN"] = "CN",
        ["IND"] = "IN", ["JPN"] = "JP", ["MEX"] = "MX", ["NZL"] = "NZ", ["RSA"] = "ZA",
        ["USA"] = "US",
    };

    /// <summary>Alle Laender, fuer die eine Zuordnung existiert - Vorauswahl fuer den PLZ-Import.</summary>
    public static IReadOnlyCollection<string> KnownIso2 { get; } = Map.Values.Distinct().ToArray();

    public static string? ToIso2(string? fideCode) =>
        !string.IsNullOrWhiteSpace(fideCode) && Map.TryGetValue(fideCode.Trim(), out var iso) ? iso : null;
}
