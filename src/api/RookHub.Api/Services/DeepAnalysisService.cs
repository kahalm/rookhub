using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// „Tiefe Analyse" einer Stellung (0.686.0, Wunsch 2026-10-06): Vereinsmitglieder lassen die Stellung auf dem Brett
/// über das ⋮-Menü der Partieseite tiefer rechnen — Stockfish bis Tiefe <see cref="StockfishDepth"/> und Lc0 bis
/// <see cref="Lc0Nodes"/> Knoten (oder bis Lc0 per Smart Pruning selbst aufhört).
///
/// <para>Zwei gewöhnliche Hintergrund-Aufträge des NUTZERS (<see cref="AnalysisJobService.CreateAsync"/>): Stockfish
/// auf der Haus-Engine (<see cref="EngineOwnerResolver"/>), Lc0 auf der Registrierung namens
/// <c>ClubSecondEngine:EngineName</c> — dieselbe wie bei den Vereinspartien. Beide sind NORMALE Aufträge und verdrängen
/// damit die Stapelarbeit auf ihrer Engine. Die Seite zeigt den Fortschritt über <c>GET /api/analysis-jobs/{id}</c> und
/// <c>/live</c>.</para>
///
/// <para>Je Nutzer EINE tiefe Analyse zur Zeit: dieselbe Stellung noch einmal = dieselben Aufträge (auch fertige — dann
/// steht das Ergebnis sofort da); eine andere Stellung räumt die noch offenen der vorigen ab.</para>
/// </summary>
public class DeepAnalysisService(AppDbContext db, AnalysisJobService jobs, IConfiguration config)
{
    public const int StockfishDepth = 40;
    public const long Lc0Nodes = 500_000;
    public const int MultiPv = 3;
    /// <summary>Titel-Kennung der Aufträge — daran erkennt der Dienst die eigenen wieder.</summary>
    public const string StockfishTitle = "Tiefe Analyse · Stockfish";
    public const string Lc0Title = "Tiefe Analyse · Lc0";

    private string? Lc0EngineName =>
        (config["ClubSecondEngine:EngineName"] ?? ClubSecondEngineScheduler.DefaultEngineName).Trim() is { Length: > 0 } n ? n : null;

    /// <summary>Die eigenen tiefen Analysen (höchstens eine Stellung, zwei Aufträge, ohne gescheiterte) — die Partieseite
    /// zeigt ihre Linien unter der Partie, sobald sie weiter sind als die hinterlegten (0.690.0).</summary>
    public async Task<List<AnalysisJobDto>> ListAsync(int userId, CancellationToken ct = default)
    {
        var jobs = await db.AnalysisJobs.AsNoTracking()
            .Where(j => j.UserId == userId && (j.Title == StockfishTitle || j.Title == Lc0Title)
                && j.Status != AnalysisJobStatus.Failed)
            .OrderByDescending(j => j.CreatedAt).ToListAsync(ct);
        return jobs.Select(AnalysisJobService.ToDto).ToList();
    }

    public async Task<DeepAnalysisDto> StartAsync(int userId, string? fen, CancellationToken ct = default)
    {
        fen = (fen ?? string.Empty).Trim();
        if (fen.Length is 0 or > 120 || !AnalysisJobService.IsLegalFen(fen)) throw new ArgumentException("Invalid FEN");

        var mine = await db.AnalysisJobs
            .Where(j => j.UserId == userId && (j.Title == StockfishTitle || j.Title == Lc0Title))
            .ToListAsync(ct);
        // Andere Stellung: deren offene Aufträge weg — eine tiefe Analyse zur Zeit.
        foreach (var old in mine.Where(j => j.Fen != fen && j.Status != AnalysisJobStatus.Done))
            await jobs.DeleteAsync(userId, old.Id, ct);
        var same = mine.Where(j => j.Fen == fen && j.Status != AnalysisJobStatus.Failed).ToList();

        var sf = same.FirstOrDefault(j => j.Title == StockfishTitle);
        var lc0 = same.FirstOrDefault(j => j.Title == Lc0Title);

        AnalysisJobDto? sfDto = sf is null ? null : AnalysisJobService.ToDto(sf);
        if (sfDto is null)
        {
            var owner = await EngineOwnerResolver.ResolveAsync(db, userId, ct)
                        ?? throw new InvalidOperationException("No engine available");
            sfDto = await jobs.CreateAsync(userId, new CreateAnalysisJobRequest
            {
                Fen = fen, TargetDepth = StockfishDepth, MultiPv = MultiPv, Title = StockfishTitle,
            }, ct, remember: false, engineOwnerUserId: owner);
        }

        AnalysisJobDto? lc0Dto = lc0 is null ? null : AnalysisJobService.ToDto(lc0);
        if (lc0Dto is null && Lc0EngineName is { } name)
        {
            var regs = await db.ExternalEngineRegistrations.AsNoTracking()
                .Where(r => r.Name == name).Select(r => new { r.Id, r.UserId }).ToListAsync(ct);
            if (regs.Count == 1)
                lc0Dto = await jobs.CreateAsync(userId, new CreateAnalysisJobRequest
                {
                    Fen = fen, TargetDepth = StockfishDepth, TargetNodes = Lc0Nodes, MultiPv = MultiPv,
                    EngineId = regs[0].Id, Title = Lc0Title,
                }, ct, remember: false, engineOwnerUserId: regs[0].UserId);
        }
        return new DeepAnalysisDto(sfDto, lc0Dto, StockfishDepth, Lc0Nodes);
    }
}
