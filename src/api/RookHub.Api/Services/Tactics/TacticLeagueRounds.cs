using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Services.Tactics;

/// <summary>
/// Ligarunde und Titel einer Vereinspartie für die Vereinskurse der Taktik-Ernte (aus <see cref="TacticHarvestService"/>
/// herausgezogen, 2026-11-09): Kapitel „Saison · Liga · Runde n" und die Namen der öffentlichen Paarung.
/// </summary>
public static class TacticLeagueRounds
{
    /// <summary>„Weiß – Schwarz (Jahr)". Eine anonymisierte Seite (<see cref="LeagueClub.AnonName"/>) heißt im Vereinskurs wie in
    /// der Paarung der Ligarunde (Wunsch 2026-10-04, Variante 2: „den Schwazer Gegner nennen") — aus den ÖFFENTLICHEN Paarungen,
    /// nie aus den internen <c>*RealName</c>-Spalten; der Kurs ist nur für die Gruppen des Vereins sichtbar. Ohne Ligarunde bleibt
    /// der Vereinsname.</summary>
    public static string ClubTitle(LeagueClubGame g, LeagueRound? round, LeagueClub club)
    {
        string Name(string name, string? fide, string? league) =>
            club.IsAnon(name, fide) && !string.IsNullOrWhiteSpace(league) ? league! : name;
        return $"{Name(g.White, g.WhiteFide, round?.WhitePlayer)} – {Name(g.Black, g.BlackFide, round?.BlackPlayer)}"
            + (g.Year is { } y ? $" ({y})" : "");
    }

    public sealed record LeagueRound(string Chapter, string? WhitePlayer, string? BlackPlayer);

    public static async Task<string?> LeagueRoundChapterAsync(AppDbContext db, LeagueClubGame g, LeagueClub club, CancellationToken ct) =>
        (await LeagueRoundAsync(db, g, club, ct))?.Chapter;

    /// <summary>
    /// Die Ligarunde einer Vereinspartie (Wunsch 2026-10-04: „pro Liga-Runde ein Kapitel"): eine Liga-Partie mit denselben
    /// Spielern in denselben Farben — die anonymisierte Seite (<see cref="LeagueClub.AnonName"/>) passt zu jedem Spieler des
    /// Vereins der Partie —, in der Saison des Jahres; bei mehreren gewinnt die mit gleichem Ergebnis. → „2026/27 · Landesliga ·
    /// Runde 1" samt den Spielern der Paarung, sonst <c>null</c>.
    /// </summary>
    public static async Task<LeagueRound?> LeagueRoundAsync(AppDbContext db, LeagueClubGame g, LeagueClub club, CancellationToken ct)
    {
        // fest zugeordnet (0.678.0): genau diese Paarung — über LeagueGameLinks aufgelöst (Id, sonst Schlüssel); eine tote Id
        // fällt auf das Raten unten zurück (0.716.1)
        if (await LeagueGameLinks.FindAsync(db, g, ct) is { } linked)
        {
            var l = await (from lg in db.LeagueGames.AsNoTracking()
                           join t in db.LeagueTournaments.AsNoTracking() on lg.Tnr equals t.Tnr
                           where lg.Id == linked.Id
                           select new { lg, t.Season, t.League, t.Grp }).FirstOrDefaultAsync(ct);
            if (l != null)
            {
                var lname = string.IsNullOrEmpty(l.Grp) ? l.League : $"{l.League} {l.Grp}";
                var lw = l.lg.HomeColor == "w";
                return new LeagueRound($"{l.Season} · {lname} · Runde {l.lg.Round}",
                    lw ? l.lg.HomePlayer : l.lg.AwayPlayer, lw ? l.lg.AwayPlayer : l.lg.HomePlayer);
            }
        }
        var fides = new[] { g.WhiteFide, g.BlackFide }.Where(f => !string.IsNullOrEmpty(f)).ToList();
        if (fides.Count == 0) return null;
        var rows = await (from lg in db.LeagueGames.AsNoTracking()
                          join t in db.LeagueTournaments.AsNoTracking() on lg.Tnr equals t.Tnr
                          where fides.Contains(lg.HomeFide!) || fides.Contains(lg.AwayFide!)
                          select new { lg, t.Season, t.League, t.Grp }).ToListAsync(ct);
        bool Side(string? clubFide, string clubName, string? fide, string team) =>
            clubFide != null ? clubFide == fide : club.IsAnon(clubName) && club.OwnsTeam(team);
        bool InSeason(string season) => g.Year is not { } y || (int.TryParse(season.Split('/')[0], out var s) && (s == y || s + 1 == y));
        var hits = rows.Where(r =>
        {
            var homeWhite = r.lg.HomeColor == "w";
            var (wf, wt, bf, bt) = homeWhite
                ? (r.lg.HomeFide, r.lg.HomeTeam, r.lg.AwayFide, r.lg.AwayTeam)
                : (r.lg.AwayFide, r.lg.AwayTeam, r.lg.HomeFide, r.lg.HomeTeam);
            return Side(g.WhiteFide, g.White, wf, wt) && Side(g.BlackFide, g.Black, bf, bt) && InSeason(r.Season);
        }).ToList();
        if (hits.Count == 0) return null;
        string Norm(string r) => r.Replace(" ", "").Replace("½", "1/2");
        var best = hits.OrderByDescending(r => Norm(LeagueFixtureGames.WhiteBlackResult(r.lg.Result, r.lg.HomeColor != "s")) == Norm(g.Result) ? 1 : 0).ThenByDescending(r => r.Season).First();
        var league = string.IsNullOrEmpty(best.Grp) ? best.League : $"{best.League} {best.Grp}";
        var homeWhite = best.lg.HomeColor == "w";
        return new LeagueRound($"{best.Season} · {league} · Runde {best.lg.Round}",
            homeWhite ? best.lg.HomePlayer : best.lg.AwayPlayer, homeWhite ? best.lg.AwayPlayer : best.lg.HomePlayer);
    }

    /// <summary>Titel „Weiß – Schwarz (Jahr), Zug n" und Kapitel: bei Vereinspartien die Ligarunde, sonst gefunden/verpasst.</summary>
    internal static async Task<(string Title, string Chapter)> DescribeAsync(AppDbContext db, TacticCandidate c, GameAnalysis a, LeagueClub? club, CancellationToken ct)
    {
        var moveNo = c.Ply / 2 + 1;
        var tail = $", Zug {moveNo}";
        if (a.Origin == GameAnalysisOrigin.Club && club is not null && a.LeagueClubGameId is { } gid
            && await db.LeagueClubGames.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gid, ct) is { } g)
        {
            var round = await LeagueRoundAsync(db, g, club, ct);
            return (ClubTitle(g, round, club) + tail, round?.Chapter ?? TacticHarvestService.OtherGamesChapter);
        }
        return ($"{a.White ?? "?"} – {a.Black ?? "?"}{tail}", c.Found ? "Gefunden" : "Verpasst");
    }
}
