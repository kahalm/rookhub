using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Services.Chessable;

namespace RookHub.Api.Services.Puzzles;

/// <summary>
/// Räumt ANONYME Spielstände auf, die niemand mehr abholen kann. Der anonyme Endless-Pfad ist
/// bewusst offen (ohne Konto spielen), und die Session-Id ist ein frei wählbares Feld des Requests:
/// jede neue Kennung legt eine eigene Zeile an — <c>ActiveGameState</c> ist LONGTEXT. Diese Retention
/// ist der RÜCKBAU; die Schranke gegen das Vollschreiben sind die Gesamtdeckel
/// (<see cref="EndlessProgressService.MaxAnonymousProgressRowsTotal"/>,
/// <see cref="EndlessProgressService.MaxAnonymousSessionRowsTotal"/>) und der engere Deckel des anonymen
/// Spielstands (<see cref="DTOs.Puzzles.SaveAnonymousProgressDto.MaxActiveGameStateLength"/>) — ein Verfall
/// nach 60 Tagen allein hätte ein Skript mit frischen Session-Ids nicht gebremst. Die Zeilen sind nach
/// dem Spielen wertlos: ein Rückkehrer bringt seine Session-Id im Browser-Speicher mit, und wer sich
/// anmeldet, übernimmt sie sofort (<c>POST /api/endless/claim-session</c>).
///
/// <para>Dazu die anonyme getReview-Senke (<c>AnonymousChessableReviewLines</c>, offener Endpunkt
/// <c>/api/extension/chessable/review-lines/anon</c>): ungeclaimte Zeilen nach
/// <see cref="AnonymousReviewLineMaxAge"/>, die von uids ohne verknüpftes Konto schon nach
/// <see cref="UnlinkedAnonymousReviewLineMaxAge"/>. Die lief bis 2026-09-30 im nächtlichen
/// Kurslisten-Refresh — und der ist nur mit <c>Chessable:Enabled=true</c> registriert: auf PROD (Schalter
/// seit 2026-09-09 aus) wurde keine Zeile mehr gelöscht, obwohl der Extension-Endpunkt weiter annimmt und
/// PRIVACY.md 90 Tage zusagt. Dieser Dienst läuft IMMER.</para>
///
/// <para>Dazu die vier anonymen Versuchs-Senken (<see cref="AnonymousSinkRetention"/>, A2-012): Frist
/// <c>Retention:AnonymousDays</c> und Gesamtdeckel <c>Retention:AnonymousCap</c> je Senke (<see cref="RetentionOptions"/>).</para>
///
/// <para>Und gelesene Admin-/Direktnachrichten nach <c>Retention:MessagesDays</c> (<see cref="Social.AdminMessageRetention"/>),
/// Formular-Fotos (<see cref="Scoresheet.ScoresheetPhotoRetention"/>: gescheiterte nach 30, offene ohne Konto nach 14 Tagen,
/// Archiv nach Ablauf).</para>
///
/// Läuft täglich (PeriodicWorker, erster Lauf kurz nach dem Start, nicht pausierbar: Aufbewahrungsfristen); jede Senke hat
/// ihren eigenen Versuch — ein Fehler der einen lässt die andere nicht aus und beendet den Dienst nicht (nur Logzeile).
/// Übersicht aller Fristen: <c>docs/api/speicherdauer.md</c>.
/// </summary>
public class AnonymousDataRetentionService : PeriodicWorker
{
    /// <summary>Anonyme Endless-Zeilen ohne Berührung verfallen danach. Großzügig gewählt: ein
    /// Gelegenheitsspieler soll seinen Lauf auch nach zwei Monaten Pause noch vorfinden.</summary>
    public static readonly TimeSpan AnonymousEndlessMaxAge = TimeSpan.FromDays(60);

    /// <summary>Ungeclaimte anonyme getReview-Linien verfallen danach (Zusage in RepCheck-PRIVACY.md).</summary>
    public static readonly TimeSpan AnonymousReviewLineMaxAge = TimeSpan.FromDays(90);

    /// <summary>Kürzere Frist für uids OHNE verknüpftes Konto: nicht claimbar, also der Vorrats-Topf, den der offene
    /// Endpunkt füllen kann (der legitime Weg dauert Tage, nicht Monate).</summary>
    public static readonly TimeSpan UnlinkedAnonymousReviewLineMaxAge = TimeSpan.FromDays(14);

    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnonymousDataRetentionService> _logger;

