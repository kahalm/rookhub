using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Engines;
using RookHub.Api.DTOs.Games;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.League;

namespace RookHub.Api.Services.Games;

/// <summary>Stand der Zusammenfassung einer eigenen Partie. <c>Source</c> = <c>own</c> oder <c>club</c> (die Partie ist mit einer
/// Vereinspartie verbunden und nur dort gibt es einen Text). <c>QuotaLeft</c> = <c>null</c> für Admins.</summary>
public sealed record OwnGameSummaryDto(string Status, string? Markdown, DateTime? DoneAt, DateTime? RequestedAt, bool Stale,
    string? Model, string? Error, ClubGameSummaryReadiness? Readiness, string Source, int? QuotaLeft, int QuotaLimit, int? RunningSeconds = null);

/// <summary>Offener Auftrag für den Watcher.</summary>
/// <summary>Zusammenfassung über den Teilen-Link <c>/g/{token}</c>: nur der fertige Text, nichts zur Anforderung, zum Besitzer oder zum Kontingent.</summary>
public sealed record SharedOwnGameSummaryDto(string Status, string? Markdown, DateTime? DoneAt, bool Stale, string? Model);

public sealed record OwnGameSummaryJob(int SavedGameId, int UserId, DateTime RequestedAt);

/// <summary>Die Partie in der Form, die der Watcher von einer Vereinspartie kennt (<c>lib/digest.py</c>).</summary>
public sealed record OwnGameSummaryGame(int Id, string? White, string? Black, string? Result, int? WhiteElo, int? BlackElo,
    int? Year, string? Event, string? OwnSide, string Uci);

