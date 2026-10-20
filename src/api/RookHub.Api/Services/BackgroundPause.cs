using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>Stand des Schalters „Hintergrundjobs pausieren": <paramref name="Paused"/> ist die WIRKSAME Pause
/// (Datenbank ODER Konfiguration), <paramref name="Since"/>/<paramref name="By"/> stammen aus der letzten Änderung in
/// der Datenbank (bei reiner Konfigurations-Pause leer).</summary>
public sealed record BackgroundPauseState(bool Paused, DateTime? Since, string? By, bool ForcedByConfig, bool PausedInDb);

/// <summary>
/// Zentraler Schalter „Hintergrundjobs pausieren" (zur Laufzeit, ohne Neustart). Der Zustand liegt dauerhaft in
/// <see cref="SystemSetting"/> (Schlüssel <see cref="SettingKey"/>) und übersteht damit Neustarts;
/// <c>BackgroundJobs:Paused=true</c> in der Konfiguration erzwingt die Pause unabhängig von der Datenbank
/// (Notbremse, wenn die Datenbank selbst das Problem ist oder die Oberfläche nicht erreichbar).
///
/// <para><b>Lesen ist billig:</b> der Datenbank-Stand wird <see cref="CacheDuration"/> lang gemerkt — jeder Takt aller
/// Dienste fragt hier, und ohne Merken wären das bei ~25 Diensten im Sekundentakt unnötige Abfragen. Eine Änderung
/// über <see cref="SetAsync"/> wirkt sofort (Merker wird ersetzt, <see cref="Changed"/> feuert), eine Änderung direkt in
/// der Datenbank spätestens nach <see cref="CacheDuration"/>.</para>
///
/// <para><b>Fällt die Datenbank aus</b>, bleibt der zuletzt gelesene Stand gültig (Warnung im Log) — ein Lesefehler
/// soll weder alles anhalten noch eine bewusste Pause aufheben.</para>
///
/// <para>Genutzt von <see cref="PeriodicWorker"/> (vor jedem Takt) und über <see cref="PauseGate"/> von den übrigen
/// Hintergrunddiensten; der Analyse-Worker hört zusätzlich auf <see cref="Changed"/>.</para>
/// </summary>
public class BackgroundPause
{
    public const string SettingKey = "BackgroundJobs.Paused";
    public const string ConfigKey = "BackgroundJobs:Paused";
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<BackgroundPause> _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private DbRow _row = DbRow.Empty;
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;
    private bool _loadFailing;

    private sealed record DbRow(bool Paused, DateTime? Since, string? By)
    {
        public static readonly DbRow Empty = new(false, null, null);
    }

    public BackgroundPause(IServiceScopeFactory scopes, IConfiguration config, ILogger<BackgroundPause> logger,
        TimeProvider? time = null)
    {
        _scopes = scopes;
        _config = config;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Feuert nach jeder Änderung über <see cref="SetAsync"/> mit dem neuen wirksamen Stand.</summary>
    public event Action<bool>? Changed;

    /// <summary>Konfiguration erzwingt die Pause (<c>BackgroundJobs:Paused=true</c>) — je Aufruf frisch gelesen.</summary>
    public bool ForcedByConfig => _config.GetValue<bool>(ConfigKey);

    /// <summary>Zuletzt bekannter wirksamer Stand OHNE Datenbankzugriff (für Stellen, die nicht warten dürfen).</summary>
    public bool IsPaused => ForcedByConfig || _row.Paused;

    /// <summary>Seit wann pausiert (laut Datenbank, zuletzt bekannt).</summary>
    public DateTime? Since => _row.Paused ? _row.Since : null;

    /// <summary>Wer pausiert hat (laut Datenbank, zuletzt bekannt).</summary>
    public string? By => _row.Paused ? _row.By : null;

    /// <summary>Wirksame Pause; liest höchstens alle <see cref="CacheDuration"/> aus der Datenbank.</summary>
    public async ValueTask<bool> IsPausedAsync(CancellationToken ct = default)
    {
        if (ForcedByConfig) return true;
        return (await GetAsync(ct)).Paused;
    }

    /// <summary>Vollständiger Stand (gecacht).</summary>
    public async ValueTask<BackgroundPauseState> GetAsync(CancellationToken ct = default)
    {
        if (_time.GetUtcNow() - _loadedAt >= CacheDuration)
            await RefreshAsync(ct);
        return Snapshot();
    }

    private BackgroundPauseState Snapshot()
    {
        var row = _row;
        var forced = ForcedByConfig;
        return new BackgroundPauseState(forced || row.Paused, row.Paused ? row.Since : null, row.Paused ? row.By : null,
            forced, row.Paused);
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        await _loadLock.WaitAsync(ct);
        try
        {
            if (_time.GetUtcNow() - _loadedAt < CacheDuration) return;   // ein anderer Aufrufer war schneller
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var s = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SettingKey, ct);
                _row = s is null ? DbRow.Empty : new DbRow(ParseBool(s.Value), s.UpdatedAt, s.UpdatedByName);
                if (_loadFailing)
                {
                    _loadFailing = false;
                    _logger.LogInformation("Hintergrundjobs-Pause: Schalter wieder lesbar");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // Nur beim Wechsel warnen — sonst schriebe jeder Takt jedes Dienstes dieselbe Zeile.
                if (!_loadFailing)
                    _logger.LogWarning(ex, "Hintergrundjobs-Pause: Schalter nicht lesbar — letzter Stand gilt weiter");
                _loadFailing = true;
            }
            _loadedAt = _time.GetUtcNow();
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>Schalter setzen (Admin). Wirkt sofort für alle Dienste dieses Prozesses.</summary>
    public async Task<BackgroundPauseState> SetAsync(bool paused, int? userId, string? userName, CancellationToken ct = default)
    {
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s = await db.SystemSettings.FirstOrDefaultAsync(x => x.Key == SettingKey, ct);
            if (s is null)
            {
                s = new SystemSetting { Key = SettingKey };
                db.SystemSettings.Add(s);
            }
            var now = _time.GetUtcNow().UtcDateTime;
            s.Value = paused ? "true" : "false";
            s.UpdatedAt = now;
            s.UpdatedByUserId = userId;
            s.UpdatedByName = userName is { Length: > 100 } ? userName[..100] : userName;
            await db.SaveChangesAsync(ct);
            _row = new DbRow(paused, now, s.UpdatedByName);
            _loadedAt = _time.GetUtcNow();
            _loadFailing = false;
        }
        _logger.LogInformation(paused
            ? "Hintergrundjobs: von {By} pausiert"
            : "Hintergrundjobs: von {By} fortgesetzt", userName ?? "?");
        var state = Snapshot();
        Changed?.Invoke(state.Paused);
        return state;
    }

    private static bool ParseBool(string? v) => bool.TryParse(v, out var b) && b;
}

/// <summary>Was ein Hintergrunddienst über sich preisgibt (Admin-Übersicht „Hintergrundjobs"). Dienste ohne diese
/// Schnittstelle gelten dort als nicht pausierbar.</summary>
public interface IBackgroundJobInfo
{
    /// <summary>Hält der Dienst bei „Hintergrundjobs pausieren" an?</summary>
    bool PausableByAdmin { get; }

