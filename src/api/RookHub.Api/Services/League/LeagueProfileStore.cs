using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
///
/// <para><b>Zerlegt wird je Karte und Stand EINMAL</b> (Codereview 2026-09-29, N4-003): Baum, Profil, letzte Partien und
/// PGN lesen die fremden Partien aus einem Cache (<see cref="CacheServiceKey"/>), Schlüssel FIDE-ID +
/// <see cref="LeaguePlayerProfile.UpdatedAt"/> — jedes Schreiben der Karte setzt den Zeitpunkt neu. Vorher zerlegte jeder
/// Klick im Eröffnungsbaum (auch anonym über einen Teilen-Link) das ganze gespeicherte PGN, bis 2 MB, und das zweimal.</para>
/// </summary>
public sealed class LeagueProfileStore
{
    public const string ClubSource = "Verein";
    /// <summary>DI-Schlüssel des eigenen Caches der zerlegten Karten-Partien (Program.cs) — mit Größengrenze, die der
    /// allgemeine <see cref="IMemoryCache"/> nicht hat (wie <see cref="RepertoireAnalyzeService.CacheServiceKey"/>).</summary>
    public const string CacheServiceKey = "league-profile-games";
    /// <summary>Größengrenze des eigenen Caches in Partien (Size eines Eintrags = Partien der Karte, eine Meldeliste eines
    /// Teilen-Links = 1). Grob geschätzt 7 KB je zerlegter Partie samt Hauptvariante — 10 000 sind rund 70 MB; die größte
    /// Karte hatte am 29.09. 2 418 Partien.</summary>
    public const long CacheSizeLimit = 10_000;
    /// <summary>So lange bleibt eine Karte ungenutzt im Cache — eine Sitzung im Eröffnungsbaum dauert ein paar Minuten.</summary>
    internal static readonly TimeSpan CacheIdle = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _db;
    private readonly IMemoryCache? _cache;

