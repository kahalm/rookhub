namespace RookHub.Api.Services;

/// <summary>
/// chess-results fuehrt Foederationen unter FIDE-/IOC-Codes (AUT, GER, SUI), der GeoNames-Gazetteer
/// unter ISO-3166-1-alpha-2 (AT, DE, CH).
///
/// <para><b>Am 2026-09-10 stark erweitert, und der Grund gehoert hierher.</b> Die Tabelle deckte
/// „Europa plus die grossen Verbaende ausserhalb" ab, weil nur fuer diese Laender ueberhaupt
/// Postleitzahlen importiert waren. Diese Voraussetzung gilt nicht mehr: inzwischen liegen die
/// PLZ von 40 weiteren Laendern im Lexikon — und ohne Zuordnung waren sie fuer den Geocoder
/// UNERREICHBAR. Peru hatte 96 968 Postleitzahl-Zeilen und kam auf 46 % Verortung, Indonesien
/// 81 058 auf 71 %, weil der PLZ-Weg einen Landfilter braucht und ohne Zuordnung ausfaellt.
/// Betroffen waren rund 5 400 Turniere in siebzig Foederationen.</para>
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
        // Katalonien fuehrt chess-results als eigene Foederation - es liegt in Spanien, und
        // mehrere Codes auf dasselbe Land sind ausdruecklich erlaubt (siehe oben, ENG/SCO/WLS).
        ["CAT"] = "ES",
        // Grosse Verbaende ausserhalb Europas
        ["ARG"] = "AR", ["AUS"] = "AU", ["BRA"] = "BR", ["CAN"] = "CA", ["CHN"] = "CN",
        ["IND"] = "IN", ["JPN"] = "JP", ["MEX"] = "MX", ["NZL"] = "NZ", ["RSA"] = "ZA",
        ["USA"] = "US",
        // Amerika
        ["CHI"] = "CL", ["COL"] = "CO", ["PER"] = "PE", ["URU"] = "UY", ["PAR"] = "PY",
        ["BOL"] = "BO", ["ECU"] = "EC", ["VEN"] = "VE", ["CUB"] = "CU", ["DOM"] = "DO",
        ["CRC"] = "CR", ["GUA"] = "GT", ["HON"] = "HN", ["ESA"] = "SV", ["NCA"] = "NI",
        ["PAN"] = "PA", ["PUR"] = "PR", ["JAM"] = "JM", ["TTO"] = "TT", ["ARU"] = "AW",
        // Asien
        ["VIE"] = "VN", ["INA"] = "ID", ["PHI"] = "PH", ["MAS"] = "MY", ["THA"] = "TH",
        ["SGP"] = "SG", ["HKG"] = "HK", ["TPE"] = "TW", ["KOR"] = "KR", ["MGL"] = "MN",
        ["SRI"] = "LK", ["BAN"] = "BD", ["NEP"] = "NP", ["PAK"] = "PK", ["AFG"] = "AF",
        ["UZB"] = "UZ", ["KGZ"] = "KG", ["TKM"] = "TM", ["IRI"] = "IR", ["IRQ"] = "IQ",
        ["LBN"] = "LB", ["KSA"] = "SA", ["UAE"] = "AE", ["QAT"] = "QA", ["KUW"] = "KW",
        ["OMA"] = "OM", ["BRN"] = "BH",
        // Afrika
        ["EGY"] = "EG", ["MAR"] = "MA", ["TUN"] = "TN", ["ALG"] = "DZ", ["LBA"] = "LY",
        ["NGR"] = "NG", ["KEN"] = "KE", ["UGA"] = "UG", ["ZAM"] = "ZM", ["ZIM"] = "ZW",
        ["BOT"] = "BW", ["NAM"] = "NA", ["MOZ"] = "MZ", ["ANG"] = "AO", ["GHA"] = "GH",
        ["MAD"] = "MG", ["MAW"] = "MW", ["LES"] = "LS", ["SWZ"] = "SZ", ["BDI"] = "BI",
        ["SSD"] = "SS",
        // Ozeanien
        ["GUM"] = "GU",
    };

    /// <summary>Alle Laender, fuer die eine Zuordnung existiert - Vorauswahl fuer den PLZ-Import.</summary>
    public static IReadOnlyCollection<string> KnownIso2 { get; } = Map.Values.Distinct().ToArray();

    /// <summary>Die Zuordnung selbst — fuer Tests, die Eigenschaften der Tabelle festhalten.</summary>
    internal static IReadOnlyDictionary<string, string> All => Map;

    public static string? ToIso2(string? fideCode) =>
        !string.IsNullOrWhiteSpace(fideCode) && Map.TryGetValue(fideCode.Trim(), out var iso) ? iso : null;

    /// <summary>
    /// Der Rueckweg: ISO-2 auf den Foederations-Code. Gebraucht, wo eine Quelle ihr Land als
    /// Laenderfaehnchen fuehrt (chess.cz) — der Bestand kennt aber nur Foederationen.
    ///
    /// <para>Mehrere Foederationen zeigen auf dasselbe Land (ENG/SCO/WLS auf GB). Der Rueckweg
    /// braucht dort eine ENTSCHEIDUNG, und sie steht ausdruecklich da statt sich aus der
    /// Reihenfolge eines Dictionary zu ergeben: GB wird zu ENG, MK zu MKD, IM zu IOM.</para>
    /// </summary>
    public static string? FromIso2(string? iso2)
    {
        if (string.IsNullOrWhiteSpace(iso2)) return null;
        return Reverse.TryGetValue(iso2.Trim(), out var code) ? code : null;
    }

    /// <summary>
    /// Wohin ein Land zurueckfuehrt, wenn MEHRERE Foederationen darauf zeigen. Ohne Eintrag
    /// entscheidet die alphabetische Reihenfolge, und die ist willkuerlich: fuer Spanien haette
    /// sie „CAT" gewaehlt und damit jede spanische Laenderflagge nach Katalonien aufgeloest.
    /// <see cref="EveryAmbiguousIso2HasAPreference"/> im Test haelt das fest — wer eine zweite
    /// Foederation auf ein Land legt, muss hier entscheiden.
    /// </summary>
    internal static readonly Dictionary<string, string> Preferred = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GB"] = "ENG", ["MK"] = "MKD", ["IM"] = "IOM", ["ES"] = "ESP",
    };

    private static readonly Dictionary<string, string> Reverse =
        Map.GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => Preferred.TryGetValue(group.Key, out var pick)
                    ? pick
                    : group.Select(pair => pair.Key).Order(StringComparer.Ordinal).First(),
                StringComparer.OrdinalIgnoreCase);
}
