using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.EngineBroker;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Library;
using RookHub.Api.DTOs.Games;
using RookHub.Api.DTOs.Engines;

namespace RookHub.Api.Services.Games;

/// <summary>
/// „Diese ganze Partie einmal durchrechnen": zerlegt ein PGN in Halbzüge und lässt jede Stellung von
/// der Hintergrund-Engine analysieren — über die bestehenden <see cref="AnalysisJob"/>s, also mit
/// demselben Broker-Pfad, demselben Vorrang der Live-Analyse und derselben Fortsetzungs-Logik.
///
/// <para>Die Klammer drumherum macht drei Dinge, die der Einzel-Auftrag nicht kann:</para>
/// <list type="number">
/// <item><b>In Blöcken nachfüttern.</b> Eine 80-Halbzug-Partie hat 80 Stellungen, der Deckel je Nutzer
/// (<see cref="AnalysisJobService.MaxOpenJobsPerUser"/>) gilt aber für ALLE seine offenen Aufträge zusammen.
/// Es laufen deshalb höchstens <see cref="GameAnalysisDefaults.MaxOpenJobsPerGame"/> gleichzeitig, der Rest
/// rückt nach — so bleibt daneben Luft für von Hand eingereihte Stellungen.</item>
/// <item><b>Ergebnisse kopieren.</b> <c>MaxJobsPerUser</c> räumt die ältesten FERTIGEN Aufträge weg;
/// ohne Kopie hätte der Trimmer die Analyse nach zweieinhalb Partien aufgefressen.</item>
/// <item><b>Blickrichtung geradeziehen.</b> Der Broker rechnet aus Weiß-Sicht und schreibt Rochaden
/// als König-schlägt-Turm — beides wird beim Einlesen begradigt (<see cref="BrokerCandidates"/>),
/// damit spätere Auswertungen nicht jedes Mal daran denken müssen.</item>
/// </list>
///
/// <para>Seit der Zerlegung Z5 (2026-10-24) eine Fassade mit unveränderter Oberfläche: Anlegen in
/// <see cref="GameAnalysisIntake"/>, Lesen in <see cref="GameAnalysisQueries"/>, Löschen in
/// <see cref="GameAnalysisDeletion"/>, die Pumpe in <see cref="GameAnalysisPump"/> (mit <see cref="GameAnalysisIngest"/>
/// und <see cref="GameAnalysisTurns"/>; Regeln als reine Funktionen in <see cref="GameAnalysisTurnRules"/>).</para>
/// </summary>
public class GameAnalysisService
{
    private readonly GameAnalysisIntake _intake;
    private readonly GameAnalysisQueries _queries;
    private readonly GameAnalysisDeletion _deletion;
    private readonly GameAnalysisPump _pump;

    /// <param name="reviewTexts">Schreibt nach dem Ende einer Analyse die Texte zur Partie (0.540.0); ohne (Tests,
    /// Werkzeuge) passiert nichts.</param>
    /// <param name="quiet">Sperrzeiten — die Meisterpartien-Analyse (<see cref="GameAnalysisOrigin.Library"/>) reiht
    /// darin keine Stellungen ein. Ohne (Tests, Werkzeuge): nie gesperrt.</param>
    public GameAnalysisService(AppDbContext db, AnalysisJobService jobs, CommentSetService comments,
        ILogger<GameAnalysisService> logger, IGameReviewTextScheduler? reviewTexts = null, QuietHours? quiet = null)
    {
        _queries = new GameAnalysisQueries(db);
        _deletion = new GameAnalysisDeletion(db, jobs, logger);
        _pump = new GameAnalysisPump(db, jobs, logger, reviewTexts, quiet);
        _intake = new GameAnalysisIntake(db, jobs, comments, logger, _pump, _queries, _deletion);
    }

    // ===== Anlegen (GameAnalysisIntake) =====================================

