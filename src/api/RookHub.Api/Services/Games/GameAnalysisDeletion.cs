using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.EngineBroker;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Library;
using RookHub.Api.DTOs.Games;
using RookHub.Api.DTOs.Engines;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Löschen von Partie-Analysen (Teil von <see cref="GameAnalysisService"/>, Zerlegung Z5): samt offener Aufträge und der
/// Punktepartie-Sitzungen, die per RESTRICT an der Analyse hängen.
/// </summary>
internal sealed class GameAnalysisDeletion
{
    private readonly AppDbContext _db;
    private readonly AnalysisJobService _jobs;
    private readonly ILogger _logger;

    internal GameAnalysisDeletion(AppDbContext db, AnalysisJobService jobs, ILogger logger)
    {
        _db = db;
        _jobs = jobs;
        _logger = logger;
    }

    internal async Task<bool> DeleteAsync(int userId, int id, CancellationToken ct = default)
    {
        var analysis = await _db.GameAnalyses
            .Include(g => g.Positions)
            .FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId, ct);
        if (analysis is null) return false;

        // Noch laufende Aufträge dieser Partie mitnehmen — sonst rechnet die Engine für nichts weiter.
        var openJobIds = analysis.Positions.Where(p => p.AnalysisJobId != null).Select(p => p.AnalysisJobId!.Value).ToList();
        foreach (var jobId in openJobIds)
        {
            try { await _jobs.DeleteAsync(userId, jobId, ct); }
            catch (Exception ex) { _logger.LogDebug(ex, "GameAnalysis: Auftrag {JobId} liess sich nicht loeschen", jobId); }
        }

        // Punktepartien auf dieser Analyse zuerst: die Sitzung zeigt per RESTRICT auf die Analyse
        // (zwei Cascade-Pfade auf GuessMoves waeren sonst unzulaessig, siehe AppDbContext) — ohne
        // dieses Aufraeumen scheitert das Loeschen in MySQL mit einem Fremdschluessel-Fehler (500).
        var sessions = await _db.GuessSessions
            .Where(s => s.GameAnalysisId == analysis.Id)
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (sessions.Count > 0)
        {
            _db.GuessMoves.RemoveRange(
                await _db.GuessMoves.Where(m => sessions.Contains(m.GuessSessionId)).ToListAsync(ct));
            _db.GuessSessions.RemoveRange(
                await _db.GuessSessions.Where(s => sessions.Contains(s.Id)).ToListAsync(ct));
        }

        _db.GameAnalysisPositions.RemoveRange(analysis.Positions);
        _db.GameAnalyses.Remove(analysis);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Die Analysen einer geloeschten Vereinspartie mitloeschen, samt ihrer offenen Auftraege
    /// (<see cref="DeleteAsync"/>) — sie tragen die Namen der Partie.</summary>
    internal async Task<int> DeleteForClubGameAsync(int leagueClubGameId, CancellationToken ct = default)
    {
        var ids = await _db.GameAnalyses.AsNoTracking()
            .Where(g => g.LeagueClubGameId == leagueClubGameId)
            .Select(g => new { g.Id, g.UserId })
            .ToListAsync(ct);
        var deleted = 0;
        foreach (var a in ids)
            if (await DeleteAsync(a.UserId, a.Id, ct)) deleted++;
        return deleted;
    }
}
