using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Repertoires;

namespace RookHub.Api.Services.Prep;

/// <summary>
/// Nächtliches Vorrechnen der Schätzung (0.884.0, Prod 07.11.: ein Gegner mit 4 177 Lücken brauchte 162 s, jedes Deploy leerte den
/// Speicher): für jeden Nutzer mit markierten Repertoires (<c>UseForExtension</c>) alle Stellungen, die eine Schätzung brauchen kann
/// (<see cref="TrainingLinesService.PrewarmPositionsAsync"/>), für JEDE Stufenkombination, die das Band ergeben kann
/// (<see cref="TrainingExplorer.StageSets"/> aus <see cref="LocalExplorerClient.LocalRatings"/>), fehlende Werte aus dem lokalen
/// Explorer holen und dauerhaft speichern (<see cref="ITrainingExplorer.PrewarmAsync"/>; vorhandene werden übersprungen — ein
/// abgebrochener Lauf macht in der nächsten Nacht dort weiter). Je Nutzer und Kombination eine INFO-Zeile. Zwischen den Portionen
/// wird der Admin-Schalter „Hintergrundjobs pausieren" gefragt; pausiert → Ende. Gestartet von <see cref="TrainingPrewarmScheduler"/>.
/// </summary>
public sealed class TrainingPrewarm(AppDbContext db, TrainingLinesService lines, ITrainingExplorer explorer, IConfiguration config,
    ILogger<TrainingPrewarm> logger, BackgroundPause? pause = null, RepertoireExplorerService? store = null)
{
    public const string ParallelismKey = "Prep:TrainingPrewarmParallelism";
    public const int DefaultParallelism = 8;
    /// <summary>So viele Stellungen je Portion — dazwischen Pause-Prüfung und Schreiben.</summary>
    public const int Chunk = 1000;

    public int Parallelism => int.TryParse(config[ParallelismKey], out var n) ? Math.Clamp(n, 1, 64) : DefaultParallelism;

    public enum Outcome { Complete, Paused, Unavailable }

    public sealed record Summary(Outcome Outcome, int Users, int Positions, int Present, int Fetched, int Failed);

    /// <param name="ct">Fensterende oder Herunterfahren — wirft dann <see cref="OperationCanceledException"/> (Geholtes ist gespeichert).</param>
    public async Task<Summary> RunAsync(CancellationToken ct)
    {
        if (!explorer.Available) return new Summary(Outcome.Unavailable, 0, 0, 0, 0, 0);
        if (store is not null)
        {
            var purged = await store.PurgeLocalAsync(ct);
            if (purged > 0) logger.LogInformation("Trainingslinien vorrechnen: {Count} abgelaufene Stellungen gelöscht", purged);
        }
        var users = await db.Repertoires.AsNoTracking().Where(r => r.UseForExtension).Select(r => r.UserId).Distinct()
            .OrderBy(id => id).ToListAsync(ct);
        var sets = TrainingExplorer.StageSets(LocalExplorerClient.LocalRatings);
        var parallelism = Parallelism;
        int positions = 0, present = 0, fetched = 0, failed = 0;
        foreach (var user in users)
        {
            var nodes = await lines.PrewarmPositionsAsync(user, ct);
            positions += nodes.Count;
            if (nodes.Count == 0) continue;
            foreach (var stages in sets)
            {
                var clock = Stopwatch.StartNew();
                int p = 0, f = 0, x = 0;
                foreach (var chunk in nodes.Chunk(Chunk))
                {
                    ct.ThrowIfCancellationRequested();
                    if (pause is not null && await pause.IsPausedAsync(ct))
                    {
                        logger.LogInformation("Trainingslinien vorrechnen: Hintergrundjobs pausiert — Abbruch bei Nutzer {User}, Stufen {Stages}",
                            user, string.Join(',', stages));
                        return new Summary(Outcome.Paused, users.Count, positions, present + p, fetched + f, failed + x);
                    }
                    var r = await explorer.PrewarmAsync(chunk, stages, parallelism, ct);
                    p += r.Present;
                    f += r.Fetched;
                    x += r.Failed;
                }
                present += p;
                fetched += f;
                failed += x;
                logger.LogInformation(
                    "Trainingslinien vorrechnen: Nutzer {User}, Stufen {Stages} — {Positions} Stellungen, {Present} vorhanden, {Fetched} geholt, "
                    + "{Failed} ohne Antwort, {Ms} ms", user, string.Join(',', stages), nodes.Count, p, f, x, clock.ElapsedMilliseconds);
            }
        }
        return new Summary(Outcome.Complete, users.Count, positions, present, fetched, failed);
    }
}

