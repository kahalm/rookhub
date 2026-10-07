using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Übernahme des Bestands aus der Python-Fassung (~/claude/league-analyzer, export_bundle.py):
/// Ligen/Paarungen/Aufstellungen (ersetzen), Spielerprofile samt PGN (einfügen/aktualisieren, in Portionen),
/// Online-Konten (ersetzen). Danach <see cref="LeagueService.RebuildViewsAsync"/>.
/// </summary>
public sealed class LeagueImportService
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public sealed record TournamentIn(int Tnr, string Name, string Season, int Level, string League, string? Grp,
        string Stage, bool Aborted, string? Start, string? End, int? Rounds);
    public sealed record RoundIn(int Tnr, int Round, string? Date);
    public sealed record MatchIn(int Tnr, int Round, int? MatchNo, string Home, string Away, double? HomePts,
        double? AwayPts, string? Date, string? Time, string? Venue);
    public sealed record GameIn(int Tnr, int Round, int MatchNo, int Board, string HomeTeam, string AwayTeam,
        string? HomePlayer, string? AwayPlayer, string? HomeTitle, string? AwayTitle, string? HomeColor, string? Result,
        double? HomeScore, double? AwayScore, int Forfeit, string? HomeFide, string? AwayFide, int? HomeRb, int? AwayRb,
        int? HomeElo, int? AwayElo, string? PgnId);
    public sealed record PlayerIn(int Tnr, string Team, int? RosterBoard, int? StartNr, string? Title, string Name,
        string NameKey, string? FideId, int? EloI, int? EloN, string? Fed, double? Points, int? Games, int? EloPerf);
    public sealed record AccountIn(string Fide, string Site, string User, string Url, string Confidence, string? Evidence);
    public sealed record ProfileIn(string Fide, string Name, int N, JsonObject Profile, string? Pgn, DateTime? CrFetchedAt);

    public sealed record Bundle(
        List<TournamentIn>? Tournaments, List<RoundIn>? Rounds, List<MatchIn>? Matches, List<GameIn>? Games,
        List<PlayerIn>? Players, List<AccountIn>? Accounts, List<ProfileIn>? Profiles);

    private readonly AppDbContext _db;
    public LeagueImportService(AppDbContext db) => _db = db;

    public async Task<JsonObject> ImportAsync(Bundle b, CancellationToken ct)
    {
        var res = new JsonObject();
        if (b.Tournaments is not null)
        {
            // Ersetzen: Paarungen/Aufstellungen sind Abbild von chess-results, kein eigener Stand. Die Zeilen
            // entstehen VOR der Transaktion — ein Wiederholversuch der Execution-Strategy fügt dieselben Objekte ein.
            var now = DateTime.UtcNow;
            var tournaments = b.Tournaments.Select(t => new LeagueTournament
            {
                Tnr = t.Tnr, Name = t.Name, Season = t.Season, Level = t.Level, League = t.League, Grp = t.Grp ?? "",
                Stage = t.Stage, Aborted = t.Aborted, Start = t.Start, End = t.End, Rounds = t.Rounds, UpdatedAt = now,
            }).ToList();
            var rounds = (b.Rounds ?? new()).Select(r => new LeagueRound { Tnr = r.Tnr, Round = r.Round, Date = LeagueDates.Parse(r.Date) }).ToList();
            var matches = (b.Matches ?? new()).Select(m => new LeagueMatch
            {
                Tnr = m.Tnr, Round = m.Round, MatchNo = m.MatchNo, Home = m.Home, Away = m.Away, HomePts = m.HomePts,
                AwayPts = m.AwayPts, Date = m.Date, Time = m.Time, Venue = Trim(m.Venue, 300),
            }).ToList();
            // Brettpaarungen je Versuch frisch (Merge setzt ihre Id) und ZUSAMMENGEFÜHRT statt ersetzt: je (Tnr, Runde, Begegnung,
            // Brett) bleibt die Id — an ihr hängen die festen Ligapaarungen der Vereinspartien (LeagueGameLinks, 0.716.1).
            List<LeagueGame> Games() => (b.Games ?? new()).Select(g => new LeagueGame
            {
                Tnr = g.Tnr, Round = g.Round, MatchNo = g.MatchNo, Board = g.Board, HomeTeam = g.HomeTeam, AwayTeam = g.AwayTeam,
                HomePlayer = g.HomePlayer, AwayPlayer = g.AwayPlayer, HomeTitle = g.HomeTitle, AwayTitle = g.AwayTitle,
                HomeColor = g.HomeColor, Result = g.Result ?? "", HomeScore = g.HomeScore, AwayScore = g.AwayScore,
                Forfeit = g.Forfeit, HomeFide = g.HomeFide, AwayFide = g.AwayFide, HomeRb = g.HomeRb, AwayRb = g.AwayRb,
                HomeElo = g.HomeElo, AwayElo = g.AwayElo, PgnId = g.PgnId,
            }).ToList();
            var players = (b.Players ?? new()).Select(p => new LeaguePlayer
            {
                Tnr = p.Tnr, Team = p.Team, RosterBoard = p.RosterBoard, StartNr = p.StartNr, Title = p.Title, Name = p.Name,
                NameKey = p.NameKey, FideId = string.IsNullOrEmpty(p.FideId) ? null : p.FideId, EloI = p.EloI, EloN = p.EloN,
                Fed = p.Fed, Points = p.Points, Games = p.Games, EloPerf = p.EloPerf,
            }).ToList();
            await ReplaceAllAsync(async () =>
            {
                _db.ChangeTracker.Clear();
                LeagueGameLinks.Merge(_db, await _db.LeagueGames.ToListAsync(ct), Games());
                await ClearAsync(_db.LeaguePlayers, ct);
                await ClearAsync(_db.LeagueMatches, ct);
                await ClearAsync(_db.LeagueRounds, ct);
                await ClearAsync(_db.LeagueTournaments, ct);
                _db.LeagueTournaments.AddRange(tournaments);
                _db.LeagueRounds.AddRange(rounds);
                _db.LeagueMatches.AddRange(matches);
                _db.LeaguePlayers.AddRange(players);
            }, ct);
            _db.ChangeTracker.Clear();
            await LeagueGameLinks.RelinkAsync(_db, null, ct);
            res["tournaments"] = b.Tournaments.Count;
            res["games"] = b.Games?.Count ?? 0;
            res["players"] = b.Players?.Count ?? 0;
        }
        if (b.Accounts is not null)
        {
            // In LeagueHub gepflegte Konten (Manual, 0.605.0) bleiben stehen, und ein Konto, das es schon gibt, wird nur
            // aktualisiert — sonst gingen mit ihm die geholten Online-Partien. Nur Konten aus einem früheren Import, die das
            // Bündel nicht mehr nennt, fallen weg.
            static string Key(string fide, string site, string user) => $"{fide}|{site}|{user.ToLowerInvariant()}";
            await ReplaceAllAsync(async () =>
            {
                var existing = await _db.LeagueOnlineAccounts.ToListAsync(ct);
                var manual = existing.Where(a => a.Manual).Select(a => Key(a.FideId, a.Site, a.UserName)).ToHashSet();
                var imported = existing.Where(a => !a.Manual).GroupBy(a => Key(a.FideId, a.Site, a.UserName))
                    .ToDictionary(g => g.Key, g => g.ToList());
                var seen = new HashSet<string>();
                foreach (var a in b.Accounts)
                {
                    var key = Key(a.Fide, a.Site, a.User);
                    if (manual.Contains(key) || !seen.Add(key)) continue;
                    if (imported.TryGetValue(key, out var rows))
                    {
                        rows[0].Url = a.Url;
                        rows[0].Confidence = a.Confidence;
                        rows[0].Evidence = Trim(a.Evidence, 1000);
                        continue;
                    }
                    _db.LeagueOnlineAccounts.Add(new LeagueOnlineAccount
                    {
                        FideId = a.Fide, Site = a.Site, UserName = a.User, Url = a.Url, Confidence = a.Confidence, Evidence = Trim(a.Evidence, 1000),
                    });
                }
                var gone = imported.Where(kv => !seen.Contains(kv.Key)).SelectMany(kv => kv.Value)
                    .Concat(imported.Values.SelectMany(v => v.Skip(1))).ToList();
                if (gone.Count > 0)
                {
                    var ids = gone.Select(a => a.Id).ToList();
                    if (_db.Database.IsRelational()) await _db.LeagueOnlineGames.Where(g => ids.Contains(g.AccountId)).ExecuteDeleteAsync(ct);
                    else _db.LeagueOnlineGames.RemoveRange(await _db.LeagueOnlineGames.Where(g => ids.Contains(g.AccountId)).ToListAsync(ct));
                    _db.LeagueOnlineAccounts.RemoveRange(gone);
                }
            }, ct);
            res["accounts"] = b.Accounts.Count;
        }
        if (b.Profiles is not null)
        {
            var ids = b.Profiles.Select(p => p.Fide).ToList();
            var have = await _db.LeaguePlayerProfiles.Where(p => ids.Contains(p.FideId)).ToDictionaryAsync(p => p.FideId, ct);
            foreach (var p in b.Profiles)
            {
                if (!have.TryGetValue(p.Fide, out var row))
                {
                    row = new LeaguePlayerProfile { FideId = p.Fide };
                    _db.LeaguePlayerProfiles.Add(row);
                }
                row.Name = p.Name;
                row.GameCount = p.N;
                row.ProfileJson = p.Profile.ToJsonString();
                row.Pgn = p.Pgn ?? "";
                row.CrFetchedAt = p.CrFetchedAt;
                row.UpdatedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(ct);
            // Das Bündel kennt nur die fremden Partien — Karten mit Vereinspartien neu rechnen, sonst fehlten sie bis
            // zur nächsten Vereinspartie des Spielers.
            var clubFides = await _db.LeagueClubGames.Where(g => g.WhiteFide != null && ids.Contains(g.WhiteFide)).Select(g => g.WhiteFide!)
                .Concat(_db.LeagueClubGames.Where(g => g.BlackFide != null && ids.Contains(g.BlackFide)).Select(g => g.BlackFide!))
                .Distinct().ToListAsync(ct);
            var store = new LeagueProfileStore(_db);
            foreach (var f in clubFides) await store.RebuildAsync(f, ct);
            // Das Bündel ersetzt die fremden Partien der Karten — die eingespielten Lichess-Übertragungen (0.608.0) wären damit
            // weg und kämen als „fertig" nie wieder: alle neu einspielen lassen.
            foreach (var bc in await _db.LeagueBroadcasts.ToListAsync(ct))
            {
                bc.Finished = false;
                bc.ImportedAt = null;
            }
            await _db.SaveChangesAsync(ct);
            res["profiles"] = b.Profiles.Count;
        }
        return res;
    }

    /// <summary>Alte Zeilen raus, neue rein — relational in EINER Transaktion (Execution-Strategy-Muster, siehe
    /// <c>KidsPuzzleService.ReplaceAsync</c>): scheitert das Einfügen (z. B. ein zu langer Wert), bleibt der alte
    /// Bestand stehen statt leerer Tabellen. <paramref name="work"/> muss wiederholbar sein.</summary>
    private async Task ReplaceAllAsync(Func<Task> work, CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
        {
            await work();
            await _db.SaveChangesAsync(ct);
            return;
        }
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await work();
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    /// <summary>Tabelle leeren: relational per <c>DELETE</c> ohne Laden, unter InMemory (kennt kein ExecuteDelete) über den Tracker.</summary>
    private async Task ClearAsync<T>(DbSet<T> set, CancellationToken ct) where T : class
    {
        if (_db.Database.IsRelational()) await set.ExecuteDeleteAsync(ct);
        else set.RemoveRange(await set.ToListAsync(ct));
    }

    private static string? Trim(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
