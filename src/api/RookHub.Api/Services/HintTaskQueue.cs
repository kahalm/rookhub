using System.Collections.Concurrent;

namespace RookHub.Api.Services;

/// <summary>
/// Eigene Queue NUR für die automatische Tipp-Generierung (je Buch-Puzzle ein Stockfish-Prozess bis 30 s plus drei
/// LLM-Aufrufe). Bewusst GETRENNT von der allgemeinen <see cref="IBackgroundTaskQueue"/> (Codereview 2026-09-29,
/// A4-002): dort reihte jeder Import einen Auftrag über ALLE Puzzles des Buchs ein, auf dem EINZIGEN allgemeinen
/// Consumer — ein persönlicher 10-MB-Kurs mit 20 000 kurzen Linien hielt ihn stundenlang fest, und Chessable-Import-
/// Tickets, Turnierkarten und Abo-Prüfungen warteten dahinter. Drei Schranken:
/// <list type="number">
/// <item>eigener Consumer (<see cref="HintTaskWorker"/>) — die allgemeine Queue blockiert nie;</item>
/// <item>höchstens EIN wartender Lauf je Buch (ein kapitelweiser Browser-Import stößt je Chunk an, der Lauf holt ohnehin
/// alle Linien ohne aktuelle Tipps); eine volle Queue VERWIRFT, statt den Import warten zu lassen — der nächste Import
/// desselben Buchs stößt erneut an;</item>
/// <item>persönliche Kurse sind gedeckelt: je Lauf <see cref="MaxPersonalPuzzlesPerRun"/>, je Besitzer und UTC-Tag
/// <see cref="MaxPersonalPuzzlesPerDay"/>. Admin-/Pool-Bücher (ohne Besitzer) bleiben ungedeckelt. Das Tagesbudget liegt
/// im Arbeitsspeicher wie die Queue selbst — ein Neustart leert beides.</item>
/// </list>
/// </summary>
public interface IHintTaskQueue : IBackgroundTaskQueue
{
    /// <summary>Tipp-Generierung für ein Buch einreihen. <paramref name="ownerUserId"/> = Besitzer eines persönlichen
    /// Kurses (entscheidet über das Tagesbudget; <c>null</c> = beim Lauf aus dem Buch lesen). <c>false</c>, wenn die
    /// Queue voll war und nichts eingereiht wurde.</summary>
    bool TryEnqueueBook(int bookId, int? ownerUserId);

    /// <summary>Nimmt bis zu <paramref name="wanted"/> Puzzles aus dem heutigen Budget des Nutzers und liefert, wie
    /// viele bewilligt sind (0, wenn es aufgebraucht ist).</summary>
    int TakeDailyBudget(int userId, int wanted);
}

public sealed class HintTaskQueue : BackgroundTaskQueue, IHintTaskQueue
{
    /// <summary>Puzzles je automatischem Lauf eines persönlichen Kurses. Bei bis zu 30 s Stockfish + drei LLM-Aufrufen je
    /// Puzzle hält ein Lauf den Tipp-Consumer damit höchstens rund eine Stunde.</summary>
    public const int MaxPersonalPuzzlesPerRun = 100;

    /// <summary>Puzzles je Besitzer und UTC-Tag (≈ 900 LLM-Aufrufe) — auch über mehrere Kurse und Läufe hinweg.</summary>
    public const int MaxPersonalPuzzlesPerDay = 300;

    private const int Capacity = 256;

    private readonly ConcurrentDictionary<int, byte> _pendingBooks = new();
    private readonly Dictionary<int, int> _usedToday = new();
    private readonly object _budgetGate = new();
    private DateOnly _budgetDay;
    private readonly TimeProvider _time;
    private readonly ILogger<HintTaskQueue>? _log;

    public HintTaskQueue(ILogger<HintTaskQueue>? logger = null, TimeProvider? time = null)
        : base(Capacity, logger, "Hint")
    {
        _log = logger;
        _time = time ?? TimeProvider.System;
    }

    public bool TryEnqueueBook(int bookId, int? ownerUserId)
    {
        if (!_pendingBooks.TryAdd(bookId, 0)) return true;   // es wartet schon ein Lauf — er holt auch diese Linien
        if (TryWrite(async (sp, ct) =>
            {
                // Ab dem Start darf ein neuer Import wieder anstoßen: was er anlegt, sieht dieser Lauf evtl. nicht mehr.
                _pendingBooks.TryRemove(bookId, out _);
                await sp.GetRequiredService<HintGenerationService>().GenerateForBookAsync(bookId, ownerUserId, this, ct);
            }))
            return true;

        _pendingBooks.TryRemove(bookId, out _);
        _log?.LogWarning("Tipp-Queue voll — Tipp-Generierung für Buch {BookId} verworfen (der nächste Import stößt sie erneut an)", bookId);
        return false;
    }

    public int TakeDailyBudget(int userId, int wanted)
    {
        if (wanted <= 0) return 0;
        lock (_budgetGate)
        {
            var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
            if (today != _budgetDay)
            {
                _usedToday.Clear();
                _budgetDay = today;
            }
            _usedToday.TryGetValue(userId, out var used);
            var granted = Math.Clamp(MaxPersonalPuzzlesPerDay - used, 0, wanted);
            if (granted > 0) _usedToday[userId] = used + granted;
            return granted;
        }
    }
}

/// <summary>Eigener Consumer der <see cref="IHintTaskQueue"/> — dieselbe Schleife wie <see cref="BackgroundTaskWorker"/>,
/// aber ohne Nachdrainen beim Herunterfahren: ein Tipp-Lauf dauert Minuten, das Zeitbudget bis zum harten Abschuss
/// (~10 s) gehört den kurzen Arbeiten der anderen Queues. Was wartet, wird nur gezählt; der nächste Import stößt es neu an.</summary>
public sealed class HintTaskWorker : BackgroundService
{
    private readonly IHintTaskQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HintTaskWorker> _logger;

    public HintTaskWorker(IHintTaskQueue queue, IServiceScopeFactory scopeFactory, ILogger<HintTaskWorker> logger)
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
                _logger.LogError(ex, "Hint task failed");
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        var pending = _queue.PendingCount;
        if (pending > 0)
            _logger.LogWarning("Herunterfahren: {Pending} wartende Tipp-Läufe verworfen (der nächste Import stößt sie erneut an)", pending);
    }
}
