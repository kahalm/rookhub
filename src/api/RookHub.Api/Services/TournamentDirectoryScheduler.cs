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
