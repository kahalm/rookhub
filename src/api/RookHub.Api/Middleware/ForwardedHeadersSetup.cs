using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace RookHub.Api.Middleware;

/// <summary>
/// Wem die API <c>X-Forwarded-For</c>/<c>-Proto</c> glaubt. <c>UseForwardedHeaders</c> läuft vor Rate-Limiter und
/// IP-Logging; ohne sie käme nur die Proxy-Adresse an, und der globale Limiter würfe ALLE Nutzer in eine Partition
/// (faktische site-weite 100/min-Drossel).
///
/// <para><b>Weg einer Anfrage:</b> Client → Nginx Proxy Manager → frontend-nginx (eines der vier Images, alle mit
/// <c>src/frontend/nginx.conf</c>) → api. Beide Proxys hängen mit <c>$proxy_add_x_forwarded_for</c> an; beim
/// Eintreffen steht also „[vom Client mitgeschickt …], &lt;Client&gt;, &lt;NPM, wie frontend-nginx ihn sieht — das
/// Docker-Gateway&gt;" im Kopf, der Socket-Peer ist frontend-nginx.</para>
///
/// <para><b>Warum höchstens <see cref="TrustedHops"/> Einträge (Codereview A10-002):</b> Mit <c>ForwardLimit = null</c>
/// rollte die Middleware zurück, solange der jeweils letzte Eintrag in einem vertrauten Netz lag. Bei einem Client mit
/// PRIVATER Adresse (LAN, WireGuard) lief sie über seine echte Adresse hinaus bis in die Einträge, die er selbst
/// mitgeschickt hatte — eine frei gewählte, je Anfrage wechselnde Adresse und damit je Anfrage eine frische Partition
/// aller Rate-Limiter (Login ohne IP-Bremse, <c>auth_bruteforce</c> des log-watchers blind). Mehr als zwei Proxys
/// liegen auf keinem Weg; ein dritter Eintrag von rechts stammt immer vom Client.</para>
///
/// <para><b>Warum nur die Docker-Netze und nicht mehr 10.0.0.0/8:</b> Die Proxy-Hops sind Container bzw. das
/// Docker-Gateway, und deren Netze stammen aus Dockers Standard-Pools (172.17–172.31/16, danach 192.168.x/20; auf
/// dem Server liegen Stacks in beiden). 10/8 ist dort das LAN: ein Gerät, das die veröffentlichten API-Ports direkt
/// ansprach, galt selbst als vertrauter Proxy und durfte <c>X-Forwarded-For</c> frei setzen. Jetzt bleibt seine
/// eigene Adresse stehen. FALLE: Bekäme Docker je Pools aus 10/8 (<c>default-address-pools</c> in
/// <c>daemon.json</c>), stünde hier das Gateway als Client-IP — dann dieses Netz ergänzen.</para>
///
/// <para>Nicht im Code lösbar: Wer aus einem Docker-Netz heraus (z. B. WireGuard hinter dem Gateway) direkt auf
/// einen veröffentlichten Port spricht, kann weiterhin EINEN Eintrag vorgeben. Dagegen hilft nur, den API-Port
/// nicht oder nur an 127.0.0.1 zu veröffentlichen.</para>
/// </summary>
public static class ForwardedHeadersSetup
{
    /// <summary>NPM + frontend-nginx.</summary>
    public const int TrustedHops = 2;

    /// <summary>Netze, aus denen ein Peer als Proxy gilt (Dockers Standard-Pools).</summary>
    public static readonly IReadOnlyList<System.Net.IPNetwork> TrustedProxyNetworks =
    [
        new(IPAddress.Parse("172.16.0.0"), 12),
        new(IPAddress.Parse("192.168.0.0"), 16),
    ];

    public static void Configure(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = TrustedHops;
        options.KnownProxies.Clear();
        // KnownIPNetworks ersetzt das veraltete KnownNetworks (ein Clear leert beide); der Loopback-Vorgabewert
        // fliegt damit wie bisher raus.
        options.KnownIPNetworks.Clear();
        foreach (var network in TrustedProxyNetworks)
            options.KnownIPNetworks.Add(network);
    }
}
