namespace RookHub.Api.Services.Prep;

/// <summary>
/// Rechnet eine unvollständige Schätzung der Trainingslinien im Hintergrund fertig (Wunsch 2026-10-08: „warum brauch ich da 4 mal
/// weiterrechnen?"): antwortet eine Abfrage mit offenen Explorer-Stellungen, startet <see cref="TrainingLinesService"/> hier einen
/// Durchlauf für genau diese Rechnung (Cache-Schlüssel), der die restlichen Stellungen abfragt und am Ende das vollständige Ergebnis
/// in den Speicher legt. Ohne Anfrage-Token, mit eigenem Zeitlimit (<see cref="Limit"/>); höchstens EIN Durchlauf je Nutzer
/// gleichzeitig. Singleton; die Arbeit läuft in einem eigenen DI-Bereich (<see cref="IServiceScopeFactory"/>).
/// </summary>
public sealed class TrainingLinesContinuation(IServiceScopeFactory scopes, ILogger<TrainingLinesContinuation> logger)
{
    /// <summary>So lange darf ein Durchlauf höchstens laufen.</summary>
    public TimeSpan Limit { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>So viele gleichzeitige Explorer-Abfragen — weniger als eine Anfrage im Vordergrund, damit der Lochfinder nicht leidet.</summary>
    public const int Parallelism = 8;

    private readonly object _lock = new();
    /// <summary>Nutzer → Schlüssel der Rechnung, die gerade für ihn läuft.</summary>
    private readonly Dictionary<int, string> _running = new();

    /// <summary>So viele Durchläufe laufen gerade (über alle Nutzer).</summary>
    public int RunningCount
    {
        get { lock (_lock) return _running.Count; }
    }

    /// <summary>Läuft für diese Rechnung gerade ein Durchlauf?</summary>
    public bool IsRunning(string key)
    {
        lock (_lock) return _running.ContainsValue(key);
    }

    /// <summary>Startet <paramref name="work"/> im Hintergrund, wenn für den Nutzer gerade nichts läuft → gestartet?</summary>
    /// <param name="work">Bekommt den DI-Bereich des Durchlaufs, das Zeitlimit-Token und die Frist (<see cref="Limit"/>).</param>
    public bool TryStart(int userId, string key, Func<IServiceProvider, TimeSpan, CancellationToken, Task> work)
    {
        lock (_lock)
        {
            if (_running.ContainsKey(userId)) return false;
            _running[userId] = key;
        }
        var limit = Limit;
        _ = Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(limit);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var scope = scopes.CreateScope();
                await work(scope.ServiceProvider, limit, timeout.Token);
                logger.LogInformation("Trainingslinien: Fortsetzung für Nutzer {User} fertig nach {Ms} ms", userId, clock.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Trainingslinien: Fortsetzung für Nutzer {User} nach dem Zeitlimit {Limit} beendet", userId, limit);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Trainingslinien: Fortsetzung für Nutzer {User} gescheitert", userId);
            }
            finally
            {
                lock (_lock) _running.Remove(userId);
            }
        });
        return true;
    }
}
