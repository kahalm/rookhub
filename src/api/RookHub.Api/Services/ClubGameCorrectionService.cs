using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Services;

/// <summary>
/// Vereinspartie und ihre Kopien zusammen korrigieren (0.660.0, Wunsch 2026-10-05: „aus einem Scoresheet ein Ligagame gemacht
/// und in meine Partien kopiert — die sollen verbunden bleiben, damit eine Korrektur alles korrigiert"; Entscheidung: alle
/// Kopien ziehen mit). Die Vereinspartie ist die Quelle:
/// <list type="bullet">
/// <item><see cref="CorrectClubAsync"/>: Züge der Vereinspartie neu (<see cref="LeagueClubService.CorrectMovesAsync"/>), dann
/// jede verbundene Kopie (<see cref="SavedGameService.ApplyClubMovesAsync"/>) und der Stand im aufbewahrten Formular.</item>
/// <item><see cref="FromCopyAsync"/>: eine Kopie wurde korrigiert — darf der Besitzer die Vereinspartie korrigieren
/// (Hochladender/Verwalter), geht es dorthin und in alle anderen Kopien; sonst löst sich seine Kopie.</item>
/// </list>
/// </summary>
public sealed class ClubGameCorrectionService(AppDbContext db, LeagueClubService club, ScoresheetScanService scans,
    ILogger<ClubGameCorrectionService> log)
{
    public async Task<(LeagueClubGame? Game, string? Reason)> CorrectClubAsync(int userId, bool canManage, int clubGameId,
        IReadOnlyList<string> moves, List<ScoresheetPly>? plies, int? exceptCopy = null, CancellationToken ct = default)
    {
        var (game, sans, reason) = await club.CorrectMovesAsync(userId, canManage, clubGameId, moves, ct);
        if (game == null || sans == null) return (null, reason);
        var copies = await SavedGameService.ApplyClubMovesAsync(db, clubGameId, sans, exceptCopy, ct);
        await scans.SaveClubEditStateAsync(clubGameId, plies, game.Pgn, ct);
        if (copies > 0) log.LogInformation("Vereinspartie {Id}: Korrektur in {Copies} Kopien übernommen", clubGameId, copies);
        return (game, null);
    }

    /// <summary>Nach dem Speichern einer Kopie (<see cref="SavedGameService.UpdateAsync"/>). → <c>true</c> = in die
    /// Vereinspartie übernommen, <c>false</c> = nichts zu tun oder die Kopie hat sich gelöst.</summary>
    public async Task<bool> FromCopyAsync(int userId, bool canManage, int savedGameId, List<ScoresheetPly>? plies, CancellationToken ct = default)
    {
        var g = await db.SavedGames.FirstOrDefaultAsync(x => x.Id == savedGameId && x.UserId == userId, ct);
        if (g?.LeagueClubGameId is not { } clubId) return false;
        var source = await db.LeagueClubGames.AsNoTracking().FirstOrDefaultAsync(x => x.Id == clubId, ct);
        var sans = GamePlies.Parse(g.Pgn, LeagueClubService.MaxPlies)?.Plies.Select(p => p.San).ToList() ?? new List<string>();
        if (source == null)
        {
            g.LeagueClubGameId = null;   // die Vereinspartie gibt es nicht mehr
            await db.SaveChangesAsync(ct);
            return false;
        }
        if (LeagueClubService.HashOf(sans) == source.MovesHash) return false;   // Züge gleich — nur Kopfdaten geändert
        if (!await club.CanCorrectAsync(userId, canManage, clubId, ct))
        {
            g.LeagueClubGameId = null;   // eigene Fassung — die Vereinspartie bleibt, wie sie ist
            await db.SaveChangesAsync(ct);
            log.LogInformation("Kopie {Copy} von Vereinspartie {Club}: eigene Korrektur, Verbindung gelöst", savedGameId, clubId);
            return false;
        }
        var (game, reason) = await CorrectClubAsync(userId, canManage, clubId, sans, plies, savedGameId, ct);
        if (game == null) log.LogWarning("Kopie {Copy}: Korrektur ging nicht in Vereinspartie {Club} ({Reason})", savedGameId, clubId, reason);
        return game != null;
    }
}