    public AnonymousDataRetentionService(IServiceScopeFactory scopeFactory,
        ILogger<AnonymousDataRetentionService> logger) : base(scopeFactory, logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Uhr des Laufs (Tests setzen eine feste).</summary>
    internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    protected override WorkerSchedule Schedule { get; } = WorkerSchedule.Every(Interval);
    /// <summary>Wie bisher gleich beim Start: der Rückstand soll nach einem Deploy nicht einen Tag stehen bleiben.</summary>
    protected override WorkerStart Start => WorkerStart.Immediately;
    /// <summary>Nicht pausierbar: Aufbewahrungsfristen (Datenschutz) und Speicherschutz.</summary>
    protected override bool Pausable => false;
    protected override void LogFailure(Exception ex) => _logger.LogError(ex, "Anonyme Retention fehlgeschlagen");

    protected override Task StepAsync(IServiceProvider services, CancellationToken ct) => RunOnceAsync(ct);

    /// <summary>Ein Durchlauf; liefert die Zahl gelöschter Zeilen (für Tests/Logs). Jede Senke in einem eigenen Versuch
    /// und eigenen Scope: scheitert die eine, laufen die anderen trotzdem.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var now = Clock();
        var removed = 0;
        removed += await TryAsync("Anon-getReview-Retention fehlgeschlagen", async sp =>
        {
            var reviewLines = sp.GetRequiredService<ChessableReviewLineService>();
            var pruned = await reviewLines.PruneAnonOlderThanAsync(AnonymousReviewLineMaxAge, ct);
            pruned += await reviewLines.PruneUnlinkedAnonOlderThanAsync(UnlinkedAnonymousReviewLineMaxAge, ct);
            if (pruned > 0)
                _logger.LogInformation("Anon-getReview-Retention: {Count} ungeclaimte Zeilen gelöscht", pruned);
            return pruned;
        }, ct);
        removed += await TryAsync("Anonyme Retention fehlgeschlagen", sp =>
            PruneAsync(sp.GetRequiredService<AppDbContext>(), now - AnonymousEndlessMaxAge, ct), ct);
        removed += await TryAsync("Anonyme Versuchs-Retention fehlgeschlagen", async sp =>
        {
            var pruned = await AnonymousSinkRetention.PruneAsync(sp.GetRequiredService<AppDbContext>(), now,
                RetentionOptions.From(sp.GetService<IConfiguration>()), ct: ct);
            if (pruned > 0)
                _logger.LogInformation("Anonyme Versuchs-Retention: {Count} Zeilen gelöscht", pruned);
            return pruned;
        }, ct);
        removed += await TryAsync("Nachrichten-Retention fehlgeschlagen", async sp =>
        {
            var pruned = await Social.AdminMessageRetention.PruneAsync(sp.GetRequiredService<AppDbContext>(), now,
                RetentionOptions.From(sp.GetService<IConfiguration>()), ct: ct);
            if (pruned > 0)
                _logger.LogInformation("Nachrichten-Retention: {Count} gelesene Nachrichten gelöscht", pruned);
            return pruned;
        }, ct);
        removed += await TryAsync("Formularfoto-Retention fehlgeschlagen", async sp =>
        {
            var (cleared, archive) = await Scoresheet.ScoresheetPhotoRetention.PruneAsync(sp.GetRequiredService<AppDbContext>(), now,
                RetentionOptions.From(sp.GetService<IConfiguration>()), ct);
            if (cleared > 0 || archive > 0)
                _logger.LogInformation("Formularfoto-Retention: {Cleared} Einlesungen geleert, {Archive} Archiv-Einträge gelöscht",
                    cleared, archive);
            return cleared + archive;
        }, ct);
        return removed;
    }

    /// <summary>Ein Schritt in eigenem Scope; ein Fehler wird mit dem festen Template des Schritts geloggt (log-watcher
    /// gruppiert danach) und ergibt 0.</summary>
    private async Task<int> TryAsync(string failure, Func<IServiceProvider, Task<int>> step, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            return await step(scope.ServiceProvider);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, failure);
            return 0;
        }
    }

    /// <summary>Portionsgröße der Löschung (Zeilen je DELETE).</summary>
    internal const int DeleteChunkSize = RetentionDelete.ChunkSize;

    /// <summary>Anonyme Endless-Zeilen älter als <paramref name="cutoff"/> löschen. Zeilen MIT
    /// <c>UserId</c> bleiben immer — das ist die Statistik angemeldeter Nutzer.</summary>
    public static Task<int> PruneAsync(AppDbContext db, DateTime cutoff, CancellationToken ct = default)
        => PruneAsync(db, cutoff, DeleteChunkSize, ct);

    /// <summary>Wie <see cref="PruneAsync(AppDbContext, DateTime, CancellationToken)"/>, mit wählbarer Portion (Tests).
    /// Portionsweise über die Ids, ohne die Zeilen zu laden: bis zu 1 MB <c>ActiveGameState</c> je Zeile — ein fälliger
    /// Rückstand von ein paar tausend Zeilen, auf einmal per ToList geladen, sprengte das Speicherlimit der API, und weil
    /// der erste Lauf beim Start kommt, scheiterte er danach jeden Tag von Neuem.</summary>
    internal static async Task<int> PruneAsync(AppDbContext db, DateTime cutoff, int chunkSize, CancellationToken ct = default)
    {
        var removed = await RetentionDelete.AllAsync(db,
            db.EndlessProgresses.Where(p => p.UserId == null && p.UpdatedAt < cutoff), chunkSize, ct);
        removed += await RetentionDelete.AllAsync(db,
            db.EndlessSessions.Where(s => s.UserId == null && s.CreatedAt < cutoff), chunkSize, ct);
        return removed;
    }
}
