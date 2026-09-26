namespace RookHub.Api.Services;

/// <summary>Warum ein laufender Kurs-Uebersetzungsauftrag abgebrochen wurde.</summary>
public enum CourseTranslationStop
{
    /// <summary>Die Sperrzeit der Spark hat begonnen — Auftrag zurueck in die Schlange.</summary>
    Quiet,
    /// <summary>Ein angeforderter Auftrag wartet, der laufende ist Automatik — zurueck in die Schlange.</summary>
    Preempted,
    /// <summary>Zurueckgezogen (Admin) — der Auftrag steht schon auf <c>Cancelled</c>.</summary>
    Withdrawn,
}

/// <summary>
/// Verbindung zwischen den Anfragen (<see cref="CourseTranslationJobService"/>, scoped) und dem
/// <see cref="CourseTranslationWorker"/> (Singleton): wecken, wenn etwas eingereiht wurde, und den LAUFENDEN Auftrag
/// abbrechen (zurueckgezogen, Vorrang). Nur Arbeitsspeicher — die Warteschlange selbst liegt in der Datenbank.
/// </summary>
public sealed class CourseTranslationSignal
{
    private readonly SemaphoreSlim _wake = new(0);
    private readonly Lock _gate = new();
    private Run? _running;

    /// <summary>Der laufende Auftrag mit seinem Abbruch — und warum abgebrochen wurde.</summary>
    public sealed class Run(int jobId, bool automatic, CancellationTokenSource cts)
    {
        public int JobId { get; } = jobId;
        public bool Automatic { get; } = automatic;
        internal CancellationTokenSource Cts { get; } = cts;
        public CourseTranslationStop? Reason { get; internal set; }
    }

    public void Wake()
    {
        // Mehr als ein wartendes Signal braucht es nicht: der Dienst schaut ohnehin in die ganze Schlange.
        lock (_gate)
            if (_wake.CurrentCount == 0) _wake.Release();
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _wake.WaitAsync(timeout, ct);

    /// <summary>Der Auftrag, der gerade laeuft (<c>null</c> = keiner).</summary>
    public int? RunningJobId
    {
        get { lock (_gate) return _running?.JobId; }
    }

    internal Run Begin(int jobId, bool automatic, CancellationTokenSource cts)
    {
        lock (_gate) return _running = new Run(jobId, automatic, cts);
    }

    internal void End(Run run)
    {
        lock (_gate)
            if (ReferenceEquals(_running, run)) _running = null;
    }

    /// <summary>Bricht den laufenden Auftrag ab, wenn es <paramref name="jobId"/> ist.</summary>
    public bool Cancel(int jobId, CourseTranslationStop reason)
    {
        lock (_gate)
        {
            if (_running is not { } run || run.JobId != jobId) return false;
            return Stop(run, reason);
        }
    }

    /// <summary>Ein angeforderter Auftrag wartet: laeuft gerade ein AUTOMATIK-Auftrag, wird er zurueckgestellt.</summary>
    public bool PreemptAutomatic()
    {
        lock (_gate)
        {
            if (_running is not { Automatic: true } run) return false;
            return Stop(run, CourseTranslationStop.Preempted);
        }
    }

    private static bool Stop(Run run, CourseTranslationStop reason)
    {
        run.Reason ??= reason;
        try { run.Cts.Cancel(); }
        catch (ObjectDisposedException) { return false; }   // der Lauf ist gerade zu Ende gegangen
        return true;
    }
}

/// <summary>
/// Arbeitet die Kurs-Uebersetzungsauftraege ab (Plan „Kurs-Kommentare mehrsprachig", Abschnitt 6) — immer nur EINEN
/// gleichzeitig; die Parallelitaet steckt IM Lauf (<c>CourseTranslation:Parallel</c> Linien).
///
/// <para><b>Schleife</b>: kein Text-Modell → schlafen. Sperrzeit der Spark (<see cref="QuietHours"/>) → schlafen bis zu
/// ihrem Ende, hoechstens <see cref="MaxQuietSleep"/> am Stueck. Sonst den naechsten Auftrag nehmen
/// (<see cref="CourseTranslationJobService.ClaimNextAsync"/>: angeforderte vor der Automatik) und laufen lassen. Ohne
/// Arbeit wartet der Dienst auf einen Weckruf (Anfordern, Nachziehen), hoechstens <see cref="IdlePoll"/>.</para>
///
/// <para><b>Abbruch mitten im Lauf</b> ueber einen eigenen Token: beginnt die Sperrzeit (alle <see cref="QuietCheck"/>
/// geprueft) oder kommt ein angeforderter Auftrag, waehrend Automatik laeuft, geht der Auftrag zurueck auf
/// <c>Queued</c>; zieht ein Admin ihn zurueck, bleibt er <c>Cancelled</c>. Beim Herunterfahren bleibt er stehen und
/// kommt beim naechsten Start zurueck (<see cref="CourseTranslationJobService.RequeueInterruptedAsync"/>). Weil alles
/// inkrementell ist, ueberspringt der naechste Lauf das Fertige.</para>
/// </summary>
public class CourseTranslationWorker : BackgroundService
{
    /// <summary>So lange wartet der Dienst ohne Arbeit auf einen Weckruf. Auftraege entstehen nur in diesem Prozess
    /// (und wecken ihn), das Nachsehen ist nur die Rueckfallebene.</summary>
    public static readonly TimeSpan IdlePoll = TimeSpan.FromMinutes(5);

