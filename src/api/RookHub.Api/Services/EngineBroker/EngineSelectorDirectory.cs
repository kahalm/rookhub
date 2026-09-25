using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>
/// Welche Provider-Selectors sind registriert, und wann hat sich der jeweilige Provider zuletzt
/// gemeldet? (Singleton, im Speicher.)
///
/// <para><b>Warum eine eigene Liste statt einer Abfrage je Poll:</b> <c>POST /api/external-engine/work</c>
/// ist anonym und vom Rate-Limiter ausgenommen (13 Provider = 78 Abrufe je Minute). Eine
/// Datenbankabfrage je Abruf wäre für echte Provider billig — für jemanden, der mit erfundenen
/// Secrets anklopft, wäre sie eine Tür zur Datenbank. Die Menge der Selectors ist klein (höchstens
/// 32 je Nutzer) und ändert sich nur über die Registrierung, die hier selbst Bescheid gibt. Zur
/// Sicherheit wird sie höchstens alle <see cref="RefreshInterval"/> neu gelesen — ein Fehlgriff kostet
/// also höchstens eine Abfrage je Minute, egal wie viele fremde Secrets ankommen.</para>
///
/// <para><b>Zuletzt gesehen</b> wird nur für BEKANNTE Selectors gespeichert (sonst wüchse die Tabelle
/// mit jedem erfundenen Secret). Daraus der Online-Punkt; <see cref="DrainSeen"/> liefert die seit dem
/// letzten Abholen gesehenen Selectors, damit sie minütlich in <c>LastSeenAt</c> landen.</para>
/// </summary>
public sealed class EngineSelectorDirectory
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopes;
    private readonly Func<DateTime> _now;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private volatile HashSet<string>? _known;
    private DateTime _loadedAt = DateTime.MinValue;
    private volatile bool _stale;
    private readonly ConcurrentDictionary<string, DateTime> _lastSeen = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTime> _unflushed = new(StringComparer.Ordinal);

    public EngineSelectorDirectory(IServiceScopeFactory scopes) : this(scopes, () => DateTime.UtcNow) { }

    public EngineSelectorDirectory(IServiceScopeFactory scopes, Func<DateTime> now)
    {
        _scopes = scopes;
        _now = now;
    }

    /// <summary>Ist dieser Selector einer registrierten Engine zugeordnet?</summary>
    public async ValueTask<bool> IsKnownAsync(string selector, CancellationToken ct)
    {
        var known = _known;
        if (known is not null && !_stale && known.Contains(selector)) return true;
        if (known is not null && !_stale && _now() - _loadedAt < RefreshInterval) return false;
        known = await LoadAsync(ct);
        return known.Contains(selector);
    }

    /// <summary>Die Registrierung hat einen Selector angelegt/umgestellt — sofort bekannt machen und den
    /// Rest beim nächsten Fehlgriff neu lesen (ein alter Selector kann einer zweiten Registrierung gehören).</summary>
    public void Register(string selector)
    {
        var known = _known;
        if (known is not null)
        {
            var copy = new HashSet<string>(known, StringComparer.Ordinal) { selector };
            _known = copy;
        }
        _stale = true;
    }

    /// <summary>Nach dem Löschen einer Registrierung: beim nächsten Zugriff neu lesen.</summary>
    public void Invalidate() => _stale = true;

    /// <summary>Provider hat sich gemeldet (Poll begonnen oder beendet). Nur bekannte Selectors!</summary>
    public void MarkSeen(string selector)
    {
        var now = _now();
        _lastSeen[selector] = now;
        _unflushed[selector] = now;
    }

    public DateTime? LastSeen(string selector) =>
        _lastSeen.TryGetValue(selector, out var t) ? t : null;

    /// <summary>Die seit dem letzten Aufruf gesehenen Selectors (für das minütliche Nachtragen).</summary>
    public IReadOnlyDictionary<string, DateTime> DrainSeen()
    {
        var result = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        foreach (var key in _unflushed.Keys)
            if (_unflushed.TryRemove(key, out var t)) result[key] = t;
        return result;
    }

    private async Task<HashSet<string>> LoadAsync(CancellationToken ct)
    {
        await _loadLock.WaitAsync(ct);
        try
        {
            // Ein zweiter Wartender hat das Neuladen womöglich schon erledigt.
            if (_known is { } fresh && !_stale && _now() - _loadedAt < TimeSpan.FromSeconds(1)) return fresh;
            _stale = false;
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var selectors = await db.ExternalEngineRegistrations.AsNoTracking()
                .Select(r => r.ProviderSelector).Distinct().ToListAsync(ct);
            var set = new HashSet<string>(selectors, StringComparer.Ordinal);
            _known = set;
            _loadedAt = _now();
            // Gesehen-Stempel verwaister Selectors nicht ewig mitschleppen.
            foreach (var key in _lastSeen.Keys)
                if (!set.Contains(key)) _lastSeen.TryRemove(key, out _);
            return set;
        }
        finally
        {
            _loadLock.Release();
        }
    }
}
