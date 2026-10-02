using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;

namespace RookHub.Api.Services.League;

/// <summary>
/// Wie viele Partien im Bestand stecken, je Quelle (0.626.0, Wunsch 2026-10-01: „auf der Hauptseite, wo du die Prognose
/// gemacht hast, ausweisen: x Spiele aus Lumbra, y aus ChessBase, z aus Lichess, w aus chess.com …").
/// <para><b>Brettpartien</b> liegen je Spieler in <see cref="Models.LeaguePlayerProfile.Pgn"/> — eine Partie zwischen zwei
/// Ligaspielern steht also ZWEIMAL da. Gezählt wird jede Partie einmal (Schlüssel: beide Namen nur aus Buchstaben + Datum +
/// Runde, gelesen aus dem Kopf, ohne die Züge nachzuspielen); die Quelle ist die der zuerst gesehenen Fassung, ermittelt mit
/// derselben Regel wie die Karte (<see cref="LeagueProfileBuilder.StoredSource"/>). Dazu die Vereinspartien
/// (<see cref="Models.LeagueClubGame"/>). <b>Online-Partien</b>: je Seite die verschiedenen Partie-Kennungen.</para>
/// <para>Das Zählen liest alle Profile (Prod 01.10.2026: 595 Profile, 58 MB PGN) — deshalb <see cref="CacheFor"/> im Speicher.</para>
/// <para><b>Liga und Begegnung</b> (0.628.0, Tabelle „Quelle | Gesamt | Liga | Begegnung" — Fassung B des Entwurfs vom 01.10.2026):
/// dieselbe Zählung für die Spieler aller Meldelisten einer Liga (Block <c>league</c>, 30 min gemerkt je Liga) bzw. für die
/// Meldeliste des Gegners der gewählten Begegnung (Block <c>opponent</c>, jedes Mal frisch — nur ein paar Profile), beide über
/// <see cref="PlayersAsync"/>. Online je Seite mit der Zahl der Konten, damit die Oberfläche „kein Konto" von „0 Partien"
/// unterscheidet.</para>
/// </summary>
public sealed class LeagueGameSources(AppDbContext db, IMemoryCache? cache = null)
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(30);
    private const string CacheKey = "league-game-sources";
    /// <summary>Mehr Spieler hat keine Meldeliste — Deckel für die frei übergebene Liste.</summary>
    public const int MaxOpponentPlayers = 40;

    /// <summary>Anzeige je Quelle; unbekannte Quellen (über <c>POST /api/league/admin/games</c> frei benannt) stehen, wie sie heißen.</summary>
    public static string Label(string source) => source switch
    {
        "Lumbra" => "Lumbra",
        "Mega" => "ChessBase-Megabase",
        "chess-results" => "chess-results",
        LeagueBroadcastImport.Source => "Lichess-Übertragungen",
        LeagueProfileStore.ClubSource => "Vereins-Datenbank",
        LeagueOnlineSites.Lichess => "Lichess",
        LeagueOnlineSites.ChessCom => "chess.com",
        _ => source,
    };

    /// <summary>Gesamt (30 min gemerkt); mit <paramref name="leagueTnr"/> der Block <c>league</c> (alle Meldelisten dieser Liga), mit
    /// <paramref name="opponentFides"/> der Block <c>opponent</c>. <paramref name="onlySure"/> = Teilen-Link: online nur „gesicherte"
    /// Konten (wie die Karte dort).</summary>
    public async Task<JsonObject> GetAsync(CancellationToken ct, IEnumerable<string?>? opponentFides = null, bool onlySure = false,
        int? leagueTnr = null)
    {
        var res = await TotalsAsync(ct);
        if (leagueTnr is { } tnr) res["league"] = await LeagueAsync(tnr, onlySure, ct);
        var fides = (opponentFides ?? []).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f!.Trim()).Distinct()
            .Take(MaxOpponentPlayers).ToList();
        if (fides.Count > 0) res["opponent"] = await PlayersAsync(fides, onlySure, ct);
        return res;
    }

    /// <summary>Alle Spieler der Meldelisten dieser Liga (<see cref="Models.LeaguePlayer"/> mit FIDE-ID) — 30 min gemerkt; eine
    /// unbekannte Liga zählt 0 Spieler.</summary>
    private async Task<JsonObject> LeagueAsync(int tnr, bool onlySure, CancellationToken ct)
    {
        var key = $"{CacheKey}:league:{tnr}:{onlySure}";
        if (cache?.TryGetValue(key, out JsonObject? hit) == true && hit is not null) return (JsonObject)hit.DeepClone();
        var fides = await db.LeaguePlayers.AsNoTracking().Where(p => p.Tnr == tnr && p.FideId != null && p.FideId != "")
            .Select(p => p.FideId!).Distinct().ToListAsync(ct);
        var r = await PlayersAsync(fides, onlySure, ct);
        cache?.Set(key, r, CacheFor);
        return (JsonObject)r.DeepClone();
    }

    /// <summary>
    /// Partien der Spieler <paramref name="fides"/> je Quelle → <c>{ players, board{ Quelle: n }, boardTotal, online{ Seite: { games,
    /// accounts } }, onlineTotal, onlineAccounts }</c>. Eine Partie zweier dieser Spieler zählt einmal.
    /// </summary>
    public async Task<JsonObject> PlayersAsync(IReadOnlyCollection<string> fides, bool onlySure, CancellationToken ct)
    {
        var board = new Dictionary<string, int>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var pgn in db.LeaguePlayerProfiles.AsNoTracking().Where(p => fides.Contains(p.FideId)).Select(p => p.Pgn)
                           .AsAsyncEnumerable().WithCancellation(ct))
            CountBoard(pgn, seen, board);
        var club = await db.LeagueClubGames.AsNoTracking()
            .CountAsync(g => (g.WhiteFide != null && fides.Contains(g.WhiteFide)) || (g.BlackFide != null && fides.Contains(g.BlackFide)), ct);
        if (club > 0) board[LeagueProfileStore.ClubSource] = club;
        var accounts = await db.LeagueOnlineAccounts.AsNoTracking()
            .Where(a => fides.Contains(a.FideId) && (!onlySure || a.Confidence == LeagueOnlineAccountService.Sure))
            // nur Spieler von LeagueHub — Konten aus der Spielervorbereitung zählen hier nicht mit (0.638.0)
            .Where(a => db.LeaguePlayers.Any(p => p.FideId == a.FideId) || db.LeaguePlayerProfiles.Any(p => p.FideId == a.FideId))
            .Select(a => new { a.Id, a.Site }).ToListAsync(ct);
        var ids = accounts.Select(a => a.Id).ToList();
        var games = ids.Count == 0 ? new Dictionary<string, int>() : (await db.LeagueOnlineGames.AsNoTracking()
                .Where(g => ids.Contains(g.AccountId))
                .GroupBy(g => g.Account.Site)
                .Select(g => new { Site = g.Key, Games = g.Select(x => x.ExternalId).Distinct().Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.Site, x => x.Games);
        var online = new JsonObject();
        foreach (var site in accounts.Select(a => a.Site).Distinct().OrderBy(s => s, StringComparer.Ordinal))
            online[site] = new JsonObject { ["games"] = games.GetValueOrDefault(site), ["accounts"] = accounts.Count(a => a.Site == site) };
        var boardJson = new JsonObject();
        foreach (var (k, v) in board.OrderByDescending(kv => kv.Value)) boardJson[k] = v;
        return new JsonObject
        {
            ["players"] = fides.Count, ["board"] = boardJson, ["boardTotal"] = board.Values.Sum(),
            ["online"] = online, ["onlineTotal"] = games.Values.Sum(), ["onlineAccounts"] = accounts.Count,
        };
    }

    private async Task<JsonObject> TotalsAsync(CancellationToken ct)
    {
        if (cache?.TryGetValue(CacheKey, out JsonObject? hit) == true && hit is not null) return (JsonObject)hit.DeepClone();
        var board = new Dictionary<string, int>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var pgn in db.LeaguePlayerProfiles.AsNoTracking().Select(p => p.Pgn).AsAsyncEnumerable().WithCancellation(ct))
            CountBoard(pgn, seen, board);
        var club = await db.LeagueClubGames.AsNoTracking().CountAsync(ct);
        if (club > 0) board[LeagueProfileStore.ClubSource] = club;
        var online = (await db.LeagueOnlineGames.AsNoTracking()
                // nur Spieler von LeagueHub — Konten aus der Spielervorbereitung zählen hier nicht mit (0.637.0)
                .Where(g => db.LeaguePlayers.Any(p => p.FideId == g.FideId) || db.LeaguePlayerProfiles.Any(p => p.FideId == g.FideId))
                .GroupBy(g => g.Account.Site)
                .Select(g => new { Site = g.Key, Games = g.Select(x => x.ExternalId).Distinct().Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.Site, x => x.Games);
        var res = new JsonObject
        {
            ["board"] = Rows(board), ["boardTotal"] = board.Values.Sum(),
            ["online"] = Rows(online), ["onlineTotal"] = online.Values.Sum(),
            ["countedAt"] = DateTime.UtcNow.ToString("O"),
        };
        cache?.Set(CacheKey, res, CacheFor);
        return (JsonObject)res.DeepClone();
    }

    private static JsonArray Rows(Dictionary<string, int> counts) => new(counts.Where(kv => kv.Value > 0)
        .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
        .Select(kv => (JsonNode)new JsonObject { ["key"] = kv.Key, ["label"] = Label(kv.Key), ["games"] = kv.Value }).ToArray());

    /// <summary>
    /// Die Partien EINES gespeicherten PGN zählen — jede, deren Schlüssel noch nicht in <paramref name="seen"/> steht, unter ihrer
    /// Quelle. Liest nur die Kopfzeilen: eine neue Partie beginnt mit einer Kopfzeile nach Zugtext.
    /// </summary>
    public static void CountBoard(string pgn, HashSet<string> seen, Dictionary<string, int> counts)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        var inMoves = false;
        void Flush()
        {
            if (headers.Count == 0) return;
            var key = string.Join('|', Letters(headers.GetValueOrDefault("White")), Letters(headers.GetValueOrDefault("Black")),
                headers.GetValueOrDefault("Date") ?? "", headers.GetValueOrDefault("Round") ?? "");
            if (seen.Add(key))
            {
                var src = LeagueProfileBuilder.StoredSource(headers);
                counts[src] = counts.GetValueOrDefault(src) + 1;
            }
            headers.Clear();
        }
        foreach (var raw in pgn.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                if (inMoves) { Flush(); inMoves = false; }
                var sp = line.IndexOf(' ');
                var q1 = line.IndexOf('"');
                var q2 = line.LastIndexOf('"');
                if (sp > 1 && q1 > sp && q2 > q1) headers[line[1..sp]] = line[(q1 + 1)..q2];
            }
            else inMoves = true;
        }
        Flush();
    }

    private static string Letters(string? s) => s is null ? "" : new string(s.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());
}
