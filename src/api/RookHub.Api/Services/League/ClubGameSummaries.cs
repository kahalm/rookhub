using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>Was die Partieseite über die Zusammenfassung wissen muss. <c>Status</c> = <c>none</c> | <c>requested</c> |
/// <c>running</c> | <c>done</c> | <c>failed</c>; <c>Markdown</c> ist auch während einer neuen Anforderung die letzte fertige
/// Fassung. <c>Stale</c> = die Partie wurde nach dem Schreiben korrigiert.</summary>
public sealed record ClubGameSummaryDto(string Status, string? Markdown, DateTime? DoneAt, DateTime? RequestedAt, bool Stale,
    string? Model, string? Error, ClubGameSummaryReadiness? Readiness = null, int? RunningSeconds = null);

/// <summary>Was die Hintergrund-Rechnung schon hat (0.914.0, Wunsch 2026-11-08: „wenn die Backgrounddinger noch nicht fertig
/// sind, darauf hinweisen — die Summary kommt erst, wenn die durch sind"). <c>Ready</c> = Claude darf schreiben: Analyse fertig,
/// Vertiefung fertig (falls vorgesehen) und jeder Fehler-Vergleich gerechnet (falls eingeschaltet).
/// <c>RefinedPositions/TotalPositions</c> zählen nur bei vorgesehener Vertiefung; <c>ComparisonsTotal</c> ist 0, solange die
/// Vergleiche noch nicht angelegt sind (<c>ComparisonsPlanned</c> = false).</summary>
public sealed record ClubGameSummaryReadiness(bool Ready, bool HasAnalysis, bool AnalysisDone, bool Refining,
    int RefinedPositions, int TotalPositions, bool ComparisonsPlanned, int ComparisonsDone, int ComparisonsTotal,
    int AnalyzedPositions = 0);

/// <summary>Ein offener Auftrag für den Watcher: Partie und Verein (für <c>?club=</c> an den Lese-Wegen der Partie).</summary>
public sealed record ClubGameSummaryJob(int ClubGameId, int ClubId, DateTime RequestedAt, int? RequestedByUserId);

/// <summary>
/// Partie-Zusammenfassungen durch Claude (0.913.0, <see cref="ClubGameSummary"/>). Anfordern darf nur ein Admin; abgeholt
/// und geschrieben wird außerhalb des Servers (Formular-Watcher mit Claude, ohne API-Schlüssel), über die Admin-Warteschlange
/// <c>api/admin/club-game-summaries</c>. Lesen darf, wer die Partie sieht.
/// </summary>
public sealed class ClubGameSummaries(AppDbContext db, TimeProvider? clock = null, Microsoft.Extensions.Configuration.IConfiguration? config = null)
{
    private bool ComparisonsEnabled => config?.GetValue<bool?>("ClubMistakeComparisons:Enabled") ?? true;