    /// <inheritdoc cref="GameAnalysisIntake.CreateAsync"/>
    public Task<GameAnalysisDto> CreateAsync(int userId, CreateGameAnalysisRequest req, CancellationToken ct = default,
        GameAnalysisOrigin origin = GameAnalysisOrigin.Manual, int? engineOwnerUserId = null,
        int? libraryGameId = null, int? leagueClubGameId = null, string? movesHash = null)
        => _intake.CreateAsync(userId, req, ct, origin, engineOwnerUserId, libraryGameId, leagueClubGameId, movesHash);

    /// <inheritdoc cref="GameAnalysisIntake.CreateLibraryBatchAsync"/>
    public Task<GameAnalysisDto> CreateLibraryBatchAsync(int ownerUserId, int libraryGameId, string pgn, string title,
        CancellationToken ct = default)
        => _intake.CreateLibraryBatchAsync(ownerUserId, libraryGameId, pgn, title, ct);

    /// <inheritdoc cref="GameAnalysisIntake.CreateClubBatchAsync"/>
    public Task<GameAnalysisDto> CreateClubBatchAsync(int ownerUserId, int leagueClubGameId, string pgn,
        CancellationToken ct = default)
        => _intake.CreateClubBatchAsync(ownerUserId, leagueClubGameId, pgn, ct);

    /// <inheritdoc cref="GameAnalysisIntake.CreateLeagueBatchAsync"/>
    public Task<GameAnalysisDto> CreateLeagueBatchAsync(int ownerUserId, string pgn, string movesHash, CancellationToken ct = default)
        => _intake.CreateLeagueBatchAsync(ownerUserId, pgn, movesHash, ct);

    /// <inheritdoc cref="GameAnalysisIntake.SyncClubGameAsync"/>
    public Task SyncClubGameAsync(LeagueClubGame game, CancellationToken ct = default)
        => _intake.SyncClubGameAsync(game, ct);

    /// <inheritdoc cref="GameAnalysisIntake.RebaseClubGameAsync"/>
    public Task<int> RebaseClubGameAsync(LeagueClubGame game, CancellationToken ct = default)
        => _intake.RebaseClubGameAsync(game, ct);

    /// <inheritdoc cref="GameAnalysisIntake.CreateForGuessAsync"/>
    public Task<GuessUploadResult> CreateForGuessAsync(int userId, CreateGuessGameRequest req,
        CancellationToken ct = default, int? libraryGameId = null,
        GameAnalysisOrigin origin = GameAnalysisOrigin.Guess)
        => _intake.CreateForGuessAsync(userId, req, ct, libraryGameId, origin);

    /// <inheritdoc cref="GameAnalysisIntake.GuessUploadStatusAsync"/>
    public Task<GuessUploadStatusDto> GuessUploadStatusAsync(int userId, CancellationToken ct = default)
        => _intake.GuessUploadStatusAsync(userId, ct);

    // ===== Lesen (GameAnalysisQueries) ======================================

    /// <inheritdoc cref="GameAnalysisQueries.ListAsync"/>
    public Task<List<GameAnalysisDto>> ListAsync(int userId, CancellationToken ct = default, bool includeSavedGames = false)
        => _queries.ListAsync(userId, ct, includeSavedGames);

    /// <inheritdoc cref="GameAnalysisQueries.GetHeadUncheckedAsync"/>
    public Task<GameAnalysisDto?> GetHeadUncheckedAsync(int id, CancellationToken ct = default)
        => _queries.GetHeadUncheckedAsync(id, ct);

    /// <inheritdoc cref="GameAnalysisQueries.ListPublicAsync"/>
    public Task<List<GameAnalysisDto>> ListPublicAsync(CancellationToken ct = default, string? line = null)
        => _queries.ListPublicAsync(ct, line);

    /// <inheritdoc cref="GameAnalysisQueries.SetPublicAsync"/>
    public Task<bool?> SetPublicAsync(int userId, bool isAdmin, int id, bool isPublic, CancellationToken ct = default)
        => _queries.SetPublicAsync(userId, isAdmin, id, isPublic, ct);

