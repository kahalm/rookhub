using System.Net;
using System.Net.Sockets;

namespace RookHub.Api.Services;

/// <summary>
/// Wofür Grenzen „je IP" gelten: IPv4 (auch als IPv6 verpackt) die Adresse, echtes IPv6 das /64-Netz — ein Anschluss
/// bekommt ein ganzes /64 und wählt darin beliebig viele Adressen. Ein <c>MapToIPv4</c> nähme bei echtem IPv6 nur die
/// letzten 32 Bit: aus einem /64 würden beliebig viele „IPs“, und fremde Anschlüsse mit gleichem Ende teilten sich eine
/// (Codereview 2026-09-29, A6-003 / F7-005). Die EINE Fassung für Formular-Einlesungen und Liga-Entwürfe ohne Konto.
/// </summary>
public static class IpBucket
{
    /// <summary>Schlüssel des Anschlusses: <c>203.0.113.7</c>, <c>2001:db8:1:2::/64</c>; <c>?</c> ohne Adresse.</summary>
    public static string Key(IPAddress? ip)
    {
        if (ip == null) return "?";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return ip.ToString();
        var bytes = ip.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }
}
