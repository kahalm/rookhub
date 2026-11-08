using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Glocke „Analyse fertig" (0.904.0): merkt sich, wer über das Ende einer Partie-Analyse informiert werden will
/// (<see cref="GameAnalysisWatcher"/>), und schickt beim ENDGÜLTIGEN Ende genau eine Glocke je Nutzer.
///
/// <para><b>Endgültig</b> heißt: erster Durchgang fertig (<see cref="GameAnalysisStatus.Done"/>) UND keine Vertiefung mehr
/// offen — entweder gibt es keine (<see cref="GameAnalysis.RefineDepth"/> leer) oder sie ist durch
/// (<see cref="GameAnalysis.RefinedAt"/> gesetzt). Nach dem schnellen ersten Durchgang steht zwar schon die Kurve, die Zahlen
/// ändern sich aber mit der Vertiefung noch — „tief analysiert" ist erst danach. Aufgerufen von der Pumpe
/// (<see cref="GameAnalysisPump"/>) genau an den beiden Stellen, an denen eine Analyse endgültig wird.</para>
///
/// <para>Die Lc0-Zweitanalyse der Vereinspartien (<c>ClubSecondEngineScheduler</c>, Origin Club MIT <see cref="GameAnalysis.EngineId"/>)
/// meldet nie: an sie hängt sich keine Zeile, und der Weg über die Vereinspartie nimmt nur die Hauptanalyse (ohne EngineId).</para>
/// </summary>
/// <param name="notifications">Mit Web-Push aus dem DI; ohne (Tests, Werkzeuge) nur die Glocke.</param>
public sealed class GameAnalysisWatchers(AppDbContext db, NotificationService? notifications = null)
{
    /// <summary>So lange bleibt eine Zeile höchstens liegen (Analyse gescheitert, nie angelegt …) — der tägliche Lauf
    /// (<c>NotificationRetentionScheduler</c>) räumt sie danach weg.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(60);

    /// <summary>Ist die Analyse endgültig fertig (siehe Klassenkommentar)?</summary>
    public static bool IsFinal(GameAnalysisStatus status, int? refineDepth, DateTime? refinedAt) =>
        status == GameAnalysisStatus.Done && (refineDepth == null || refinedAt != null);

    /// <summary>
    /// <paramref name="userId"/> will wissen, wann Analyse <paramref name="analysisId"/> fertig ist. Nichts zu tun, wenn sie
    /// schon endgültig fertig ist (die Seite zeigt sie ja), wenn sie gescheitert ist oder die Zeile schon steht.
    /// → ob eine Zeile angelegt wurde.
    /// </summary>
    public async Task<bool> WatchAsync(int userId, int analysisId, string link, CancellationToken ct = default)
    {
        var a = await db.GameAnalyses.AsNoTracking().Where(g => g.Id == analysisId)
            .Select(g => new { g.Status, g.RefineDepth, g.RefinedAt }).FirstOrDefaultAsync(ct);
        if (a is null || a.Status == GameAnalysisStatus.Failed || IsFinal(a.Status, a.RefineDepth, a.RefinedAt)) return false;
        if (await db.GameAnalysisWatchers.AnyAsync(w => w.GameAnalysisId == analysisId && w.UserId == userId, ct)) return false;
        db.GameAnalysisWatchers.Add(new GameAnalysisWatcher { UserId = userId, GameAnalysisId = analysisId, Link = Clip(link) });
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// <paramref name="userId"/> hat in LeagueHub diese Vereinspartien hochgeladen — ihre Analyse legt erst der Stapel an.
    /// Vorhandene Zeilen bleiben, wie sie sind. → Zahl der neuen Zeilen.
    /// </summary>
    public async Task<int> WatchClubGamesAsync(int userId, IEnumerable<int> clubGameIds, CancellationToken ct = default)
    {
        var ids = clubGameIds.Distinct().ToList();
        if (ids.Count == 0) return 0;
        var known = await db.GameAnalysisWatchers.AsNoTracking()
            .Where(w => w.UserId == userId && w.LeagueClubGameId != null && ids.Contains(w.LeagueClubGameId.Value))
            .Select(w => w.LeagueClubGameId!.Value).ToListAsync(ct);
        var fresh = ids.Except(known).ToList();
        foreach (var id in fresh)
            db.GameAnalysisWatchers.Add(new GameAnalysisWatcher { UserId = userId, LeagueClubGameId = id, Link = $"/club-games/{id}" });
        if (fresh.Count > 0) await db.SaveChangesAsync(ct);
        return fresh.Count;
    }

    /// <summary>
    /// Die Analyse ist gerade endgültig fertig geworden: eine Glocke je wartendem Nutzer (Link auf SEINE Partie), danach
    /// sind die Zeilen weg — eine spätere Korrektur samt Neurechnung meldet nur, wer erneut klickt. → Zahl der Empfänger.
    /// </summary>
    public async Task<int> NotifyFinishedAsync(GameAnalysis analysis, CancellationToken ct = default)
    {
        if (!IsFinal(analysis.Status, analysis.RefineDepth, analysis.RefinedAt)) return 0;
        // Über die Vereinspartie nur die Hauptanalyse — die Lc0-Zweitanalyse trägt eine EngineId.
        var clubGameId = analysis.Origin == GameAnalysisOrigin.Club && analysis.EngineId == null ? analysis.LeagueClubGameId : null;
        var rows = await db.GameAnalysisWatchers
            .Where(w => w.GameAnalysisId == analysis.Id || (clubGameId != null && w.LeagueClubGameId == clubGameId))
            .OrderBy(w => w.Id)
            .ToListAsync(ct);
        if (rows.Count == 0) return 0;
        db.GameAnalysisWatchers.RemoveRange(rows);
        await db.SaveChangesAsync(ct);

        var bell = notifications ?? new NotificationService(db);
        var data = new Dictionary<string, string> { ["white"] = analysis.White ?? "?", ["black"] = analysis.Black ?? "?" };
        var sent = 0;
        // Je Nutzer EINE Glocke, auch wenn er die Partie zweimal hat (Kopie + Vereinspartie): die älteste Zeile gibt den Link.
        foreach (var row in rows.GroupBy(r => r.UserId).Select(g => g.First()))
        {
            await bell.CreateAsync(row.UserId, NotificationType.GameAnalysisFinished, data, row.Link);
            sent++;
        }
        return sent;
    }

    /// <summary>Liegengebliebene Zeilen (älter als <see cref="StaleAfter"/>) entfernen. → Zahl.</summary>
    public async Task<int> PurgeStaleAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var cutoff = nowUtc - StaleAfter;
        var stale = await db.GameAnalysisWatchers.Where(w => w.CreatedAt < cutoff).ToListAsync(ct);
        if (stale.Count == 0) return 0;
        db.GameAnalysisWatchers.RemoveRange(stale);
        await db.SaveChangesAsync(ct);
        return stale.Count;
    }

    /// <summary>Nach einem gescheiterten Speichern (zwei gleichzeitige Klicks) die eigenen, nicht gespeicherten Zeilen
    /// wieder aus dem Kontext nehmen — der Rest des Aufrufers (Partie, Upload) bleibt verfolgt.</summary>
    public void Forget()
    {
        foreach (var e in db.ChangeTracker.Entries<GameAnalysisWatcher>().Where(e => e.State == EntityState.Added).ToList())
            e.State = EntityState.Detached;
    }

    private static string Clip(string link) => link.Length <= 300 ? link : link[..300];
}
