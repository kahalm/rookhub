using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Naechtlicher Verzeichnis-Sweep um 03:00 UTC, gestaffelt:
///   Stufe 1 - die Nachbarlaender jede Nacht (dort spielt der Nutzer wirklich),
///   Stufe 2 - alle uebrigen Foederationen rotierend, die am laengsten nicht besuchten zuerst.
///
/// 03:00 UTC ist bewusst gewaehlt: Rundenmonitore laufen nur eine Stunde nach manueller Aktivierung,
/// nachts steht der prozessweite Rate-Limiter des Crawlers also frei. Kein Lauf beim Start - ein
/// Deploy soll keinen Sweep-Sturm ausloesen.
/// </summary>
public class TournamentDirectoryScheduler : BackgroundService
{
    public static readonly TimeSpan RunAtUtc = TimeSpan.FromHours(3);

    /// <summary>
    /// Nachbarlaender: von Oesterreich aus deckt das jeden realistischen Umkreis ab, auch einen
    /// ueber die Grenze. Ueber <c>TournamentDirectory:DailyFederations</c> aenderbar.
    /// </summary>
    private static readonly string[] DefaultDailyFederations =
        ["AUT", "GER", "SUI", "ITA", "CZE", "SVK", "HUN", "SLO", "LIE"];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TournamentDirectoryScheduler> _logger;
    private readonly string[] _dailyFederations;
    private readonly int _weeklyBatchSize;
    private readonly int _disambiguationBatchSize;
    private readonly int _roundPlanBatchSize;
    private readonly int _fideDetailBatchSize;
    private readonly int _fideYears;
    private readonly bool _enabled;

