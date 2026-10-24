using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.Repertoires;
using RookHub.Api.Services.Scoresheet;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Social;
using RookHub.Api.DTOs.Games;
using RookHub.Api.DTOs.Scoresheet;
using static RookHub.Api.Services.Games.SavedGamePgn;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Geteilte Partie (Zerlegung Z13 aus <see cref="SavedGameService"/>): öffentliche Sicht über das ShareToken, aus wessen
/// Sicht sie aufgeht und die fehlenden Elo aus den Liga-Meldelisten.
/// </summary>
internal sealed class SavedGameSharing
{
    private readonly AppDbContext _db;

    internal SavedGameSharing(AppDbContext db) => _db = db;

    /// <summary>Öffentliche Sicht über das ShareToken; null wenn unbekannt.</summary>
    /// <param name="callerUserId">Angemeldeter Aufrufer (oder <c>null</c>): ist er der Besitzer, traegt die Antwort
    /// <see cref="SharedGameDto.OwnGameId"/> — die Seite wechselt dann auf seine eigene Ansicht.</param>
    public async Task<SharedGameDto?> GetSharedAsync(string token, int? callerUserId = null)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var g = await _db.SavedGames.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ShareToken == token);
        if (g == null) return null;
        var profile = await _db.UserProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == g.UserId);
        var (whiteElo, blackElo) = await WithLeagueEloAsync(g.LeagueClubGameId,
            ParseEloHeader(g.Pgn, "WhiteElo"), ParseEloHeader(g.Pgn, "BlackElo"));
        return new SharedGameDto
        {
            Source = g.Source,
            White = g.White,
            Black = g.Black,
            Result = g.Result,
            PlayedAt = g.PlayedAt,
            SourceUrl = SafeSourceUrl(g.SourceUrl),
            Pgn = g.Pgn,
            CreatedAt = g.CreatedAt,
            WhiteElo = whiteElo,
            BlackElo = blackElo,
            OwnerSide = DetermineOwnerSide(g, profile),
            OwnGameId = callerUserId is int caller && caller == g.UserId ? g.Id : null,
            Recap = (await GameRecapService.CurrentAsync(_db, g.Id, g.ReviewLanguage))?.Text,
        };
    }

    /// <summary>Welche Seite spielte der Besitzer? Vergleich der Spielernamen mit seinem
    /// Plattform-Username (lichess/chess.com je nach Quelle, case-insensitiv) — dieselbe
    /// Logik wie das clientseitige Flippen der eigenen Nachspiel-Ansicht (games-list.isFlipped).</summary>
    internal static string? DetermineOwnerSide(SavedGame g, UserProfile? profile)
    {
        // Selbst festgelegt schlägt jede Vermutung (0.531.0).
        if (g.OwnerSide is "white" or "black") return g.OwnerSide;
        var myName = g.Source == "lichess" ? profile?.LichessUsername : profile?.ChessComUsername;
        if (string.IsNullOrWhiteSpace(myName)) return null;
        if (string.Equals(g.Black?.Trim(), myName.Trim(), StringComparison.OrdinalIgnoreCase)) return "black";
        if (string.Equals(g.White?.Trim(), myName.Trim(), StringComparison.OrdinalIgnoreCase)) return "white";
        return null;
    }

    /// <summary>
    /// Fehlende Elo einer mit einer Vereinspartie verbundenen Kopie aus den Liga-Meldelisten (0.737.0, Wunsch 2026-10-10:
    /// „beim Anschauen und Teilen der Partie die Elo der Spieler oben im Namen dazu"): über die FIDE-ID der Seite (bei
    /// „Schwaz" die interne — die Kopie trägt ohnehin den echten Namen), bevorzugt die Liga der zugeordneten Paarung, sonst die
    /// jüngste Meldeliste; <c>EloI ?? EloN</c>. Was schon da ist, bleibt.
    /// </summary>
    internal async Task<(int? White, int? Black)> WithLeagueEloAsync(int? clubGameId, int? white, int? black)
    {
        if (clubGameId is not int id || (white != null && black != null)) return (white, black);
        var c = await _db.LeagueClubGames.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { W = x.WhiteFide ?? x.WhiteRealFide, B = x.BlackFide ?? x.BlackRealFide, x.LeagueTnr }).FirstOrDefaultAsync();
        if (c == null) return (white, black);
        async Task<int?> Of(string? fide)
        {
            if (string.IsNullOrWhiteSpace(fide)) return null;
            var rows = await _db.LeaguePlayers.AsNoTracking().Where(p => p.FideId == fide)
                .Select(p => new { p.Tnr, Elo = p.EloI ?? p.EloN }).ToListAsync();
            return PlausibleElo(rows.Where(r => r.Elo > 0).OrderByDescending(r => r.Tnr == c.LeagueTnr).ThenByDescending(r => r.Tnr)
                .Select(r => r.Elo).FirstOrDefault());
        }
        return (white ?? await Of(c.W), black ?? await Of(c.B));
    }
}
