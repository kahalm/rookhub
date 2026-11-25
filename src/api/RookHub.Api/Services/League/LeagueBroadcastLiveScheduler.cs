namespace RookHub.Api.Services.League;

/// <summary>
/// Der schnelle Takt der Lichess-Übertragungen (1.017.0, Wunsch 2026-11-25: „laufende Übertragungen alle ~10 min statt alle 6 h
/// prüfen“): alle <see cref="LeagueBroadcastImport.LiveEvery"/> <see cref="LeagueBroadcastImport.RunLiveAsync"/> — nur Übertragungen
/// mit laufender Runde; alles andere (Suche, nie eingespielte, ruhende) bleibt beim <see cref="LeagueOnlineSyncScheduler"/>.
/// Lichess-schonend: derselbe benannte Client (gleicher User-Agent, <see cref="LeagueRateGateHandler"/>), und solange das Tor nach
/// einem 429 pausiert, fällt der Takt aus. Abschaltbar wie der Rest mit <c>LeagueOnline:Enabled=false</c> bzw.
/// <c>LeagueBroadcasts:Enabled=false</c>, nur den schnellen Takt mit <c>LeagueBroadcasts:Live=false</c>.
/// </summary>
public sealed class LeagueBroadcastLiveScheduler(IServiceScopeFactory scopes, IConfiguration config, LeagueRateGate gate,
    ILogger<LeagueBroadcastLiveScheduler> logger) : PeriodicWorker(scopes, logger)
{
    protected override bool Enabled => config.GetValue("LeagueOnline:Enabled", true) && config.GetValue("LeagueBroadcasts:Enabled", true)
        && config.GetValue("LeagueBroadcasts:Live", true);
    protected override WorkerSchedule Schedule { get; } = WorkerSchedule.Every(LeagueBroadcastImport.LiveEvery);
    protected override WorkerStart Start => WorkerStart.After(TimeSpan.FromMinutes(4));
    protected override void LogFailure(Exception ex) => logger.LogError(ex, "LeagueHub: schneller Takt der Lichess-Übertragungen fehlgeschlagen");

    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
    {
        if (gate.IsPaused(LeagueRateGate.Lichess)) return;
        var n = await services.GetRequiredService<LeagueBroadcastImport>().RunLiveAsync(ct);
        if (n > 0) logger.LogInformation("LeagueHub: {Count} laufende Lichess-Übertragung(en) eingespielt", n);
    }
}
