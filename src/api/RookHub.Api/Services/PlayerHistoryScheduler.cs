namespace RookHub.Api.Services;

/// <summary>
/// Haelt „Meine Turniere" von selbst aktuell: einmal taeglich (und kurz nach dem Start) werden die
/// Trefferlisten aller Konten mit Identitaet aufgefrischt und die fehlenden Spielerkarten geholt.
///
/// <para><b>Warum es diesen Dienst gibt.</b> Vorher entstand der Verlauf ausschliesslich beim
/// Ansehen: die Seite loeste die Abrufe aus und fragte rund eine Minute lang nach, was fertig ist.
/// Wer die Seite schloss, liess den Rest liegen — beim naechsten Besuch stand wieder „noch kein
/// Ergebnis" in den Zeilen. Der Durchgang hier arbeitet den Rueckstand ab, waehrend niemand
/// hinsieht.</para>
///
/// <para><b>04:30 UTC</b>, also nach dem Verzeichnis-Sweep um 03:00: beide sprechen mit
/// chess-results ueber denselben prozessweiten Rate-Limiter, und der Sweep ist der laengere Lauf.
/// Der <b>Startlauf</b> nach zehn Minuten ist bewusst: nach einem Deploy soll der Rueckstand nicht
/// bis zur naechsten Nacht liegen bleiben — er kostet einen Listenabruf je Konto, und die Karten
/// sind ohnehin gedeckelt.</para>
/// </summary>
public class PlayerHistoryScheduler : BackgroundService
{
    public static readonly TimeSpan RunAtUtc = TimeSpan.FromHours(4.5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlayerHistoryScheduler> _logger;
    private readonly bool _enabled;
    private readonly int _maxCards;
    private readonly TimeSpan _startupDelay;

    public PlayerHistoryScheduler(
        IServiceScopeFactory scopeFactory,
        ILogger<PlayerHistoryScheduler> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _enabled = configuration.GetValue("PlayerHistory:Enabled", true);

        // Wie viele SEITEN ein Lauf holt (Spielerkarten und Bedenkzeiten zusammen). Jede ist ein
        // Abruf hinter dem Rate-Limiter; was nicht mehr hineinpasst, kommt in der naechsten Nacht.
        _maxCards = Math.Clamp(configuration.GetValue("PlayerHistory:MaxCardsPerRun", 200), 0, 2000);

        // 0 schaltet den Startlauf ab.
        _startupDelay = TimeSpan.FromMinutes(
            Math.Clamp(configuration.GetValue("PlayerHistory:StartupDelayMinutes", 10), 0, 720));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Turnierverlauf: Hintergrund-Durchgang per Konfiguration abgeschaltet");
            return;
        }

        if (_startupDelay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(_startupDelay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
            await RunOnceAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeUntilNextRun(DateTime.UtcNow), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
            await RunOnceAsync(stoppingToken);
        }
    }

    public static TimeSpan TimeUntilNextRun(DateTime nowUtc)
    {
        var todayRun = nowUtc.Date + RunAtUtc;
        var next = nowUtc < todayRun ? todayRun : todayRun.AddDays(1);
        var delay = next - nowUtc;
        return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var history = scope.ServiceProvider.GetRequiredService<TournamentHistoryService>();

            var sweep = await history.RefreshAllAsync(_maxCards, ct);

            if (sweep.Unavailable > 0)
                _logger.LogWarning(
                    "Turnierverlauf: {Players} Konten aufgefrischt, {Cards} Spielerkarten und {TimeControls} Bedenkzeiten geholt, {Unavailable} Trefferlisten nicht erreichbar",
                    sweep.Players, sweep.Cards, sweep.TimeControls, sweep.Unavailable);
            else
                _logger.LogInformation(
                    "Turnierverlauf: {Players} Konten aufgefrischt, {Cards} Spielerkarten und {TimeControls} Bedenkzeiten geholt",
                    sweep.Players, sweep.Cards, sweep.TimeControls);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Herunterfahren — kein Fehler.
        }
        catch (Exception ex)
        {
            // Alles fangen: BackgroundServiceExceptionBehavior ist StopHost, ein HttpClient-Timeout
            // wuerde sonst die ganze API mitnehmen.
            _logger.LogError(ex, "Turnierverlauf: Hintergrund-Durchgang fehlgeschlagen");
        }
    }
}
