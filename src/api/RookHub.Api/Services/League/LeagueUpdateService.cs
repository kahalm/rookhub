namespace RookHub.Api.Services.League;

/// <summary>
/// „Daten aktualisieren" — auf Knopfdruck, NICHT per Zeitplan (Wunsch des Nutzers 2026-09-27).
/// Ein Lauf auf einmal; ein neuer Start frühestens <see cref="MinGap"/> nach dem letzten (schont chess-results).
/// Der eigentliche Ablauf steckt in <see cref="LeagueRefresh"/>.
/// </summary>
public sealed class LeagueUpdateService
{
    public static readonly TimeSpan MinGap = TimeSpan.FromMinutes(2);
    public enum StartResult { Started, Running, TooSoon }

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<LeagueUpdateService> _log;
    private readonly IHostApplicationLifetime _life;
    private readonly object _lock = new();
    private bool _running;
    private DateTime? _started, _finished;
    private bool? _ok;
    private string? _message;

    public LeagueUpdateService(IServiceScopeFactory scopes, ILogger<LeagueUpdateService> log, IHostApplicationLifetime life)
    {
        _scopes = scopes; _log = log; _life = life;
    }

    public StartResult TryStart()
    {
        lock (_lock)
        {
            if (_running) return StartResult.Running;
            if (_started is not null && DateTime.UtcNow - _started < MinGap) return StartResult.TooSoon;
            _running = true;
            _started = DateTime.UtcNow;
            _finished = null; _ok = null; _message = null;
        }
        _ = Task.Run(RunAsync);
        return StartResult.Started;
    }

    private async Task RunAsync()
    {
        string? msg = null;
        var ok = false;
        try
        {
            using var scope = _scopes.CreateScope();
            var refresh = scope.ServiceProvider.GetRequiredService<LeagueRefresh>();
            msg = await refresh.RunAsync(_life.ApplicationStopping);
            ok = true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LeagueHub: Aktualisieren fehlgeschlagen");
            msg = ex.Message;
        }
        finally
        {
            lock (_lock) { _running = false; _finished = DateTime.UtcNow; _ok = ok; _message = msg; }
        }
    }

    public object Status()
    {
        lock (_lock)
            return new { running = _running, started = _started, finished = _finished, ok = _ok, message = _message };
    }
}
