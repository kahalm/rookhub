using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.League;

/// <summary>
/// Die Spielerkarte aus ALLEN Quellen: gespeichert liegen in <see cref="LeaguePlayerProfile.Pgn"/> nur die fremden
/// Partien (Lumbra, chess-results), die Vereinspartien in <see cref="LeagueClubGame"/>. Karte, Partienzahl und
/// PGN-Download nehmen beides zusammen — so lässt sich eine Vereinspartie löschen, ohne das PGN umzuschreiben, und ein
/// neuer chess-results-Abruf überschreibt keine Vereinspartie.
///
/// <para><b>Doppelte über die ZÜGE</b>, nicht über die Namen: eine anonymisierte Vereinspartie heißt auf einer Seite
/// „Schwaz", dieselbe Partie von chess-results trägt den echten Namen. Gleiches Jahr + gleiche Hauptvariante = dieselbe
/// Partie; die fremde Fassung gewinnt (sie trägt das volle Datum).</para>
/// </summary>
public sealed class LeagueProfileStore
{
    public const string ClubSource = "Verein";
    private readonly AppDbContext _db;

    public LeagueProfileStore(AppDbContext db) => _db = db;

    /// <summary>Jahr + Hauptvariante — der Schlüssel, an dem dieselbe Partie in zwei Quellen erkannt wird.</summary>
    public static string MovesKey(IReadOnlyDictionary<string, string> headers, string raw)
    {
        headers.TryGetValue("Date", out var date);
        var year = date is { Length: >= 4 } && date[..4].All(char.IsDigit) ? date[..4] : "????";
        var moveText = PgnParser.SplitGames(raw).Select(g => g.MoveText).FirstOrDefault() ?? "";
        return year + "|" + string.Join(' ', PgnParser.ExtractMainlineSans(moveText));
    }

    /// <summary>Vereinspartien dazunehmen, die nicht schon unter den fremden stehen.</summary>
    public static List<LeagueProfileBuilder.Game> WithClub(List<LeagueProfileBuilder.Game> external, IEnumerable<LeagueClubGame> club)
    {
        var known = external.Select(g => MovesKey(g.Headers, g.Raw)).ToHashSet(StringComparer.Ordinal);
        var all = new List<LeagueProfileBuilder.Game>(external);
        foreach (var c in club)
        {
            var parsed = LeagueProfileBuilder.Parse(c.Pgn, ClubSource).FirstOrDefault();
            if (parsed is null || !known.Add(MovesKey(parsed.Headers, parsed.Raw))) continue;
            all.Add(parsed);
        }
        return all.OrderByDescending(g => g.Headers.TryGetValue("Date", out var d) ? d : "", StringComparer.Ordinal).ToList();
    }

    private Task<List<LeagueClubGame>> ClubGamesAsync(string fide, CancellationToken ct) =>
        _db.LeagueClubGames.AsNoTracking().Where(g => g.WhiteFide == fide || g.BlackFide == fide).ToListAsync(ct);

    private static List<LeagueProfileBuilder.Game> Stored(string? pgn) =>
        string.IsNullOrEmpty(pgn) ? new() : PgnParser.SplitGameBlocks(pgn)
            .Select(b => new LeagueProfileBuilder.Game(b.Headers, b.Raw.Trim(), LeagueProfileBuilder.StoredSource(b.Headers))).ToList();

    private async Task<string> NameAsync(string fide, string? stored, CancellationToken ct) =>
        !string.IsNullOrEmpty(stored) ? stored
            : await _db.LeaguePlayers.Where(x => x.FideId == fide).OrderByDescending(x => x.Tnr).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "";

