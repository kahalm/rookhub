namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Übersetzt die Bücher der Bibliothek (0.876.0, <see cref="EbookTranslationService"/>) — EIN Buch nach dem anderen, die
/// Parallelität steckt im Lauf (Fuhren je Buch). Aufbau wie der <c>CourseTranslationWorker</c>:
///
/// <list type="bullet">
/// <item><b>Aus</b>, solange <c>EbookTranslation:Languages</c> leer ist (Vorgabe) oder <c>Ebooks:Root</c> fehlt — der Dienst endet
/// nach einer Logzeile.</item>
/// <item><b>Sprache erkennen</b> läuft immer (CPU, keine Spark): je Durchgang bis zu <see cref="DetectBatch"/> Bücher.</item>
/// <item><b>Sperrzeiten der Spark</b> (<see cref="QuietHours"/>, dieselben wie Kurs-Übersetzung und Partietexte): kein Lauf; ein
/// laufender wird alle <see cref="QuietCheck"/> geprüft und bei Beginn der Sperrzeit abgebrochen — die Zeile geht zurück
/// auf wartend, der Zwischenspeicher hält das Fertige, am Abend geht es an derselben Stelle weiter.</item>
/// <item><b>Pause</b> (Admin „Hintergrundjobs pausieren“): wie die Sperrzeit.</item>
/// <item>Nur über <b>eigene Hardware</b> (<c>TextLlm:BaseUrl</c>); ohne sie oder wenn die Spark nicht antwortet, wartet der Dienst.</item>
/// <item>Ohne Arbeit: fertige Übersetzungen gegen ihre Quelle prüfen (<see cref="EbookTranslationService.VerifyAsync"/>), dann
/// auf einen Weckruf warten (Admin zieht ein Buch vor), höchstens <see cref="IdlePoll"/>.</item>
/// </list>
/// </summary>
public class EbookTranslationWorker : BackgroundService, IBackgroundJobInfo
{
    private PauseGate? _pauseGate;
    private PauseGate Pause => _pauseGate ??= new PauseGate(nameof(EbookTranslationWorker), _logger);
    /// <inheritdoc/>
    public bool PausableByAdmin => true;
    /// <inheritdoc/>
    public DateTime? LastTickUtc => _pauseGate?.LastTickUtc;

    public static readonly TimeSpan IdlePoll = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan NotConfiguredPoll = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan UnreachablePoll = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxQuietSleep = TimeSpan.FromMinutes(10);

    /// <summary>Bücher je Durchgang für die Spracherkennung.</summary>
    public const int DetectBatch = 10;

    /// <summary>Fertige Übersetzungen je Leerlauf-Durchgang gegen ihre Quelle prüfen.</summary>
    public const int VerifyBatch = 3;

    private readonly IServiceScopeFactory _scopes;
    private readonly EbookTranslationSignal _signal;
    private readonly ILogger<EbookTranslationWorker> _logger;
    private readonly QuietHours? _quiet;

    /// <summary>So oft prüft ein laufender Lauf Sperrzeit und Pause (Tests setzen es kurz).</summary>
    public TimeSpan QuietCheck { get; init; } = TimeSpan.FromSeconds(30);

