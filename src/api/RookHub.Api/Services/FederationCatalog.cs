namespace RookHub.Api.Services;

/// <summary>
/// Alle Foederationscodes, die das Laenderfeld der chess-results-Turniersuche kennt (Stand
/// 2026-09-06, aus dem Dropdown der Seite gezogen). Gebraucht wird die Liste fuer die Wochen-
/// rotation: ohne sie koennte der Scheduler nur Foederationen besuchen, die er schon einmal
/// besucht hat.
///
/// Es sind FIDE-/IOC-Codes plus ein paar chess-results-Eigenheiten (ACC, ACF, ZZZ = "alle
/// uebrigen Verbaende"). Verschwindet ein Code, laeuft der Sweep dafuer ins Leere und wird als
/// fehlgeschlagene Foederation geloggt - kein Grund, die Liste laufend zu pflegen.
/// </summary>
public static class FederationCatalog
{
    public static readonly IReadOnlyList<string> All =
    [
        "ACC", "ACF", "AFG", "AFR", "AHO", "ALA", "ALB", "ALG", "AND", "ANG", "ANT", "ARG", "ARM",
        "ARU", "ASM", "AUS", "AUT", "AZE", "BAH", "BAN", "BAR", "BDI", "BEL", "BEN", "BER", "BES",
        "BHU", "BIH", "BIZ", "BLR", "BOL", "BOT", "BRA", "BRN", "BRU", "BUL", "BUR", "CAF", "CAM",
        "CAN", "CAT", "CAY", "CCA", "CHA", "CHI", "CHN", "CIV", "CMR", "COD", "COK", "COL", "COM",
        "CPV", "CRC", "CRO", "CUB", "CUR", "CUW", "CYP", "CZE", "DEN", "DJI", "DMA", "DOM", "ECU",
        "ECX", "EGY", "ENG", "ERI", "ESA", "ESP", "EST", "ETH", "EUF", "FAI", "FID", "FIJ", "FIN",
        "FLK", "FRA", "FRM", "FSM", "GAB", "GAM", "GBR", "GCI", "GEO", "GEQ", "GER", "GHA", "GIB",
        "GLP", "GNB", "GRE", "GRL", "GRN", "GUA", "GUF", "GUI", "GUM", "GUY", "HAI", "HKG", "HON",
        "HUN", "IMN", "INA", "IND", "IOM", "IOT", "IRI", "IRL", "IRQ", "ISL", "ISR", "ISV", "ITA",
        "IVB", "JAM", "JCI", "JOR", "JPN", "KAZ", "KEN", "KGZ", "KIR", "KOR", "KOS", "KSA", "KUW",
        "LAO", "LAT", "LBA", "LBN", "LBR", "LCA", "LES", "LIE", "LTU", "LUX", "MAC", "MAD", "MAF",
        "MAR", "MAS", "MAW", "MDA", "MDV", "MEX", "MGL", "MHL", "MKD", "MLI", "MLT", "MNC", "MNE",
        "MNP", "MOZ", "MRI", "MSR", "MTN", "MTQ", "MYA", "MYT", "NAM", "NCA", "NCL", "NED", "NEP",
        "NFK", "NGR", "NIG", "NIR", "NIU", "NOR", "NRU", "NZL", "OMA", "ONL", "PAK", "PAN", "PAR",
        "PER", "PHI", "PLE", "PLW", "PNG", "POL", "POR", "PRK", "PUR", "PYF", "QAT", "REU", "ROU",
        "RSA", "RUS", "RWA", "SCG", "SCO", "SEN", "SEY", "SGP", "SKN", "SLE", "SLO", "SMR", "SOL",
        "SOM", "SPM", "SRB", "SRI", "SSD", "STP", "SUD", "SUI", "SUR", "SVK", "SWE", "SWZ", "SXM",
        "SYR", "TAN", "TCA", "THA", "TJK", "TKL", "TKM", "TLS", "TOG", "TON", "TPE", "TTO", "TUN",
        "TUR", "TUV", "UAE", "UGA", "UKR", "URU", "USA", "UZB", "VAN", "VAT", "VEN", "VIE", "VIN",
        "VUT", "WFC", "WLF", "WLS", "WSM", "XXX", "YEM", "ZAM", "ZIM", "ZZZ"
    ];
}
