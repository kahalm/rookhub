using System.Globalization;
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

    /// <summary>So viele Halbzüge vom Anfang genügen, um dieselbe Partie über zwei Quellen zu erkennen.</summary>
    public const int SameGamePlies = 20;

    /// <summary>
    /// „Dieselbe Partie" ohne Datum: beide Nachnamen + die ersten <see cref="SameGamePlies"/> Halbzüge (kürzer: alle, aber
    /// mindestens 10). Nicht die ganze Partie — Abschriften (chess-results) verlieren gern die letzten Züge.
    /// <c>null</c> = zu kurz, um sicher zu sein.
    /// </summary>
    public static string? SameGameKey(LeagueProfileBuilder.Game g)
    {
        var moveText = PgnParser.SplitGames(g.Raw).Select(x => x.MoveText).FirstOrDefault() ?? "";
        var sans = PgnParser.ExtractMainlineSans(moveText);
        if (sans.Count < 10) return null;
        g.Headers.TryGetValue("White", out var w);
        g.Headers.TryGetValue("Black", out var b);
        return $"{LeagueProfileBuilder.LastName(w)}|{LeagueProfileBuilder.LastName(b)}|{string.Join(' ', sans.Take(SameGamePlies))}";
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

    /// <summary>Alle Brettpartien eines Spielers (fremde + Vereinspartien) samt seinem Namen — für den Stellungs-Abgleich der
    /// Team-Suche (<see cref="LeagueTeamScout"/>). Leer, wenn es keine Karte gibt.</summary>
    public async Task<(string Name, List<LeagueProfileBuilder.Game> Games)> GamesAsync(string fide, CancellationToken ct)
    {
        var row = await _db.LeaguePlayerProfiles.AsNoTracking().Where(p => p.FideId == fide).Select(p => new { p.Name, p.Pgn }).FirstOrDefaultAsync(ct);
        var name = await NameAsync(fide, row?.Name, ct);
        return (name, WithClub(Stored(row?.Pgn), await ClubGamesAsync(fide, ct)));
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
    public async Task<(int Games, int Players)> ImportGamesAsync(string pgn, string source, CancellationToken ct,
        bool skipSameMoves = false)
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
        foreach (var (fide, all) in byFide)
        {
            var list = all;
            if (skipSameMoves)
            {
                // Dieselbe Partie aus einer anderen Quelle MIT anderem Datum (Lichess-Übertragungen tragen es gelegentlich
                // falsch — Kufstein 2026: 30.05. statt 31.07.) fiele durch die Datums-Regel von Merge und stünde doppelt da.
                var row = await _db.LeaguePlayerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.FideId == fide, ct);
                var have = WithClub(Stored(row?.Pgn), await ClubGamesAsync(fide, ct))
                    .Select(SameGameKey).Where(k => k is not null).ToHashSet(StringComparer.Ordinal);
                list = all.Where(g => SameGameKey(g) is not { } k || !have.Contains(k)).ToList();
                if (list.Count == 0) continue;
            }
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
    /// Filter des Eröffnungsbaums (0.605.0, Wunsch 2026-09-30: „Filtermöglichkeiten fürs Eröffnungsrepertoire: online ja/nein,
    /// wenn online: Zeitformat; außerdem nur Partien der letzten x Jahre"). <see cref="Source"/>: <c>board</c> (Vorgabe, die
    /// Brettpartien wie bisher), <c>both</c> oder <c>online</c>; <see cref="Speeds"/> gilt nur für Online-Partien (leer = alle);
    /// <see cref="Years"/> für beide (Brettpartien kennen oft nur das Jahr — gezählt wird ab dem Jahr der Grenze).
    /// <see cref="OnlySure"/> (Teilen-Links): nur Partien gesicherter Konten.
    /// </summary>
    public sealed record TreeFilter(string Source, IReadOnlyList<string> Speeds, int? Years, bool OnlySure)
    {
        public static readonly TreeFilter Default = new("board", Array.Empty<string>(), null, false);
        public bool Board => Source is "board" or "both";
        public bool Online => Source is "online" or "both";

        /// <summary>Aus der Adresse — Unbekanntes fällt auf die Vorgabe zurück, statt die Anfrage scheitern zu lassen.</summary>
        public static TreeFilter Parse(string? source, string? speeds, int? years, bool onlySure) => new(
            source is "board" or "both" or "online" ? source : "board",
            (speeds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => LeagueOnlineSync.Speeds.Contains(x)).Distinct().ToList(),
            years is >= 1 and <= 50 ? years : null, onlySure);
    }

    /// <summary>
    /// Eröffnungsbaum eines Spielers (Knopf „Eröffnungsbaum anzeigen" auf der Spielerkarte, Wunsch 2026-09-28): alle
    /// seine Partien (fremde + Vereinspartien, mit <paramref name="filter"/> auch die Online-Partien seiner Konten) mit
    /// <paramref name="color"/> („w"/„s"), die mit <paramref name="line"/> beginnen (Züge mit Leerzeichen, englische SAN);
    /// je nächstem Zug Anzahl, Punkte aus SEINER Sicht und das jüngste Jahr. Die Züge kommen aus dem Partietext (ohne Brett —
    /// bei 2000 Partien je Klick zählt die Zeit), bereinigt wie überall (<c>PgnParser.ExtractMainlineSans</c>); Online-Partien
    /// über ihre gespeicherte Zeile, gesucht per Präfix in SQL. <c>null</c> = weder Karte noch Online-Konto.
    /// </summary>
    public async Task<JsonObject?> TreeAsync(string fide, string color, string? line, CancellationToken ct, TreeFilter? filter = null)
    {
        filter ??= TreeFilter.Default;
        var p = await _db.LeaguePlayerProfiles.AsNoTracking().Where(x => x.FideId == fide)
            .Select(x => new { x.Name, x.Pgn }).FirstOrDefaultAsync(ct);
        var club = await ClubGamesAsync(fide, ct);
        if (p is null && club.Count == 0 && !await _db.LeagueOnlineAccounts.AnyAsync(a => a.FideId == fide, ct)) return null;
        var name = await NameAsync(fide, p?.Name, ct);
        var prefix = (line ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(TreeMaxPlies).ToList();
        var cutoff = filter.Years is { } y ? DateTime.UtcNow.AddYears(-y) : (DateTime?)null;
        var stats = new Dictionary<string, (int N, double Pts, int Scored, string Last)>(StringComparer.Ordinal);
        var order = new List<string>();
        int total = 0, ended = 0, board = 0, online = 0;

        void Count(IReadOnlyList<string> sans, double? pts, string year)
        {
            total++;
            if (sans.Count == prefix.Count || prefix.Count >= TreeMaxPlies) { ended++; return; }
            var next = sans[prefix.Count];
            if (!stats.TryGetValue(next, out var st)) { order.Add(next); st = (0, 0, 0, ""); }
            stats[next] = (st.N + 1, st.Pts + (pts ?? 0), st.Scored + (pts is null ? 0 : 1),
                string.CompareOrdinal(year, st.Last) > 0 ? year : st.Last);
        }

        if (filter.Board)
            foreach (var g in WithClub(Stored(p?.Pgn), club))
            {
                if (LeagueProfileBuilder.ColorOf(g, fide, name) != color) continue;
                if (g.Headers.TryGetValue("FEN", out var fen) && !string.IsNullOrWhiteSpace(fen)) continue;
                var year = g.Headers.TryGetValue("Date", out var d) && d.Length >= 4 && d[..4].All(char.IsDigit) ? d[..4] : "";
                if (cutoff is { } c && (year.Length == 0 || int.Parse(year) < c.Year)) continue;
                var moveText = PgnParser.SplitGames(g.Raw).Select(x => x.MoveText).FirstOrDefault() ?? "";
                var sans = PgnParser.ExtractMainlineSans(moveText);
                if (sans.Count < prefix.Count || !prefix.Select((m, k) => sans[k] == m).All(x => x)) continue;
                board++;
                Count(sans, LeagueProfileBuilder.Points(g, color), year);
            }

        if (filter.Online)
        {
            var white = color == "w";
            var q = _db.LeagueOnlineGames.AsNoTracking().Where(g => g.FideId == fide && g.White == white);
            if (filter.Speeds.Count > 0) q = q.Where(g => filter.Speeds.Contains(g.Speed));
            if (cutoff is { } c) q = q.Where(g => g.PlayedAt >= c);
            if (filter.OnlySure) q = q.Where(g => g.Account.Confidence == LeagueOnlineAccountService.Sure);
            if (prefix.Count > 0)
            {
                var pre = string.Join(' ', prefix);
                var preSpace = pre + " ";
                q = q.Where(g => g.Line == pre || g.Line.StartsWith(preSpace));
            }
            foreach (var g in await q.Select(g => new { g.Line, g.Result, g.PlayedAt }).ToListAsync(ct))
            {
                online++;
                double? pts = g.Result switch
                {
                    "1-0" => white ? 1 : 0,
                    "0-1" => white ? 0 : 1,
                    "1/2-1/2" => 0.5,
                    _ => null,
                };
                Count(g.Line.Split(' ', StringSplitOptions.RemoveEmptyEntries), pts, g.PlayedAt.Year.ToString(CultureInfo.InvariantCulture));
            }
        }

        return new JsonObject
        {
            ["fide"] = fide, ["name"] = name, ["color"] = color, ["line"] = string.Join(' ', prefix),
            ["total"] = total, ["ended"] = ended, ["board"] = board, ["online"] = online,
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
    /// Das Eröffnungsprofil der Karte („Mit Weiß", „Mit Schwarz gegen 1.e4" … samt häufigster Zugfolgen) über GEFILTERTE Partien
    /// (0.617.0, Wunsch 2026-09-30: „auch an der Stelle will ich die vollen Filtermöglichkeiten") — dieselben Regeln wie
    /// <see cref="TreeAsync"/>: Brett- und/oder Online-Partien, Tempo, letzte x Jahre, nur gesicherte Konten. Die Züge wie im Baum
    /// aus dem Partietext bzw. der gespeicherten Zeile (ohne Brett); Partien ab eigener Stellung zählen nicht. Die gespeicherte
    /// Karte (Brettpartien, alle Jahre) bleibt die Vorgabe — diese Fassung fragt die Seite nur mit gesetztem Filter.
    /// <c>null</c> = weder Karte noch Online-Konto.
    /// </summary>
    public async Task<JsonObject?> ProfileAsync(string fide, CancellationToken ct, TreeFilter? filter = null)
    {
        filter ??= TreeFilter.Default;
        var p = await _db.LeaguePlayerProfiles.AsNoTracking().Where(x => x.FideId == fide)
            .Select(x => new { x.Name, x.Pgn }).FirstOrDefaultAsync(ct);
        var club = await ClubGamesAsync(fide, ct);
        if (p is null && club.Count == 0 && !await _db.LeagueOnlineAccounts.AnyAsync(a => a.FideId == fide, ct)) return null;
        var name = await NameAsync(fide, p?.Name, ct);
        var cutoff = filter.Years is { } y ? DateTime.UtcNow.AddYears(-y) : (DateTime?)null;
        var games = new List<LeagueProfileBuilder.ProfileGame>();
        var years = new List<string>();
        int board = 0, online = 0;

        if (filter.Board)
            foreach (var g in WithClub(Stored(p?.Pgn), club))
            {
                var color = LeagueProfileBuilder.ColorOf(g, fide, name);
                if (color is null) continue;
                if (g.Headers.TryGetValue("FEN", out var fen) && !string.IsNullOrWhiteSpace(fen)) continue;
                var year = g.Headers.TryGetValue("Date", out var d) && d.Length >= 4 && d[..4].All(char.IsDigit) ? d[..4] : "";
                if (cutoff is { } c && (year.Length == 0 || int.Parse(year) < c.Year)) continue;
                var moveText = PgnParser.SplitGames(g.Raw).Select(x => x.MoveText).FirstOrDefault() ?? "";
                board++;
                if (year.Length > 0) years.Add(year);
                games.Add(new(PgnParser.ExtractMainlineSans(moveText).Take(8).ToList(), color, LeagueProfileBuilder.Points(g, color)));
            }

        if (filter.Online)
        {
            var q = _db.LeagueOnlineGames.AsNoTracking().Where(g => g.FideId == fide);
            if (filter.Speeds.Count > 0) q = q.Where(g => filter.Speeds.Contains(g.Speed));
            if (cutoff is { } c) q = q.Where(g => g.PlayedAt >= c);
            if (filter.OnlySure) q = q.Where(g => g.Account.Confidence == LeagueOnlineAccountService.Sure);
            foreach (var g in await q.Select(g => new { g.Line, g.Result, g.White, g.PlayedAt }).ToListAsync(ct))
            {
                online++;
                years.Add(g.PlayedAt.Year.ToString(CultureInfo.InvariantCulture));
                double? pts = g.Result switch
                {
                    "1-0" => g.White ? 1 : 0,
                    "0-1" => g.White ? 0 : 1,
                    "1/2-1/2" => 0.5,
                    _ => null,
                };
                games.Add(new(g.Line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(8).ToList(), g.White ? "w" : "s", pts));
            }
        }

        var o = new JsonObject
        {
            ["fide"] = fide, ["name"] = name, ["n"] = board + online, ["board"] = board, ["online"] = online,
            ["with_moves"] = games.Count(x => x.Moves.Count > 0),
            ["years"] = years.Count > 0 ? new JsonArray(years.Min(StringComparer.Ordinal), years.Max(StringComparer.Ordinal)) : null,
        };
        LeagueProfileBuilder.AddSections(o, games);
        return o;
    }

    /// <summary>
    /// Die letzten Partien der Karte MIT PGN (Wunsch 2026-09-28: „die letzten Partien sollen auch klickbar sein") — dieselbe
    /// Auswahl und Reihenfolge wie <c>recent</c> im Profil (<see cref="LeagueProfileBuilder.Recent"/>), aus dem aktuellen
    /// Bestand gerechnet. Datum, Gegner und Farbe gehen mit, damit die Seite eine Zeile auch dann wiederfindet, wenn die
    /// Karte älter ist. <c>null</c> = keine Karte.
    /// </summary>
    public async Task<JsonObject?> RecentAsync(string fide, CancellationToken ct, string? color = null)
    {
        var p = await _db.LeaguePlayerProfiles.AsNoTracking().Where(x => x.FideId == fide)
            .Select(x => new { x.Name, x.Pgn }).FirstOrDefaultAsync(ct);
        var club = await ClubGamesAsync(fide, ct);
        if (p is null && club.Count == 0) return null;
        var name = await NameAsync(fide, p?.Name, ct);
        color = color is "w" or "s" ? color : null;
        return new JsonObject
        {
            ["fide"] = fide,
            ["games"] = new JsonArray(LeagueProfileBuilder.Recent(fide, name, WithClub(Stored(p?.Pgn), club), color)
                .Select(x =>
                {
                    var e = LeagueProfileBuilder.RecentEntry(x.G, x.Color);   // dieselben Angaben wie die Karte …
                    e["pgn"] = x.G.Raw.Trim() + "\n";                          // … plus die Partie zum Nachspielen
                    return (JsonNode)e;
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