    /// <param name="cache">Der Cache <see cref="CacheServiceKey"/> — ohne (Schreibwege, Tests) wird je Aufruf zerlegt.</param>
    public LeagueProfileStore(AppDbContext db, IMemoryCache? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    /// <summary>Jahr + Hauptvariante — der Schlüssel, an dem dieselbe Partie in zwei Quellen erkannt wird.</summary>
    public static string MovesKey(IReadOnlyDictionary<string, string> headers, string raw) => MovesKey(headers, SansOf(raw));

    private static string MovesKey(IReadOnlyDictionary<string, string> headers, IReadOnlyList<string> sans)
    {
        headers.TryGetValue("Date", out var date);
        var year = date is { Length: >= 4 } && date[..4].All(char.IsDigit) ? date[..4] : "????";
        return year + "|" + string.Join(' ', sans);
    }

    /// <summary>Die Hauptvariante einer Partie (englische SAN, bereinigt wie überall, <c>PgnParser.ExtractMainlineSans</c>).</summary>
    private static List<string> SansOf(string raw) =>
        PgnParser.ExtractMainlineSans(PgnParser.SplitGames(raw).Select(g => g.MoveText).FirstOrDefault() ?? "");

    /// <summary>Eine Brettpartie, deren Hauptvariante beim ersten Bedarf EINMAL zerlegt wird und dann bleibt — auch über
    /// Anfragen hinweg, wenn die Partie im Cache liegt (gleichzeitige Leser rechnen schlimmstenfalls beide, das Ergebnis
    /// ist dasselbe).</summary>
    private sealed class BoardGame(LeagueProfileBuilder.Game game)
    {
        private List<string>? _sans;
        public LeagueProfileBuilder.Game Game { get; } = game;
        public IReadOnlyList<string> Sans => LazyInitializer.EnsureInitialized(ref _sans, () => SansOf(Game.Raw));
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
    public static List<LeagueProfileBuilder.Game> WithClub(List<LeagueProfileBuilder.Game> external, IEnumerable<LeagueClubGame> club) =>
        WithClub(external.Select(g => new BoardGame(g)).ToList(), club).Select(b => b.Game).ToList();

    /// <summary>Wie oben über zerlegte Partien; die Schlüssel der fremden nur, wenn es überhaupt eine Vereinspartie gibt.</summary>
    private static List<BoardGame> WithClub(List<BoardGame> external, IEnumerable<LeagueClubGame> club)
    {
        HashSet<string>? known = null;
        var all = new List<BoardGame>(external);
        foreach (var c in club)
        {
            var parsed = LeagueProfileBuilder.Parse(c.Pgn, ClubSource).FirstOrDefault();
            if (parsed is null) continue;
            var b = new BoardGame(parsed);
            known ??= external.Select(x => MovesKey(x.Game.Headers, x.Sans)).ToHashSet(StringComparer.Ordinal);
            if (!known.Add(MovesKey(parsed.Headers, b.Sans))) continue;
            all.Add(b);
        }
        return ByDate(all);
    }

    private static List<BoardGame> ByDate(IEnumerable<BoardGame> games) =>
        games.OrderByDescending(b => b.Game.Headers.TryGetValue("Date", out var d) ? d : "", StringComparer.Ordinal).ToList();

    /// <summary>
    /// Alle Brettpartien der Karte: fremde + Vereinspartien (<see cref="WithClub(List{BoardGame}, IEnumerable{LeagueClubGame})"/>)
    /// + die Teilpartien aus von Hand eingegebenen ersten Zügen (<see cref="LeaguePartialGames"/>, 0.725.0) — ohne die, zu deren
    /// Brett es eine volle Partie gibt. Für Karte (Zählung + Profil), Baum, gefiltertes Profil und <see cref="GamesAsync"/>
    /// (Spielervorbereitung, Team-Suche); NICHT für „letzte Partien" und den PGN-Download — eine Teilpartie ist keine Partie zum
    /// Nachspielen. Ohne Teilpartie kostet das eine kleine Abfrage (Join der Zug-Einträge auf die Paarungen dieses Spielers).
    /// </summary>
    private async Task<List<BoardGame>> BoardAsync(string fide, List<BoardGame> external, List<LeagueClubGame> club, CancellationToken ct)
    {
        var all = WithClub(external, club);
        var partials = await LeaguePartialGames.LoadAsync(_db, new[] { fide }, ct);
        if (partials.Count == 0) return all;
        partials = await LeaguePartialGames.FilterAsync(_db, partials, external.Select(b => LeaguePartialGames.RefOf(b.Game.Headers)), ct);
        return partials.Count == 0 ? all : ByDate(all.Concat(partials.Select(x => new BoardGame(x.Game))));
    }

    /// <summary>Die fremden Partien einer Karte, zerlegt; <c>null</c> = keine Karte. <see cref="HasPgn"/>: das gespeicherte
    /// PGN ist nicht leer (der Download fragt das).</summary>
    private sealed record Card(string Name, List<BoardGame> Games, bool HasPgn);

    private sealed record CardKey(string Fide, DateTime UpdatedAt);

    /// <summary>Die Karte mit ihren fremden Partien — aus dem Cache, solange sich <see cref="LeaguePlayerProfile.UpdatedAt"/>
    /// nicht geändert hat; sonst einmal gelesen und zerlegt. Ohne Cache je Aufruf.</summary>
    private async Task<Card?> CardAsync(string fide, CancellationToken ct)
    {
        var head = await _db.LeaguePlayerProfiles.AsNoTracking().Where(x => x.FideId == fide)
            .Select(x => new { x.Name, x.UpdatedAt }).FirstOrDefaultAsync(ct);
        if (head is null) return null;
        var key = new CardKey(fide, head.UpdatedAt);
        if (_cache != null && _cache.TryGetValue(key, out Card? hit) && hit != null) return hit;
        var pgn = await _db.LeaguePlayerProfiles.AsNoTracking().Where(x => x.FideId == fide).Select(x => x.Pgn).FirstOrDefaultAsync(ct);
        var card = new Card(head.Name, Stored(pgn).Select(g => new BoardGame(g)).ToList(), !string.IsNullOrEmpty(pgn));
        _cache?.Set(key, card, new MemoryCacheEntryOptions { Size = Math.Max(1, card.Games.Count), SlidingExpiration = CacheIdle });
        return card;
    }

    /// <summary>Alle Brettpartien eines Spielers (fremde + Vereinspartien + Teilpartien aus ersten Zügen, 0.725.0 — erkennbar an
    /// <see cref="LeaguePartialGames.Is"/>) samt seinem Namen — für die Spielervorbereitung, die Trainingslinien und den Stellungs-Abgleich der
    /// Team-Suche (<see cref="LeagueTeamScout"/>). Leer, wenn es keine Karte gibt.</summary>
    public async Task<(string Name, List<LeagueProfileBuilder.Game> Games)> GamesAsync(string fide, CancellationToken ct)
    {
        var row = await _db.LeaguePlayerProfiles.AsNoTracking().Where(p => p.FideId == fide).Select(p => new { p.Name, p.Pgn }).FirstOrDefaultAsync(ct);
        var name = await NameAsync(fide, row?.Name, ct);
        var all = await BoardAsync(fide, Stored(row?.Pgn).Select(g => new BoardGame(g)).ToList(), await ClubGamesAsync(fide, ct), ct);
        return (name, all.Select(b => b.Game).ToList());
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
        var all = await BoardAsync(fide, external.Select(g => new BoardGame(g)).ToList(), club, ct);
        if (row is null && all.Count == 0 && fresh is null) return;
        var (profile, _, count) = LeagueProfileBuilder.Build(fide, name, all.Select(b => b.Game).ToList());
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
    /// jeder hochgeladenen Vereinspartie. Speichert selbst, mit Konkurrenzschutz (<see cref="LeagueService.PatchViewsAsync"/>).
    /// </summary>
    public async Task PatchViewCountsAsync(IReadOnlyCollection<string> fides, CancellationToken ct)
    {
        if (fides.Count == 0) return;
        var ids = fides.ToList();
        var counts = await _db.LeaguePlayerProfiles.AsNoTracking().Where(p => ids.Contains(p.FideId))
            .Select(p => new { p.FideId, p.GameCount }).ToDictionaryAsync(p => p.FideId, p => p.GameCount, ct);
        await LeagueService.PatchViewsAsync(_db, json =>
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject;
            if (root?["fixtures"] is not System.Text.Json.Nodes.JsonObject teams) return null;
            var changed = false;
            void Patch(System.Text.Json.Nodes.JsonNode? r)
            {
                var f = r?["fide"]?.GetValue<string>();
                if (f is null || !ids.Contains(f)) return;
                var g = counts.GetValueOrDefault(f);
                if (r!["g"]?.GetValue<int>() == g) return;
                r["g"] = g;
                changed = true;
            }
            foreach (var (_, rounds) in teams)
                foreach (var (_, fx) in rounds?.AsObject() ?? new System.Text.Json.Nodes.JsonObject())
                {
                    foreach (var r in fx?["roster"] as System.Text.Json.Nodes.JsonArray ?? new System.Text.Json.Nodes.JsonArray()) Patch(r);
                    // die Vorschläge je Brett tragen die Zahl seit 0.649.0 ebenfalls
                    foreach (var bo in fx?["boards"] as System.Text.Json.Nodes.JsonArray ?? new System.Text.Json.Nodes.JsonArray())
                        foreach (var c in bo?["cand"] as System.Text.Json.Nodes.JsonArray ?? new System.Text.Json.Nodes.JsonArray()) Patch(c);
                }
            return changed ? root.ToJsonString() : null;
        }, ct);
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

    /// <summary>Die Online-Partien eines Spielers nach <paramref name="filter"/> — Tempo, ab <paramref name="cutoff"/>, ohne
    /// <c>unsure</c> nur die gesicherter Konten. Dieselbe Auswahl für Baum und Profil der Karte und für die Spielervorbereitung
    /// (<c>Services/Prep</c>, 0.633.0), damit die Regel nur hier steht.</summary>
    public static IQueryable<LeagueOnlineGame> OnlineGames(AppDbContext db, string fide, TreeFilter filter, DateTime? cutoff)
    {
        var q = db.LeagueOnlineGames.AsNoTracking().Where(g => g.FideId == fide);
        if (filter.Speeds.Count > 0) q = q.Where(g => filter.Speeds.Contains(g.Speed));
        if (cutoff is { } c) q = q.Where(g => g.PlayedAt >= c);
        if (filter.OnlySure) q = q.Where(g => g.Account.Confidence == LeagueOnlineAccountService.Sure);
        return q;
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
        var p = await CardAsync(fide, ct);
        var club = await ClubGamesAsync(fide, ct);
        // Online-Konten zählen nur für Spieler von LeagueHub — die eines Spielers der Spielervorbereitung nicht (0.638.0).
        var league = p is not null || await LeagueOnlineAccountService.LeagueKnowsAsync(_db, fide, ct);
        if (p is null && club.Count == 0 && !(league && await _db.LeagueOnlineAccounts.AnyAsync(a => a.FideId == fide, ct))) return null;
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
            foreach (var b in await BoardAsync(fide, p?.Games ?? new(), club, ct))
            {
                var g = b.Game;
                if (LeagueProfileBuilder.ColorOf(g, fide, name) != color) continue;
                if (g.Headers.TryGetValue("FEN", out var fen) && !string.IsNullOrWhiteSpace(fen)) continue;
                var year = g.Headers.TryGetValue("Date", out var d) && d.Length >= 4 && d[..4].All(char.IsDigit) ? d[..4] : "";
                if (cutoff is { } c && (year.Length == 0 || int.Parse(year) < c.Year)) continue;
                var sans = b.Sans;
                if (sans.Count < prefix.Count || !prefix.Select((m, k) => sans[k] == m).All(x => x)) continue;
                board++;
                Count(sans, LeagueProfileBuilder.Points(g, color), year);
            }

        if (filter.Online && league)
        {
            var white = color == "w";
            var q = OnlineGames(_db, fide, filter, cutoff).Where(g => g.White == white);
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
        var p = await CardAsync(fide, ct);
        var club = await ClubGamesAsync(fide, ct);
        // Online-Konten zählen nur für Spieler von LeagueHub — die eines Spielers der Spielervorbereitung nicht (0.638.0).
        var league = p is not null || await LeagueOnlineAccountService.LeagueKnowsAsync(_db, fide, ct);
        if (p is null && club.Count == 0 && !(league && await _db.LeagueOnlineAccounts.AnyAsync(a => a.FideId == fide, ct))) return null;
        var name = await NameAsync(fide, p?.Name, ct);
        var cutoff = filter.Years is { } y ? DateTime.UtcNow.AddYears(-y) : (DateTime?)null;
        var games = new List<LeagueProfileBuilder.ProfileGame>();
        var years = new List<string>();
        int board = 0, online = 0;

        if (filter.Board)
            foreach (var b in await BoardAsync(fide, p?.Games ?? new(), club, ct))
            {
                var g = b.Game;
                var color = LeagueProfileBuilder.ColorOf(g, fide, name);
                if (color is null) continue;
                if (g.Headers.TryGetValue("FEN", out var fen) && !string.IsNullOrWhiteSpace(fen)) continue;
                var year = g.Headers.TryGetValue("Date", out var d) && d.Length >= 4 && d[..4].All(char.IsDigit) ? d[..4] : "";
                if (cutoff is { } c && (year.Length == 0 || int.Parse(year) < c.Year)) continue;
                board++;
                if (year.Length > 0) years.Add(year);
                games.Add(new(b.Sans.Take(8).ToList(), color, LeagueProfileBuilder.Points(g, color)));
            }

        if (filter.Online && league)
        {
            var q = OnlineGames(_db, fide, filter, cutoff);
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
        var p = await CardAsync(fide, ct);
        var club = await ClubGamesAsync(fide, ct);
        if (p is null && club.Count == 0) return null;
        var name = await NameAsync(fide, p?.Name, ct);
        color = color is "w" or "s" ? color : null;
        var games = WithClub(p?.Games ?? new(), club).Select(b => b.Game).ToList();
        return new JsonObject
        {
            ["fide"] = fide,
            ["games"] = new JsonArray(LeagueProfileBuilder.Recent(fide, name, games, color)
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
        var p = await CardAsync(fide, ct);
        var club = await ClubGamesAsync(fide, ct);
        if ((p is null || !p.HasPgn) && club.Count == 0) return null;
        var games = WithClub(p?.Games ?? new(), club);
        var name = await NameAsync(fide, p?.Name, ct);
        return (name, string.Join("\n\n", games.Select(b => b.Game.Raw)) + "\n");
    }
}