    /// <summary>Karte eines Spielers neu rechnen: fremde Partien (optional um <paramref name="fresh"/> ergänzt, dann auch
    /// gespeichert) + Vereinspartien. Legt die Zeile an, wenn es noch keine gibt. Speichert NICHT — der Aufrufer tut es.</summary>
    public async Task RebuildAsync(string fide, CancellationToken ct, IEnumerable<LeagueProfileBuilder.Game>? fresh = null,
        DateTime? crFetchedAt = null, DateTime? now = null)
    {
        var row = await _db.LeaguePlayerProfiles.FindAsync(new object[] { fide }, ct);
        var name = await NameAsync(fide, row?.Name, ct);
        var external = Stored(row?.Pgn);
        string? externalPgn = null;
        if (fresh is not null)
        {
            external = LeagueProfileBuilder.Merge(external, fresh);
            externalPgn = LeagueProfileBuilder.Build(fide, name, external).Pgn;
        }
        var club = await ClubGamesAsync(fide, ct);
        if (row is null && club.Count == 0 && fresh is null) return;
        var (profile, _, count) = LeagueProfileBuilder.Build(fide, name, WithClub(external, club));
        if (row is null)
        {
            row = new LeaguePlayerProfile { FideId = fide };
            _db.LeaguePlayerProfiles.Add(row);
        }
        row.Name = name;
        row.GameCount = count;
        row.ProfileJson = profile.ToJsonString();
        if (externalPgn is not null) row.Pgn = externalPgn;
        if (crFetchedAt is not null) row.CrFetchedAt = crFetchedAt;
        row.UpdatedAt = now ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Die Partienzahl in den fertig gerechneten Liga-Ansichten nachziehen (Meldeliste, Feld <c>g</c>), ohne die Ligen
    /// neu zu rechnen — das bräuchte die ganze Historie im Speicher und gehört zum Knopf „Daten aktualisieren", nicht zu
    /// jeder hochgeladenen Vereinspartie. Speichert selbst.
    /// </summary>
    public async Task PatchViewCountsAsync(IReadOnlyCollection<string> fides, CancellationToken ct)
    {
        if (fides.Count == 0) return;
        var ids = fides.ToList();
        var counts = await _db.LeaguePlayerProfiles.AsNoTracking().Where(p => ids.Contains(p.FideId))
            .Select(p => new { p.FideId, p.GameCount }).ToDictionaryAsync(p => p.FideId, p => p.GameCount, ct);
        foreach (var view in await _db.LeagueViews.ToListAsync(ct))
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(view.Json) as System.Text.Json.Nodes.JsonObject;
            if (root?["fixtures"] is not System.Text.Json.Nodes.JsonObject teams) continue;
            var changed = false;
            foreach (var (_, rounds) in teams)
                foreach (var (_, fx) in rounds?.AsObject() ?? new System.Text.Json.Nodes.JsonObject())
                    foreach (var r in fx?["roster"] as System.Text.Json.Nodes.JsonArray ?? new System.Text.Json.Nodes.JsonArray())
                    {
                        var f = r?["fide"]?.GetValue<string>();
                        if (f is null || !ids.Contains(f)) continue;
                        var g = counts.GetValueOrDefault(f);
                        if (r!["g"]?.GetValue<int>() == g) continue;
                        r["g"] = g;
                        changed = true;
                    }
            if (changed) view.Json = root.ToJsonString();
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Eine Sammlung fremder Partien einspielen (<paramref name="source"/>, z. B. „Mega" für die ChessBase-Megabase): jede
    /// Partie geht an die Ligaspieler, deren FIDE-ID im Kopf steht (<c>WhiteFideId</c>/<c>BlackFideId</c>) — Namen allein
    /// zählen nicht. Zusammengeführt wie ein chess-results-Abruf (Dubletten über Datum + Nachnamen + Ergebnis, die schon
    /// gespeicherte Fassung bleibt), die Quelle steht als <see cref="LeagueProfileBuilder.SourceHeader"/> im Kopf. Danach
    /// Karten neu und die Partienzahl in den Ansichten nachgezogen. Speichert selbst.
    /// </summary>
    public async Task<(int Games, int Players)> ImportGamesAsync(string pgn, string source, CancellationToken ct)
    {
        var known = (await _db.LeaguePlayers.AsNoTracking().Where(p => p.FideId != null && p.FideId != "")
            .Select(p => p.FideId!).Distinct().ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        var byFide = new Dictionary<string, List<LeagueProfileBuilder.Game>>(StringComparer.Ordinal);
        var games = 0;
        foreach (var b in PgnParser.SplitGameBlocks(pgn))
        {
            var fides = new[] { "WhiteFideId", "BlackFideId" }
                .Select(k => b.Headers.TryGetValue(k, out var f) ? f.Trim() : null)
                .Where(f => f != null && known.Contains(f)).Select(f => f!).Distinct().ToList();
            if (fides.Count == 0) continue;
            var headers = new Dictionary<string, string>(b.Headers, StringComparer.OrdinalIgnoreCase)
            {
                [LeagueProfileBuilder.SourceHeader] = source,
            };
            var raw = b.Raw.Trim();
            if (!b.Headers.ContainsKey(LeagueProfileBuilder.SourceHeader))
                raw = PgnWriter.Tag(LeagueProfileBuilder.SourceHeader, source) + raw;
            var g = new LeagueProfileBuilder.Game(headers, raw, source);
            foreach (var f in fides)
            {
                if (!byFide.TryGetValue(f, out var list)) byFide[f] = list = new();
                list.Add(g);
            }
            games++;
        }
        var i = 0;
        foreach (var (fide, list) in byFide)
        {
            await RebuildAsync(fide, ct, list);
            if (++i % 20 == 0) { await _db.SaveChangesAsync(ct); _db.ChangeTracker.Clear(); }
        }
        await _db.SaveChangesAsync(ct);
        _db.ChangeTracker.Clear();
        await PatchViewCountsAsync(byFide.Keys.ToList(), ct);
        return (games, byFide.Count);
    }

    /// <summary>Höchstens so viele Halbzüge tief geht der Eröffnungsbaum.</summary>
    public const int TreeMaxPlies = 30;

    /// <summary>
    /// Eröffnungsbaum eines Spielers (Knopf „Eröffnungsbaum anzeigen" auf der Spielerkarte, Wunsch 2026-09-28): alle
    /// seine Partien (fremde + Vereinspartien) mit <paramref name="color"/> („w"/„s"), die mit <paramref name="line"/>
    /// beginnen (Züge mit Leerzeichen, englische SAN); je nächstem Zug Anzahl, Punkte aus SEINER Sicht und das jüngste
    /// Jahr. Die Züge kommen aus dem Partietext (ohne Brett — bei 2000 Partien je Klick zählt die Zeit), bereinigt wie
    /// überall (<c>PgnParser.ExtractMainlineSans</c>). <c>null</c> = keine Karte.
    /// </summary>
    public async Task<JsonObject?> TreeAsync(string fide, string color, string? line, CancellationToken ct)
    {
        var p = await _db.LeaguePlayerProfiles.AsNoTracking().Where(x => x.FideId == fide)
            .Select(x => new { x.Name, x.Pgn }).FirstOrDefaultAsync(ct);
        var club = await ClubGamesAsync(fide, ct);
        if (p is null && club.Count == 0) return null;
        var name = await NameAsync(fide, p?.Name, ct);
        var prefix = (line ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(TreeMaxPlies).ToList();
        var stats = new Dictionary<string, (int N, double Pts, int Scored, string Last)>(StringComparer.Ordinal);
        var order = new List<string>();
        int total = 0, ended = 0;
        foreach (var g in WithClub(Stored(p?.Pgn), club))
        {
            if (LeagueProfileBuilder.ColorOf(g, fide, name) != color) continue;
            if (g.Headers.TryGetValue("FEN", out var fen) && !string.IsNullOrWhiteSpace(fen)) continue;
            var moveText = PgnParser.SplitGames(g.Raw).Select(x => x.MoveText).FirstOrDefault() ?? "";
            var sans = PgnParser.ExtractMainlineSans(moveText);
            if (sans.Count < prefix.Count || !prefix.Select((m, k) => sans[k] == m).All(x => x)) continue;
            total++;
            if (sans.Count == prefix.Count || prefix.Count >= TreeMaxPlies) { ended++; continue; }
            var next = sans[prefix.Count];
            var year = g.Headers.TryGetValue("Date", out var d) && d.Length >= 4 && d[..4].All(char.IsDigit) ? d[..4] : "";
            var pts = LeagueProfileBuilder.Points(g, color);
            if (!stats.TryGetValue(next, out var st)) { order.Add(next); st = (0, 0, 0, ""); }
            stats[next] = (st.N + 1, st.Pts + (pts ?? 0), st.Scored + (pts is null ? 0 : 1),
                string.CompareOrdinal(year, st.Last) > 0 ? year : st.Last);
        }
        return new JsonObject
        {
            ["fide"] = fide, ["name"] = name, ["color"] = color, ["line"] = string.Join(' ', prefix),
            ["total"] = total, ["ended"] = ended,
            ["moves"] = new JsonArray(order.OrderByDescending(m => stats[m].N).ThenBy(m => order.IndexOf(m))
                .Select(m => (JsonNode)new JsonObject
                {
                    ["san"] = m, ["n"] = stats[m].N,
                    ["score"] = stats[m].Scored > 0 ? (int)Math.Round(100 * stats[m].Pts / stats[m].Scored, MidpointRounding.ToEven) : null,
                    ["last"] = stats[m].Last.Length > 0 ? stats[m].Last : null,
                }).ToArray()),
        };
    }

    /// <summary>
    /// Die letzten Partien der Karte MIT PGN (Wunsch 2026-09-28: „die letzten Partien sollen auch klickbar sein") — dieselbe
    /// Auswahl und Reihenfolge wie <c>recent</c> im Profil (<see cref="LeagueProfileBuilder.Recent"/>), aus dem aktuellen
    /// Bestand gerechnet. Datum, Gegner und Farbe gehen mit, damit die Seite eine Zeile auch dann wiederfindet, wenn die
    /// Karte älter ist. <c>null</c> = keine Karte.
    /// </summary>
    public async Task<JsonObject?> RecentAsync(string fide, CancellationToken ct)
    {
        var p = await _db.LeaguePlayerProfiles.AsNoTracking().Where(x => x.FideId == fide)
            .Select(x => new { x.Name, x.Pgn }).FirstOrDefaultAsync(ct);
        var club = await ClubGamesAsync(fide, ct);
        if (p is null && club.Count == 0) return null;
        var name = await NameAsync(fide, p?.Name, ct);
        string Hd(LeagueProfileBuilder.Game g, string k) => g.Headers.TryGetValue(k, out var v) ? v : "";
        return new JsonObject
        {
            ["fide"] = fide,
            ["games"] = new JsonArray(LeagueProfileBuilder.Recent(fide, name, WithClub(Stored(p?.Pgn), club))
                .Select(x => (JsonNode)new JsonObject
                {
                    ["date"] = Hd(x.G, "Date"), ["vs"] = Hd(x.G, x.Color == "w" ? "Black" : "White"), ["color"] = x.Color,
                    ["pgn"] = x.G.Raw.Trim() + "\n",
                }).ToArray()),
        };
    }

    /// <summary>Alle Partien eines Spielers als PGN (fremde + Vereinspartien) — für den Download.</summary>
    public async Task<(string Name, string Pgn)?> PgnAsync(string fide, CancellationToken ct)
    {
        var p = await _db.LeaguePlayerProfiles.AsNoTracking().Where(x => x.FideId == fide)
            .Select(x => new { x.Name, x.Pgn }).FirstOrDefaultAsync(ct);
        var club = await ClubGamesAsync(fide, ct);
        if ((p is null || string.IsNullOrEmpty(p.Pgn)) && club.Count == 0) return null;
        var games = WithClub(Stored(p?.Pgn), club);
        var name = await NameAsync(fide, p?.Name, ct);
        return (name, string.Join("\n\n", games.Select(g => g.Raw)) + "\n");
    }
}
