namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Bremsen für anonyme Uploads, die nicht an der frei wählbaren <c>X-Visitor-Id</c> hängen (R3-002). Das Kontingent je
/// Besucher-Kennung ließ sich durch eine neue Kennung je Anfrage beliebig vervielfachen, und jeder Upload klingelte bei
/// allen Admins. Darum zusätzlich, im Speicher des API-Prozesses (Singleton; ein Neustart setzt zurück — reicht für eine
/// Bremse):
/// <list type="bullet">
/// <item><b>je Anschluss</b> (<see cref="IpBucket.Key"/>): innerhalb von <see cref="Window"/> höchstens das anonyme
/// Kontingent (<see cref="EbookOptions.AnonymousQuotaBytes"/>) und <see cref="MaxUploadsPerIp"/> Uploads, über alle
/// Besucher-Kennungen dieses Anschlusses zusammen. Reserviert wird VOR dem Speichern (parallele Anfragen zählen sofort),
/// ein gescheiterter Upload gibt seine Reservierung zurück;</item>
/// <item><b>Glocke</b>: anonyme Uploads klingeln höchstens einmal je <see cref="BellInterval"/> (das Buch steht trotzdem in
/// der Übersicht der Admins). Uploads angemeldeter Nutzer klingeln wie bisher jedes Mal.</item>
/// </list>
/// </summary>
public sealed class EbookAnonymousUploadGuard
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);
    public const int MaxUploadsPerIp = 20;
    public static readonly TimeSpan BellInterval = TimeSpan.FromHours(1);

    private readonly object _lock = new();
    private readonly Dictionary<string, List<(DateTime At, long Bytes)>> _byIp = new(StringComparer.Ordinal);
    private DateTime _lastBell = DateTime.MinValue;

    /// <summary>Platz für einen Upload von <paramref name="bytes"/> Bytes reservieren; <c>false</c> = Anschluss hat sein
    /// Kontingent bzw. die Zahl der Uploads im Fenster schon erreicht.</summary>
    public bool TryReserve(string ip, long bytes, long quotaBytes, DateTime now)
    {
        lock (_lock)
        {
            Prune(now);
            if (!_byIp.TryGetValue(ip, out var list)) _byIp[ip] = list = [];
            if (list.Count >= MaxUploadsPerIp) return false;
            if (quotaBytes > 0 && list.Sum(e => e.Bytes) + Math.Max(0, bytes) > quotaBytes) return false;
            list.Add((now, Math.Max(0, bytes)));
            return true;
        }
    }

    /// <summary>Reservierung eines gescheiterten Uploads zurückgeben.</summary>
    public void Release(string ip, long bytes)
    {
        lock (_lock)
        {
            if (!_byIp.TryGetValue(ip, out var list)) return;
            var i = list.FindLastIndex(e => e.Bytes == Math.Max(0, bytes));
            if (i >= 0) list.RemoveAt(i);
            if (list.Count == 0) _byIp.Remove(ip);
        }
    }

    /// <summary>Darf dieser anonyme Upload klingeln? Höchstens einmal je <see cref="BellInterval"/>.</summary>
    public bool TryTakeAnonymousBell(DateTime now)
    {
        lock (_lock)
        {
            if (now - _lastBell < BellInterval) return false;
            _lastBell = now;
            return true;
        }
    }

    private void Prune(DateTime now)
    {
        foreach (var ip in _byIp.Keys.ToList())
        {
            var list = _byIp[ip];
            list.RemoveAll(e => now - e.At >= Window);
            if (list.Count == 0) _byIp.Remove(ip);
        }
    }
}
