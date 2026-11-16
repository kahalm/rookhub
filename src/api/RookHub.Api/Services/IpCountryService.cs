using System.Net;
using System.Net.Sockets;

namespace RookHub.Api.Services;

/// <summary>
/// IP-Bereiche → Land (ISO-3166-Kürzel), sortiert und per Binärsuche abgefragt. Quelle ist die freie
/// Liste „IP to Country Lite" von DB-IP (CSV: <c>start,ende,land</c>, IPv4 und IPv6 gemischt; CC BY 4.0 —
/// die Seite, die sie benutzt, nennt „IP Geolocation by DB-IP").
/// </summary>
public sealed class IpCountryTable
{
    private readonly IpRangeTable<string> _ranges;

    private IpCountryTable(IpRangeTable<string> ranges) => _ranges = ranges;

    public int Count => _ranges.Count;

    /// <summary>Liest die CSV. Unlesbare Zeilen fallen weg, statt den ganzen Bestand zu verwerfen.</summary>
    public static IpCountryTable Parse(TextReader reader)
    {
        var countries = new Dictionary<string, string>(StringComparer.Ordinal);
        return new IpCountryTable(IpRangeTable<string>.Parse(reader, (string field, out string shared) =>
        {
            var code = field.Trim().ToUpperInvariant();
            shared = code;
            if (code.Length != 2) return false;
            // Zwei Buchstaben je Zeile, ~700 000 Zeilen: dieselbe Instanz je Land statt 700 000 Strings.
            if (!countries.TryGetValue(code, out var known)) countries[code] = known = code;
            shared = known;
            return true;
        }));
    }

    /// <summary>Land der Adresse oder <c>null</c> (unbekannt, reserviert = <c>ZZ</c>).</summary>
    public string? Lookup(IPAddress ip)
    {
        var code = _ranges.TryLookup(ip, out var c) ? c : null;
        return code is null or "ZZ" ? null : code;
    }
}

/// <summary>
/// Land einer Besucher-IP — für die Startsprache von KidHub, wenn die Browsersprache keine der
/// Kindersprachen ist, und für die Besucher-Herkunft. Alles bleibt auf dem eigenen Server: die Länderliste wird hier
/// heruntergeladen (einmal im Monat, <c>IpCountry:SourceUrlTemplate</c>) und lokal abgefragt; keine Besucher-IP geht
/// an einen fremden Dienst, und keine wird gespeichert. Laden, Zwischenspeichern und Fehlerbehandlung:
/// <see cref="DbIpLiteList{TTable}"/> (Schlüssel unter <c>IpCountry:*</c>).
/// </summary>
public class IpCountryService : DbIpLiteList<IpCountryTable>
{
    internal const string DefaultSourceUrlTemplate = "https://download.db-ip.com/free/dbip-country-lite-{0:yyyy-MM}.csv.gz";

    public IpCountryService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<IpCountryService> logger)
        : this(httpFactory, config, logger, () => DateTime.UtcNow) { }

    internal IpCountryService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<IpCountryService> logger,
        Func<DateTime> utcNow)
        : base(httpFactory, config, logger, utcNow, new DbIpLiteListOptions(
            Section: "IpCountry", ClientName: nameof(IpCountryService), DefaultSourceUrlTemplate: DefaultSourceUrlTemplate,
            DefaultCacheFile: "dbip-country-lite.csv.gz", Label: "IP-Länderliste")) { }

    protected override IpCountryTable Parse(TextReader reader) => IpCountryTable.Parse(reader);
    protected override int CountOf(IpCountryTable table) => table.Count;

    /// <summary>ISO-Land der Adresse, oder <c>null</c> (private/lokale Adresse, unbekannt, Liste nicht da).</summary>
    public async Task<string?> CountryOfAsync(IPAddress? ip, CancellationToken ct = default)
    {
        if (!Enabled || ip is null || !IsPublic(ip)) return null;
        var table = await EnsureTableAsync(ct);
        return table?.Lookup(ip);
    }

    /// <summary>
    /// Wie <see cref="CountryOfAsync"/>, aber ohne zu warten — für den Anfrage-Weg der Besucher-Herkunft
    /// (<see cref="Accounts.VisitorOriginRecorder"/>): ist die Liste nicht da oder alt, stößt der Aufruf die
    /// Aktualisierung im Hintergrund an und antwortet mit dem, was da ist (sonst <c>null</c>).
    /// </summary>
    public string? TryCountryOf(IPAddress? ip)
    {
        if (!Enabled || ip is null || !IsPublic(ip)) return null;
        return CurrentTable()?.Lookup(ip);
    }

    /// <summary>Nur öffentliche Adressen haben ein Land — LAN, Loopback, CGNAT und Link-Local nicht.</summary>
    internal static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 10 || b[0] == 0
                     || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                     || (b[0] == 192 && b[1] == 168)
                     || (b[0] == 169 && b[1] == 254)
                     || (b[0] == 100 && b[1] >= 64 && b[1] <= 127));
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal || ip.Equals(IPAddress.IPv6None));
        return false;
    }
}
