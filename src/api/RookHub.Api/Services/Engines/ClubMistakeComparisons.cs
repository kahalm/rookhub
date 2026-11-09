using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Services.Engines;

/// <summary>
/// Zugvergleiche zu den Fehlern einer Vereinspartie (0.908.0, Wunsch 2026-11-08: „bei Ligapartien lässt du ja Fehler
/// nachspielen — kannst du für diese Fehler auch immer gleich den Fehlzug mit dem besten Zug vergleichen? Denke da an
/// Compare Moves, die beiden Tiefe 35 oder so vergleichen." und „Ligapartien dürfen an der Stelle alle mit 35 rechnen"; seit
/// 0.909.0 „vertiefe Ligapartien auf 30 und vergleiche auch 30" — die Analyse vertieft auf 30, die Vergleiche rechnen 30).
///
/// <list type="bullet">
/// <item><b>Was</b>: je Fehler (<see cref="GameMistakeSelection"/>, der Spiegel von „Fehler nachspielen") ein normaler
/// Zugvergleich (<see cref="MoveComparisonService"/>) „bester Zug gegen Partiezug" mit <see cref="DefaultDepth"/>, Begründung
/// auf Deutsch (<see cref="Language"/> — LeagueHub ist eine deutschsprachige Oberfläche, die Vereine sind Tiroler).</item>
/// <item><b>Wann</b>: sobald die HAUPTanalyse der Vereinspartie endgültig fertig ist (<see cref="GameAnalysisWatchers.IsFinal"/>,
/// Origin Club ohne <see cref="GameAnalysis.EngineId"/> — die Lc0-Zweitanalyse nie). Seit 0.909.0 heißt das: NACH der
/// Vertiefung auf 30 (<see cref="GameAnalysisDefaults.ClubRefineDepth"/>), die Fehler und besten Züge kommen also aus den
/// Zahlen der Tiefe 30, auch für den Nachtrag (eine noch zu vertiefende Analyse ist nicht fertig). Die Pumpe der Partie-Analysen ruft
/// <see cref="EnqueueAsync"/>. Was davor schon fertig war, holt der Nachtrag (<see cref="TickAsync"/>) nach — immer nur eine
/// Partie, wenn nichts mehr wartet, die zuletzt fertig gewordene zuerst.</item>
/// <item><b>Wie gerechnet</b>: angelegt wird nur die Zeile (<see cref="MoveComparisonStatus.Waiting"/>); die Pumpe startet
/// höchstens <see cref="DefaultMaxActive"/> gleichzeitig (angeforderte Zusammenfassung: vorne und ohne Deckel), jüngste Analyse zuerst, und die Aufträge laufen als
/// Stapel-Hintergrundarbeit wie die Analyse der Vereinspartien (verdrängt nichts, Sperrzeiten gelten). Besitzer ist der
/// Besitzer der Analyse (Haus-Engine-Konto); die Deckel der Nutzer (<see cref="MoveComparisonService.MaxOpenPerUser"/>,
/// <see cref="MoveComparisonService.MaxKeptPerUser"/>) und seine Liste sehen diese Zeilen nicht.</item>
/// <item><b>Weg damit</b>: mit der Analyse (Löschen der Partie, Cascade) und beim Korrigieren der Züge
/// (<see cref="DeleteForAnalysisAsync"/> aus dem Umbau — danach legt das neue Ende sie neu an).</item>
/// </list>
/// Konfiguration: <c>ClubMistakeComparisons:Enabled</c> (Vorgabe an), <c>:Depth</c> (30, seit 0.909.0; vorher 35), <c>:MaxActive</c> (1).
/// </summary>
public sealed class ClubMistakeComparisons
{
    public const int DefaultDepth = GameAnalysisDefaults.ClubRefineDepth;
    public const int DefaultMaxActive = 1;
    public const string Language = "de";

    private readonly AppDbContext _db;
    private readonly MoveComparisonService _comparisons;
    private readonly ILogger<ClubMistakeComparisons> _logger;
    private readonly bool _enabled;
    private readonly int _depth;
    private readonly int _maxActive;

    public ClubMistakeComparisons(AppDbContext db, MoveComparisonService comparisons, ILogger<ClubMistakeComparisons> logger,
        IConfiguration? config = null)
    {
        _db = db;
        _comparisons = comparisons;
        _logger = logger;
        _enabled = config?.GetValue<bool?>("ClubMistakeComparisons:Enabled") ?? true;
        _depth = Math.Clamp(config?.GetValue<int?>("ClubMistakeComparisons:Depth") ?? DefaultDepth, MoveComparisonService.MinDepth,
            AnalysisJobService.MaxDepth);
        _maxActive = Math.Clamp(config?.GetValue<int?>("ClubMistakeComparisons:MaxActive") ?? DefaultMaxActive, 1, 8);
    }

