namespace RookHub.Api.Services.Accounts;

/// <summary>
/// Eigene kleine Queue NUR für die Mails von „Passwort vergessen“ (R1-002). Vorher liefen sie über die
/// <see cref="IWebhookTaskQueue"/> der schach-bot-Pushes: jede Mail hielt deren einzigen Consumer eine ganze SMTP-Runde fest,
/// eine Anfrage-Flut stellte Tagespuzzle-/Wochenpost-Löser hinten an und ließ bei voller Queue die Anfragen selbst warten.
/// Hier gilt: eigener Consumer (<see cref="ResetMailWorker"/>), und eine volle Queue VERWIRFT statt zu warten — die Antwort
/// an den Browser bleibt dieselbe neutrale 200, wer wirklich eine Mail braucht, fordert sie erneut an. Die Sperrfrist je
/// Konto steht in <see cref="PasswordResetService.RequestResetAsync"/>.
/// </summary>
public interface IResetMailQueue : IBackgroundTaskQueue
{
    /// <summary>Einreihen ohne Warten; <c>false</c>, wenn die Queue voll war (nichts eingereiht).</summary>
    bool TryEnqueue(Func<IServiceProvider, CancellationToken, Task> workItem);
}

public sealed class ResetMailQueue : BackgroundTaskQueue, IResetMailQueue
{
    /// <summary>Wartende Mails höchstens — mehr wäre bei 10 Anfragen je Minute und IP ohnehin eine Flut.</summary>
    public const int Capacity = 64;

    private readonly ILogger<ResetMailQueue>? _log;

    public ResetMailQueue(ILogger<ResetMailQueue>? logger = null) : base(Capacity, logger, "ResetMail") => _log = logger;

    public bool TryEnqueue(Func<IServiceProvider, CancellationToken, Task> workItem)
    {
        if (TryWrite(workItem)) return true;
        _log?.LogWarning("Reset-Mail-Queue voll — Anfrage verworfen");
        return false;
    }
}

/// <summary>Consumer der <see cref="IResetMailQueue"/> — dieselbe Schleife wie die anderen Queue-Worker, ohne Nachdrainen beim
/// Herunterfahren (eine SMTP-Runde passt nicht ins Stoppfenster); was wartet, wird nur gezählt.</summary>
public sealed class ResetMailWorker : BackgroundService
{
    private readonly IResetMailQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ResetMailWorker> _logger;

    public ResetMailWorker(IResetMailQueue queue, IServiceScopeFactory scopeFactory, ILogger<ResetMailWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var workItem = await _queue.DequeueAsync(stoppingToken);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await workItem(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reset mail task failed");
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        var pending = _queue.PendingCount;
        if (pending > 0)
            _logger.LogWarning("Herunterfahren: {Pending} wartende Reset-Mails verworfen (erneut anfordern)", pending);
    }
}
