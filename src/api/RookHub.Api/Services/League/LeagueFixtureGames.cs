using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Brettpaarungen einer GESPIELTEN Begegnung samt Partie, wo es eine gibt (0.673.0, Wunsch 2026-10-05: „bei vergangenen Runden
/// oben unter dem Ergebnis auch die Paarungen direkt anzeigen, inkl. Link zu Partien, wenn vorhanden").
/// <list type="bullet">
/// <item>Paarungen aus <see cref="LeagueGame"/> (chess-results, öffentlich): Brett, Weiß/Schwarz samt Elo, Ergebnis.</item>
/// <item>Partie zuerst aus der Vereins-Datenbank: Jahr der Runde, Farben passen, jede Seite über die FIDE-ID (an der Partie bzw.
/// intern hinter „Schwaz"), über den Nachnamen oder als „Schwaz" für einen Spieler des eigenen Vereins — mindestens eine Seite
/// muss über FIDE-ID oder Namen passen. Mehrere Treffer: die jüngste Partie.</item>
/// <item>Sonst aus den Spielerkarten (<see cref="LeaguePlayerProfile.Pgn"/>: chess-results, Übertragungen …): Datum höchstens
/// <see cref="DayTolerance"/> Tage neben dem Rundentermin, Nachname des Gegners auf der anderen Farbe.</item>
/// </list>
/// Ausgegeben wird nur das PGN der Partie, wie es in der jeweiligen Quelle steht (eine Vereinspartie also mit „Schwaz").
/// </summary>
public sealed class LeagueFixtureGames(AppDbContext db)
{
    public const int DayTolerance = 3;

    public sealed record Pairing(int Board, string? White, int? WhiteElo, string? Black, int? BlackElo, string Result,
        bool Forfeit, string? Pgn, string? Source, int? ClubGameId, bool CanEdit = false);

    /// <param name="userId">Der Angemeldete (über einen Teilen-Link <c>null</c>) — entscheidet mit <paramref name="canManage"/>,
    /// ob er eine Vereinspartie bearbeiten darf (0.675.0, dieselbe Regel wie die Vereinsliste: Verwalter oder Hochladender).</param>
    public async Task<List<Pairing>> ForFixtureAsync(int tnr, int round, string team, CancellationToken ct,
        int? userId = null, bool canManage = false)
    {
        var games = await db.LeagueGames.AsNoTracking()
            .Where(g => g.Tnr == tnr && g.Round == round && (g.HomeTeam == team || g.AwayTeam == team))
            .OrderBy(g => g.Board).ToListAsync(ct);
        if (games.Count == 0) return new();
        var date = await db.LeagueRounds.AsNoTracking().Where(r => r.Tnr == tnr && r.Round == round)
            .Select(r => r.Date).FirstOrDefaultAsync(ct);

        var fides = games.SelectMany(g => new[] { g.HomeFide, g.AwayFide }).Where(f => !string.IsNullOrEmpty(f)).Select(f => f!)
            .Distinct().ToList();
        var year = date?.Year;
        var club = year is null ? new List<LeagueClubGame>() : await db.LeagueClubGames.AsNoTracking()
            .Where(c => c.Year == year).OrderByDescending(c => c.Id).ToListAsync(ct);
        var profiles = date is null || fides.Count == 0 ? new Dictionary<string, string>() : await db.LeaguePlayerProfiles.AsNoTracking()
            .Where(p => fides.Contains(p.FideId)).ToDictionaryAsync(p => p.FideId, p => p.Pgn, ct);

        var result = new List<Pairing>();
        foreach (var g in games)
        {
            var homeWhite = g.HomeColor != "s";
            var w = homeWhite ? (g.HomePlayer, g.HomeFide, g.HomeElo, g.HomeTeam) : (g.AwayPlayer, g.AwayFide, g.AwayElo, g.AwayTeam);
            var b = homeWhite ? (g.AwayPlayer, g.AwayFide, g.AwayElo, g.AwayTeam) : (g.HomePlayer, g.HomeFide, g.HomeElo, g.HomeTeam);
            var forfeit = g.Forfeit != 0 || w.Item1 is null || b.Item1 is null;
            string? pgn = null, source = null;
            int? clubId = null;
            var canEdit = false;
            if (!forfeit)
            {
                var hit = club.FirstOrDefault(c => SideMatches(c.White, c.WhiteFide ?? c.WhiteRealFide, w.Item1, w.Item2, w.Item4)
                    && SideMatches(c.Black, c.BlackFide ?? c.BlackRealFide, b.Item1, b.Item2, b.Item4)
                    && (Strong(c.White, c.WhiteFide ?? c.WhiteRealFide, w.Item1, w.Item2)
                        || Strong(c.Black, c.BlackFide ?? c.BlackRealFide, b.Item1, b.Item2)));
                if (hit is not null)
                {
                    (pgn, source, clubId) = (hit.Pgn, "club", hit.Id);
                    canEdit = userId is { } me && (canManage || hit.UploadedByUserId == me);
                }
                else if (date is { } d && FromProfiles(profiles, d, w.Item1!, w.Item2, b.Item1!, b.Item2) is { } raw)
                    (pgn, source) = (raw, "profile");
            }
            result.Add(new Pairing(g.Board, w.Item1, w.Item3, b.Item1, b.Item3, g.Result, forfeit, pgn, source, clubId, canEdit));
        }
        return result;
    }

    /// <summary>Passt eine Seite der Vereinspartie zum Spieler am Brett? FIDE-ID, Nachname oder „Schwaz" für den eigenen Verein.</summary>
    internal static bool SideMatches(string name, string? fide, string? player, string? playerFide, string playerTeam) =>
        Strong(name, fide, player, playerFide)
        || (string.IsNullOrEmpty(fide) && name.Trim() == LeagueRefresh.OwnTeam && playerTeam.StartsWith(LeagueRefresh.OwnTeam));

    private static bool Strong(string name, string? fide, string? player, string? playerFide) =>
        !string.IsNullOrEmpty(fide) && !string.IsNullOrEmpty(playerFide)
            ? fide == playerFide
            : player is not null && name.Trim() != LeagueRefresh.OwnTeam
              && LeagueProfileBuilder.LastName(name) == LeagueProfileBuilder.LastName(player);

    /// <summary>Die Partie aus der Karte eines der beiden Spieler: Datum nahe am Rundentermin, Gegner auf der anderen Farbe.</summary>
    internal static string? FromProfiles(IReadOnlyDictionary<string, string> profiles, DateOnly date,
        string white, string? whiteFide, string black, string? blackFide)
    {
        foreach (var fide in new[] { whiteFide, blackFide })
        {
            if (fide is null || !profiles.TryGetValue(fide, out var pgn)) continue;
            foreach (var game in LeagueProfileBuilder.Parse(pgn, "profile"))
            {
                var h = game.Headers;
                if (LeagueAnalysisQueue.DateOf(h.GetValueOrDefault("Date")) is not { } gd
                    || Math.Abs(gd.DayNumber - date.DayNumber) > DayTolerance) continue;
                if (LeagueProfileBuilder.LastName(h.GetValueOrDefault("White")) == LeagueProfileBuilder.LastName(white)
                    && LeagueProfileBuilder.LastName(h.GetValueOrDefault("Black")) == LeagueProfileBuilder.LastName(black))
                    return game.Raw;
            }
        }
        return null;
    }
}
