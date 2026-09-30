using System.Threading.RateLimiting;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>
/// Drossel für Long-Polls mit UNBEKANNTEM Selector, je Client-Adresse (Singleton).
///
/// <para><c>POST /api/external-engine/work</c> ist anonym und vom Rate-Limiter ausgenommen — für echte Provider mit
/// Absicht (13 Provider pollen 78-mal je Minute, <c>docs/eigener-engine-broker.md</c>). Ein unbekannter Selector wird
/// dort <see cref="LocalBrokerOptions.AcquireWait"/> lang gehalten und bekommt dann 204: ein veralteter Provider
/// schickt nach einem 204 SOFORT den nächsten Poll (<c>example-provider.py</c>: <c>if res.status != 200: continue</c>),
/// ohne das Halten pollte er im Leerlauf. Ohne Deckel konnte aber jeder mit erfundenen Secrets beliebig viele
/// Verbindungen je Adresse je zehn Sekunden festhalten.</para>
///
/// <para>Deshalb: bekannte Selectors laufen weiter ungedrosselt; unbekannte bekommen je Adresse ein Fenster von
/// <see cref="DefaultPermitPerMinute"/> Polls je Minute (ein veralteter Provider braucht sechs). Darüber antwortet der
/// Broker SOFORT mit 429 — ohne Halten, und der Provider wartet nach einer 4xx-Antwort selbst (Backoff bis 10 s).</para>
/// </summary>
public sealed class UnknownSelectorThrottle : IDisposable
{
    public const int DefaultPermitPerMinute = 30;

    private readonly PartitionedRateLimiter<string> _limiter;

    public UnknownSelectorThrottle(int permitPerMinute = DefaultPermitPerMinute)
    {
        _limiter = PartitionedRateLimiter.Create<string, string>(ip => RateLimitPartitions.FixedWindow(ip, permitPerMinute));
    }

    /// <summary>Darf diese Adresse noch einen unbekannten Selector halten lassen?</summary>
    public bool TryEnter(string clientIp)
    {
        using var lease = _limiter.AttemptAcquire(clientIp);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
