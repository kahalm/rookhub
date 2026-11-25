namespace RookHub.Api.Services.Tournaments.Live;

/// <summary>
/// Gleichzeitige Live-Bretter-Verbindungen (Server-Sent Events) je Adresse und insgesamt deckeln (Singleton). Eine offene
/// SSE-Verbindung ist EINE Anfrage — das Rate-Limit je Minute bremst sie nicht; ohne diesen Deckel könnte eine Adresse
/// tausende Verbindungen offen halten. Vorgabe <c>LiveBoards:MaxStreamsPerIp</c> = 6 (ein Haushalt mit mehreren Tabs),
/// <c>LiveBoards:MaxStreams</c> = 2000.
/// </summary>
public sealed class SseConnectionLimiter
{
    private readonly Dictionary<string, int> _perKey = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private int _total;

    public SseConnectionLimiter(int maxPerKey = 6, int maxTotal = 2000)
    {
        MaxPerKey = maxPerKey;
        MaxTotal = maxTotal;
    }

    public int MaxPerKey { get; }
    public int MaxTotal { get; }

    /// <summary>Platz belegen → Freigabe beim Dispose; <c>null</c> = voll.</summary>
    public IDisposable? TryAcquire(string key)
    {
        lock (_lock)
        {
            var n = _perKey.GetValueOrDefault(key);
            if (n >= MaxPerKey || _total >= MaxTotal) return null;
            _perKey[key] = n + 1;
            _total++;
        }
        return new Lease(this, key);
    }

    public int Count(string key)
    {
        lock (_lock) return _perKey.GetValueOrDefault(key);
    }

    private void Release(string key)
    {
        lock (_lock)
        {
            _total--;
            if (_perKey.TryGetValue(key, out var n) && n > 1) _perKey[key] = n - 1;
            else _perKey.Remove(key);
        }
    }

    private sealed class Lease(SseConnectionLimiter owner, string key) : IDisposable
    {
        private int _done;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) owner.Release(key);
        }
    }
}
