using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Jede Vereinspartie bekommt neben der Stockfish-Analyse eine zweite auf einer festen Engine (0.684.0, Wunsch 2026-10-06:
/// „alle aktuellen Ligapartien, die neu dazukommen, automatisch mit 100k rechnen, und einmalig alle alten nachrechnen").
///
/// <list type="bullet">
/// <item>Die Engine heißt per Konfiguration (<c>ClubSecondEngine:EngineName</c>, z. B. „RookHub Spark Lc0") — nicht per Kennung,
/// die wechselt beim Neuanmelden. Besitzer = das Konto, unter dem genau EINE Registrierung so heißt.</item>
/// <item>Die Analyse ist <see cref="GameAnalysisOrigin.Club"/> MIT <c>EngineId</c> und Knotenziel — daran erkennt jeder Leser,
/// dass sie die ZWEITE ist (Leser fragen <c>EngineId == null</c> für die Stockfish-Analyse); die Stockfish-Analyse bleibt die der Partie.</item>
/// <item>Neueste Vereinspartien zuerst (neu Hochgeladenes wartet nicht hinter dem Altbestand), höchstens
/// <c>ClubSecondEngine:MaxOpen</c> zugleich — die Engine rechnet je Stellung ohnehin nur einen Auftrag.</item>
/// <item><c>ClubSecondEngine:EngineName</c> leer = aus.</item>
/// </list>
/// </summary>
public sealed class ClubSecondEngineScheduler(IServiceScopeFactory scopes, IConfiguration config,
    ILogger<ClubSecondEngineScheduler> logger) : PeriodicWorker(scopes, logger)
{
    /// <summary>Vorgabe: die Lc0-Engine auf der Spark. Leer gesetzt = aus.</summary>
    public const string DefaultEngineName = "RookHub Spark Lc0";
    private readonly string? _engineName = (config["ClubSecondEngine:EngineName"] ?? DefaultEngineName).Trim() is { Length: > 0 } n ? n : null;
    private readonly long _nodes = Math.Clamp(config.GetValue<long?>("ClubSecondEngine:TargetNodes") ?? 100_000,
        AnalysisJobService.MinTargetNodes, AnalysisJobService.MaxTargetNodes);
    private readonly int _multiPv = Math.Clamp(config.GetValue<int?>("ClubSecondEngine:MultiPv") ?? 3, 1, AnalysisJobService.MaxMultiPv);
    private readonly int _maxOpen = Math.Clamp(config.GetValue<int?>("ClubSecondEngine:MaxOpen") ?? 1, 1, 10);
    /// <summary>Partien, die sich nicht anlegen ließen — bis zum Neustart nicht erneut versuchen.</summary>
    private readonly HashSet<int> _unplayable = [];

    protected override bool Enabled => _engineName is not null;
    protected override WorkerSchedule Schedule { get; } = WorkerSchedule.Every(TimeSpan.FromMinutes(1));
    protected override WorkerStart Start => WorkerStart.After(TimeSpan.FromMinutes(3));
    protected override void LogFailure(Exception ex) => logger.LogError(ex, "Zweite Engine für Vereinspartien: Takt fehlgeschlagen");
    protected override void LogDisabled() => logger.LogInformation("Zweite Engine für Vereinspartien aus (ClubSecondEngine:EngineName leer)");

    protected override async Task StepAsync(IServiceProvider services, CancellationToken ct)
        => await TickOnceAsync(services.GetRequiredService<AppDbContext>(), services.GetRequiredService<GameAnalysisService>(), ct);

    /// <summary>Ein Takt: legt höchstens EINE Analyse an und liefert ihre Id (sonst <c>null</c>).</summary>
    internal async Task<int?> TickOnceAsync(AppDbContext db, GameAnalysisService analyses, CancellationToken ct)
    {
        if (_engineName is null) return null;
        var regs = await db.ExternalEngineRegistrations.AsNoTracking()
            .Where(r => r.Name == _engineName).Select(r => new { r.Id, r.UserId }).ToListAsync(ct);
        if (regs.Count != 1)
        {
            if (regs.Count > 1) logger.LogWarning("Zweite Engine für Vereinspartien: {Count} Registrierungen heißen {Engine}", regs.Count, _engineName);
            return null;
        }
        var reg = regs[0];

        var open = await db.GameAnalyses.CountAsync(a => a.Origin == GameAnalysisOrigin.Club && a.EngineId == reg.Id
            && (a.Status == GameAnalysisStatus.Pending || a.Status == GameAnalysisStatus.Running), ct);
        if (open >= _maxOpen) return null;

        var q = db.LeagueClubGames.AsNoTracking()
            .Where(c => !db.GameAnalyses.Any(a => a.LeagueClubGameId == c.Id && a.Origin == GameAnalysisOrigin.Club
                && a.EngineId == reg.Id && a.Status != GameAnalysisStatus.Failed));
        if (_unplayable.Count > 0) q = q.Where(c => !_unplayable.Contains(c.Id));
        var game = await q.OrderByDescending(c => c.Id).Select(c => new { c.Id, c.Pgn }).FirstOrDefaultAsync(ct);
        if (game is null) return null;

        try
        {
            var dto = await analyses.CreateAsync(reg.UserId, new CreateGameAnalysisRequest
            {
                Pgn = game.Pgn,
                TargetDepth = GameAnalysisDefaults.GuessTargetDepth,
                MultiPv = _multiPv,
                EngineId = reg.Id,
                TargetNodes = _nodes,
            }, ct, GameAnalysisOrigin.Club, engineOwnerUserId: reg.UserId, leagueClubGameId: game.Id);
            logger.LogInformation("Vereinspartie {LeagueClubGameId} für {Engine} eingereiht (Analyse {AnalysisId}, {Nodes} Knoten)",
                game.Id, _engineName, dto.Id, _nodes);
            return dto.Id;
        }
        catch (ArgumentException ex)
        {
            _unplayable.Add(game.Id);
            logger.LogWarning("Vereinspartie {LeagueClubGameId} für die zweite Engine übersprungen: {Reason}", game.Id, ex.Message);
            return null;
        }
    }
}