    /// <summary>Ohne Text-Modell schaut der Dienst so selten nach.</summary>
    public static readonly TimeSpan NotConfiguredPoll = TimeSpan.FromMinutes(10);

    /// <summary>Hoechstens so lange am Stueck schlafen, wenn die Spark gesperrt ist.</summary>
    public static readonly TimeSpan MaxQuietSleep = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopes;
    private readonly CourseTranslationSignal _signal;
    private readonly ILogger<CourseTranslationWorker> _logger;
    private readonly QuietHours? _quiet;

    /// <summary>So oft prueft ein laufender Auftrag, ob die Sperrzeit begonnen hat (Tests setzen es kurz).</summary>
    public TimeSpan QuietCheck { get; init; } = TimeSpan.FromSeconds(30);

    public CourseTranslationWorker(IServiceScopeFactory scopes, CourseTranslationSignal signal,
        ILogger<CourseTranslationWorker> logger, QuietHours? quiet = null)
    {
        _scopes = scopes;
        _signal = signal;
        _logger = logger;
        _quiet = quiet;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var requeued = await scope.ServiceProvider.GetRequiredService<CourseTranslationJobService>()
                .RequeueInterruptedAsync(stoppingToken);
            if (requeued > 0) _logger.LogInformation("{Count} unterbrochene Kurs-Uebersetzung(en) wieder eingereiht", requeued);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Kurs-Uebersetzungen: Aufraeumen beim Start fehlgeschlagen");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                wait = await StepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kurs-Uebersetzungen: Durchgang fehlgeschlagen, neuer Versuch spaeter");
                wait = IdlePoll;
            }
            if (wait <= TimeSpan.Zero) continue;
            try { await _signal.WaitAsync(wait, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Ein Durchgang: hoechstens EIN Auftrag. Gibt zurueck, wie lange danach gewartet wird (<see cref="TimeSpan.Zero"/> =
    /// gleich weiter — es lief etwas, vielleicht wartet der naechste).
    /// </summary>
    public async Task<TimeSpan> StepAsync(CancellationToken stoppingToken)
    {
        if (_quiet?.QuietUntil() is { } until)
        {
            var left = until - _quiet.Now;
            return left < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : left > MaxQuietSleep ? MaxQuietSleep : left;
        }

        (int Id, bool Automatic)? claimed;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            var jobs = scope.ServiceProvider.GetRequiredService<CourseTranslationJobService>();
            if (!jobs.IsAvailable) return NotConfiguredPoll;
            claimed = await jobs.ClaimNextAsync(stoppingToken);
        }
        if (claimed is not { } job) return IdlePoll;

        await RunClaimedAsync(job.Id, job.Automatic, stoppingToken);
        return TimeSpan.Zero;
    }

    private async Task RunClaimedAsync(int jobId, bool automatic, CancellationToken stoppingToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var run = _signal.Begin(jobId, automatic, cts);
        using var watchStop = new CancellationTokenSource();
        var watch = WatchQuietAsync(jobId, watchStop.Token);
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var outcome = await scope.ServiceProvider.GetRequiredService<CourseTranslationJobService>()
                .RunAsync(jobId, cts.Token);
            _logger.LogInformation("Kurs-Uebersetzung {JobId}: {Outcome}", jobId, outcome);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                // Beim Herunterfahren bleibt der Auftrag auf Running und kommt beim naechsten Start zurueck.
                _logger.LogInformation("Kurs-Uebersetzung {JobId}: Dienst stoppt, Auftrag kommt beim Start zurueck", jobId);
            }
            else if (run.Reason is CourseTranslationStop.Quiet or CourseTranslationStop.Preempted)
            {
                await WithJobsAsync(jobs => jobs.RequeueAsync(jobId, CancellationToken.None));
                _logger.LogInformation("Kurs-Uebersetzung {JobId}: {Reason}, zurueck in die Schlange", jobId, run.Reason);
            }
            else
            {
                _logger.LogInformation("Kurs-Uebersetzung {JobId}: abgebrochen ({Reason})", jobId, run.Reason);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kurs-Uebersetzung {JobId}: unerwarteter Fehler, Auftrag gescheitert", jobId);
            await WithJobsAsync(jobs => jobs.MarkFailedAsync(jobId, ex.Message, CancellationToken.None));
        }
        finally
        {
            _signal.End(run);
            await watchStop.CancelAsync();
            try { await watch; }
            catch (OperationCanceledException) { }
        }
    }

    /// <summary>Waehrend ein Auftrag laeuft: beginnt die Sperrzeit, wird er abgebrochen (und zurueckgestellt).</summary>
    private async Task WatchQuietAsync(int jobId, CancellationToken ct)
    {
        if (_quiet is not { Enabled: true } quiet) return;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(QuietCheck, ct);
            if (quiet.IsQuietNow())
            {
                _signal.Cancel(jobId, CourseTranslationStop.Quiet);
                return;
            }
        }
    }

    private async Task WithJobsAsync(Func<CourseTranslationJobService, Task> action)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<CourseTranslationJobService>());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kurs-Uebersetzung: Auftrag nachtragen fehlgeschlagen (kommt beim Start zurueck)");
        }
    }
}