    /// <summary>Ende des letzten erfolgreich durchgeführten (nicht übersprungenen) Takts, UTC.</summary>
    DateTime? LastTickUtc { get; }
}

/// <summary>
/// Pausenprüfung für einen einzelnen Hintergrunddienst, der NICHT von <see cref="PeriodicWorker"/> erbt: fragt
/// <see cref="BackgroundPause"/> (aufgelöst über die Scope-Fabrik des Dienstes — damit ändert sich kein Konstruktor und
/// kein Test, der den Dienst direkt baut; ohne Registrierung gilt „läuft") und loggt den WECHSEL genau einmal
/// („pausiert"/„läuft wieder"), nicht jeden übersprungenen Takt. Merkt sich außerdem den letzten Takt.
/// </summary>
public sealed class PauseGate
{
    private readonly string _worker;
    private readonly ILogger _logger;
    private BackgroundPause? _pause;
    private bool _resolved;

    public PauseGate(string worker, ILogger logger)
    {
        _worker = worker;
        _logger = logger;
    }

    /// <summary>Wartezeit, nach der ein pausierter Dienst wieder nachsieht (ein übersprungener Tageslauf wird so nach
    /// dem Fortsetzen nachgeholt, statt einen ganzen Tag zu fehlen).</summary>
    public static readonly TimeSpan Recheck = TimeSpan.FromSeconds(30);

    /// <summary>Aktuell im Pausenzustand (letzte Prüfung).</summary>
    public bool Paused { get; private set; }

    /// <summary>Ende des letzten durchgeführten Takts (UTC).</summary>
    public DateTime? LastTickUtc { get; private set; }

    /// <summary>Takt erledigt.</summary>
    public void MarkTick() => LastTickUtc = DateTime.UtcNow;

    /// <summary>Den Schalter direkt vorgeben (Tests, oder ein Dienst, der ihn selbst injiziert bekommt).</summary>
    public PauseGate Use(BackgroundPause? pause)
    {
        _pause = pause;
        _resolved = true;
        return this;
    }

    /// <summary>Für Schleifen ohne eigenen Takt-Rumpf: ist pausiert, wird <paramref name="wait"/> gewartet (endet still
    /// beim Herunterfahren) und <c>true</c> geliefert — der Aufrufer macht dann <c>continue</c>.</summary>
    public async Task<bool> SkipAndWaitAsync(IServiceScopeFactory? scopes, TimeSpan wait, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();   // sonst drehte eine `while (SkipAndWait…)`-Schleife beim Herunterfahren leer
        if (!await ShouldSkipAsync(scopes, ct)) return false;
        try { await Task.Delay(wait, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* Herunterfahren */ }
        return true;
    }

    /// <summary>Soll der anstehende Takt ausfallen? Wirft nur bei Abbruch über <paramref name="ct"/>.</summary>
    public async ValueTask<bool> ShouldSkipAsync(IServiceScopeFactory? scopes, CancellationToken ct)
    {
        if (!_resolved)
        {
            _resolved = true;
            if (scopes is not null)
            {
                using var scope = scopes.CreateScope();
                _pause = scope.ServiceProvider.GetService<BackgroundPause>();
            }
        }
        var paused = _pause is not null && await _pause.IsPausedAsync(ct);
        if (paused != Paused)
        {
            Paused = paused;
            if (paused) _logger.LogInformation("{Worker}: Hintergrundjobs pausiert — Takte ruhen", _worker);
            else _logger.LogInformation("{Worker}: Hintergrundjobs laufen wieder", _worker);
        }
        return paused;
    }
}
