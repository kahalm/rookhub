using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Analysiert den Bibliotheksbestand — die Meisterpartien — im Hintergrund. Wunsch 2026-09-28: „zu den gleichen Zeiten
/// wie die Übersetzung auch Analyse der Meisterpartien, auf allen 16 Direktengines, aber wenn ein anderer Auftrag
/// reinkommt, hat der Vorrang".
///
/// <para><b>Wann</b>: außerhalb der Sperrzeiten der Spark (<see cref="QuietHours"/>, dieselbe Angabe wie die
/// Übersetzung — Mo–Do 08–17 und Fr 08–14 gesperrt). In der Sperrzeit legt der Takt keine Partie an, und die gerade
/// laufende reiht keine Stellungen mehr ein (<c>GameAnalysisService.EnqueueNextAsync</c>); was schon eingereiht ist,
/// rechnet noch zu Ende — höchstens ein Block.</para>
///
/// <para><b>Wo</b>: auf den Hintergrund-Engines des Besitzers. Vorgabe ist der Haus-Engine-Besitzer (Admin mit „als
/// Haus-Engine teilen"), abweichend <c>MasterAnalysis:OwnerUserId</c>. Die Analysen gehören ihm, erscheinen aber in
/// keiner seiner Listen und nicht in der Reihenfolge seiner eigenen Partien (<see cref="GameAnalysisOrigin.Library"/>).</para>
///
/// <para><b>Vorrang</b>: jeder Auftrag der Meisterpartien ist Hintergrundarbeit. Der Worker nimmt ihn nur, wenn für die
/// Engine nichts anderes wartet, und ein neu eingereihter normaler Auftrag verdrängt ihn sogar, wenn er schon rechnet
/// (<see cref="IAnalysisJobControl.PreemptBackground"/>) — er geht auf Paused und rechnet danach weiter.</para>
///
/// <para><b>Menge</b>: so viel, dass alle Engines Arbeit haben — eine neue Partie erst, wenn die laufenden zusammen
/// weniger offene Stellungen haben, als Engines da sind (derselbe „Schwanz" wie in 0.543.0: sonst stünden am Ende jeder
/// Partie fast alle Engines still). Reihenfolge: zuerst die kommentierten Partien — an ihnen hängen die Erklärungen und
/// „Frag die Kommentare" —, danach die übrigen, jeweils nach Id.</para>
///
/// <para><b>Vereinspartien zuerst</b> (Wunsch 2026-09-28: „wirf die Partien aus dem Vereinsverzeichnis auch immer in die
/// Analyse"): jede Partie der LeagueHub-Vereins-Datenbank (<see cref="LeagueClubGame"/>) ohne Analyse kommt VOR der
/// nächsten Meisterpartie dran (<see cref="GameAnalysisOrigin.Club"/>) — derselbe Weg, dieselben Zeiten, derselbe Vorrang
/// jedes anderen Auftrags. Es sind wenige, und eine neu hochgeladene ist so beim nächsten Takt im Fenster eingereiht.</para>
///
/// <para>Abschalten: <c>MasterAnalysis:Enabled=false</c>. Takt <c>MasterAnalysis:TickSeconds</c> (Vorgabe 30).</para>
/// </summary>
public class MasterAnalysisScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly QuietHours _quiet;
    private readonly ILogger<MasterAnalysisScheduler> _logger;
    private readonly bool _enabled;
    private readonly int? _configuredOwner;
    private readonly TimeSpan _tick;
    private readonly TimeSpan _initialDelay;

    // Stand des Durchgangs, nur im Speicher: erst die kommentierten Partien aufsteigend nach Id, dann die übrigen. Nach
    // einem Neustart beginnt er vorn — was schon eine Analyse hat, fällt über NOT EXISTS heraus, das kostet einmal einen
    // Lauf über die Primärschlüssel.
    private bool _uncommentedPhase;
    private int _cursor;
    private readonly HashSet<int> _unplayable = [];
    private readonly HashSet<int> _unplayableClub = [];

    private readonly League.LeagueAnalysisQueue _league;
    private readonly IReadOnlySet<string> _explicitOnly;

    public MasterAnalysisScheduler(IServiceScopeFactory scopes, QuietHours quiet, IConfiguration config,
        ILogger<MasterAnalysisScheduler> logger, League.LeagueAnalysisQueue? league = null)
    {
        _league = league ?? new League.LeagueAnalysisQueue();
        _scopes = scopes;
        _quiet = quiet;
        _logger = logger;
        _enabled = config.GetValue<bool?>("MasterAnalysis:Enabled") ?? true;
        _configuredOwner = config.GetValue<int?>("MasterAnalysis:OwnerUserId");
        _explicitOnly = ExplicitOnlyEngines.From(config);
        _tick = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("MasterAnalysis:TickSeconds") ?? 30, 5, 600));
        _initialDelay = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("MasterAnalysis:InitialDelaySeconds") ?? 180, 0, 3600));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _logger.LogInformation("Meisterpartien-Analyse abgeschaltet (MasterAnalysis:Enabled=false)");
            return;
        }
        // Nach einem Deploy erst die Engines anmelden lassen, bevor die erste Partie sie anspricht.
        try { await Task.Delay(_initialDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await TickOnceAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                    scope.ServiceProvider.GetRequiredService<GameAnalysisService>(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Meisterpartien-Analyse: Takt fehlgeschlagen"); }

            try { await Task.Delay(_tick, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Ein Takt: legt höchstens EINE Partie an — eine Vereinspartie, sonst eine Meisterpartie — und liefert ihre
    /// Analyse-Id (sonst <c>null</c>).</summary>
    internal async Task<int?> TickOnceAsync(AppDbContext db, GameAnalysisService analyses, CancellationToken ct)
    {
        if (_quiet.IsQuietNow()) return null;

        var owner = await OwnerAsync(db, ct);
        if (owner is null) return null;
        var slots = Math.Max(1, ExplicitOnlyEngines.Automatic(owner.BackgroundEngines, _explicitOnly).Count);

        var open = await db.GameAnalyses
            .Where(g => (g.Origin == GameAnalysisOrigin.Library || g.Origin == GameAnalysisOrigin.Club || g.Origin == GameAnalysisOrigin.League)
                && (g.Status == GameAnalysisStatus.Pending || g.Status == GameAnalysisStatus.Running))
            .SumAsync(g => g.Positions.Count(p => p.CandidatesJson == null), ct);
        if (open >= slots) return null;

        var club = await NextClubGameAsync(db, ct);
        if (club is not null)
        {
            try
            {
                var dto = await analyses.CreateClubBatchAsync(owner.UserId, club.Id, club.Pgn, ct);
                _logger.LogInformation("Vereinspartie {LeagueClubGameId} eingereiht (Analyse {AnalysisId}, {Plies} Halbzüge)",
                    club.Id, dto.Id, dto.PlyCount);
                return dto.Id;
            }
            catch (ArgumentException ex)
            {
                _unplayableClub.Add(club.Id);
                _logger.LogWarning("Vereinspartie {LeagueClubGameId} übersprungen: {Reason}", club.Id, ex.Message);
                return null;
            }
        }

        // Liga-Partien aktueller Ligaspieler (0.665.0): nach den Vereinspartien, vor den Meisterpartien
        if (await _league.NextAsync(db, DateTime.UtcNow, ct) is { } next)
        {
            try
            {
                var dto = await analyses.CreateLeagueBatchAsync(owner.UserId, next.Item.Pgn, next.MovesHash, ct);
                _logger.LogInformation("Liga-Partie eingereiht (Analyse {AnalysisId}, {Plies} Halbzüge, Gegner nächste Runde: {Opponent})",
                    dto.Id, dto.PlyCount, next.Item.Opponent);
                return dto.Id;
            }
            catch (ArgumentException ex)
            {
                _league.Skip(next.Item.Key);
                _logger.LogWarning("Liga-Partie übersprungen: {Reason}", ex.Message);
                return null;
            }
        }

        var game = await NextGameAsync(db, ct);
        if (game is null) return null;
        _cursor = game.Id;
        try
        {
            var dto = await analyses.CreateLibraryBatchAsync(owner.UserId, game.Id, game.Pgn,
                LibraryGameService.TitleOf(game), ct);
            _logger.LogInformation("Meisterpartie {LibraryGameId} eingereiht (Analyse {AnalysisId}, {Plies} Halbzüge)",
                game.Id, dto.Id, dto.PlyCount);
            return dto.Id;
        }
        catch (ArgumentException ex)
        {
            // Unspielbares PGN: merken und weiter — sonst stünde der Stapel an dieser einen Partie.
            _unplayable.Add(game.Id);
            _logger.LogWarning("Meisterpartie {LibraryGameId} übersprungen: {Reason}", game.Id, ex.Message);
            return null;
        }
    }

    /// <summary>Der Besitzer, auf dessen Hintergrund-Engines gerechnet wird — mit mindestens einer Engine.</summary>
    private Task<LichessEngineCredential?> OwnerAsync(AppDbContext db, CancellationToken ct)
    {
        var q = db.LichessEngineCredentials.AsNoTracking()
            .Where(c => c.BackgroundEngineIds != null && c.BackgroundEngineIds != "");
        q = _configuredOwner is int id
            ? q.Where(c => c.UserId == id)
            : q.Where(c => c.ShareAsHouseEngine && c.User!.IsAdmin);
        return q.OrderBy(c => c.UserId).FirstOrDefaultAsync(ct);
    }

    /// <summary>Die nächste Vereinspartie ohne Analyse, nach Id. Ohne Zeiger: es sind wenige, und eine gelöschte oder neu
    /// hochgeladene ändert den Bestand jederzeit — der Index auf <see cref="GameAnalysis.LeagueClubGameId"/> trägt das.</summary>
    private Task<LeagueClubGame?> NextClubGameAsync(AppDbContext db, CancellationToken ct)
    {
        var q = db.LeagueClubGames.AsNoTracking().Where(c => !db.GameAnalyses.Any(a => a.LeagueClubGameId == c.Id));
        if (_unplayableClub.Count > 0) q = q.Where(c => !_unplayableClub.Contains(c.Id));
        return q.OrderBy(c => c.Id).FirstOrDefaultAsync(ct);
    }

    /// <summary>Die nächste Meisterpartie ohne Analyse: erst die kommentierten, dann die übrigen, jeweils nach Id.</summary>
    private async Task<LibraryGame?> NextGameAsync(AppDbContext db, CancellationToken ct)
    {
        while (true)
        {
            var commented = !_uncommentedPhase;
            var q = db.LibraryGames.AsNoTracking()
                .Where(g => g.Status != LibraryGameStatus.Rejected && g.Status != LibraryGameStatus.Duplicate
                    && g.Id > _cursor
                    && !db.GameAnalyses.Any(a => a.LibraryGameId == g.Id));
            q = commented
                ? q.Where(g => g.CommentedPlies > 0)
                : q.Where(g => g.CommentedPlies == null || g.CommentedPlies == 0);
            if (_unplayable.Count > 0) q = q.Where(g => !_unplayable.Contains(g.Id));

            var game = await q.OrderBy(g => g.Id).FirstOrDefaultAsync(ct);
            if (game is not null || _uncommentedPhase) return game;
            _uncommentedPhase = true;   // alle kommentierten durch — jetzt der Rest, wieder von vorn
            _cursor = 0;
        }
    }
}
