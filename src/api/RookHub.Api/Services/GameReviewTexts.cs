using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Die Texte zur Partie entstehen von selbst (0.540.0, Wunsch des Nutzers: „direkt nach der Analyse, damit sie gleich
/// vorhanden sind"): ist die Analyse einer eigenen Partie fertig, schreibt das Modell auf eigener Hardware ERST die
/// Fehler-Erklärungen (<see cref="GameMoveExplanationService"/>) und DANACH die drei Roasts (<see cref="GameRoastService"/>).
///
/// <para><b>Zweimal</b>: nach dem ersten, schnellen Durchgang (die Seite hat sofort etwas) und nach der Vertiefung
/// (<see cref="GameAnalysis.RefinedAt"/>) noch einmal die Erklärungen — die genauere Rechnung kann Bestzug und Klasse
/// eines Fehlers verschieben, und ein Text über den alten Bestzug wäre falsch. Dann in JEDER Sprache, die es gab.
/// Vorhandene Roasts bleiben; es entstehen nur fehlende (ein gewürfelter oder geteilter Text wird nie ersetzt).</para>
/// <para><b>Sprache</b>: die der Partie (<see cref="SavedGame.ReviewLanguage"/>, von der Seite mit „Partie analysieren"
/// mitgeschickt), sonst die zuletzt so gemerkte des Nutzers, sonst Englisch — der Server kennt die Sprache der
/// Oberfläche sonst nicht, und eine über die Erweiterung angestoßene Analyse bringt keine mit.</para>
/// <para>Nur für die Partie des BESITZERS der Analyse (eine Punktepartie ohne verknüpfte Partie bekommt nichts) und nur
/// mit einem Modell auf eigener Hardware — über Claude kostete jeder Text Geld.</para>
/// </summary>
public sealed class GameReviewTexts
{
    private readonly AppDbContext _db;
    private readonly GameMoveExplanationService _explanations;
    private readonly GameRoastService _roasts;
    private readonly GameExplanationJobs _jobs;
    private readonly ILogger<GameReviewTexts> _logger;

    public GameReviewTexts(AppDbContext db, GameMoveExplanationService explanations, GameRoastService roasts,
        GameExplanationJobs jobs, ILogger<GameReviewTexts> logger)
    {
        _db = db;
        _explanations = explanations;
        _roasts = roasts;
        _jobs = jobs;
        _logger = logger;
    }

    /// <summary>Erklärungen und fehlende Roasts zur Analyse schreiben; <paramref name="refined"/> = nach der Vertiefung
    /// (die vorhandenen Erklärungen werden dann neu geschrieben).</summary>
    public async Task WriteAsync(int analysisId, bool refined, CancellationToken ct)
    {
        if (!_explanations.Available) return;
        var analysis = await _db.GameAnalyses.AsNoTracking().Where(a => a.Id == analysisId)
            .Select(a => new { a.UserId, a.Status }).FirstOrDefaultAsync(ct);
        if (analysis == null || analysis.Status != GameAnalysisStatus.Done) return;
        var game = await _db.SavedGames.AsNoTracking()
            .Where(g => g.GameAnalysisId == analysisId && g.UserId == analysis.UserId)
            .OrderBy(g => g.Id).Select(g => new { g.Id, g.ReviewLanguage }).FirstOrDefaultAsync(ct);
        if (game == null) return;
        var explained = await _explanations.OwnGameAsync(analysis.UserId, game.Id, ct);
        if (explained == null) return;

        var language = await LanguageAsync(analysis.UserId, game.ReviewLanguage, ct);
        var languages = new List<string> { language };
        if (refined)
            languages.AddRange((await _db.GameMoveExplanations.Where(e => e.GameAnalysisId == analysisId)
                .Select(e => e.Language).Distinct().ToListAsync(ct)).Where(l => l != language));

        foreach (var lang in languages)
        {
            // Läuft für diese Sprache schon ein Durchgang (Knopf), schreibt der — nicht beide.
            if (!_jobs.TryStart(analysisId, lang)) continue;
            try
            {
                if (refined)
                {
                    _db.GameMoveExplanations.RemoveRange(await _db.GameMoveExplanations
                        .Where(e => e.GameAnalysisId == analysisId && e.Language == lang).ToListAsync(ct));
                    await _db.SaveChangesAsync(ct);
                }
                await _explanations.GenerateAsync(explained, lang, ct);
            }
            finally
            {
                _jobs.Finish(analysisId, lang);
            }
        }

        foreach (var style in GameRoastService.Styles)
        {
            var result = await _roasts.RoastAsync(analysis.UserId, game.Id, style, language, ct, automatic: true);
            if (result.Reason is not (null or "exists"))
                _logger.LogInformation("Roast ({Style}) zu Partie {GameId} nicht geschrieben: {Reason}", style, game.Id, result.Reason);
        }
    }

    /// <summary>Die Sprache der Partie, sonst die zuletzt gemerkte des Nutzers, sonst Englisch.</summary>
    private async Task<string> LanguageAsync(int userId, string? gameLanguage, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(gameLanguage)) return GameMoveExplanationService.NormalizeLanguage(gameLanguage);
        var last = await _db.SavedGames.AsNoTracking()
            .Where(g => g.UserId == userId && g.ReviewLanguage != null)
            .OrderByDescending(g => g.Id).Select(g => g.ReviewLanguage).FirstOrDefaultAsync(ct);
        return GameMoveExplanationService.NormalizeLanguage(last);
    }
}

/// <summary>Der Auslöser von <see cref="GameReviewTexts"/> — für die Pumpe (<see cref="GameAnalysisService"/>) eine
/// Schnittstelle, damit sie weder an den Texten hängt (die hängen über <see cref="SavedGameService"/> wieder an ihr)
/// noch in Tests ein Sprachmodell braucht.</summary>
public interface IGameReviewTextScheduler
{
    void Schedule(int analysisId, bool refined);
}

/// <summary>Schreibt im Hintergrund, in einem eigenen Scope — die Pumpe wartet nicht auf das Modell (15 Erklärungen und
/// drei Roasts brauchen auch auf eigener Hardware eine Minute). Ein Neustart verwirft den Lauf; der Knopf „Fehler erklären
/// lassen" bzw. „Roast my game" holt dann nach.</summary>
public sealed class GameReviewTextScheduler : IGameReviewTextScheduler
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<GameReviewTextScheduler> _logger;

    public GameReviewTextScheduler(IServiceScopeFactory scopes, ILogger<GameReviewTextScheduler> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public void Schedule(int analysisId, bool refined)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<GameReviewTexts>().WriteAsync(analysisId, refined, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Texte zur Analyse {AnalysisId} (vertieft: {Refined}) gescheitert", analysisId, refined);
            }
        });
    }
}
