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
public class DeepAnalysisService(AppDbContext db, AnalysisJobService jobs, IConfiguration config, GameAnalysisService? analyses = null)
{
    public const int StockfishDepth = 40;
    public const long Lc0Nodes = 500_000;
    public const int MultiPv = 3;
    /// <summary>Titel-Kennung der Aufträge — daran erkennt der Dienst die eigenen wieder.</summary>
    public const string StockfishTitle = "Tiefe Analyse · Stockfish";
    public const string Lc0Title = "Tiefe Analyse · Lc0";

    private string? Lc0EngineName =>
        (config["ClubSecondEngine:EngineName"] ?? ClubSecondEngineScheduler.DefaultEngineName).Trim() is { Length: > 0 } n ? n : null;

    /// <summary>
    /// Eine ganze Partie auf Lc0 analysieren (0.692.0, Wunsch 2026-10-06: „beliebige Partien via Lc0 analysieren, ein Knopf"):
    /// eine eigene Analyse des Nutzers (<see cref="GameAnalysisOrigin.Manual"/>) mit der Lc0-Registrierung und demselben
    /// Knotenziel wie die Vereinspartien (<c>ClubSecondEngine:TargetNodes</c>, Vorgabe 100 000). Die Partieseite findet sie über
    /// „gleiche Partie" und bietet den Umschalter Stockfish | Lc0 | Beide an. Gibt es schon eine (nicht gescheiterte) mit genau
    /// diesem PGN, kommt die zurück — ein zweiter Klick rechnet nichts doppelt.
    /// </summary>
    public async Task<GameAnalysisDto> StartLc0GameAsync(int userId, string? pgn, CancellationToken ct = default)
    {
        if (analyses is null) throw new InvalidOperationException("Analyses unavailable");
        if (string.IsNullOrWhiteSpace(pgn)) throw new ArgumentException("PGN missing");
        var name = Lc0EngineName ?? throw new InvalidOperationException("No Lc0 engine");
        var regs = await db.ExternalEngineRegistrations.AsNoTracking()
            .Where(r => r.Name == name).Select(r => new { r.Id, r.UserId }).ToListAsync(ct);
        if (regs.Count != 1) throw new InvalidOperationException("No Lc0 engine");
        var reg = regs[0];

        var existing = await db.GameAnalyses.AsNoTracking()
            .Where(a => a.UserId == userId && a.EngineId == reg.Id && a.Pgn == pgn && a.Status != GameAnalysisStatus.Failed)
            .OrderByDescending(a => a.Id).Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);
        if (existing is { } id && await analyses.GetAsync(userId, id, ct) is { } dto) return dto;

        var nodes = Math.Clamp(config.GetValue<long?>("ClubSecondEngine:TargetNodes") ?? 100_000,
            AnalysisJobService.MinTargetNodes, AnalysisJobService.MaxTargetNodes);
        return await analyses.CreateAsync(userId, new CreateGameAnalysisRequest
        {
            Pgn = pgn, TargetDepth = GameAnalysisDefaults.GuessTargetDepth, MultiPv = MultiPv,
            EngineId = reg.Id, TargetNodes = nodes,
        }, ct, GameAnalysisOrigin.Manual, engineOwnerUserId: reg.UserId);
    }

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
