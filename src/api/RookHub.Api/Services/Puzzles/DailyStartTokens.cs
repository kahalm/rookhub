using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace RookHub.Api.Services.Puzzles;

/// <summary>
/// Signierte Startmarke des Tagespuzzles (Codereview A2-014, Entscheidung 25.10.: Wertung mit SERVERZEIT). Bis 0.839
/// rankte die Tageswertung nach der Zeit, die der Browser meldete — <c>timeSeconds: 0</c> gab jeden Tag Gold. Jetzt
/// bekommt ein angemeldeter Nutzer beim Laden des HEUTIGEN Tagespuzzles eine Marke (Nutzer + Datum + Startzeit,
/// versiegelt mit dem DataProtection-Schlüsselbund der API — derselbe Ring wie Passwort-Reset-Links, HMAC-SHA256
/// innen), die der Client beim Ergebnis zurückschickt; gewertet wird die Differenz zur Serveruhr
/// (<see cref="Courses.BookPuzzleService.RecordAttemptAsync"/>). KEINE Tabelle: die Marke trägt alles selbst.
/// <para>Die Startzeit ist je (Nutzer, Tag) die FRÜHESTE ausgegebene (Prozess-Cache) — sonst setzte ein Neuladen
/// nach langem Nachdenken die Uhr zurück. Nach einem API-Neustart beginnt sie beim nächsten Laden neu (bewusst
/// hingenommen: ohne Tabelle nicht dauerhaft merkbar).</para>
/// </summary>
public sealed class DailyStartTokens
{
    /// <summary>Unter dieser Serverzeit ist eine Lösung nicht plausibel (Seite laden, Setup-Zug, eigener Zug) — sie
    /// zählt als gelöst, aber ohne Rang (wie eine Lösung ohne gültige Marke).</summary>
    public const int MinRankedSeconds = 2;

    private const string Purpose = "RookHub.Daily.Start.v1";
    private readonly IDataProtector _protector;
    private readonly TimeProvider _clock;
    private readonly IMemoryCache? _cache;

    public DailyStartTokens(IDataProtectionProvider provider, TimeProvider? clock = null, IMemoryCache? cache = null)
    {
        _protector = provider.CreateProtector(Purpose);
        _clock = clock ?? TimeProvider.System;
        _cache = cache;
    }

    /// <summary>Jetzt (UTC) nach der Serveruhr dieses Dienstes.</summary>
    public DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;

    /// <summary>Heutiges UTC-Datum (Tagespuzzle-Tag).</summary>
    public DateOnly Today => DateOnly.FromDateTime(UtcNow);

    /// <summary>Marke für <paramref name="userId"/> am heutigen Tagespuzzle; Startzeit = frühester Abruf heute.</summary>
    public string Issue(int userId)
    {
        var now = UtcNow;
        var date = DateOnly.FromDateTime(now);
        var start = now;
        if (_cache != null)
        {
            var key = $"daily-start:{userId}:{date:yyyyMMdd}";
            if (_cache.TryGetValue<DateTime>(key, out var first) && first <= now) start = first;
            // Relativ statt absolut: der Cache misst mit seiner eigenen Uhr; zwei Tage decken den Tag sicher ab.
            else _cache.Set(key, now, TimeSpan.FromDays(2));
        }
        var payload = string.Join('|', "u", userId.ToString(CultureInfo.InvariantCulture),
            date.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            new DateTimeOffset(start, TimeSpan.Zero).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        return _protector.Protect(payload);
    }

    /// <summary>
    /// Serverzeit in Sekunden seit dem Start der Marke, wenn sie echt ist, <paramref name="userId"/> gehört, für HEUTE
    /// ausgestellt wurde und nicht in der Zukunft liegt; sonst <c>null</c> (fehlend, fremd, manipuliert, anderer Tag,
    /// anderer Schlüsselbund).
    /// </summary>
    public int? ElapsedSeconds(string? token, int userId)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        string payload;
        try { payload = _protector.Unprotect(token); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }

        var parts = payload.Split('|');
        if (parts.Length != 4 || parts[0] != "u") return null;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var uid) || uid != userId) return null;
        if (!DateOnly.TryParseExact(parts[2], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
        if (!long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var ms)) return null;

        var now = UtcNow;
        if (date != DateOnly.FromDateTime(now)) return null;
        var start = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        if (start > now) return null;
        return (int)Math.Min((now - start).TotalSeconds, AttemptRecording.MaxSeconds);
    }
}
