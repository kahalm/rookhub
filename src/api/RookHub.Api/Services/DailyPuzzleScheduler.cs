namespace RookHub.Api.Services;

/// <summary>
/// Sorgt dafuer, dass jeden UTC-Tag um 00:00 das Tagespuzzle vorab ausgewaehlt + in
/// die <c>DailyPuzzles</c>-Tabelle eingetragen ist. Beim Start wird heute ebenfalls
/// einmal sichergestellt (catch-up nach Downtime). Anschliessend wartet der Loop
/// bis 00:00 UTC des Folgetags.
///
/// Selbst-heilend: scheitert die Pool-Auswahl (z. B. weil kein Buch <c>forDaily</c>
/// markiert ist), wird das nur geloggt; beim naechsten Tageswechsel wird wieder
/// versucht, und sobald per Endpoint <c>/api/book-puzzles/daily/{today}</c>
/// angefragt, springt die On-Demand-Logik in <see cref="BookPuzzleService"/> ein.
/// </summary>
public class DailyPuzzleScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyPuzzleScheduler> _logger;

    public DailyPuzzleScheduler(IServiceScopeFactory scopeFactory, ILogger<DailyPuzzleScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial-Catch-up unmittelbar nach Start.
        await TryEnsureTodayAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextMidnightUtc(DateTime.UtcNow);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
            await TryEnsureTodayAsync(stoppingToken);
        }
    }

    /// <summary>Wartezeit bis zum naechsten 00:00 UTC (mit 1s Puffer, um Mitternacht safe zu queren).</summary>
    public static TimeSpan TimeUntilNextMidnightUtc(DateTime nowUtc)
    {
        var tomorrow = nowUtc.Date.AddDays(1);
        var delay = (tomorrow - nowUtc) + TimeSpan.FromSeconds(1);
        if (delay < TimeSpan.FromSeconds(1))
            delay = TimeSpan.FromSeconds(1);
        return delay;
    }

    private async Task TryEnsureTodayAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<BookPuzzleService>();
            var dto = await svc.GetOrAssignDailyAsync(DateOnly.FromDateTime(DateTime.UtcNow));
            _logger.LogInformation("DailyPuzzleScheduler: heutige Zuordnung sichergestellt (puzzleId={PuzzleId}).", dto.Id);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning("DailyPuzzleScheduler: kein Daily-Pool verfuegbar — {Reason}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DailyPuzzleScheduler: Ensure-Today fehlgeschlagen.");
        }
    }
}
