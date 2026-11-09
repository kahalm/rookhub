using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Services.Games;

/// <summary>Eine gespeicherte Partie im Umstellungslauf. <c>Action</c>: <c>convert</c> (wird Verweis), <c>waiting</c> (die Vereinspartie
/// hat noch keine fertige Analyse), <c>done</c> (zeigt schon auf sie), <c>diverged</c> (verbunden, aber andere Züge — bleibt).</summary>
public sealed record ClubGameReferenceItem(int SavedGameId, int UserId, int ClubGameId, int? OldAnalysisId, int? ClubAnalysisId, string Action);

public sealed record ClubGameReferenceReport(bool Applied, int Convert, int Waiting, int Done, int Diverged, int AnalysesDeleted,
    List<ClubGameReferenceItem> Items);

/// <summary>
/// Bestehende Kopien von Vereinspartien in Verweise umstellen (0.924.0, „eine Partie, eine Analyse"). Der Eintrag in
/// „Meine Partien" bleibt als Zeile stehen — daran hängen Teilen-Link, festgelegte Seite, ausgeblendete Fehler und eigene
/// Zusammenfassung, die alle über die Id der <see cref="SavedGame"/> laufen und deshalb von selbst erhalten bleiben. Umgestellt wird nur
/// der Verweis: <see cref="SavedGame.LeagueClubGameId"/> und <see cref="SavedGame.GameAnalysisId"/> auf die Hauptanalyse der
/// Vereinspartie. Die bisherige eigene Analyse wird gelöscht, wenn sie dem Besitzer gehört, aus „Partie analysieren" stammt und nichts
/// anderes auf sie zeigt. Ohne fertige Vereinsanalyse wartet der Eintrag (nächster Lauf); Vereinspartien nur aus den
/// Vereinen des Besitzers.
/// </summary>
public sealed class ClubGameReferences(AppDbContext db, GameAnalysisService analyses)
{
    public async Task<ClubGameReferenceReport> RunAsync(bool apply, CancellationToken ct = default)
    {
        var clubGames = await db.LeagueClubGames.AsNoTracking().Select(c => new { c.Id, c.MovesHash, c.ClubId }).ToListAsync(ct);
        var byHash = clubGames.GroupBy(c => c.MovesHash).ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Id).ToList());
        var byId = clubGames.ToDictionary(c => c.Id);
        var mains = (await db.GameAnalyses.AsNoTracking()
                .Where(a => a.Origin == GameAnalysisOrigin.Club && a.EngineId == null && a.LeagueClubGameId != null)
                .OrderBy(a => a.Id).Select(a => new { a.Id, a.LeagueClubGameId, a.Status }).ToListAsync(ct))
            .GroupBy(a => a.LeagueClubGameId!.Value).ToDictionary(g => g.Key, g => g.Last());
        var memberships = new Dictionary<int, HashSet<int>>();
        var items = new List<ClubGameReferenceItem>();
        var deleted = 0;

        foreach (var g in await db.SavedGames.OrderBy(s => s.Id).ToListAsync(ct))
        {
            var parts = PgnParser.SplitGames(g.Pgn).FirstOrDefault();
            if (parts.Headers is { } h && h.TryGetValue("FEN", out var fen) && !string.IsNullOrWhiteSpace(fen) && !SavedGamePgn.IsStandardStart(fen)) continue;
            var sans = PgnParser.ExtractMainlineSans(parts.MoveText ?? string.Empty);
            if (sans.Count == 0) continue;
            var hash = LeagueClubService.HashOf(sans);

            int clubId;
            if (g.LeagueClubGameId is int linked)
            {
                if (!byId.TryGetValue(linked, out var club)) continue;   // archiviert oder gelöscht
                if (club.MovesHash != hash)
                {
                    items.Add(new(g.Id, g.UserId, linked, g.GameAnalysisId, null, "diverged"));
                    continue;
                }
                clubId = linked;
            }
            else
            {
                if (!byHash.TryGetValue(hash, out var sources)) continue;
                if (!memberships.TryGetValue(g.UserId, out var mine))
                    memberships[g.UserId] = mine = await LeagueClubResolver.ClubIdsOfAsync(db, g.UserId, ct);
                var match = sources.FirstOrDefault(c => mine.Contains(c.ClubId));
                if (match is null) continue;
                clubId = match.Id;
            }

            mains.TryGetValue(clubId, out var main);
            if (main is null || main.Status != GameAnalysisStatus.Done)
            {
                items.Add(new(g.Id, g.UserId, clubId, g.GameAnalysisId, main?.Id, "waiting"));
                continue;
            }
            if (g.LeagueClubGameId == clubId && g.GameAnalysisId == main.Id)
            {
                items.Add(new(g.Id, g.UserId, clubId, g.GameAnalysisId, main.Id, "done"));
                continue;
            }
            items.Add(new(g.Id, g.UserId, clubId, g.GameAnalysisId, main.Id, "convert"));
            if (!apply) continue;

            var old = g.GameAnalysisId;
            g.LeagueClubGameId = clubId;
            g.GameAnalysisId = main.Id;
            await db.SaveChangesAsync(ct);
            if (old is int oldId && oldId != main.Id && await DeletableAsync(oldId, g.UserId, ct)
                && await analyses.DeleteAsync(g.UserId, oldId, ct)) deleted++;
        }
        return new(apply, items.Count(i => i.Action == "convert"), items.Count(i => i.Action == "waiting"),
            items.Count(i => i.Action == "done"), items.Count(i => i.Action == "diverged"), deleted, items);
    }

    private async Task<bool> DeletableAsync(int analysisId, int userId, CancellationToken ct) =>
        await db.GameAnalyses.AnyAsync(a => a.Id == analysisId && a.UserId == userId && a.Origin == GameAnalysisOrigin.SavedGame, ct)
        && !await db.SavedGames.AnyAsync(s => s.GameAnalysisId == analysisId, ct);
}
