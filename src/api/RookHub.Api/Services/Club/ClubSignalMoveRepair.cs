using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.League;

namespace RookHub.Api.Services.Club;

/// <summary>
/// Reparatur der schon gespeicherten Vereinspartien (1.022.0): ein Signal-Königszug des Siegers am Ende
/// (<see cref="SignalKingMove"/>, Befund Prod-Partie 268 „45.Ke4“) wird über den NORMALEN Korrekturweg entfernt
/// (<see cref="ClubGameCorrectionService.CorrectClubAsync"/>: PGN, Halbzüge, Zug-Hash neu, Analyse umgebaut, Spielerkarten,
/// verbundene Kopien in „Meine Partien“). Eine schon geschriebene Zusammenfassung wird neu angefordert
/// (<see cref="ClubGameSummaries.RequestAsync"/>) — sie sprach über den Signalzug. Nur nicht archivierte Partien (archivierte lassen sich
/// nicht korrigieren). <c>dryRun</c> listet nur, was entfernt würde.
/// </summary>
public sealed class ClubSignalMoveRepair(AppDbContext db, ClubGameCorrectionService corrections, LeagueClubResolver clubs,
    ClubGameSummaries summaries, ILogger<ClubSignalMoveRepair> log)
{
    /// <param name="Move">Der (zu) entfernende Zug, z. B. „45.Ke4“.</param>
    /// <param name="Error">Grund, wenn die Korrektur nicht ging (<c>illegal</c>, <c>notFound</c> …).</param>
    public sealed record Item(int ClubGameId, int ClubId, string White, string Black, string Result, int Plies, string Move, string Reason,
        bool Applied, bool SummaryRequested, string? Error);

    public sealed record Report(bool DryRun, int Scanned, List<Item> Games);

    private const int Chunk = 200;

    /// <param name="onlyId">Nur diese Vereinspartie (<c>null</c> = alle).</param>
    public async Task<Report> RunAsync(int userId, bool dryRun, int? onlyId = null, CancellationToken ct = default)
    {
        var items = new List<Item>();
        var scanned = 0;
        var lastId = 0;
        while (true)
        {
            var q = db.LeagueClubGames.AsNoTracking().Where(g => g.Id > lastId && (g.Result == "1-0" || g.Result == "0-1"));
            if (onlyId is { } only) q = q.Where(g => g.Id == only);
            var batch = await q.OrderBy(g => g.Id).Take(Chunk)
                .Select(g => new { g.Id, g.ClubId, g.White, g.Black, g.Result, g.Plies, g.Pgn }).ToListAsync(ct);
            if (batch.Count == 0) break;
            lastId = batch[^1].Id;
            foreach (var g in batch)
            {
                scanned++;
                var sans = ClubPgnText.SansOf(g.Pgn);
                if (SignalKingMove.Detect(sans, g.Result) is not { } found) continue;
                if (dryRun)
                {
                    items.Add(new Item(g.Id, g.ClubId, g.White, g.Black, g.Result, g.Plies, found.Label, found.Reason, false, false, null));
                    continue;
                }
                var club = await clubs.ByIdAsync(g.ClubId, ct);
                if (club is null)
                {
                    items.Add(new Item(g.Id, g.ClubId, g.White, g.Black, g.Result, g.Plies, found.Label, found.Reason, false, false, "noClub"));
                    continue;
                }
                var (game, reason) = await corrections.CorrectClubAsync(club, userId, canManage: true, g.Id, sans.Take(sans.Count - 1).ToList(),
                    plies: null, ct: ct);
                var summary = false;
                if (game != null && await db.ClubGameSummaries.AsNoTracking().AnyAsync(s => s.ClubGameId == g.Id, ct))
                    summary = await summaries.RequestAsync(club, g.Id, userId, ct) != null;
                log.LogInformation("Vereinspartie {Id}: Signal-Königszug {Move} entfernt ({Reason}) — {Outcome}", g.Id, found.Label, found.Reason,
                    game != null ? summary ? "Zusammenfassung neu angefordert" : "ohne Zusammenfassung" : $"Korrektur abgelehnt: {reason}");
                items.Add(new Item(g.Id, g.ClubId, g.White, g.Black, g.Result, game?.Plies ?? g.Plies, found.Label, found.Reason, game != null,
                    summary, game == null ? reason : null));
            }
            if (onlyId != null) break;
        }
        return new Report(dryRun, scanned, items);
    }
}