/// <summary>
/// Takt des Vorrechnens (<see cref="TrainingPrewarm"/>): alle 5 min nachsehen, gerechnet wird nur im Fenster
/// <c>Prep:TrainingPrewarmWindow</c> (Vorgabe „03:30-05:30", Zeitzone <c>Prep:TrainingPrewarmTimeZone</c>, Vorgabe Europe/Vienna)
/// und höchstens bis zu dessen Ende; ist eine Nacht vollständig durch, ruht der Dienst bis zur nächsten. Schalter
/// <c>Prep:TrainingPrewarm</c> (Vorgabe an); ohne <c>LichessExplorer:LocalUrl</c> läuft er nie. Pausierbar (Admin-Schalter).
/// </summary>
public sealed class TrainingPrewarmScheduler : PeriodicWorker
{
    public const string EnabledKey = "Prep:TrainingPrewarm";
    public const string WindowKey = "Prep:TrainingPrewarmWindow";
    public const string TimeZoneKey = "Prep:TrainingPrewarmTimeZone";
    public const string DefaultWindow = "03:30-05:30";

    private readonly IConfiguration _config;
    private readonly TimeProvider _time;
    private DateOnly? _doneFor;

    public TrainingPrewarmScheduler(IServiceScopeFactory scopeFactory, ILogger<TrainingPrewarmScheduler> logger, IConfiguration config,
        TimeProvider? time = null) : base(scopeFactory, logger)
    {
        _config = config;
        _time = time ?? TimeProvider.System;
    }

    protected override WorkerSchedule Schedule { get; } = WorkerSchedule.Every(TimeSpan.FromMinutes(5));
    protected override WorkerStart Start => WorkerStart.After(TimeSpan.FromMinutes(1));
    protected override void LogFailure(Exception ex) => Logger.LogError(ex, "Trainingslinien vorrechnen: Lauf fehlgeschlagen");

    protected override bool Enabled => ShouldRun(_config);

    /// <summary>Schalter an (Vorgabe) UND ein lokaler Explorer eingerichtet.</summary>
    internal static bool ShouldRun(IConfiguration config) =>
        !string.Equals(config[EnabledKey], "false", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(config[LocalExplorerClient.ConfigKey]);

    protected override void LogDisabled() => Logger.LogInformation(
        "Trainingslinien vorrechnen: aus (Schalter {Key} oder kein {Local})", EnabledKey, LocalExplorerClient.ConfigKey);

    /// <summary>Ende des Fensters, in dem <paramref name="nowUtc"/> liegt (UTC), sonst <c>null</c>; dazu das Ortsdatum der Nacht.
    /// Fenster „HH:mm-HH:mm" am selben Tag; Unlesbares → Vorgabe.</summary>
    public static (DateTimeOffset End, DateOnly Night)? WindowEnd(DateTimeOffset nowUtc, string? window, string? timeZone)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(timeZone) ? QuietHours.DefaultTimeZone : timeZone.Trim());
        if (!TryParse(window, out var from, out var to)) TryParse(DefaultWindow, out from, out to);
        var local = TimeZoneInfo.ConvertTime(nowUtc, zone);
        if (local.TimeOfDay < from || local.TimeOfDay >= to) return null;
        var endLocal = local.Date + to;
        var end = new DateTimeOffset(endLocal, zone.GetUtcOffset(endLocal));
        return (end, DateOnly.FromDateTime(local.Date));
    }

    private static bool TryParse(string? window, out TimeSpan from, out TimeSpan to)
    {
        from = to = default;
        var parts = (window ?? "").Split('-', StringSplitOptions.TrimEntries);
        return parts.Length == 2
               && TimeSpan.TryParseExact(parts[0], @"hh\:mm", CultureInfo.InvariantCulture, out from)
               && TimeSpan.TryParseExact(parts[1], @"hh\:mm", CultureInfo.InvariantCulture, out to)
               && from < to;
    }

    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        if (WindowEnd(now, _config[WindowKey], _config[TimeZoneKey]) is not { } w || _doneFor == w.Night) return;
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(w.End - now);
        try
        {
            var s = await services.GetRequiredService<TrainingPrewarm>().RunAsync(window.Token);
            Logger.LogInformation(
                "Trainingslinien vorrechnen: {Outcome} — {Users} Nutzer, {Positions} Stellungen, {Present} vorhanden, {Fetched} geholt, {Failed} ohne Antwort",
                s.Outcome, s.Users, s.Positions, s.Present, s.Fetched, s.Failed);
            if (s.Outcome == TrainingPrewarm.Outcome.Complete) _doneFor = w.Night;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Logger.LogInformation("Trainingslinien vorrechnen: Fensterende erreicht — nächste Nacht geht es weiter");
        }
    }
}
