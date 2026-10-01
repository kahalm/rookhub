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
/// </summary>
public sealed class LeagueGameSources(AppDbContext db, IMemoryCache? cache = null)
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(30);
    private const string CacheKey = "league-game-sources";

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

    public async Task<JsonObject> GetAsync(CancellationToken ct)
    {
        if (cache?.TryGetValue(CacheKey, out JsonObject? hit) == true && hit is not null) return (JsonObject)hit.DeepClone();
        var board = new Dictionary<string, int>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var pgn in db.LeaguePlayerProfiles.AsNoTracking().Select(p => p.Pgn).AsAsyncEnumerable().WithCancellation(ct))
            CountBoard(pgn, seen, board);
        var club = await db.LeagueClubGames.AsNoTracking().CountAsync(ct);
        if (club > 0) board[LeagueProfileStore.ClubSource] = club;
        var online = (await db.LeagueOnlineGames.AsNoTracking()
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
