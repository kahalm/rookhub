using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Zusammenfassung AUTOMATISCH anfordern (Wunsch 2026-11-14: „neue aktuelle Ligapartien automatisch Summary erzeugen“) —
/// als hätte ein Admin auf „Zusammenfassung anfordern“ geklickt (<see cref="ClubGameSummaries.RequestAsync"/>). Die EINE
/// Stelle dafür; gerufen wird sie nur aus <see cref="ClubGameWriter"/>: nach dem Anlegen (<c>SaveAsync</c> — über das jeder
/// Anlageweg läuft: PGN-/ChessBase-Import, Partieformular, beides auch über den Teilen-Link) und nach dem nachträglichen
/// Zuordnen einer Ligapaarung (<c>AssignPairingIfUnlinkedAsync</c>, <c>UpdateAsync</c> mit <c>leagueGameId</c>).
/// <para>Angefordert wird nur, wenn ALLES gilt:</para>
/// <list type="bullet">
/// <item><c>ClubGameSummaries:Auto:Enabled</c> (Vorgabe an).</item>
/// <item>Die Partie ist nicht archiviert und hat noch KEINE Zusammenfassungs-Zeile (egal welcher Status).</item>
/// <item>Sie ist eine Ligapartie der laufenden Saison: ihre Ligapaarung (<see cref="LeagueClubGame.LeagueGameId"/> bzw. der
/// Schlüssel, aufgelöst über <see cref="LeagueGameLinks.ResolveAsync"/> — auch eine vorläufige zählt, eine tote Id nicht)
/// gehört zu einer Liga, deren Saison die jüngste ihrer Region ist (<see cref="LeagueOnlineRegions.CurrentSeasonTnrsAsync"/>).</item>
/// <item>Das Partiedatum liegt höchstens <c>ClubGameSummaries:Auto:MaxAgeDays</c> (30) Tage zurück. Datum = der volle Tag aus
/// dem Upload (PGN-Kopf <c>Date</c>, Formular), sonst der Rundentermin der Paarung, sonst heute. (Die gespeicherte Partie
/// trägt nur das Jahr.)</item>
/// <item>Heute (UTC) sind noch keine <c>ClubGameSummaries:Auto:DailyLimit</c> (20) automatischen Anforderungen angelegt —
/// gezählt an <see cref="ClubGameSummary.AutoRequestedAt"/>, über alle Vereine. Ein Lauf kostet etwa 3,50 $.</item>
/// </list>
/// Kein Nachholen: der Bestand wird nie durchgesehen. Ein Fehler hier lässt das Anlegen der Partie nie scheitern.
/// </summary>
internal sealed class ClubGameAutoSummary(AppDbContext db, ILogger log, Func<DateTime> now,
    Microsoft.Extensions.Configuration.IConfiguration? config = null)
{
    public const int DefaultMaxAgeDays = 30;
    public const int DefaultDailyLimit = 20;

    private bool Enabled => config?.GetValue<bool?>("ClubGameSummaries:Auto:Enabled") ?? true;
    private int MaxAgeDays => Math.Max(0, config?.GetValue<int?>("ClubGameSummaries:Auto:MaxAgeDays") ?? DefaultMaxAgeDays);
    private int DailyLimit => Math.Max(0, config?.GetValue<int?>("ClubGameSummaries:Auto:DailyLimit") ?? DefaultDailyLimit);

    /// <summary>Für die (gespeicherten) Partien anfordern, was die Regeln erlauben. → Zahl der Anforderungen.</summary>
    /// <param name="userId">Wer die Partie anlegt bzw. zuordnet — steht als Anforderer an der Zeile; <c>null</c> = ohne Konto
    /// (Teilen-Link).</param>
    /// <param name="dateOf">Der volle Partietag aus dem Upload, wenn es ihn gibt.</param>
    public async Task<int> RequestAsync(IReadOnlyCollection<LeagueClubGame> games, int? userId, CancellationToken ct,
        Func<LeagueClubGame, DateOnly?>? dateOf = null)
    {
        try
        {
            return await RequestCoreAsync(games, userId, dateOf, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Beiwerk: die Partie ist gespeichert und bleibt es. Halb angelegte Zeilen aus dem Kontext nehmen, sonst scheitert
            // das nächste SaveChanges des Aufrufers an ihnen.
            foreach (var e in db.ChangeTracker.Entries<ClubGameSummary>().Where(e => e.State == EntityState.Added).ToList())
                e.State = EntityState.Detached;
            log.LogWarning(ex, "Zusammenfassung: automatisches Anfordern gescheitert (Partien {Ids}) — die Partien bleiben angelegt",
                string.Join(",", games.Select(g => g.Id)));
            return 0;
        }
    }

    private async Task<int> RequestCoreAsync(IReadOnlyCollection<LeagueClubGame> games, int? userId,
        Func<LeagueClubGame, DateOnly?>? dateOf, CancellationToken ct)
    {
        if (!Enabled) return 0;
        var linked = games.Where(g => g.Id > 0 && g.ArchivedAt == null && (g.LeagueGameId != null || LeagueGameLinks.KeyOf(g) != null))
            .DistinctBy(g => g.Id).ToList();
        if (linked.Count == 0) return 0;

        // Ligapartie der laufenden Saison: die Paarung, wie sie heute steht, in einer Liga der jüngsten Saison ihrer Region.
        var live = await LeagueGameLinks.ResolveAsync(db, linked, ct, log);
        if (live.Count == 0) return 0;
        var current = (await LeagueOnlineRegions.CurrentSeasonTnrsAsync(db, ct)).ToHashSet();
        var league = linked.Where(g => live.TryGetValue(g.Id, out var p) && current.Contains(p.Tnr)).ToList();
        if (league.Count == 0) return 0;

        var ids = league.Select(g => g.Id).ToList();
        var have = (await db.ClubGameSummaries.AsNoTracking().Where(s => ids.Contains(s.ClubGameId)).Select(s => s.ClubGameId)
            .ToListAsync(ct)).ToHashSet();
        var fresh = league.Where(g => !have.Contains(g.Id)).ToList();
        if (fresh.Count == 0) return 0;

        var tnrs = fresh.Select(g => live[g.Id].Tnr).Distinct().ToList();
        var roundDates = (await db.LeagueRounds.AsNoTracking().Where(r => tnrs.Contains(r.Tnr) && r.Date != null)
                .Select(r => new { r.Tnr, r.Round, r.Date }).ToListAsync(ct))
            .GroupBy(r => (r.Tnr, r.Round)).ToDictionary(x => x.Key, x => x.First().Date!.Value);

        var at = now();
        var today = DateOnly.FromDateTime(at);
        var oldest = today.AddDays(-MaxAgeDays);
        var limit = DailyLimit;
        var used = await db.ClubGameSummaries.AsNoTracking().CountAsync(s => s.AutoRequestedAt >= at.Date, ct);
        var added = new List<int>();
        foreach (var g in fresh)
        {
            var p = live[g.Id];
            var date = dateOf?.Invoke(g) ?? (roundDates.TryGetValue((p.Tnr, p.Round), out var rd) ? rd : today);
            if (date < oldest)
            {
                log.LogInformation("Zusammenfassung: Vereinspartie {Id} nicht automatisch angefordert — Partiedatum {Date} liegt mehr als "
                    + "{Days} Tage zurück", g.Id, date, MaxAgeDays);
                continue;
            }
            if (used >= limit)
            {
                log.LogInformation("Zusammenfassung: Vereinspartie {Id} nicht automatisch angefordert — Tagesdeckel {Limit} erreicht", g.Id, limit);
                continue;
            }
            db.ClubGameSummaries.Add(new ClubGameSummary
            {
                ClubGameId = g.Id, Status = ClubGameSummaryStatus.Requested, RequestedAt = at, RequestedByUserId = userId, AutoRequestedAt = at,
            });
            used++;
            added.Add(g.Id);
        }
        if (added.Count == 0) return 0;
        await db.SaveChangesAsync(ct);
        log.LogInformation("Zusammenfassung: automatisch angefordert für Vereinspartie(n) {Ids} (Ligapartie der laufenden Saison; heute {Used}/{Limit})",
            string.Join(",", added), used, limit);
        return added.Count;
    }
}
