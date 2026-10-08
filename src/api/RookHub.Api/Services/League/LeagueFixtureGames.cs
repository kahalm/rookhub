using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Brettpaarungen einer GESPIELTEN Begegnung samt Partie, wo es eine gibt (0.673.0, Wunsch 2026-10-05: „bei vergangenen Runden
/// oben unter dem Ergebnis auch die Paarungen direkt anzeigen, inkl. Link zu Partien, wenn vorhanden").
/// <list type="bullet">
/// <item>Paarungen aus <see cref="LeagueGame"/> (chess-results, öffentlich): Brett, Weiß/Schwarz samt Elo, Ergebnis.</item>
/// <item>Partie zuerst aus der Vereins-Datenbank DES VEREINS DER ANFRAGE (Mandanten-Schritt 2026-10-07; über einen Teilen-Link
/// der Verein des Links): Jahr der Runde, Farben passen, jede Seite über die FIDE-ID (an der Partie bzw. intern hinter der
/// anonymisierten Seite), über den Nachnamen oder als <see cref="LeagueClub.AnonName"/> für einen Spieler des eigenen Vereins —
/// mindestens eine Seite muss über FIDE-ID oder Namen passen. Mehrere Treffer: die jüngste Partie.</item>
/// <item>Sonst aus den Spielerkarten (<see cref="LeaguePlayerProfile.Pgn"/>: chess-results, Übertragungen …): Datum höchstens
/// <see cref="DayTolerance"/> Tage neben dem Rundentermin, Nachname des Gegners auf der anderen Farbe.</item>
/// </list>
/// Ausgegeben wird nur das PGN der Partie, wie es in der jeweiligen Quelle steht (eine Vereinspartie also mit dem Vereinsnamen).
/// </summary>
public sealed class LeagueFixtureGames(AppDbContext db, ILogger<LeagueFixtureGames>? log = null)
{
    public const int DayTolerance = 3;

    public sealed record Pairing(int Board, string? White, int? WhiteElo, string? Black, int? BlackElo, string Result,
        bool Forfeit, string? Pgn, string? Source, int? ClubGameId, bool CanEdit = false);

    /// <param name="userId">Der Angemeldete (über einen Teilen-Link <c>null</c>) — entscheidet mit <paramref name="canManage"/>,
    /// ob er eine Vereinspartie bearbeiten darf (0.675.0, dieselbe Regel wie die Vereinsliste: Verwalter oder Hochladender).</param>
    public async Task<List<Pairing>> ForFixtureAsync(LeagueClub club, int tnr, int round, string team, CancellationToken ct,
        int? userId = null, bool canManage = false)
    {
        var games = await db.LeagueGames.AsNoTracking()
            .Where(g => g.Tnr == tnr && g.Round == round && (g.HomeTeam == team || g.AwayTeam == team))
            .OrderBy(g => g.Board).ToListAsync(ct);
        if (games.Count == 0) return new();
        var byId = await ForGamesAsync(club, tnr, round, games, ct, userId, canManage);
        return games.Select(g => byId[g.Id]).ToList();
    }

    /// <summary>
    /// Dieselbe Regel für beliebig viele Brettpaarungen EINER Runde auf einmal (0.724.0, Aufstellungen je Runde: „wenn ich die
    /// Partie hab, soll er nicht Züge eingeben lassen, sondern die Partie ausweisen") — Schlüssel ist <see cref="LeagueGame.Id"/>.
    /// Kosten je Aufruf, gleich für eine Begegnung wie für die ganze Runde: Rundentermin, Vereinspartien des Vereins (Jahr der
    /// Runde ODER zugeordnet), deren Zuordnung (<see cref="LeagueGameLinks.ResolveAsync"/>, ≤ 2 Abfragen) und die Spielerkarten
    /// NUR der Bretter, für die keine Vereinspartie gefunden wurde. Eine geratene Vereinspartie wird höchstens EINEM Brett
    /// zugeordnet (über eine ganze Runde könnten sonst zwei Bretter mit gleichen Nachnamen dieselbe Partie bekommen).
    /// </summary>
    public async Task<Dictionary<int, Pairing>> ForGamesAsync(LeagueClub club, int tnr, int round, IReadOnlyList<LeagueGame> games,
        CancellationToken ct, int? userId = null, bool canManage = false)
    {
        var result = new Dictionary<int, Pairing>();
        if (games.Count == 0) return result;
        var date = await db.LeagueRounds.AsNoTracking().Where(r => r.Tnr == tnr && r.Round == round)
            .Select(r => r.Date).FirstOrDefaultAsync(ct);
        var year = date?.Year;
        // fest zugeordnete Partien (0.678.0) schlagen jede Raterei — und werden nie einer ANDEREN Paarung zugeraten. Die Zuordnung
        // wird über LeagueGameLinks aufgelöst (Id, sonst Schlüssel Tnr/Runde/Begegnung/Brett): eine Partie mit TOTER Id (vor 0.716.1
        // legte jedes Aktualisieren die Paarungen neu an) zählt wie eine ohne Zuordnung und darf geraten werden — vorher fiel sie
        // aus beiden Töpfen und verschwand ganz aus den Paarungen (gemeldet 2026-10-07). Archivierte Partien blendet der globale
        // Filter aus; Vereinspartien nur des Vereins der Anfrage.
        var gameIds = games.Select(g => g.Id).Distinct().ToList();
        var pool = await db.LeagueClubGames.AsNoTracking()
            .Where(c => c.ClubId == club.Id && (c.Year == year && year != null
                || c.LeagueGameId != null && gameIds.Contains(c.LeagueGameId.Value)
                || c.LeagueTnr == tnr && c.LeagueRound == round))
            .OrderByDescending(c => c.Id).ToListAsync(ct);
        var resolved = await LeagueGameLinks.ResolveAsync(db, pool, ct, log);
        var linked = pool.Where(c => resolved.TryGetValue(c.Id, out var lg) && gameIds.Contains(lg.Id))
            .GroupBy(c => resolved[c.Id].Id).ToDictionary(x => x.Key, x => x.First());
        var clubGames = pool.Where(c => c.Year == year && year != null && !resolved.ContainsKey(c.Id)).ToList();
        var guessed = new HashSet<int>();

        var open = new List<(LeagueGame G, bool HomeWhite, (string? Name, string? Fide, int? Elo, string Team) W,
            (string? Name, string? Fide, int? Elo, string Team) B, bool Forfeit)>();
        foreach (var g in games.DistinctBy(g => g.Id))
        {
            var homeWhite = g.HomeColor != "s";
            var w = homeWhite ? (g.HomePlayer, g.HomeFide, g.HomeElo, g.HomeTeam) : (g.AwayPlayer, g.AwayFide, g.AwayElo, g.AwayTeam);
            var b = homeWhite ? (g.AwayPlayer, g.AwayFide, g.AwayElo, g.AwayTeam) : (g.HomePlayer, g.HomeFide, g.HomeElo, g.HomeTeam);
            var forfeit = g.Forfeit != 0 || w.Item1 is null || b.Item1 is null;
            if (!forfeit)
            {
                var hit = linked.GetValueOrDefault(g.Id) ?? clubGames.FirstOrDefault(c => !guessed.Contains(c.Id)
                    && SideMatches(club, c.White, c.WhiteFide ?? c.WhiteRealFide, w.Item1, w.Item2, w.Item4)
                    && SideMatches(club, c.Black, c.BlackFide ?? c.BlackRealFide, b.Item1, b.Item2, b.Item4)
                    && (Strong(club, c.White, c.WhiteFide ?? c.WhiteRealFide, w.Item1, w.Item2)
                        || Strong(club, c.Black, c.BlackFide ?? c.BlackRealFide, b.Item1, b.Item2)));
                if (hit is not null)
                {
                    if (!linked.ContainsKey(g.Id)) guessed.Add(hit.Id);
                    var canEdit = userId is { } me && (canManage || hit.UploadedByUserId == me);
                    result[g.Id] = new Pairing(g.Board, w.Item1, w.Item3, b.Item1, b.Item3, WhiteBlackResult(g.Result, homeWhite),
                        false, hit.Pgn, "club", hit.Id, canEdit);
                    continue;
                }
            }
            open.Add((g, homeWhite, w, b, forfeit));
        }

        // Spielerkarten nur für die Bretter ohne Vereinspartie — eine Karte trägt alle Partien eines Spielers (große PGNs).
        var fides = open.Where(o => !o.Forfeit).SelectMany(o => new[] { o.W.Fide, o.B.Fide })
            .Where(f => !string.IsNullOrEmpty(f)).Select(f => f!).Distinct().ToList();
        var profiles = date is null || fides.Count == 0 ? new Dictionary<string, string>() : await db.LeaguePlayerProfiles.AsNoTracking()
            .Where(p => fides.Contains(p.FideId)).ToDictionaryAsync(p => p.FideId, p => p.Pgn, ct);
        foreach (var (g, homeWhite, w, b, forfeit) in open)
        {
            string? pgn = null, source = null;
            if (!forfeit && date is { } d && FromProfiles(profiles, d, w.Name!, w.Fide, b.Name!, b.Fide) is { } raw)
                (pgn, source) = (raw, "profile");
            result[g.Id] = new Pairing(g.Board, w.Name, w.Elo, b.Name, b.Elo, WhiteBlackResult(g.Result, homeWhite), forfeit, pgn, source, null);
        }
        return result;
    }

    /// <summary>
    /// Das Ergebnis aus Sicht Weiß – Schwarz. chess-results schreibt es in der Brettpaarung aus Sicht HEIM – GAST
    /// („1 - 0" = der Heimspieler gewinnt); hat der Heimspieler Schwarz, ist es zu drehen (gemeldet 2026-10-06: Runde 1 der
    /// 1. Klasse zeigte „Ranner – Haselsberger 0 – 1", gewonnen hatte Ranner mit Weiß).
    /// </summary>
    public static string WhiteBlackResult(string result, bool homeWhite)
    {
        if (homeWhite) return result;
        var parts = result.Split(" - ");
        return parts.Length == 2 ? $"{parts[1]} - {parts[0]}" : result;
    }

    /// <summary>Passt eine Seite der Vereinspartie zum Spieler am Brett? FIDE-ID, Nachname oder die anonymisierte Seite des
    /// Vereins (<see cref="LeagueClub.AnonName"/>) für einen seiner Spieler.</summary>
    internal static bool SideMatches(LeagueClub club, string name, string? fide, string? player, string? playerFide, string playerTeam) =>
        Strong(club, name, fide, player, playerFide)
        || (club.IsAnon(name, fide) && club.OwnsTeam(playerTeam));

    private static bool Strong(LeagueClub club, string name, string? fide, string? player, string? playerFide) =>
        !string.IsNullOrEmpty(fide) && !string.IsNullOrEmpty(playerFide)
            ? fide == playerFide
            : player is not null && name.Trim() != club.AnonName
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
