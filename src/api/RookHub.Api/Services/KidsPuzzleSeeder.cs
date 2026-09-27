namespace RookHub.Api.Services;

/// <summary>
/// Baut die Kinder-Leiter beim Start, wenn sie fehlt oder aus einem aelteren Lehrplan stammt
/// (<see cref="KidsCurriculum.Version"/>). Damit braucht der Deploy keinen Handgriff: die erste API mit
/// diesem Code fuellt die Tabelle selbst, auf Dev wie auf Prod, aus dem jeweils eigenen Bestand.
///
/// <para>Wartet kurz, damit der Aufbau nicht mit dem Anlauf der anderen Dienste konkurriert; die
/// Abfrage selbst dauert ein paar Sekunden (Rating-Index, ~250 000 Kandidaten). Ein Fehler kostet nur
/// die Leiter — die Kinderseite zeigt dann „noch keine Stufen", der Rest der API laeuft weiter.</para>
/// </summary>
public class KidsPuzzleSeeder : BackgroundService
{
    internal static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(20);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KidsPuzzleSeeder> _logger;

    public KidsPuzzleSeeder(IServiceScopeFactory scopeFactory, ILogger<KidsPuzzleSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, stoppingToken);
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<KidsPuzzleService>();
            if (await service.IsCurrentAsync(stoppingToken)) return;

            var result = await service.RebuildAsync(stoppingToken);
            _logger.LogInformation("Kinder-Leiter aufgebaut: {Levels} Stufen, {Puzzles} Aufgaben (Lehrplan {Version}).",
                result.Levels, result.Puzzles, KidsCurriculum.Version);
        }
        catch (OperationCanceledException)
        {
            // Herunterfahren: der naechste Start holt es nach.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kinder-Leiter: Aufbau fehlgeschlagen.");
        }
    }
}
