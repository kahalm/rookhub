using System.Globalization;
using System.Net;

namespace RookHub.Api.Services;

/// <summary>
/// IP-Bereiche → Netzbetreiber (Autonomes System, „AS-Nummer“). Quelle ist die freie Liste „IP to ASN Lite“ von DB-IP
/// (CSV: <c>start,ende,asn,"Firmenname"</c>, IPv4 und IPv6 gemischt; CC BY 4.0). Behalten wird nur die Nummer — der
/// Firmenname würde je Bereich einen String kosten und wird nirgends gebraucht.
/// </summary>
public sealed class IpAsnTable
{
    private readonly IpRangeTable<uint> _ranges;

    private IpAsnTable(IpRangeTable<uint> ranges) => _ranges = ranges;

    public int Count => _ranges.Count;

    public static IpAsnTable Parse(TextReader reader) =>
        new(IpRangeTable<uint>.Parse(reader, static (string field, out uint asn) =>
            uint.TryParse(field.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out asn) && asn > 0));

    /// <summary>AS-Nummer der Adresse oder <c>null</c> (unbekannt).</summary>
    public uint? Lookup(IPAddress ip) => _ranges.TryLookup(ip, out var asn) ? asn : null;
}

/// <summary>
/// Netzbetreiber einer Besucher-IP — für die Besucher-Herkunft (<see cref="Accounts.VisitorNetworkClassifier"/>:
/// Google-Abrufe nicht zählen, VPN-Austritte als „VPN“ statt als Land). Lädt wie die Länderliste einmal im Monat
/// (<c>IpAsn:SourceUrlTemplate</c>, Vorgabe DB-IP „IP to ASN Lite“, ≈ 7 MB) beim ersten Bedarf, Schlüssel unter
/// <c>IpAsn:*</c> (<see cref="DbIpLiteList{TTable}"/>).
/// </summary>
public class IpAsnService : DbIpLiteList<IpAsnTable>
{
    internal const string DefaultSourceUrlTemplate = "https://download.db-ip.com/free/dbip-asn-lite-{0:yyyy-MM}.csv.gz";

    public IpAsnService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<IpAsnService> logger)
        : this(httpFactory, config, logger, () => DateTime.UtcNow) { }

    internal IpAsnService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<IpAsnService> logger,
        Func<DateTime> utcNow)
        : base(httpFactory, config, logger, utcNow, new DbIpLiteListOptions(
            Section: "IpAsn", ClientName: nameof(IpAsnService), DefaultSourceUrlTemplate: DefaultSourceUrlTemplate,
            DefaultCacheFile: "dbip-asn-lite.csv.gz", Label: "IP-Netzbetreiberliste")) { }

    protected override IpAsnTable Parse(TextReader reader) => IpAsnTable.Parse(reader);
    protected override int CountOf(IpAsnTable table) => table.Count;

    /// <summary>AS-Nummer der Adresse ohne zu warten, oder <c>null</c> (private Adresse, unbekannt, Liste noch nicht
    /// da — dann lädt sie im Hintergrund).</summary>
    public uint? TryAsnOf(IPAddress? ip)
    {
        if (!Enabled || ip is null || !IpCountryService.IsPublic(ip)) return null;
        return CurrentTable()?.Lookup(ip);
    }

    /// <summary>Wie <see cref="TryAsnOf"/>, wartet aber kurz auf das erste Laden (Tests, Werkzeuge).</summary>
    public async Task<uint?> AsnOfAsync(IPAddress? ip, CancellationToken ct = default)
    {
        if (!Enabled || ip is null || !IpCountryService.IsPublic(ip)) return null;
        return (await EnsureTableAsync(ct))?.Lookup(ip);
    }
}
