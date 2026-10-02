namespace RookHub.Api.Services.Prep;

/// <summary>
/// FIDE-Föderation (drei Buchstaben, wie Lichess <c>/api/fide/player</c> sie liefert) → Landeskürzel der Profile (ISO, zwei
/// Buchstaben, wie Lichess-Flaggen und chess.com-Länder) — vollständig, für Spieler OHNE Liga-Bezug (Spielervorbereitung, 0.637.0;
/// <see cref="League.LeagueAccountFinder.AllowedCountries"/> mit <c>local = false</c>). Die Tabelle von LeagueHub (26 Föderationen)
/// bleibt für Ligaspieler unverändert; deren Einträge ergeben hier dasselbe. England, Schottland und Wales spielen als eigene
/// Föderationen, ihre Profile zeigen das Vereinigte Königreich (GB; Lichess „GB-ENG" wird auf „GB" gekürzt). <c>FID</c> (unter
/// FIDE-Flagge) und Unbekanntes stehen nicht darin — dann gilt kein Land als seins, die Suche bleibt streng.
/// </summary>
public static class PrepFederations
{
    public static readonly IReadOnlyDictionary<string, string> Iso = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["AFG"] = "AF", ["ALB"] = "AL", ["ALG"] = "DZ", ["AND"] = "AD", ["ANG"] = "AO", ["ANT"] = "AG", ["ARG"] = "AR", ["ARM"] = "AM",
        ["ARU"] = "AW", ["AUS"] = "AU", ["AUT"] = "AT", ["AZE"] = "AZ", ["BAH"] = "BS", ["BAN"] = "BD", ["BAR"] = "BB", ["BDI"] = "BI",
        ["BEL"] = "BE", ["BEN"] = "BJ", ["BER"] = "BM", ["BHU"] = "BT", ["BIH"] = "BA", ["BIZ"] = "BZ", ["BLR"] = "BY", ["BOL"] = "BO",
        ["BOT"] = "BW", ["BRA"] = "BR", ["BRN"] = "BH", ["BRU"] = "BN", ["BUL"] = "BG", ["BUR"] = "BF", ["CAF"] = "CF", ["CAM"] = "KH",
        ["CAN"] = "CA", ["CAY"] = "KY", ["CGO"] = "CG", ["CHA"] = "TD", ["CHI"] = "CL", ["CHN"] = "CN", ["CIV"] = "CI", ["CMR"] = "CM",
        ["COD"] = "CD", ["COL"] = "CO", ["COM"] = "KM", ["CPV"] = "CV", ["CRC"] = "CR", ["CRO"] = "HR", ["CUB"] = "CU", ["CYP"] = "CY",
        ["CZE"] = "CZ", ["DEN"] = "DK", ["DJI"] = "DJ", ["DMA"] = "DM", ["DOM"] = "DO", ["ECU"] = "EC", ["EGY"] = "EG", ["ENG"] = "GB",
        ["ERI"] = "ER", ["ESA"] = "SV", ["ESP"] = "ES", ["EST"] = "EE", ["ETH"] = "ET", ["FAI"] = "FO", ["FIJ"] = "FJ", ["FIN"] = "FI",
        ["FRA"] = "FR", ["GAB"] = "GA", ["GAM"] = "GM", ["GCI"] = "GG", ["GEO"] = "GE", ["GEQ"] = "GQ", ["GER"] = "DE", ["GHA"] = "GH",
        ["GRE"] = "GR", ["GRN"] = "GD", ["GUA"] = "GT", ["GUI"] = "GN", ["GUM"] = "GU", ["GUY"] = "GY", ["HAI"] = "HT", ["HKG"] = "HK",
        ["HON"] = "HN", ["HUN"] = "HU", ["INA"] = "ID", ["IND"] = "IN", ["IRI"] = "IR", ["IRL"] = "IE", ["IRQ"] = "IQ", ["ISL"] = "IS",
        ["ISR"] = "IL", ["ISV"] = "VI", ["ITA"] = "IT", ["IVB"] = "VG", ["JAM"] = "JM", ["JCI"] = "JE", ["JOR"] = "JO", ["JPN"] = "JP",
        ["KAZ"] = "KZ", ["KEN"] = "KE", ["KGZ"] = "KG", ["KOR"] = "KR", ["KOS"] = "XK", ["KSA"] = "SA", ["KUW"] = "KW", ["LAO"] = "LA",
        ["LAT"] = "LV", ["LBA"] = "LY", ["LBN"] = "LB", ["LBR"] = "LR", ["LCA"] = "LC", ["LES"] = "LS", ["LIE"] = "LI", ["LTU"] = "LT",
        ["LUX"] = "LU", ["MAC"] = "MO", ["MAD"] = "MG", ["MAR"] = "MA", ["MAS"] = "MY", ["MAW"] = "MW", ["MDA"] = "MD", ["MDV"] = "MV",
        ["MEX"] = "MX", ["MGL"] = "MN", ["MKD"] = "MK", ["MLI"] = "ML", ["MLT"] = "MT", ["MNC"] = "MC", ["MNE"] = "ME", ["MOZ"] = "MZ",
        ["MRI"] = "MU", ["MTN"] = "MR", ["MYA"] = "MM", ["NAM"] = "NA", ["NCA"] = "NI", ["NED"] = "NL", ["NEP"] = "NP", ["NGR"] = "NG",
        ["NIG"] = "NE", ["NOR"] = "NO", ["NRU"] = "NR", ["NZL"] = "NZ", ["OMA"] = "OM", ["PAK"] = "PK", ["PAN"] = "PA", ["PAR"] = "PY",
        ["PER"] = "PE", ["PHI"] = "PH", ["PLE"] = "PS", ["PLW"] = "PW", ["PNG"] = "PG", ["POL"] = "PL", ["POR"] = "PT", ["PUR"] = "PR",
        ["QAT"] = "QA", ["ROU"] = "RO", ["RSA"] = "ZA", ["RUS"] = "RU", ["RWA"] = "RW", ["SCO"] = "GB", ["SEN"] = "SN", ["SEY"] = "SC",
        ["SGP"] = "SG", ["SKN"] = "KN", ["SLE"] = "SL", ["SLO"] = "SI", ["SMR"] = "SM", ["SOL"] = "SB", ["SOM"] = "SO", ["SRB"] = "RS",
        ["SRI"] = "LK", ["SSD"] = "SS", ["STP"] = "ST", ["SUD"] = "SD", ["SUI"] = "CH", ["SUR"] = "SR", ["SVK"] = "SK", ["SWE"] = "SE",
        ["SWZ"] = "SZ", ["SYR"] = "SY", ["TAN"] = "TZ", ["TGA"] = "TO", ["TJK"] = "TJ", ["TKM"] = "TM", ["TLS"] = "TL", ["TOG"] = "TG",
        ["TPE"] = "TW", ["TTO"] = "TT", ["TUN"] = "TN", ["TUR"] = "TR", ["UAE"] = "AE", ["UGA"] = "UG", ["UKR"] = "UA", ["URU"] = "UY",
        ["USA"] = "US", ["UZB"] = "UZ", ["VAN"] = "VU", ["VEN"] = "VE", ["VIE"] = "VN", ["VIN"] = "VC", ["WLS"] = "GB", ["YEM"] = "YE",
        ["ZAM"] = "ZM", ["ZIM"] = "ZW",
    };
}
