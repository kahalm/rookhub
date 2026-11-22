using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Einmaliger Start-Abgleich (1.002.4): bis 1.002.1 verlor eine Vereinspartie bei jeder Korrektur von Namen, Ergebnis oder
/// Paarung (<c>PUT /api/league/club/games/{id}</c>) alle Schach- und Mattzeichen — die Zugliste kam aus
/// <c>PgnParser.ExtractMainlineSans</c>, das <c>+</c>/<c>#</c> abschneidet (Prod-Partie 10: „56. Kg1 Qg2# 1-0" → „56. Kg1 Qg2
/// 0-1"). Ihre Analysen übernahmen dieses PGN (<c>GameAnalysisIntake.SyncClubGameAsync</c>). Seitdem setzt
/// <c>ClubPgnText.PgnOf</c> die Zeichen wieder; dieser Lauf bereinigt den Bestand: Vereinspartien, die Analysen von
/// Vereinspartien und die verbundenen Kopien in „Meine Partien".
///
/// <para><b>Streng</b> (<see cref="PgnCheckMarks.Repair"/>): gespeichert wird nur, wenn sich ausschließlich <c>+</c> und
/// <c>#</c> an Zügen der Hauptvariante ändern; was sonst abweicht oder sich nicht nachspielen lässt, bleibt und steht im Log.
/// Einmalig über <see cref="DoneKey"/> (Muster <see cref="ClubCopyResultRepair"/>); idempotent — ein zweiter Lauf fände nichts.</para>
/// </summary>
public class ClubCheckMarkRepair(IServiceScopeFactory scopeFactory, ILogger<ClubCheckMarkRepair> logger) : BackgroundService
{
    public const string DoneKey = "LeagueClub.CheckMarkRepair";
    public const string DoneValue = "1";

    public sealed record Counts(int ClubGames, int Analyses, int Copies, int Skipped);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await RunOnceAsync(db, logger, stoppingToken) is { } c && (c.ClubGames + c.Analyses + c.Copies + c.Skipped) > 0)
                logger.LogInformation(
                    "Vereins-Datenbank: Schach-/Mattzeichen nachgetragen in {Games} Vereinspartien, {Analyses} Analysen, {Copies} Kopien ({Skipped} nicht angefasst).",
                    c.ClubGames, c.Analyses, c.Copies, c.Skipped);
        }
        catch (OperationCanceledException)
        {
            // Herunterfahren während des Laufs: der nächste Start macht weiter (Marke noch nicht gesetzt).
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Vereins-Datenbank: Nachtragen der Schach-/Mattzeichen fehlgeschlagen.");
        }
    }

    /// <summary>Läuft nur, solange <see cref="DoneKey"/> nicht gesetzt ist; <c>null</c> = schon erledigt.</summary>
    internal static async Task<Counts?> RunOnceAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var mark = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == DoneKey, ct);
        if (mark?.Value == DoneValue) return null;
        var counts = await RepairAsync(db, logger, ct);
        if (mark is null) db.SystemSettings.Add(mark = new SystemSetting { Key = DoneKey });
        mark.Value = DoneValue;
        mark.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return counts;
    }

    /// <summary>Die Regel auf den Bestand anwenden (ohne Marke).</summary>
    internal static async Task<Counts> RepairAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var skipped = 0;
        bool Fix(string kind, int id, string pgn, Action<string> set)
        {
            var (fixedPgn, problem) = PgnCheckMarks.Repair(pgn);
            if (problem != null)
            {
                skipped++;
                logger.LogWarning("Vereins-Datenbank: Schach-/Mattzeichen in {Kind} {Id} nicht nachgetragen ({Problem}).", kind, id, problem);
                return false;
            }
            if (fixedPgn == null) return false;
            set(fixedPgn);
            return true;
        }

        var games = 0;
        foreach (var g in await db.LeagueClubGames.IgnoreQueryFilters().OrderBy(g => g.Id).ToListAsync(ct))
            if (Fix("Vereinspartie", g.Id, g.Pgn, p => g.Pgn = p)) games++;
        var analyses = 0;
        foreach (var a in await db.GameAnalyses.Where(a => a.LeagueClubGameId != null).OrderBy(a => a.Id).ToListAsync(ct))
            if (Fix("Analyse", a.Id, a.Pgn, p => { a.Pgn = p; a.UpdatedAt = DateTime.UtcNow; })) analyses++;
        var copies = 0;
        foreach (var s in await db.SavedGames.Where(s => s.LeagueClubGameId != null).OrderBy(s => s.Id).ToListAsync(ct))
            if (Fix("Kopie", s.Id, s.Pgn, p => s.Pgn = p)) copies++;
        if (games + analyses + copies > 0) await db.SaveChangesAsync(ct);
        return new Counts(games, analyses, copies, skipped);
    }
}
