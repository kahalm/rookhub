using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json;

namespace RookHub.Api.Services.Accounts;

/// <summary>Wohin eine Besucher-Adresse gehört (<see cref="VisitorNetworkClassifier"/>).</summary>
public enum VisitorNetwork
{
    /// <summary>Gewöhnlicher Anschluss — Land aus der DB-IP-Liste.</summary>
    Normal,
    /// <summary>Google-Server (Linkvorschau, Vorabladen, Crawler) — wird nicht gezählt.</summary>
    Google,
    /// <summary>Bekanntes VPN-/Rechenzentrumsnetz — zählt als <see cref="VisitorNetworkClassifier.VpnCountry"/>.</summary>
    Vpn,
}

/// <summary>
/// Teilt Besucher-Adressen der Besucher-Herkunft (<see cref="VisitorOriginRecorder"/>) ein: Google-Server laden geteilte
/// Links wenige Sekunden nach dem echten Besucher noch einmal (Linkvorschau, Vorabladen; Browser-User-Agent, darum
/// erkennt <see cref="Logging.DeviceClassifier"/> sie nicht) und zählten sonst als Besucher aus den USA/Kanada; VPN-
/// Austritte zählten als das Land des Servers (Österreicher über NordVPN als „Brasilien“).
///
/// <para>Grundlage ist die Liste <c>Assets/visitor-networks.json</c> (im Repo, von Hand gepflegt; Google-Netze
/// erzeugt <c>tools/visitor-networks/update_google.py</c> aus Googles <c>goog.json</c>): AS-Nummern, nachgeschlagen über
/// die lokale DB-IP-Liste „IP to ASN Lite“ (<see cref="IpAsnService"/>, lädt monatlich neu), und feste Netze, die
/// auch greifen, solange jene Liste noch nicht geladen ist. Kein Abruf je Anfrage, alles im Speicher.</para>
/// </summary>
public sealed class VisitorNetworkClassifier
{
    /// <summary>Ländercode für VPN-Austritte in <c>VisitorDays.Country</c> — „XV“ ist im ISO-Bereich für
    /// Benutzercodes (XA–XZ) und wird keinem Land zugeteilt. Das Frontend zeigt ihn als „VPN“.</summary>
    public const string VpnCountry = "XV";

    internal const string ResourceName = "RookHub.Api.Assets.visitor-networks.json";

    private readonly Func<IPAddress, uint?> _asnOf;
    private readonly HashSet<uint> _googleAsns;
    private readonly HashSet<uint> _vpnAsns;
    private readonly IpRangeTable<VisitorNetwork> _networks;

    public VisitorNetworkClassifier(IpAsnService asn) : this(VisitorNetworkList.Embedded.Value, asn.TryAsnOf) { }

    internal VisitorNetworkClassifier(VisitorNetworkList list, Func<IPAddress, uint?> asnOf)
    {
        _asnOf = asnOf;
        _googleAsns = list.GoogleAsns.ToHashSet();
        _vpnAsns = list.VpnAsns.ToHashSet();
        _networks = list.Ranges();
    }

    /// <summary>Einteilung einer (öffentlichen) Adresse. Feste Netze zuerst, dann die AS-Nummer.</summary>
    public VisitorNetwork Classify(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (_networks.TryLookup(ip, out var byNetwork)) return byNetwork;
        if (_asnOf(ip) is uint asn)
        {
            if (_googleAsns.Contains(asn)) return VisitorNetwork.Google;
            if (_vpnAsns.Contains(asn)) return VisitorNetwork.Vpn;
        }
        return VisitorNetwork.Normal;
    }
}

/// <summary>Inhalt von <c>Assets/visitor-networks.json</c>.</summary>
public sealed record VisitorNetworkList(
    IReadOnlyList<uint> GoogleAsns, IReadOnlyList<uint> VpnAsns,
    IReadOnlyList<string> GoogleNetworks, IReadOnlyList<string> VpnNetworks)
{
    internal static readonly Lazy<VisitorNetworkList> Embedded = new(LoadEmbedded);

    private static VisitorNetworkList LoadEmbedded()
    {
        using var stream = typeof(VisitorNetworkList).Assembly.GetManifestResourceStream(VisitorNetworkClassifier.ResourceName)
            ?? throw new InvalidOperationException($"Ressource {VisitorNetworkClassifier.ResourceName} fehlt.");
        return Parse(stream);
    }

    public static VisitorNetworkList Parse(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        static IEnumerable<JsonElement> Items(JsonElement root, string name) =>
            root.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray() : [];
        return new VisitorNetworkList(
            Items(root, "googleAsns").Select(e => e.GetProperty("asn").GetUInt32()).ToList(),
            Items(root, "vpnAsns").Select(e => e.GetProperty("asn").GetUInt32()).ToList(),
            Items(root, "googleNetworks").Select(e => e.GetString()!).ToList(),
            Items(root, "vpnNetworks").Select(e => e.GetProperty("cidr").GetString()!).ToList());
    }

    /// <summary>Die festen Netze als Bereichstabelle. Wirft bei unlesbaren oder sich überlappenden Netzen — die
    /// Binärsuche fände sonst nicht jeden Treffer (abgesichert durch einen Test auf die eingebettete Liste).</summary>
    internal IpRangeTable<VisitorNetwork> Ranges()
    {
        var v4 = new List<(uint Start, uint End, VisitorNetwork Value)>();
        var v6 = new List<(UInt128 Start, UInt128 End, VisitorNetwork Value)>();
        foreach (var (cidrs, kind) in new[] { (GoogleNetworks, VisitorNetwork.Google), (VpnNetworks, VisitorNetwork.Vpn) })
            foreach (var cidr in cidrs)
            {
                var net = IPNetwork.Parse(cidr);
                var first = net.BaseAddress;
                if (first.AddressFamily == AddressFamily.InterNetwork)
                {
                    var start = IpRangeTable<VisitorNetwork>.ToUInt32(first);
                    var hostBits = 32 - net.PrefixLength;
                    var end = hostBits == 32 ? uint.MaxValue : start | ((1u << hostBits) - 1);
                    v4.Add((start, end, kind));
                }
                else
                {
                    var start = IpRangeTable<VisitorNetwork>.ToUInt128(first);
                    var hostBits = 128 - net.PrefixLength;
                    var end = hostBits == 128 ? UInt128.MaxValue : start | ((UInt128.One << hostBits) - 1);
                    v6.Add((start, end, kind));
                }
            }
        CheckDisjoint(v4.Select(r => (BigInteger)r.Start).Zip(v4.Select(r => (BigInteger)r.End)).ToList());
        CheckDisjoint(v6.Select(r => (BigInteger)r.Start).Zip(v6.Select(r => (BigInteger)r.End)).ToList());
        return new IpRangeTable<VisitorNetwork>(v4, v6);
    }

    private static void CheckDisjoint(List<(BigInteger Start, BigInteger End)> ranges)
    {
        var sorted = ranges.OrderBy(r => r.Start).ToList();
        for (var i = 1; i < sorted.Count; i++)
            if (sorted[i].Start <= sorted[i - 1].End)
                throw new InvalidDataException("visitor-networks.json: Netze überlappen sich.");
    }
}