    /// <summary>Gilt die Analyse für die Vergleiche (Hauptanalyse einer Vereinspartie, endgültig fertig)?</summary>
    public static bool Applies(GameAnalysis a) =>
        a.Origin == GameAnalysisOrigin.Club && a.EngineId == null && a.LeagueClubGameId != null
        && GameAnalysisWatchers.IsFinal(a.Status, a.RefineDepth, a.RefinedAt);

    /// <summary>
    /// Je Fehler der Analyse einen wartenden Vergleich anlegen (mit geladenen <see cref="GameAnalysis.Positions"/>). Doppelt
    /// aufgerufen legt nichts zweimal an: <see cref="GameAnalysis.MistakeComparisonsAt"/> und je Halbzug höchstens eine Zeile.
    /// → Zahl der neuen Vergleiche.
    /// </summary>
    public static async Task<int> EnqueueAsync(AppDbContext db, GameAnalysis analysis, CancellationToken ct = default,
        int depth = DefaultDepth)
    {
        if (!Applies(analysis) || analysis.MistakeComparisonsAt != null) return 0;
        var known = (await db.MoveComparisons.AsNoTracking().Where(c => c.GameAnalysisId == analysis.Id && c.Ply != null)
            .Select(c => c.Ply!.Value).ToListAsync(ct)).ToHashSet();
        var now = DateTime.UtcNow;
        var added = 0;
        foreach (var m in GameMistakeSelection.FromPositions(analysis.Positions, analysis.PlyCount))
        {
            if (known.Contains(m.Ply)) continue;
            var c = new MoveComparison
            {
                UserId = analysis.UserId, EngineOwnerUserId = analysis.EngineOwnerUserId, GameAnalysisId = analysis.Id, Ply = m.Ply,
                Fen = m.FenBefore, Depth = depth, Language = Language, Status = MoveComparisonStatus.Waiting,
                Title = Clip(GameAnalysisText.BuildTitle(analysis.White, analysis.Black, null) + $" · {MoveLabel(m)}"),
                CreatedAt = now, UpdatedAt = now,
            };
            var ordinal = 0;
            foreach (var uci in new[] { m.BestUci, m.PlayedUci })
            {
                if (MoveComparisonService.PlayUci(m.FenBefore, uci) is not { } after) continue;
                var line = new MoveComparisonLine { Kind = MoveComparisonLineKind.Candidate, CandidateUci = uci, Ordinal = ordinal++, Fen = after };
                if (MoveComparisonService.Terminal(after) is not null) line.State = MoveComparisonLineState.Done;
                c.Lines.Add(line);
            }
            if (c.Lines.Count < MoveComparisonService.MinCandidates) continue;
            db.MoveComparisons.Add(c);
            added++;
        }
        analysis.MistakeComparisonsAt = now;
        await db.SaveChangesAsync(ct);
        return added;
    }

    /// <summary>„17…Nf8 / e5" — der Halbzug in Worten für den Titel.</summary>
    private static string MoveLabel(GameMistakeSelection.Selected m)
    {
        var number = m.Ply / 2 + 1;
        var played = MoveComparisonService.SanOf(m.FenBefore, m.PlayedUci) ?? m.PlayedUci;
        var best = MoveComparisonService.SanOf(m.FenBefore, m.BestUci) ?? m.BestUci;
        return $"{number}{(m.Ply % 2 == 0 ? "." : "...")}{played} / {best}";
    }

    private static string Clip(string s) => s.Length <= 200 ? s : s[..200];

    /// <summary>Ein Takt der Pumpe: Nachtrag (eine Partie, wenn nichts mehr wartet), dann wartende Vergleiche starten.</summary>
    public async Task TickAsync(CancellationToken ct = default)
    {
        if (!_enabled) return;
        await BackfillStepAsync(ct);
        await StartWaitingAsync(ct);
    }

