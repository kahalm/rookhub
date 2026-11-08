using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>Was die Partieseite über die Zusammenfassung wissen muss. <c>Status</c> = <c>none</c> | <c>requested</c> |
/// <c>running</c> | <c>done</c> | <c>failed</c>; <c>Markdown</c> ist auch während einer neuen Anforderung die letzte fertige
/// Fassung. <c>Stale</c> = die Partie wurde nach dem Schreiben korrigiert.</summary>
public sealed record ClubGameSummaryDto(string Status, string? Markdown, DateTime? DoneAt, DateTime? RequestedAt, bool Stale,
    string? Model, string? Error);

/// <summary>Ein offener Auftrag für den Watcher: Partie und Verein (für <c>?club=</c> an den Lese-Wegen der Partie).</summary>
public sealed record ClubGameSummaryJob(int ClubGameId, int ClubId, DateTime RequestedAt, int? RequestedByUserId);

/// <summary>
/// Partie-Zusammenfassungen durch Claude (0.913.0, <see cref="ClubGameSummary"/>). Anfordern darf nur ein Admin; abgeholt
/// und geschrieben wird außerhalb des Servers (Formular-Watcher mit Claude, ohne API-Schlüssel), über die Admin-Warteschlange
/// <c>api/admin/club-game-summaries</c>. Lesen darf, wer die Partie sieht.
/// </summary>
public sealed class ClubGameSummaries(AppDbContext db, TimeProvider? clock = null)
{
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
        return new ClubGameSummaryDto(StatusOf(s.Status), s.Markdown, s.DoneAt, s.RequestedAt,
            s.Markdown != null && s.MovesHash != null && s.MovesHash != hash.MovesHash, s.Model,
            s.Status == ClubGameSummaryStatus.Failed ? s.Error : null);
    }

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

    /// <summary>Offene Aufträge, älteste zuerst: angefordert, oder „läuft" länger als <see cref="StaleRunning"/>.</summary>
    public async Task<List<ClubGameSummaryJob>> QueueAsync(CancellationToken ct = default)
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
        if (!open) return Outcome.Conflict;
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