    /// <summary>
    /// Ist die Hintergrund-Rechnung der Partie durch? Hauptanalyse (Origin Club, ohne EngineId, die jüngste) fertig, ihre
    /// Vertiefung fertig und — wenn die Fehler-Vergleiche eingeschaltet sind — alle angelegt und gerechnet (fertig oder
    /// gescheitert). Eine gescheiterte Analyse zählt als fertig: dann schreibt Claude ohne (oder meldet fail).
    /// </summary>
    public async Task<ClubGameSummaryReadiness> ReadinessAsync(int clubGameId, CancellationToken ct = default)
    {
        var id = await db.GameAnalyses.AsNoTracking()
            .Where(x => x.LeagueClubGameId == clubGameId && x.Origin == GameAnalysisOrigin.Club && x.EngineId == null)
            .OrderByDescending(x => x.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
        return id is int analysisId ? await ReadinessOfAnalysisAsync(analysisId, ct) : NoAnalysis;
    }

    /// <summary>Stand, solange es noch keine Analyse gibt.</summary>
    public static readonly ClubGameSummaryReadiness NoAnalysis = new(false, false, false, false, 0, 0, false, 0, 0);

    /// <summary>Der Stand einer bestimmten Analyse (0.923.0: auch für eigene Partien, <see cref="Games.OwnGameSummaries"/>).</summary>
    public async Task<ClubGameSummaryReadiness> ReadinessOfAnalysisAsync(int analysisId, CancellationToken ct = default)
    {
        var a = await db.GameAnalyses.AsNoTracking().Where(x => x.Id == analysisId)
            .Select(x => new { x.Id, x.Status, x.RefineDepth, x.RefinedAt, x.MistakeComparisonsAt })
            .FirstOrDefaultAsync(ct);
        if (a is null) return NoAnalysis;
        var failed = a.Status == GameAnalysisStatus.Failed;
        var done = a.Status == GameAnalysisStatus.Done || failed;
        var refining = !failed && a.RefineDepth != null && a.RefinedAt == null;
        int refined = 0, total = 0, analyzed = 0;
        if (!failed && (!done || a.RefineDepth != null))
        {
            // 0.919.3 (Wunsch 09.11.: „zeig mir den Fortschritt, x/x tief/schnell“): auch der schnelle Durchgang zählt mit.
            var positions = db.GameAnalysisPositions.AsNoTracking().Where(p => p.GameAnalysisId == a.Id);
            total = await positions.CountAsync(ct);
            analyzed = done ? total : await positions.CountAsync(p => p.AnalyzedAt != null, ct);
            if (a.RefineDepth != null) refined = await positions.CountAsync(p => p.Refined, ct);
        }
        var planned = a.MistakeComparisonsAt != null;
        int cDone = 0, cTotal = 0;
        if (planned)
        {
            var cs = await db.MoveComparisons.AsNoTracking().Where(c => c.GameAnalysisId == a.Id && c.Ply != null)
                .Select(c => c.Status).ToListAsync(ct);
            cTotal = cs.Count;
            cDone = cs.Count(st => st == MoveComparisonStatus.Done || st == MoveComparisonStatus.Failed);
        }
        var comparisonsReady = failed || !ComparisonsEnabled || (planned && cDone == cTotal);
        return new(done && !refining && comparisonsReady, true, done, refining, refined, total, planned, cDone, cTotal, analyzed);
    }

    /// <summary>So lange darf ein Auftrag „läuft" stehen; danach gilt der Watcher als abgestürzt und die Warteschlange gibt ihn
    /// wieder her.</summary>
    public static readonly TimeSpan StaleRunning = TimeSpan.FromMinutes(90);

    /// <summary>Höchstlänge des abgelieferten Texts (Zeichen) — ein Schutz gegen Ausreißer, keine Stilvorgabe.</summary>
    public const int MaxMarkdown = 200_000;

    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    private static string StatusOf(ClubGameSummaryStatus s) => s switch
    {
        ClubGameSummaryStatus.Requested => "requested",
        ClubGameSummaryStatus.Running => "running",
        ClubGameSummaryStatus.Done => "done",
        _ => "failed",
    };

    /// <summary>Stand für eine Partie DIESES Vereins; <c>null</c> = Partie unbekannt oder fremd.</summary>
    public async Task<ClubGameSummaryDto?> GetAsync(LeagueClub club, int clubGameId, CancellationToken ct = default)
    {
        var hash = await db.LeagueClubGames.AsNoTracking().Where(g => g.Id == clubGameId && g.ClubId == club.Id)
            .Select(g => new { g.MovesHash }).FirstOrDefaultAsync(ct);
        if (hash is null) return null;
        var s = await db.ClubGameSummaries.AsNoTracking().FirstOrDefaultAsync(x => x.ClubGameId == clubGameId, ct);
        if (s is null) return new ClubGameSummaryDto("none", null, null, null, false, null, null);
        var open = s.Status is ClubGameSummaryStatus.Requested or ClubGameSummaryStatus.Running;
        return new ClubGameSummaryDto(StatusOf(s.Status), s.Markdown, s.DoneAt, s.RequestedAt,
            s.Markdown != null && s.MovesHash != null && s.MovesHash != hash.MovesHash, s.Model,
            s.Status == ClubGameSummaryStatus.Failed ? s.Error : null,
            open ? await ReadinessAsync(clubGameId, ct) : null,
            RunningSecondsOf(s.Status, s.StartedAt, Now));
    }

    /// <summary>So lange schreibt Claude schon (Zähler auf der Seite, 0.926.1); vom Server gerechnet, damit Zeitzone und Uhr des
    /// Browsers keine Rolle spielen. <c>null</c>, solange nicht geschrieben wird.</summary>
    internal static int? RunningSecondsOf(ClubGameSummaryStatus status, DateTime? startedAt, DateTime now) =>
        status == ClubGameSummaryStatus.Running && startedAt is { } t ? Math.Max(0, (int)(now - t).TotalSeconds) : null;

    /// <summary>Anfordern bzw. neu anfordern (auch nach Fehler oder wenn schon fertig). Läuft gerade einer, bleibt es dabei.
    /// <c>null</c> = Partie unbekannt oder fremd.</summary>
    public async Task<ClubGameSummaryDto?> RequestAsync(LeagueClub club, int clubGameId, int userId, CancellationToken ct = default)
    {
        if (!await db.LeagueClubGames.AnyAsync(g => g.Id == clubGameId && g.ClubId == club.Id, ct)) return null;
        var s = await db.ClubGameSummaries.FirstOrDefaultAsync(x => x.ClubGameId == clubGameId, ct);
        if (s is null)
        {
            s = new ClubGameSummary { ClubGameId = clubGameId };
            db.ClubGameSummaries.Add(s);
        }
        if (s.Status != ClubGameSummaryStatus.Running || s.StartedAt < Now - StaleRunning)
        {
            s.Status = ClubGameSummaryStatus.Requested;
            s.RequestedAt = Now;
            s.RequestedByUserId = userId;
            s.StartedAt = null;
            s.Error = null;
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(club, clubGameId, ct);
    }

    /// <summary>Offene Aufträge, älteste zuerst: angefordert, oder „läuft" länger als <see cref="StaleRunning"/> — nur die,
    /// deren Hintergrund-Rechnung durch ist (<see cref="ReadinessAsync"/>); die anderen warten still weiter.</summary>
    public async Task<List<ClubGameSummaryJob>> QueueAsync(CancellationToken ct = default)
    {
        var ready = new List<ClubGameSummaryJob>();
        foreach (var job in await OpenAsync(ct))
            if ((await ReadinessAsync(job.ClubGameId, ct)).Ready) ready.Add(job);
        return ready;
    }

    private async Task<List<ClubGameSummaryJob>> OpenAsync(CancellationToken ct)
    {
        var cutoff = Now - StaleRunning;
        return await (from s in db.ClubGameSummaries.AsNoTracking()
                      join g in db.LeagueClubGames.IgnoreQueryFilters().AsNoTracking() on s.ClubGameId equals g.Id
                      where s.Status == ClubGameSummaryStatus.Requested
                          || (s.Status == ClubGameSummaryStatus.Running && s.StartedAt < cutoff)
                      orderby s.RequestedAt
                      select new ClubGameSummaryJob(s.ClubGameId, g.ClubId, s.RequestedAt, s.RequestedByUserId))
            .ToListAsync(ct);
    }

    public enum Outcome { Ok, NotFound, Conflict, Invalid }

    /// <summary>Der Watcher übernimmt den Auftrag. <see cref="Outcome.Conflict"/>, wenn er nicht (mehr) offen ist.</summary>
    public async Task<Outcome> StartAsync(int clubGameId, CancellationToken ct = default)
    {
        var s = await db.ClubGameSummaries.FirstOrDefaultAsync(x => x.ClubGameId == clubGameId, ct);
        if (s is null) return Outcome.NotFound;
        var open = s.Status == ClubGameSummaryStatus.Requested
            || (s.Status == ClubGameSummaryStatus.Running && s.StartedAt < Now - StaleRunning);
        if (!open || !(await ReadinessAsync(clubGameId, ct)).Ready) return Outcome.Conflict;
        s.Status = ClubGameSummaryStatus.Running;
        s.StartedAt = Now;
        await db.SaveChangesAsync(ct);
        return Outcome.Ok;
    }

    /// <summary>Den fertigen Text abliefern. Geht auch ohne vorheriges <see cref="StartAsync"/> (Handlauf).</summary>
    public async Task<Outcome> CompleteAsync(int clubGameId, string? markdown, string? model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(markdown) || markdown.Length > MaxMarkdown) return Outcome.Invalid;
        var game = await db.LeagueClubGames.IgnoreQueryFilters().AsNoTracking().Where(g => g.Id == clubGameId)
            .Select(g => new { g.MovesHash }).FirstOrDefaultAsync(ct);
        if (game is null) return Outcome.NotFound;
        var s = await db.ClubGameSummaries.FirstOrDefaultAsync(x => x.ClubGameId == clubGameId, ct);
        if (s is null)
        {
            s = new ClubGameSummary { ClubGameId = clubGameId, RequestedAt = Now };
            db.ClubGameSummaries.Add(s);
        }
        s.Status = ClubGameSummaryStatus.Done;
        s.Markdown = markdown.Trim();
        s.MovesHash = game.MovesHash;
        s.Model = string.IsNullOrWhiteSpace(model) ? null : model.Trim()[..Math.Min(model.Trim().Length, 60)];
        s.DoneAt = Now;
        s.Error = null;
        await db.SaveChangesAsync(ct);
        return Outcome.Ok;
    }

    /// <summary>Der Watcher meldet einen Fehlschlag; eine vorhandene ältere Fassung bleibt lesbar.</summary>
    public async Task<Outcome> FailAsync(int clubGameId, string? error, CancellationToken ct = default)
    {
        var s = await db.ClubGameSummaries.FirstOrDefaultAsync(x => x.ClubGameId == clubGameId, ct);
        if (s is null) return Outcome.NotFound;
        s.Status = ClubGameSummaryStatus.Failed;
        var e = (error ?? "").Trim();
        s.Error = e.Length == 0 ? "failed" : e[..Math.Min(e.Length, 1000)];
        await db.SaveChangesAsync(ct);
        return Outcome.Ok;
    }
}