    /// <summary>
    /// Nachtrag für Analysen, die schon vor 0.908.0 (oder während der Dienst aus war) fertig waren: EINE Partie je Takt, und
    /// nur, wenn kein Vergleich mehr wartet — so stehen neue Partien nie hinter dem Altbestand. Die zuletzt fertig gewordene
    /// zuerst. → ob eine Partie angelegt wurde.
    /// </summary>
    internal async Task<bool> BackfillStepAsync(CancellationToken ct)
    {
        if (await _db.MoveComparisons.AnyAsync(c => c.GameAnalysisId != null && c.Status == MoveComparisonStatus.Waiting, ct)) return false;
        var id = await _db.GameAnalyses.AsNoTracking()
            .Where(a => a.Origin == GameAnalysisOrigin.Club && a.EngineId == null && a.LeagueClubGameId != null
                && a.MistakeComparisonsAt == null && a.Status == GameAnalysisStatus.Done
                && (a.RefineDepth == null || a.RefinedAt != null))
            .OrderByDescending(a => a.FinishedAt).ThenByDescending(a => a.Id)
            .Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);
        if (id is null) return false;
        var analysis = await _db.GameAnalyses.Include(a => a.Positions).FirstAsync(a => a.Id == id, ct);
        var added = await EnqueueAsync(_db, analysis, ct, _depth);
        _logger.LogInformation("Zugvergleiche Vereinspartie {ClubGameId} (Analyse {Id}) nachgetragen: {Count} Fehler",
            analysis.LeagueClubGameId, analysis.Id, added);
        return true;
    }

    /// <summary>
    /// Wartet auf die Vergleiche dieser Analyse eine Zusammenfassung (angefordert oder in Arbeit)? Dann gehen sie vor
    /// (Wunsch 2026-11-09): sie starten am <c>MaxActive</c>-Deckel vorbei und ihre Aufträge laufen als normale
    /// Hintergrundarbeit statt als Stapel — sonst stünde die Zusammenfassung hinter dem ganzen Nachtrag.
    /// </summary>
    public static Task<bool> SummaryWantedAsync(AppDbContext db, int analysisId, CancellationToken ct = default) =>
        db.GameAnalyses.AnyAsync(a => a.Id == analysisId && db.ClubGameSummaries.Any(s => s.ClubGameId == a.LeagueClubGameId
            && (s.Status == ClubGameSummaryStatus.Requested || s.Status == ClubGameSummaryStatus.Running)), ct);

    /// <summary>Wartende Vergleiche starten: zuerst die der Partien mit angeforderter Zusammenfassung (ohne Deckel, siehe
    /// <see cref="SummaryWantedAsync"/>), dann die übrigen, solange weniger als <c>MaxActive</c> rechnen — die jüngste Analyse
    /// zuerst, darin nach Halbzug. Ohne freigegebene Haus-Engine oder bei vollem Deckel bleibt der Rest liegen (nächster
    /// Takt). → Zahl gestartet.</summary>
    internal async Task<int> StartWaitingAsync(CancellationToken ct)
    {
        var active = await _db.MoveComparisons.CountAsync(c => c.GameAnalysisId != null
            && (c.Status == MoveComparisonStatus.Candidates || c.Status == MoveComparisonStatus.Replies), ct);
        var started = 0;
        var startedNormal = 0;
        while (true)
        {
            var next = await _db.MoveComparisons.Include(c => c.Lines)
                .Where(c => c.GameAnalysisId != null && c.Status == MoveComparisonStatus.Waiting)
                .OrderByDescending(c => _db.ClubGameSummaries.Any(s => s.ClubGameId == c.GameAnalysis!.LeagueClubGameId
                    && (s.Status == ClubGameSummaryStatus.Requested || s.Status == ClubGameSummaryStatus.Running)))
                .ThenByDescending(c => c.GameAnalysis!.FinishedAt).ThenByDescending(c => c.GameAnalysisId).ThenBy(c => c.Ply)
                .FirstOrDefaultAsync(ct);
            if (next is null) break;
            var wanted = await SummaryWantedAsync(_db, next.GameAnalysisId!.Value, ct);
            if (!wanted && active + startedNormal >= _maxActive) break;
            if (next.EngineOwnerUserId is int owner && !await EngineOwnerResolver.IsHouseEngineSharedAsync(_db, owner, ct)) break;
            if (next.Depth != _depth) next.Depth = _depth;   // eine geänderte Tiefe gilt auch für schon angelegte, nicht gestartete
            try { await _comparisons.StartWaitingAsync(next, ct); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                _logger.LogInformation("Zugvergleich {Id} (Vereinspartie) wartet weiter: {Message}", next.Id, ex.Message);
                break;
            }
            started++;
            if (!wanted) startedNormal++;
        }
        return started;
    }

    /// <summary>Die Vergleiche einer Analyse samt offener Aufträge löschen und den Nachtrag zurücksetzen (korrigierte Züge,
    /// gelöschte Analyse — InMemory und Aufträge kaskadieren nicht). Das Löschen der Aufträge speichert schon, die Zeilen selbst
    /// speichert der Aufrufer. → Zahl.</summary>
    public static async Task<int> DeleteForAnalysisAsync(AppDbContext db, AnalysisJobService jobs, GameAnalysis analysis, CancellationToken ct = default)
    {
        var rows = await db.MoveComparisons.Include(c => c.Lines).Where(c => c.GameAnalysisId == analysis.Id).ToListAsync(ct);
        foreach (var c in rows)
            foreach (var jobId in c.Lines.Where(l => l.AnalysisJobId != null).Select(l => l.AnalysisJobId!.Value).ToList())
                await jobs.DeleteAsync(c.UserId, jobId, ct);   // hält einen laufenden Auftrag an
        db.MoveComparisonLines.RemoveRange(rows.SelectMany(c => c.Lines));
        db.MoveComparisons.RemoveRange(rows);
        analysis.MistakeComparisonsAt = null;
        return rows.Count;
    }
}