    public TournamentDirectoryScheduler(
        IServiceScopeFactory scopeFactory,
        ILogger<TournamentDirectoryScheduler> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var configured = configuration["TournamentDirectory:DailyFederations"];
        _dailyFederations = string.IsNullOrWhiteSpace(configured)
            ? DefaultDailyFederations
            : configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(f => f.ToUpperInvariant()).Distinct().ToArray();

        // 0 = nur die taegliche Stufe, keine Weltrotation.
        _weeklyBatchSize = Math.Clamp(configuration.GetValue("TournamentDirectory:WeeklyBatchSize", 40), 0, 261);
        _enabled = configuration.GetValue("TournamentDirectory:Enabled", true);

        // Wie viele Turniere je Nacht ueber die Vereinsnamen aufgeloest werden. Jedes kostet einen
        // Seitenabruf bei chess-results; 0 schaltet den Schritt ab.
        _disambiguationBatchSize = Math.Clamp(
            configuration.GetValue("TournamentDirectory:DisambiguationBatchSize", 50), 0, 500);

        // Der Rundenplan-Durchgang darf grosszuegiger sein als die Spielort-Aufloesung: ein
        // Abruf je Turnier auf einer 17-kB-Seite, und der Nutzen ist unmittelbar sichtbar (eine
        // Liga stand an rund 200 Kalendertagen statt an ihren elf Spieltagen). Am Dev-Stand
        // waren 610 Eintraege nachzutragen — mit 200 je Nacht ist der Bestand in drei Naechten
        // durch, danach kommen nur die neuen dazu.
        _roundPlanBatchSize = Math.Clamp(
            configuration.GetValue("TournamentDirectory:RoundPlanBatchSize", 200), 0, 1000);

        // Wie viele Jahre des FIDE-Kalenders je Nacht: das laufende plus die naechsten. Drei
        // Abrufe, denn weiter voraus fuehrt FIDE praktisch nichts (2028 war leer). 0 = aus.
        _fideYears = Math.Clamp(configuration.GetValue("TournamentDirectory:FideYears", 3), 0, 5);

        // Kleiner als die Rundenplan-Portion, weil es viel weniger zu tun gibt: der
        // FIDE-Jahreskalender bringt je Nacht eine Handvoll neuer Ereignisse, und nur die haben
        // noch keine Detailangaben. 50 holt einen Rueckstand in wenigen Naechten auf und kostet
        // im eingeschwungenen Zustand fast nichts.
        _fideDetailBatchSize = Math.Clamp(
            configuration.GetValue("TournamentDirectory:FideDetailBatchSize", 50), 0, 500);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Turnierverzeichnis: Sweep per Konfiguration abgeschaltet");
            return;
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
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var directory = scope.ServiceProvider.GetRequiredService<TournamentDirectoryService>();

            var federations = await BuildRunListAsync(db, _dailyFederations, _weeklyBatchSize, ct);
            _logger.LogInformation("Turnierverzeichnis: Sweep ueber {Count} Foederationen", federations.Count);

            var results = await directory.RunSweepAsync(federations, ct);

            var failed = results.Where(r => !r.Succeeded).Select(r => r.Federation).ToList();
            if (failed.Count > 0)
                _logger.LogWarning("Turnierverzeichnis: {Count} Foederationen fehlgeschlagen ({List})",
                    failed.Count, string.Join(", ", failed));

            // Danach eine GEDECKELTE Runde Spielort-Aufloesung ueber die Vereinsnamen: sie kostet
            // einen Seitenabruf je Turnier, arbeitet sich also Nacht fuer Nacht durch den Rueckstand
            // statt ihn in einem Lauf abzuarbeiten. Ein Fehlschlag hier darf den Sweep, der schon
            // durch ist, nicht als gescheitert erscheinen lassen — deshalb der eigene Fang.
            try
            {
                if (_disambiguationBatchSize > 0)
                {
                    var disambiguation = scope.ServiceProvider.GetRequiredService<VenueDisambiguationService>();
                    await disambiguation.RunAsync(_disambiguationBatchSize, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Turnierverzeichnis: Spielort-Aufloesung fehlgeschlagen");
            }

            // Und die Spieltermine der langlaufenden Turniere — eigener Fang aus demselben
            // Grund: was schon durch ist, soll nicht wegen eines Nachtrags als gescheitert
            // gelten.
            try
            {
                if (_roundPlanBatchSize > 0)
                {
                    var roundPlans = scope.ServiceProvider.GetRequiredService<TournamentRoundPlanService>();
                    await roundPlans.RunAsync(_roundPlanBatchSize, retryEmpty: false, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Turnierverzeichnis: Rundenplan-Durchgang fehlgeschlagen");
            }

            // Und der FIDE-Kalender als zweite Quelle. Eigener Fang wie die uebrigen Nachtraege:
            // ein Ausfall dort darf den erledigten chess-results-Sweep nicht als gescheitert
            // erscheinen lassen.
            try
            {
                if (_fideYears > 0)
                {
                    var fide = scope.ServiceProvider.GetRequiredService<FideDirectorySweepService>();
                    var year = DateTime.UtcNow.Year;
                    // AUFSTEIGEND — die Jahres-Zuordnung eines Ereignisses ueber den
                    // Jahreswechsel haengt daran (siehe FideDirectorySweepService).
                    await fide.RunAsync([.. Enumerable.Range(year, _fideYears)], ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Turnierverzeichnis: FIDE-Durchgang fehlgeschlagen");
            }

            // Der ANKUENDIGUNGS-Kalender von chess-results. Laeuft NACH dem Sweep: der legt die
            // Eintraege an, die der Kalender dann nur noch zuordnen muss, statt sie ein zweites
            // Mal anzulegen. Ein Abruf fuer alle 16 Foederationen, die ihn benutzen.
            //
            // Kein eigener Deckel: es ist EIN Abruf, unabhaengig von der Bestandsgroesse.
            try
            {
                var calendar = scope.ServiceProvider.GetRequiredService<TournamentCalendarSweepService>();
                await calendar.RunAsync("-", ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Turnierverzeichnis: Ankuendigungskalender fehlgeschlagen");
            }

            // Und die DETAILangaben der FIDE-Eintraege. Muss NACH dem Jahreskalender laufen: der
            // legt die neuen Ereignisse ueberhaupt erst an, und genau die haben noch keine
            // Bedenkzeit, kein System und keine Anschrift.
            //
            // Ohne diesen Block bliebe der Nachtrag eine Handarbeit — jedes neue FIDE-Ereignis
            // kaeme mit Name, Termin und Ort herein und wuerde nie wieder angefasst. `retryEmpty`
            // steht bewusst auf false: ein Ereignis ohne gepflegte Angaben ist der haeufige Fall
            // und darf nicht jede Nacht erneut abgefragt werden.
            try
            {
                if (_fideDetailBatchSize > 0)
                {
                    var details = scope.ServiceProvider.GetRequiredService<FideEventDetailService>();
                    await details.RunAsync(_fideDetailBatchSize, retryEmpty: false, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Turnierverzeichnis: FIDE-Detail-Durchgang fehlgeschlagen");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Herunterfahren - kein Fehler.
        }
        catch (Exception ex)
        {
            // Alles fangen: BackgroundServiceExceptionBehavior ist StopHost, und ein
            // HttpClient-Timeout wuerde sonst die ganze API mitnehmen.
            _logger.LogError(ex, "Turnierverzeichnis: naechtlicher Sweep fehlgeschlagen");
        }
    }

    /// <summary>
    /// Die taeglichen Foederationen plus die naechste Charge der Rotation. Grundmenge ist
    /// <see cref="FederationCatalog.All"/> und nicht die Sweep-Tabelle: die ist anfangs leer, eine
    /// Rotation ueber sie wuerde nie anlaufen. Ausgewaehlt werden die am laengsten nicht
    /// ERFOLGREICH gesweepten (nie besuchte zuerst) - damit holt sich ein gescheiterter Lauf
    /// seinen Platz von selbst zurueck, denn ein Fehlschlag laesst LastSweptAt alt.
    /// </summary>
    internal static async Task<List<string>> BuildRunListAsync(
        AppDbContext db, IReadOnlyList<string> daily, int weeklyBatchSize, CancellationToken ct)
    {
        var run = new List<string>(daily);
        if (weeklyBatchSize <= 0) return run;

        var dailySet = daily.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lastSwept = await db.TournamentDirectorySweeps.AsNoTracking()
            .Select(s => new { s.Federation, s.LastSweptAt })
            .ToDictionaryAsync(s => s.Federation, s => s.LastSweptAt, StringComparer.OrdinalIgnoreCase, ct);

        var rotating = FederationCatalog.All
            .Where(f => !dailySet.Contains(f))
            .OrderBy(f => lastSwept.GetValueOrDefault(f) ?? DateTime.MinValue)
            .Take(weeklyBatchSize);

        run.AddRange(rotating);
        return run;
    }
}
