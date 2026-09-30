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
public class PlayerHistoryScheduler : PeriodicWorker
{
    public static readonly TimeSpan RunAtUtc = TimeSpan.FromHours(4.5);

    private readonly ILogger<PlayerHistoryScheduler> _logger;
    private readonly bool _enabled;
    private readonly int _maxCards;
    private readonly int _maxTournamentCrawls;
    private readonly TimeSpan _startupDelay;

    public PlayerHistoryScheduler(
        IServiceScopeFactory scopeFactory,
        ILogger<PlayerHistoryScheduler> logger,
        IConfiguration configuration)
        : base(scopeFactory, logger)
    {
        _logger = logger;
        _enabled = configuration.GetValue("PlayerHistory:Enabled", true);

        // Wie viele SEITEN ein Lauf holt (Spielerkarten und Bedenkzeiten zusammen). Jede ist ein
        // Abruf hinter dem Rate-Limiter; was nicht mehr hineinpasst, kommt in der naechsten Nacht.
        _maxCards = Math.Clamp(configuration.GetValue("PlayerHistory:MaxCardsPerRun", 200), 0, 2000);

        // Wie viele GANZE Turniere (Teilnehmer, Paarungen) ein Lauf beim Crawler anfordert — laufende
        // zuerst, dann fehlende, neueste zuerst. Jedes sind ein Dutzend Seiten und mehr. 0 schaltet ab.
        _maxTournamentCrawls = Math.Clamp(
            configuration.GetValue("PlayerHistory:MaxTournamentCrawlsPerRun", 40), 0, 500);

        // 0 schaltet den Startlauf ab.
        _startupDelay = TimeSpan.FromMinutes(
            Math.Clamp(configuration.GetValue("PlayerHistory:StartupDelayMinutes", 10), 0, 720));
    }

    protected override bool Enabled => _enabled;
    protected override void LogDisabled()
        => _logger.LogInformation("Turnierverlauf: Hintergrund-Durchgang per Konfiguration abgeschaltet");
    protected override WorkerSchedule Schedule { get; } = WorkerSchedule.DailyAtUtc(RunAtUtc);
    protected override WorkerStart Start => _startupDelay > TimeSpan.Zero ? WorkerStart.After(_startupDelay) : WorkerStart.OnSchedule;
    protected override void LogFailure(Exception ex)
        => _logger.LogError(ex, "Turnierverlauf: Hintergrund-Durchgang fehlgeschlagen");

    public static TimeSpan TimeUntilNextRun(DateTime nowUtc) => DailySchedule.TimeUntilNextRun(nowUtc, RunAtUtc);

    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        var history = services.GetRequiredService<TournamentHistoryService>();

        var sweep = await history.RefreshAllAsync(_maxCards, ct);

        if (sweep.Unavailable > 0)
            _logger.LogWarning(
                "Turnierverlauf: {Players} Konten aufgefrischt, {Cards} Spielerkarten und {TimeControls} Bedenkzeiten geholt, {Reclassified} neu eingeordnet, {Unavailable} Trefferlisten nicht erreichbar",
                sweep.Players, sweep.Cards, sweep.TimeControls, sweep.Reclassified, sweep.Unavailable);
        else
            _logger.LogInformation(
                "Turnierverlauf: {Players} Konten aufgefrischt, {Cards} Spielerkarten und {TimeControls} Bedenkzeiten geholt, {Reclassified} neu eingeordnet",
                sweep.Players, sweep.Cards, sweep.TimeControls, sweep.Reclassified);

        // NACH den Listen: die haben eben erst die neuen Turniere in die Verlaeufe gebracht.
        var crawls = await history.CrawlHistoryTournamentsAsync(_maxTournamentCrawls, ct);
        _logger.LogInformation(
            "Turnierverlauf: {Missing} fehlende und {Refreshed} laufende Turniere beim Crawler angefordert ({Known} schon da)",
            crawls.Missing, crawls.Refreshed, crawls.Known);
        // Fehler und Herunterfahren behandelt PeriodicWorker (alles fangen ausser dem echten Shutdown).
    }
}