    /// <inheritdoc cref="GameAnalysisQueries.ThroughputAsync"/>
    public Task<AnalysisThroughputDto> ThroughputAsync(int userId, CancellationToken ct = default)
        => _queries.ThroughputAsync(userId, ct);

    /// <inheritdoc cref="GameAnalysisQueries.GetPlayableHeadAsync"/>
    public Task<GameAnalysisDto?> GetPlayableHeadAsync(int userId, int id, CancellationToken ct = default)
        => _queries.GetPlayableHeadAsync(userId, id, ct);

    /// <inheritdoc cref="GameAnalysisQueries.ConvergenceAsync"/>
    public Task<ConvergenceDto?> ConvergenceAsync(int userId, int id, CancellationToken ct = default)
        => _queries.ConvergenceAsync(userId, id, ct);

    /// <summary>Eine eigene Analyse samt Stellungen (ohne Endstellung); <c>null</c> = gibt es nicht oder fremd.</summary>
    public Task<GameAnalysisDto?> GetAsync(int userId, int id, CancellationToken ct = default)
        => _queries.GetAsync(userId, id, ct);

    /// <summary>Höchstzahl Halbzüge, die als „dieselbe Partie" verglichen werden — wie die längste Analyse.</summary>
    public const int MaxSameGamePlies = 600;

    /// <inheritdoc cref="GameAnalysisQueries.SameGameAsync"/>
    public Task<List<GameAnalysisAlternativeDto>> SameGameAsync(int userId, IReadOnlyList<string>? ucis, CancellationToken ct = default,
        bool clubReader = false)
        => _queries.SameGameAsync(userId, ucis, ct, clubReader);

    /// <inheritdoc cref="GameAnalysisQueries.SharedAlternativesAsync"/>
    public Task<List<GameAnalysisAlternativeDto>> SharedAlternativesAsync(string? token, CancellationToken ct = default)
        => _queries.SharedAlternativesAsync(token, ct);

    /// <inheritdoc cref="GameAnalysisQueries.SharedAlternativeEvalsAsync"/>
    public Task<GameEvalsDto?> SharedAlternativeEvalsAsync(string? token, int id, CancellationToken ct = default)
        => _queries.SharedAlternativeEvalsAsync(token, id, ct);

    /// <inheritdoc cref="GameAnalysisQueries.EvalsOfAsync"/>
    public Task<GameEvalsDto?> EvalsOfAsync(int userId, int id, CancellationToken ct = default, bool clubReader = false)
        => _queries.EvalsOfAsync(userId, id, ct, clubReader);

    // ===== Neustart / Löschen ===============================================

    /// <inheritdoc cref="GameAnalysisPump.RestartAsync"/>
    /// <returns>Die Partie mit neuem Stand, oder <c>null</c>, wenn es sie nicht (mehr) gibt.</returns>
    public async Task<GameAnalysisDto?> RestartAsync(int userId, int id, CancellationToken ct = default)
        => await _pump.RestartAsync(userId, id, ct) ? await _queries.GetAsync(userId, id, ct) : null;

    /// <inheritdoc cref="GameAnalysisDeletion.DeleteAsync"/>
    public Task<bool> DeleteAsync(int userId, int id, CancellationToken ct = default)
        => _deletion.DeleteAsync(userId, id, ct);

    /// <inheritdoc cref="GameAnalysisDeletion.DeleteForClubGameAsync"/>
    public Task<int> DeleteForClubGameAsync(int leagueClubGameId, CancellationToken ct = default)
        => _deletion.DeleteForClubGameAsync(leagueClubGameId, ct);

    // ===== Pumpe (GameAnalysisPump) =========================================

    /// <inheritdoc cref="GameAnalysisPump.PumpAllAsync"/>
    public Task<int> PumpAllAsync(CancellationToken ct = default) => _pump.PumpAllAsync(ct);

    /// <inheritdoc cref="GameAnalysisPump.PumpOneAsync"/>
    public Task<bool> PumpOneAsync(int analysisId, CancellationToken ct = default) => _pump.PumpOneAsync(analysisId, ct);
}
