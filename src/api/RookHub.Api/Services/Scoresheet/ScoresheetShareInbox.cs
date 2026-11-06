using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace RookHub.Api.Services.Scoresheet;

/// <summary>
/// Kurzzeit-Ablage für Fotos, die über „Teilen → RookHub“ (Web Share Target, 0.883.0) kommen. Android schickt das Bild
/// per POST an die API, OHNE Anmeldung — die steckt im Browser-Speicher der App, nicht in einem Cookie. Die Fotos liegen
/// deshalb hier unter einem Zufallsschlüssel, bis die Einlese-Seite sie angemeldet abholt. Nur im Speicher: ein
/// Neustart verliert sie, und das ist bei ein paar Minuten Lebensdauer in Ordnung.
/// </summary>
public sealed class ScoresheetShareInbox
{
    /// <summary>So lange wartet ein geteiltes Foto auf die Abholung (Anmeldung dazwischen eingerechnet).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    /// <summary>Höchstens so viele Fotos liegen gleichzeitig — der Endpunkt ist offen, die Ablage darf nicht wachsen.</summary>
    public const int MaxEntries = 50;
    public const long MaxTotalBytes = 200L * 1024 * 1024;

    private sealed record Entry(IReadOnlyList<ScoresheetUpload> Pages, DateTimeOffset ExpiresAt, long Bytes);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    public ScoresheetShareInbox(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>Legt die Fotos ab und gibt den Abholschlüssel zurück; <c>null</c>, wenn die Ablage voll ist.</summary>
    public string? Put(IReadOnlyList<ScoresheetUpload> pages)
    {
        var bytes = pages.Sum(p => (long)p.Data.Length);
        lock (_gate)
        {
            Prune();
            if (_entries.Count >= MaxEntries || _entries.Values.Sum(e => e.Bytes) + bytes > MaxTotalBytes) return null;
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            _entries[token] = new Entry(pages, _time.GetUtcNow() + Lifetime, bytes);
            return token;
        }
    }

    /// <summary>Die abgelegten Fotos, oder <c>null</c> bei unbekanntem/abgelaufenem Schlüssel.</summary>
    public IReadOnlyList<ScoresheetUpload>? Get(string token)
    {
        if (!_entries.TryGetValue(token, out var e)) return null;
        if (e.ExpiresAt > _time.GetUtcNow()) return e.Pages;
        _entries.TryRemove(token, out _);
        return null;
    }

    public bool Remove(string token) => _entries.TryRemove(token, out _);

    private void Prune()
    {
        var now = _time.GetUtcNow();
        foreach (var (k, e) in _entries)
            if (e.ExpiresAt <= now) _entries.TryRemove(k, out _);
    }
}
