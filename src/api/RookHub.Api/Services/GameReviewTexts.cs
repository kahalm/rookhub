using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Die Texte zur Partie entstehen von selbst (0.540.0, Wunsch des Nutzers: „direkt nach der Analyse, damit sie gleich
/// vorhanden sind"): ist die Analyse einer eigenen Partie fertig, schreibt das Modell auf eigener Hardware die
/// Nacherzählung (<see cref="GameRecapService"/>, seit 0.541.0 — EIN Aufruf, und sie steht in der Link-Vorschau: wer gleich
/// nach der Analyse teilt, hat sie schon), dann die Fehler-Erklärungen (<see cref="GameMoveExplanationService"/>) und
/// DANACH die drei Roasts (<see cref="GameRoastService"/>).
///
/// <para><b>Zweimal</b>: nach dem ersten, schnellen Durchgang (die Seite hat sofort etwas) und nach der Vertiefung
/// (<see cref="GameAnalysis.RefinedAt"/>) noch einmal die Erklärungen — die genauere Rechnung kann Bestzug und Klasse
/// eines Fehlers verschieben, und ein Text über den alten Bestzug wäre falsch. Dann in JEDER Sprache, die es gab — ebenso
/// die Nacherzählung (ihr Wendepunkt kann wandern).
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
    private readonly GameRecapService _recaps;
    private readonly GameExplanationJobs _jobs;
    private readonly ILogger<GameReviewTexts> _logger;

    public GameReviewTexts(AppDbContext db, GameMoveExplanationService explanations, GameRoastService roasts,
        GameRecapService recaps, GameExplanationJobs jobs, ILogger<GameReviewTexts> logger)
    {
        _db = db;
        _explanations = explanations;
        _roasts = roasts;
        _recaps = recaps;
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
        var recapLanguages = new List<string> { language };
        if (refined)
            recapLanguages.AddRange((await _db.GameRecaps.Where(r => r.SavedGameId == game.Id)
                .Select(r => r.Language).Distinct().ToListAsync(ct)).Where(l => l != language));
        foreach (var lang in recapLanguages)
            await RecapAsync(analysis.UserId, game.Id, lang, replace: refined, ct);

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

    /// <summary>Nur die Nacherzählung — für eine Analyse von VOR 0.541.0, beim Öffnen der eigenen Partie angestoßen
    /// (<c>GET /api/games/{id}/recap</c>). Schreibt nichts, was schon da ist.</summary>
    public async Task WriteRecapAsync(int savedGameId, CancellationToken ct)
    {
        if (!_recaps.Available) return;
        var game = await _db.SavedGames.AsNoTracking().Where(g => g.Id == savedGameId)
            .Select(g => new { g.Id, g.UserId, g.GameAnalysisId, g.ReviewLanguage }).FirstOrDefaultAsync(ct);
        if (game?.GameAnalysisId is null) return;
        await RecapAsync(game.UserId, game.Id, await LanguageAsync(game.UserId, game.ReviewLanguage, ct), replace: false, ct);
    }

    /// <summary>Eine Nacherzählung — nicht zweimal gleichzeitig für dieselbe Partie und Sprache (Nachtrag beim Öffnen und
    /// Lauf nach der Analyse können sich treffen). Der Schlüssel in <see cref="GameExplanationJobs"/> trägt die Partie-Id
    /// und das Präfix <c>recap:</c> — mit den Einträgen der Erklärungen (Analyse-Id + Sprache) kann er nie zusammenfallen.</summary>
    private async Task RecapAsync(int userId, int gameId, string lang, bool replace, CancellationToken ct)
    {
        var key = "recap:" + lang;
        if (!_jobs.TryStart(gameId, key)) return;
        try
        {
            var result = await _recaps.WriteAsync(userId, gameId, lang, replace, ct);
            if (result.Reason is not (null or "exists"))
                _logger.LogInformation("Nacherzählung zu Partie {GameId} nicht geschrieben: {Reason}", gameId, result.Reason);
        }
        finally
        {
            _jobs.Finish(gameId, key);
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

    /// <summary>Nur die Nacherzählung einer Partie nachtragen (<see cref="GameReviewTexts.WriteRecapAsync"/>).</summary>
    void ScheduleRecap(int savedGameId);
}

/// <summary>Schreibt im Hintergrund, in einem eigenen Scope — die Pumpe wartet nicht auf das Modell (15 Erklärungen und
/// drei Roasts brauchen auch auf eigener Hardware eine Minute). Ein Neustart verwirft den Lauf; der Knopf „Fehler erklären
/// lassen" bzw. „Roast my game" holt dann nach.
///
/// <para><b>Sperrzeiten</b> (<see cref="QuietHours"/>, 0.546.0): endet eine Analyse, während die Spark anderen gehört, wird
/// sie ZURÜCKGESTELLT und nach dem Fenster geschrieben (ein Wartender je Prozess, in Schritten von höchstens zehn
/// Minuten). Zurückgestellt wird nur im Arbeitsspeicher: ein Neustart dazwischen verliert die Liste — dann holen die
/// Knöpfe bzw. der Abruf der Nacherzählung beim Öffnen der Partie nach. Der nächtliche Neustart (Watchtower ~02:00)
/// liegt außerhalb jedes Fensters. Die Nacherzählung beim Öffnen (<see cref="ScheduleRecap"/>) wartet NICHT: sie wird beim
/// nächsten Öffnen außerhalb der Sperrzeit ohnehin angestoßen.</para></summary>
public class GameReviewTextScheduler : IGameReviewTextScheduler
{
    private readonly IServiceScopeFactory _scopes;
    private readonly QuietHours? _quiet;
    private readonly ILogger<GameReviewTextScheduler> _logger;
    /// <summary>Zurückgestellt: Analyse → „nach der Vertiefung" (ODER — die Vertiefung schreibt mehr neu).</summary>
    private readonly ConcurrentDictionary<int, bool> _deferred = new();
    private int _waiting;

    public GameReviewTextScheduler(IServiceScopeFactory scopes, ILogger<GameReviewTextScheduler> logger, QuietHours? quiet = null)
    {
        _scopes = scopes;
        _logger = logger;
        _quiet = quiet;
    }

    internal int DeferredCount => _deferred.Count;

    public void Schedule(int analysisId, bool refined)
    {
        if (_quiet?.IsQuietNow() == true)
        {
            _deferred.AddOrUpdate(analysisId, refined, (_, old) => old || refined);
            _logger.LogInformation("Texte zur Analyse {AnalysisId} zurückgestellt (Sperrzeit der Spark bis {Until})",
                analysisId, _quiet.QuietUntil());
            StartWaiting();
            return;
        }
        Execute(analysisId, refined);
    }

    public void ScheduleRecap(int savedGameId)
    {
        if (_quiet?.IsQuietNow() == true) return;
        ExecuteRecap(savedGameId);
    }

    /// <summary>Alles Zurückgestellte jetzt anstoßen — der Wartende ruft es nach dem Fenster (Tests direkt).</summary>
    internal void ReleaseDeferred()
    {
        foreach (var id in _deferred.Keys.ToList())
            if (_deferred.TryRemove(id, out var refined)) Schedule(id, refined);
    }

    private void StartWaiting()
    {
        if (Interlocked.Exchange(ref _waiting, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                while (_quiet?.QuietUntil() is { } until)
                {
                    var wait = until - _quiet.Now;
                    await Task.Delay(wait < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5)
                        : wait > TimeSpan.FromMinutes(10) ? TimeSpan.FromMinutes(10) : wait);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Warten auf das Ende der Sperrzeit gescheitert");
            }
            finally
            {
                Interlocked.Exchange(ref _waiting, 0);
            }
            ReleaseDeferred();
        });
    }

    internal virtual void Execute(int analysisId, bool refined)
        => Run(t => t.WriteAsync(analysisId, refined, CancellationToken.None),
            ex => _logger.LogWarning(ex, "Texte zur Analyse {AnalysisId} (vertieft: {Refined}) gescheitert", analysisId, refined));

    internal virtual void ExecuteRecap(int savedGameId)
        => Run(t => t.WriteRecapAsync(savedGameId, CancellationToken.None),
            ex => _logger.LogWarning(ex, "Nacherzählung zu Partie {GameId} gescheitert", savedGameId));

    private void Run(Func<GameReviewTexts, Task> work, Action<Exception> failed)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await work(scope.ServiceProvider.GetRequiredService<GameReviewTexts>());
            }
            catch (Exception ex)
            {
                failed(ex);
            }
        });
    }
}
