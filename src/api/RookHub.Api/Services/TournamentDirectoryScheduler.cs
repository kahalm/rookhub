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
    private readonly int _catchUpAfterHours;
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

        // Ab welchem Alter des letzten erfolgreichen Sweeps beim START nachgeholt wird. 20 h
        // statt 24, damit ein Neustand kurz VOR der ueblichen Uhrzeit nicht bis zum Folgetag
        // wartet — und deutlich mehr als ein Arbeitstag voller Deploys, damit nicht jedes
        // Deploy einen Lauf ausloest (siehe CatchUpAge).
        _catchUpAfterHours = Math.Clamp(
            configuration.GetValue("TournamentDirectory:CatchUpAfterHours", 20), 0, 168);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Turnierverzeichnis: Sweep per Konfiguration abgeschaltet");
            return;
        }

        // AUFHOLEN nach einem Neustart, bevor die Warteschleife beginnt.
        //
        // Warum das noetig ist: die Warteschleife unten ist EIN `Task.Delay` bis 03:00 UTC, und
        // jeder Neustart setzt sie neu an. Wird tagsueber mehrfach deployt, laeuft der Container
        // nie durchgehend von einem Deploy bis zur Uhrzeit — der Sweep kommt dann NIE dran. Am
        // Dev-Stand nachgemessen: er lief genau EINMAL (2026-09-07, 03:00:29 bis 03:11:58), und
        // danach nie wieder; 44 von 257 Foederationen waren je erfolgreich gesweept, die uebrigen
        // 213 holte niemand nach. Spanien stand deshalb bei einem einzigen Eintrag.
        try
        {
            if (await ShouldCatchUpAsync(stoppingToken))
            {
                _logger.LogInformation(
                    "Turnierverzeichnis: Aufhol-Lauf nach Neustart (letzter Sweep aelter als {Hours} h)",
                    _catchUpAfterHours);
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
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

    /// <summary>
    /// Ist der letzte ERFOLGREICHE Sweep laenger her als die Karenz?
    ///
    /// <para><b>Warum am Marker und nicht an der Startzeit.</b> „Zehn Minuten nach jedem Start"
    /// waere die naheliegende Bauform (der Turnierverlauf-Nachlauf macht es so), hier aber
    /// gefaehrlich: die Zusatzquellen zaehlen ihre Verschwunden-Karenz in LAEUFEN, nicht in Tagen
    /// (<c>ExternalDirectorySource.MissesUntilRetired</c>). Jedes Deploy waere ein Lauf — bei drei
    /// Deploys an einem Nachmittag gaelte ein Turnier, dessen Quelle einmal kurz nichts
    /// ausliefert, binnen einer Stunde als abgesagt, samt Benachrichtigung an die Abonnenten.
    /// Der Marker verhindert das von selbst: nach dem ersten Aufhol-Lauf steht er neu, das zweite
    /// und dritte Deploy loesen keinen weiteren aus.</para>
    ///
    /// <para>Ohne jeden erfolgreichen Sweep (frische Datenbank) wird nachgeholt — dort ist
    /// „nie gelaufen" das staerkste Argument dafuer.</para>
    /// </summary>
    private async Task<bool> ShouldCatchUpAsync(CancellationToken ct)
    {
        if (_catchUpAfterHours <= 0) return false;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var last = await db.TournamentDirectorySweeps.AsNoTracking()
            .Where(s => s.LastSweptAt != null)
            .MaxAsync(s => s.LastSweptAt, ct);

        return IsStale(last, DateTime.UtcNow, _catchUpAfterHours);
    }

    /// <summary>
    /// Die Entscheidung allein, damit sie ohne Datenbank pruefbar ist. <c>null</c> heisst „noch
    /// nie erfolgreich gesweept" und ist immer ueberfaellig.
    /// </summary>
    internal static bool IsStale(DateTime? lastSweptUtc, DateTime nowUtc, int catchUpAfterHours) =>
        lastSweptUtc is not { } last || nowUtc - last >= TimeSpan.FromHours(catchUpAfterHours);

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

            // Der italienische Verbandskalender. Eigener Fang wie die uebrigen Zusatzquellen: ein
            // Ausfall dort darf den erledigten chess-results-Sweep nicht als gescheitert
            // erscheinen lassen. Ein Abruf, unabhaengig von der Bestandsgroesse.
            try
            {
                var fsi = scope.ServiceProvider.GetRequiredService<FsiDirectorySweepService>();
                await fsi.RunAsync(18, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Turnierverzeichnis: FSI-Kalender fehlgeschlagen");
            }

            // Der slowenische Verbandskalender. Eigener Fang wie die uebrigen Zusatzquellen.
            try
            {
                var szs = scope.ServiceProvider.GetRequiredService<SzsDirectorySweepService>();
                await szs.RunAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Turnierverzeichnis: SZS-Kalender fehlgeschlagen");
            }

            // Der slowakische Verbandskalender. Er kostet mehr als die uebrigen — ein Abruf der
            // Schnittstelle plus einer je Turnier fuer die Detailseite (mit Pause, rund eine
            // Minute fuer 79 Turniere) — und liefert dafuer als einzige Quelle Anschrift,
            // Bedenkzeit, Rundenzahl und System auf einmal.
            try
            {
                var chessSk = scope.ServiceProvider.GetRequiredService<ChessSkDirectorySweepService>();
                await chessSk.RunAsync(details: true, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Turnierverzeichnis: chess.sk-Kalender fehlgeschlagen");
            }

            // Der ungarische Verbandskalender. Ein Abruf — aber ein langsamer (rund 75 Sekunden
            // fuer 31 kB), deshalb steht er hinter den uebrigen.
            try
            {
                var chessHu = scope.ServiceProvider.GetRequiredService<ChessHuDirectorySweepService>();
                await chessHu.RunAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Turnierverzeichnis: chess.hu-Kalender fehlgeschlagen");
            }

            // Der tschechische Verbandskalender. Er bringt SPIELTERMINE mit (die Ligarunden), und
            // er laeuft NACH dem Rundenplan-Nachtrag — das kostet nichts: dessen Auswahl haengt an
            // `RoundPlanCheckedAt`, nicht daran, ob schon Termine dastehen. Ein heute Nacht
            // angelegter Liga-Eintrag kommt also morgen dort an die Reihe, und findet
            // chess-results keinen Plan (leere Antwort), bleiben die hier eingetragenen stehen.
            try
            {
                var chessCz = scope.ServiceProvider.GetRequiredService<ChessCzDirectorySweepService>();
                await chessCz.RunAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Turnierverzeichnis: chess.cz-Kalender fehlgeschlagen");
            }

            // Der polnische Verbandskalender — die ergiebigste Einzelquelle (611 kuenftige
            // Turniere in einem Abruf). Die Detailseiten holt er nur fuer noch unbekannte
            // Turniere und gedeckelt; der Bestand ist damit nach wenigen Naechten vollstaendig.
            try
            {
                var chessArbiter = scope.ServiceProvider
                    .GetRequiredService<ChessArbiterDirectorySweepService>();
                await chessArbiter.RunAsync(null, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Turnierverzeichnis: chessarbiter-Kalender fehlgeschlagen");
            }

            // Die Turnierdatenbank des Deutschen Schachbunds. Sie dauert am laengsten (zwei
            // Abrufe je Region mit der Wartezeit aus ihrer robots.txt) und steht deshalb zuletzt.
            try
            {
                var schachbund = scope.ServiceProvider
                    .GetRequiredService<SchachbundDirectorySweepService>();
                await schachbund.RunAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Turnierverzeichnis: schachbund-Turnierdatenbank fehlgeschlagen");
            }

            // Der englische Verbandskalender. Zwei geblaetterte Endpunkte mit der Wartezeit aus
            // der robots.txt der Quelle — rund drei Minuten, und die einzige Quelle, die die
            // Koordinaten gleich mitbringt.
            try
            {
                var ecf = scope.ServiceProvider.GetRequiredService<EcfDirectorySweepService>();
                await ecf.RunAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Turnierverzeichnis: ECF-Kalender fehlgeschlagen");
            }

            // Die sechs Quellen der dritten Runde (Europa ausserhalb der Nachbarschaft). Jede in
            // ihrem eigenen Fang: sie sind voneinander unabhaengig, und ein Ausfall bei einer darf
            // die uebrigen nicht mitnehmen.
            //
            // Reihenfolge nach KOSTEN, billig zuerst — dasselbe Prinzip wie oben: faellt etwas
            // grundsaetzlich aus (Netz, Crawler tot), sieht man es nach Sekunden statt nach einer
            // Viertelstunde.
            await RunSourceAsync(scope, "Rumaenien (FRSah)",
                s => s.GetRequiredService<FrsahDirectorySweepService>().RunAsync(ct), ct);

            await RunSourceAsync(scope, "Wales (WCU)",
                s => s.GetRequiredService<WcuDirectorySweepService>().RunAsync(ct), ct);

            await RunSourceAsync(scope, "Kanada (CFC)",
                s => s.GetRequiredService<CfcDirectorySweepService>().RunAsync(ct), ct);

            await RunSourceAsync(scope, "Niederlande (KNSB)",
                s => s.GetRequiredService<KnsbDirectorySweepService>().RunAsync(ct), ct);

            await RunSourceAsync(scope, "Schottland (Chess Scotland)",
                s => s.GetRequiredService<ChessScotlandDirectorySweepService>().RunAsync(null, ct), ct);

            await RunSourceAsync(scope, "Irland (ICU)",
                s => s.GetRequiredService<IcuDirectorySweepService>().RunAsync(null, ct), ct);

            await RunSourceAsync(scope, "Norwegen (sjakk.no)",
                s => s.GetRequiredService<SjakkDirectorySweepService>().RunAsync(null, ct), ct);

            // Frankreich zuletzt: zwoelf Monatsseiten plus bis zu 150 Turnierseiten sind der
            // laengste Durchgang der Reihe.
            await RunSourceAsync(scope, "Frankreich (FFE)",
                s => s.GetRequiredService<FfeDirectorySweepService>().RunAsync(12, null, ct), ct);

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

    /// <summary>
    /// Eine Zusatzquelle laufen lassen und ihren Ausfall eindaemmen.
    ///
    /// <para>Der Fang war zehnmal ausgeschrieben, bevor die dritte Runde ihn auf ein Dutzend
    /// gebracht haette. Die Regel dahinter ist bei allen dieselbe und wichtiger als die
    /// Wiederholung: die Quellen sind voneinander unabhaengig, und ein Netzausfall bei einer darf
    /// weder die uebrigen noch den erledigten chess-results-Sweep als gescheitert erscheinen
    /// lassen. Ein ABBRUCH des Dienstes ist dagegen kein Quellenfehler und wird durchgereicht.</para>
    /// </summary>
    private async Task RunSourceAsync(IServiceScope scope, string name,
        Func<IServiceProvider, Task> run, CancellationToken ct)
    {
        try
        {
            await run(scope.ServiceProvider);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Turnierverzeichnis: {Source} fehlgeschlagen", name);
        }
    }

}