/// <summary>
/// Zusammenfassungen eigener Partien (0.923.0). Der Besitzer fordert an (<see cref="DefaultDailyPerUser"/> je Konto und UTC-Tag,
/// <c>OwnGameSummaries:DailyPerUser</c>; Admins frei), die Partie wird dabei analysiert, soweit noch nicht geschehen; Vertiefung
/// und Fehler-Vergleiche (<see cref="ClubMistakeComparisons"/>) laufen, solange die Anforderung offen ist. Geschrieben wird
/// wie bei Vereinspartien außerhalb des Servers (Watcher, <c>api/admin/own-game-summaries</c>).
/// </summary>
public sealed class OwnGameSummaries(AppDbContext db, SavedGameService games, ClubGameSummaries clubSummaries,
    MoveComparisonService comparisons, TimeProvider? clock = null, IConfiguration? config = null)
{
    public const int DefaultDailyPerUser = 2;
    private int DailyPerUser => Math.Max(0, config?.GetValue<int?>("OwnGameSummaries:DailyPerUser") ?? DefaultDailyPerUser);
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public enum RequestError { None, NotFound, QuotaExceeded, AnalysisUnavailable, UnsupportedStart }
    public enum Outcome { Ok, NotFound, Conflict, Invalid }

    private static bool Open(ClubGameSummaryStatus s) => s is ClubGameSummaryStatus.Requested or ClubGameSummaryStatus.Running;

    private static string StatusOf(ClubGameSummaryStatus s) => s switch
    {
        ClubGameSummaryStatus.Requested => "requested",
        ClubGameSummaryStatus.Running => "running",
        ClubGameSummaryStatus.Done => "done",
        _ => "failed",
    };

    private static string MovesHashOf(string pgn) =>
        LeagueClubService.HashOf(PgnParser.ExtractMainlineSans(PgnParser.SplitGames(pgn).FirstOrDefault().MoveText ?? string.Empty));

    private async Task<int?> QuotaLeftAsync(int userId, bool isAdmin, CancellationToken ct)
    {
        if (isAdmin) return null;
        var start = Now.Date;
        return Math.Max(0, DailyPerUser - await db.OwnGameSummaryRequests.CountAsync(r => r.UserId == userId && r.At >= start, ct));
    }

    /// <summary>Stand für den Besitzer; <c>null</c> = Partie unbekannt oder fremd.</summary>
    public async Task<OwnGameSummaryDto?> GetAsync(int userId, int savedGameId, bool isAdmin, CancellationToken ct = default)
    {
        var g = await db.SavedGames.AsNoTracking().Where(x => x.Id == savedGameId && x.UserId == userId)
            .Select(x => new { x.Pgn, x.GameAnalysisId, x.LeagueClubGameId }).FirstOrDefaultAsync(ct);
        if (g is null) return null;
        var left = await QuotaLeftAsync(userId, isAdmin, ct);
        var s = await db.OwnGameSummaries.AsNoTracking().FirstOrDefaultAsync(x => x.SavedGameId == savedGameId, ct);
        if (s is null || (s.Markdown is null && !Open(s.Status) && s.Status != ClubGameSummaryStatus.Failed))
        {
            // Verbundene Vereinspartie: deren fertiger Text gilt auch hier, solange es keinen eigenen gibt.
            if (g.LeagueClubGameId is int clubId
                && await db.ClubGameSummaries.AsNoTracking().FirstOrDefaultAsync(x => x.ClubGameId == clubId && x.Markdown != null, ct) is { } club)
                return new OwnGameSummaryDto("done", club.Markdown, club.DoneAt, club.RequestedAt, club.MovesHash != null && club.MovesHash != MovesHashOf(g.Pgn),
                    club.Model, null, null, "club", left, DailyPerUser);
            return new OwnGameSummaryDto("none", null, null, null, false, null, null, null, "own", left, DailyPerUser);
        }
        var readiness = Open(s.Status)
            ? g.GameAnalysisId is int a ? await clubSummaries.ReadinessOfAnalysisAsync(a, ct) : ClubGameSummaries.NoAnalysis
            : null;
        return new OwnGameSummaryDto(StatusOf(s.Status), s.Markdown, s.DoneAt, s.RequestedAt,
            s.Markdown != null && s.MovesHash != null && s.MovesHash != MovesHashOf(g.Pgn), s.Model,
            s.Status == ClubGameSummaryStatus.Failed ? s.Error : null, readiness, "own", left, DailyPerUser,
            ClubGameSummaries.ElapsedSecondsOf(s.Status, s.RequestedAt, s.StartedAt, DateTime.UtcNow));
    }

    /// <summary>Fertiger Text der über <paramref name="token"/> geteilten Partie (Wunsch 2026-11-09); ohne Text <c>none</c>, auch bei
    /// offener oder fehlgeschlagener Anforderung. <c>null</c> = unbekannter Link. Bewusst KEIN Rückfall auf die verbundene Vereinspartie:
    /// deren Text sehen sonst nur Vereinsmitglieder und Teilen-Links DIESER Begegnung.</summary>
    public async Task<SharedOwnGameSummaryDto?> GetSharedAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var g = await db.SavedGames.AsNoTracking().Where(x => x.ShareToken == token)
            .Select(x => new { x.Id, x.Pgn }).FirstOrDefaultAsync(ct);
        if (g is null) return null;
        var s = await db.OwnGameSummaries.AsNoTracking().FirstOrDefaultAsync(x => x.SavedGameId == g.Id, ct);
        return s?.Markdown is null
            ? new SharedOwnGameSummaryDto("none", null, null, false, null)
            : new SharedOwnGameSummaryDto("done", s.Markdown, s.DoneAt, s.MovesHash != null && s.MovesHash != MovesHashOf(g.Pgn), s.Model);
    }

    /// <summary>Anfordern. Eine noch offene Anforderung kostet nichts; das Kontingent wird erst nach erfolgreichem Analyse-Start belastet.</summary>
    public async Task<(OwnGameSummaryDto? Dto, RequestError Error)> RequestAsync(int userId, int savedGameId, bool isAdmin, string? lang, CancellationToken ct = default)
    {
        var game = await db.SavedGames.AsNoTracking().Where(x => x.Id == savedGameId && x.UserId == userId)
            .Select(x => new { x.Pgn }).FirstOrDefaultAsync(ct);
        if (game is null) return (null, RequestError.NotFound);
        var s = await db.OwnGameSummaries.FirstOrDefaultAsync(x => x.SavedGameId == savedGameId, ct);
        if (s is not null && Open(s.Status) && (s.Status == ClubGameSummaryStatus.Requested || s.StartedAt >= Now - ClubGameSummaries.StaleRunning))
            return (await GetAsync(userId, savedGameId, isAdmin, ct), RequestError.None);
        // Der Watcher kennt nur Partien ab der Grundstellung (lib/digest.py, Diagramme nach Halbzug).
        if (GamePlies.Parse(game.Pgn, GameAnalysisDefaults.MaxPlies) is not { } parsed || !SavedGamePgn.IsStandardStart(parsed.Header.StartFen))
            return (null, RequestError.UnsupportedStart);
        if (await QuotaLeftAsync(userId, isAdmin, ct) is 0) return (null, RequestError.QuotaExceeded);

        var analysis = await games.AnalyzeAsync(userId, savedGameId, ct, lang);
        if (analysis?.Analysis is null) return (null, RequestError.AnalysisUnavailable);

        if (s is null)
        {
            s = new OwnGameSummary { SavedGameId = savedGameId, UserId = userId };
            db.OwnGameSummaries.Add(s);
        }
        s.Status = ClubGameSummaryStatus.Requested;
        s.RequestedAt = Now;
        s.StartedAt = null;
        s.Error = null;
        db.OwnGameSummaryRequests.Add(new OwnGameSummaryRequest { UserId = userId, SavedGameId = savedGameId, At = Now });
        await db.SaveChangesAsync(ct);
        return (await GetAsync(userId, savedGameId, isAdmin, ct), RequestError.None);
    }

    /// <summary>Die Analyse-Id der eigenen Partie: <c>(true, Id)</c>, ohne Analyse <c>(true, null)</c>, fremd/unbekannt <c>(false, null)</c>.</summary>
    private async Task<(bool Known, int? AnalysisId)> AnalysisOfAsync(int? userId, int savedGameId, CancellationToken ct)
    {
        var q = db.SavedGames.AsNoTracking().Where(x => x.Id == savedGameId);
        if (userId is int u) q = q.Where(x => x.UserId == u);
        var g = await q.Select(x => new { x.GameAnalysisId }).FirstOrDefaultAsync(ct);
        return g is null ? (false, null) : (true, g.GameAnalysisId);
    }

    /// <summary>Fehler-Vergleiche der Partie; <paramref name="userId"/> = <c>null</c> liest ohne Besitzer-Prüfung (Admin).</summary>
    public async Task<List<ClubMistakeComparisonDto>?> ComparisonsAsync(int? userId, int savedGameId, CancellationToken ct = default)
    {
        var (known, id) = await AnalysisOfAsync(userId, savedGameId, ct);
        if (!known) return null;
        return id is int a ? await comparisons.ListForAnalysisAsync(a, ct) : new();
    }

    public async Task<MoveComparisonDto?> ComparisonAsync(int userId, int savedGameId, int comparisonId, CancellationToken ct = default)
    {
        var (known, id) = await AnalysisOfAsync(userId, savedGameId, ct);
        return known && id is int a ? await comparisons.GetForAnalysisAsync(a, comparisonId, ct) : null;
    }

    // ── Watcher ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Offene Aufträge, deren Hintergrund-Rechnung durch ist, älteste zuerst.</summary>
    public async Task<List<OwnGameSummaryJob>> QueueAsync(CancellationToken ct = default)
    {
        var cutoff = Now - ClubGameSummaries.StaleRunning;
        var open = await (from s in db.OwnGameSummaries.AsNoTracking()
                          join g in db.SavedGames.AsNoTracking() on s.SavedGameId equals g.Id
                          where s.Status == ClubGameSummaryStatus.Requested || (s.Status == ClubGameSummaryStatus.Running && s.StartedAt < cutoff)
                          orderby s.RequestedAt
                          select new { s.SavedGameId, s.UserId, s.RequestedAt, g.GameAnalysisId }).ToListAsync(ct);
        var ready = new List<OwnGameSummaryJob>();
        foreach (var j in open)
            if (j.GameAnalysisId is int a && (await clubSummaries.ReadinessOfAnalysisAsync(a, ct)).Ready)
                ready.Add(new OwnGameSummaryJob(j.SavedGameId, j.UserId, j.RequestedAt));
        return ready;
    }

    public async Task<Outcome> StartAsync(int savedGameId, CancellationToken ct = default)
    {
        var s = await db.OwnGameSummaries.FirstOrDefaultAsync(x => x.SavedGameId == savedGameId, ct);
        if (s is null) return Outcome.NotFound;
        var open = s.Status == ClubGameSummaryStatus.Requested
            || (s.Status == ClubGameSummaryStatus.Running && s.StartedAt < Now - ClubGameSummaries.StaleRunning);
        var analysis = (await AnalysisOfAsync(null, savedGameId, ct)).AnalysisId;
        if (!open || analysis is not int a || !(await clubSummaries.ReadinessOfAnalysisAsync(a, ct)).Ready) return Outcome.Conflict;
        s.Status = ClubGameSummaryStatus.Running;
        s.StartedAt = Now;
        await db.SaveChangesAsync(ct);
        return Outcome.Ok;
    }

    public async Task<Outcome> CompleteAsync(int savedGameId, string? markdown, string? model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(markdown) || markdown.Length > ClubGameSummaries.MaxMarkdown) return Outcome.Invalid;
        var pgn = await db.SavedGames.AsNoTracking().Where(x => x.Id == savedGameId).Select(x => x.Pgn).FirstOrDefaultAsync(ct);
        var s = await db.OwnGameSummaries.FirstOrDefaultAsync(x => x.SavedGameId == savedGameId, ct);
        if (pgn is null || s is null) return Outcome.NotFound;
        s.Status = ClubGameSummaryStatus.Done;
        s.Markdown = markdown.Trim();
        s.MovesHash = MovesHashOf(pgn);
        s.Model = string.IsNullOrWhiteSpace(model) ? null : model.Trim()[..Math.Min(model.Trim().Length, 60)];
        s.DoneAt = Now;
        s.Error = null;
        await db.SaveChangesAsync(ct);
        return Outcome.Ok;
    }

    public async Task<Outcome> FailAsync(int savedGameId, string? error, CancellationToken ct = default)
    {
        var s = await db.OwnGameSummaries.FirstOrDefaultAsync(x => x.SavedGameId == savedGameId, ct);
        if (s is null) return Outcome.NotFound;
        s.Status = ClubGameSummaryStatus.Failed;
        var e = (error ?? "").Trim();
        s.Error = e.Length == 0 ? "failed" : e[..Math.Min(e.Length, 1000)];
        await db.SaveChangesAsync(ct);
        return Outcome.Ok;
    }

    /// <summary>Die Partie für den Watcher; <c>null</c> = unbekannt oder nicht lesbar.</summary>
    public async Task<OwnGameSummaryGame?> WatcherGameAsync(int savedGameId, CancellationToken ct = default)
    {
        var g = await db.SavedGames.AsNoTracking().FirstOrDefaultAsync(x => x.Id == savedGameId, ct);
        if (g is null || GamePlies.Parse(g.Pgn, GameAnalysisDefaults.MaxPlies) is not { } parsed) return null;
        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == g.UserId, ct);
        return new OwnGameSummaryGame(g.Id, g.White, g.Black, g.Result, g.WhiteElo, g.BlackElo, g.PlayedAt?.Year, parsed.Header.Event,
            SavedGameService.DetermineOwnerSide(g, profile), string.Join(' ', parsed.Plies.Select(p => p.Uci)));
    }

    /// <summary>Bewertungen für den Watcher; <c>null</c> = keine Analyse.</summary>
    public async Task<GameEvalsDto?> WatcherEvalsAsync(int savedGameId, CancellationToken ct = default)
    {
        if ((await AnalysisOfAsync(null, savedGameId, ct)).AnalysisId is not int id) return null;
        var head = await GameEvalsStore.Heads(db.GameAnalyses.AsNoTracking().Where(a => a.Id == id)).FirstOrDefaultAsync(ct);
        return head is null ? null : await GameEvalsStore.ReadAsync(db, head, null, ct);
    }
}