    public EbookTranslationWorker(IServiceScopeFactory scopes, EbookTranslationSignal signal, ILogger<EbookTranslationWorker> logger,
        QuietHours? quiet = null)
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
            var svc = scope.ServiceProvider.GetRequiredService<EbookTranslationService>();
            if (!svc.IsEnabled)
            {
                _logger.LogInformation("E-Book-Übersetzung: aus (EbookTranslation:Languages leer oder keine E-Book-Wurzel)");
                return;
            }
            var requeued = await svc.RequeueInterruptedAsync(stoppingToken);
            if (requeued > 0) _logger.LogInformation("{Count} unterbrochene E-Book-Übersetzung(en) wieder eingereiht", requeued);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "E-Book-Übersetzung: Aufräumen beim Start fehlgeschlagen");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (await Pause.SkipAndWaitAsync(_scopes, PauseGate.Recheck, stoppingToken)) continue;
            TimeSpan wait;
            try
            {
                wait = await StepAsync(stoppingToken);
                Pause.MarkTick();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "E-Book-Übersetzung: Durchgang fehlgeschlagen, neuer Versuch später");
                wait = IdlePoll;
            }
            if (wait <= TimeSpan.Zero) continue;
            try { await _signal.WaitAsync(wait, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Ein Durchgang: Sprachen erkennen, dann höchstens EIN Buch. Antwort = Wartezeit danach (<see cref="TimeSpan.Zero"/> = gleich weiter).</summary>
    public async Task<TimeSpan> StepAsync(CancellationToken stoppingToken)
    {
        int? claimed;
        var detected = 0;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<EbookTranslationService>();
            if (!svc.IsEnabled) return NotConfiguredPoll;
            detected = await svc.DetectLanguagesAsync(DetectBatch, stoppingToken);
            if (_quiet?.RemainingClamped(TimeSpan.FromSeconds(1), MaxQuietSleep) is { } quiet)
                return detected > 0 ? TimeSpan.FromSeconds(1) : quiet;
            if (!svc.IsAvailable) return detected > 0 ? TimeSpan.FromSeconds(1) : NotConfiguredPoll;
            if (svc.IsUnreachable) return UnreachablePoll;
            claimed = await svc.ClaimNextAsync(stoppingToken);
            if (claimed == null)
            {
                if (detected > 0) return TimeSpan.FromSeconds(1);
                await svc.VerifyAsync(VerifyBatch, stoppingToken);
                return IdlePoll;
            }
        }
        var outcome = await RunClaimedAsync(claimed.Value, stoppingToken);
        return outcome == EbookTranslationOutcome.Unreachable ? UnreachablePoll : TimeSpan.Zero;
    }

    private async Task<EbookTranslationOutcome?> RunClaimedAsync(int translationId, CancellationToken stoppingToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var watchStop = new CancellationTokenSource();
        var reason = new StrongBox<string?>(null);
        var watch = WatchAsync(cts, reason, watchStop.Token);
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var outcome = await scope.ServiceProvider.GetRequiredService<EbookTranslationService>().RunAsync(translationId, cts.Token);
            _logger.LogInformation("E-Book-Übersetzung {Id}: {Outcome}", translationId, outcome);
            if (outcome == EbookTranslationOutcome.Unreachable) await WithServiceAsync(s => s.RequeueAsync(translationId, CancellationToken.None));
            return outcome;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("E-Book-Übersetzung {Id}: Dienst stoppt, Lauf kommt beim Start zurück", translationId);
            }
            else
            {
                await WithServiceAsync(s => s.RequeueAsync(translationId, CancellationToken.None));
                _logger.LogInformation("E-Book-Übersetzung {Id}: {Reason}, zurück in die Reihe", translationId, reason.Value);
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "E-Book-Übersetzung {Id}: unerwarteter Fehler, Lauf gescheitert", translationId);
            await WithServiceAsync(s => s.MarkFailedAsync(translationId, ex.Message, CancellationToken.None));
            return EbookTranslationOutcome.Failed;
        }
        finally
        {
            await watchStop.CancelAsync();
            try { await watch; }
            catch (OperationCanceledException) { }
        }
    }

    /// <summary>Während ein Lauf läuft: beginnt die Sperrzeit oder pausiert ein Admin, wird er abgebrochen.</summary>
    private async Task WatchAsync(CancellationTokenSource run, StrongBox<string?> reason, CancellationToken ct)
    {
        var quiet = _quiet is { Enabled: true } q ? q : null;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(QuietCheck, ct);
            if (quiet?.IsQuietNow() == true) { reason.Value = "Sperrzeit"; await run.CancelAsync(); return; }
            if (await Pause.ShouldSkipAsync(_scopes, ct)) { reason.Value = "pausiert"; await run.CancelAsync(); return; }
        }
    }

    private async Task WithServiceAsync(Func<EbookTranslationService, Task> action)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<EbookTranslationService>());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "E-Book-Übersetzung: Zeile nachtragen fehlgeschlagen (kommt beim Start zurück)");
        }
    }

    private sealed class StrongBox<T>(T value) { public T Value { get; set; } = value; }
}
