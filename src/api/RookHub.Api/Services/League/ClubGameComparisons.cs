using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Engines;
using RookHub.Api.Models;
using RookHub.Api.Services.Engines;

namespace RookHub.Api.Services.League;

/// <summary>
/// Die Zugvergleiche zu den Fehlern einer Vereinspartie lesen (0.908.0, angelegt von <see cref="ClubMistakeComparisons"/>).
/// Lesen darf, wer die Vereinspartien des Vereins sieht (<c>league.view</c>, Prüfung im Controller) — die Vergleiche gehören
/// dem Haus-Engine-Konto, der Zugang hängt an der Partie wie bei der Analyse selbst. Gelesen wird immer die Hauptanalyse
/// (die jüngste mit Origin Club, ohne EngineId — wie <c>GET …/evals</c>).
/// </summary>
public sealed class ClubGameComparisons(AppDbContext db, MoveComparisonService comparisons)
{
    /// <summary>Die Hauptanalyse einer Partie DIESES Vereins: <c>(true, Id)</c>, ohne Analyse <c>(true, null)</c>, Partie
    /// unbekannt/fremd <c>(false, null)</c>.</summary>
    private async Task<(bool Known, int? AnalysisId)> AnalysisOfAsync(LeagueClub club, int clubGameId, CancellationToken ct)
    {
        if (!await db.LeagueClubGames.AsNoTracking().AnyAsync(g => g.Id == clubGameId && g.ClubId == club.Id, ct)) return (false, null);
        var id = await db.GameAnalyses.AsNoTracking()
            .Where(a => a.LeagueClubGameId == clubGameId && a.Origin == GameAnalysisOrigin.Club && a.EngineId == null)
            .OrderByDescending(a => a.Id).Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);
        return (true, id);
    }

    /// <summary>Alle Vergleiche der Partie nach Halbzug (leer = keine, auch ohne Analyse); <c>null</c> = Partie unbekannt/fremd.</summary>
    public async Task<List<ClubMistakeComparisonDto>?> ForGameAsync(LeagueClub club, int clubGameId, CancellationToken ct = default)
    {
        var (known, analysisId) = await AnalysisOfAsync(club, clubGameId, ct);
        if (!known) return null;
        return analysisId is int id ? await comparisons.ListForAnalysisAsync(id, ct) : new();
    }

    /// <summary>EIN Vergleich der Partie (für die volle Vergleichsseite); <c>null</c> = Partie oder Vergleich unbekannt/fremd.</summary>
    public async Task<MoveComparisonDto?> GetAsync(LeagueClub club, int clubGameId, int comparisonId, CancellationToken ct = default)
    {
        var (known, analysisId) = await AnalysisOfAsync(club, clubGameId, ct);
        return known && analysisId is int id ? await comparisons.GetForAnalysisAsync(id, comparisonId, ct) : null;
    }
}
