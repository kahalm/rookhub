using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
/// <para><b>Ohne Warten auf die Gesamtzählung</b> (0.869.6, 28.10.2026): Die Gesamtzählung (und der Block <c>league</c>) läuft in einem
/// eigenen Scope im Hintergrund, unabhängig vom Abbruch der Anfrage — vorher brach nginx sie nach 60 s ab, der Cache blieb leer und
/// jede weitere Anfrage begann von vorn (Dev 28.10.: 28 von 28 Anfragen 499). Die Anfrage wartet höchstens <see cref="Budget"/>;
/// ist die Zählung bis dahin nicht fertig, kommt <c>pending: true</c> ohne diese Blöcke (die Oberfläche fragt nach). Nach Ablauf
/// der 30 min liefert die Anfrage die letzte Zählung sofort und rechnet im Hintergrund neu.</para>
/// </summary>
public sealed class LeagueGameSources(AppDbContext db, IMemoryCache? cache = null, IServiceScopeFactory? scopes = null,
    ILogger<LeagueGameSources>? log = null)
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(30);
    /// <summary>So lange wartet eine Anfrage auf eine kalte Zählung, dann antwortet sie mit <c>pending</c> (nginx bricht nach 60 s ab).</summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(8);
    private const string CacheKey = "league-game-sources";
    /// <summary>Mehr Spieler hat keine Meldeliste — Deckel für die frei übergebene Liste.</summary>
    public const int MaxOpponentPlayers = 40;

    /// <summary>Wartezeit dieser Instanz auf eine kalte Zählung (Tests setzen sie herab).</summary>
    public TimeSpan Budget { get; init; } = DefaultBudget;

    /// <summary>Nur für Tests: läuft im Hintergrund vor jeder Zählung (hält sie an, um „noch nicht fertig" zu prüfen).</summary>
    internal Func<Task>? BeforeComputeForTests { get; init; }

    /// <summary>
    /// Nur EINE kalte Zählung gleichzeitig (16.10.2026): Die Gesamtzählung liest alle Profile (Prod 16.10.: 2 267 Profile,
    /// 843 MB PGN, 9–12 s). Kamen mehrere Anfragen bei leerem Cache gleichzeitig (UI-Sweep: 8 Browser), rechnete jede für
    /// sich — die Dev-API lief ohne Grenze auf ~16 GB, mit 6 GB in <c>OutOfMemoryException</c> (500). Jetzt warten die
    /// übrigen und nehmen danach das Ergebnis aus dem Cache.
    /// </summary>
    private static readonly SemaphoreSlim ColdGate = new(1, 1);
    private static readonly object StartLock = new();

    /// <summary>Anzeige je Quelle; unbekannte Quellen (über <c>POST /api/league/admin/games</c> frei benannt) stehen, wie sie heißen.</summary>
    public static string Label(string source) => source switch
    {
        "Lumbra" => "Lumbra",
        "Mega" => "ChessBase-Megabase",
        "chess-results" => "chess-results",
        LeagueBroadcastImport.Source => "Lichess-Übertragungen",
        LigamanagerSource.PgnSource => "SBV-Ligamanager",
        LeagueProfileStore.ClubSource => "Vereins-Datenbank",
        LeaguePartialGames.Source => "Ligarunde (erste Züge)",
        LeagueOnlineSites.Lichess => "Lichess",
        LeagueOnlineSites.ChessCom => "chess.com",
        _ => source,
    };

    /// <summary>Gesamt (30 min gemerkt); mit <paramref name="leagueTnr"/> der Block <c>league</c> (alle Meldelisten dieser Liga), mit
    /// <paramref name="opponentFides"/> der Block <c>opponent</c>. <paramref name="onlySure"/> = Teilen-Link: online nur „gesicherte"
    /// Konten (wie die Karte dort). Ist eine kalte Zählung nach <see cref="Budget"/> nicht fertig: <c>pending: true</c>, die Gesamtzeilen
    /// leer, <c>league</c> fehlt — <c>opponent</c> (nur ein paar Profile) kommt immer.</summary>
    /// <param name="clubId">Der Verein der Anfrage (Mandanten-Schritt 2026-10-07): die Zeile „Vereins-Datenbank" zählt nur
    /// SEINE Partien; alles andere ist für alle Vereine gleich.</param>
    public async Task<JsonObject> GetAsync(int clubId, CancellationToken ct, IEnumerable<string?>? opponentFides = null, bool onlySure = false,
        int? leagueTnr = null)
    {
        var deadline = DateTime.UtcNow + Budget;
        // Ohne Hintergrund (Tests) rechnet alles auf dem DbContext der Anfrage — dann nacheinander, nie zwei Abfragen gleichzeitig.
        var inline = cache is null || scopes is null;
        var totals = CachedAsync(TotalsKey(clubId), (d, c) => ComputeTotalsAsync(d, clubId, c), ct);
        if (inline) await totals;
        var league = leagueTnr is { } tnr
            ? CachedAsync(LeagueKey(clubId, tnr, onlySure), (d, c) => ComputeLeagueAsync(d, clubId, tnr, onlySure, c), ct)
            : null;
        if (inline && league is not null) await league;
        var fides = (opponentFides ?? []).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f!.Trim()).Distinct()
            .Take(MaxOpponentPlayers).ToList();
        var opponent = fides.Count > 0 ? await CountPlayersAsync(db, clubId, fides, onlySure, ct) : null;

        var res = await WithinAsync(totals, deadline, ct) ?? new JsonObject
        {
            ["board"] = new JsonArray(), ["boardTotal"] = 0, ["online"] = new JsonArray(), ["onlineTotal"] = 0, ["countedAt"] = null,
        };
        var pending = !totals.IsCompletedSuccessfully;
        if (league is not null)
        {
            if (await WithinAsync(league, deadline, ct) is { } l) res["league"] = l;
            else pending = true;
        }
        if (opponent is not null) res["opponent"] = opponent;
        if (pending) res["pending"] = true;
        return res;
    }

    internal static string TotalsKey(int clubId) => $"{CacheKey}:{clubId}";
    internal static string LeagueKey(int clubId, int tnr, bool onlySure) => $"{CacheKey}:{clubId}:league:{tnr}:{onlySure}";

    /// <summary>Das Ergebnis, wenn es bis <paramref name="deadline"/> fertig ist, sonst <c>null</c> (die Zählung läuft weiter).</summary>
    private static async Task<JsonObject?> WithinAsync(Task<JsonObject> task, DateTime deadline, CancellationToken ct)
    {
        if (!task.IsCompleted)
        {
            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) return null;
            try { await task.WaitAsync(left, ct); }
            catch (TimeoutException) { return null; }
        }
        return (JsonObject)(await task).DeepClone();
    }

    /// <summary>
    /// Ein 30 min gemerkter Block: frisch aus dem Cache; sonst die laufende bzw. eine neu gestartete Zählung. Mit
    /// <c>scopes</c> (im Betrieb) rechnet sie im Hintergrund in einem eigenen Scope und ohne den Abbruch der Anfrage zu Ende,
    /// und gibt es eine ältere Zählung, kommt die sofort. Ohne <c>scopes</c> (Tests) rechnet die Anfrage selbst, wie bisher.
    /// </summary>
    private Task<JsonObject> CachedAsync(string key, Func<AppDbContext, CancellationToken, Task<JsonObject>> compute, CancellationToken ct)
    {
        if (cache is null) return compute(db, ct);
        if (cache.TryGetValue(key, out JsonObject? hit) && hit is not null) return Task.FromResult(hit);
        if (scopes is null) return GatedAsync(key, () => compute(db, ct), ct);
        Task<JsonObject> run;
        lock (StartLock)
        {
            if (!cache.TryGetValue(key + ":run", out Task<JsonObject>? running) || running is null || running.IsCompleted)
            {
                running = Task.Run(() => GatedAsync(key, async () =>
                {
                    using var scope = scopes.CreateScope();
                    return await compute(scope.ServiceProvider.GetRequiredService<AppDbContext>(), CancellationToken.None);
                }, CancellationToken.None));
                cache.Set(key + ":run", running, TimeSpan.FromHours(1));
                running.ContinueWith(t => log?.LogError(t.Exception, "Quellen-Zählung {Key} fehlgeschlagen", key),
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            }
            run = running;
        }
        return cache.TryGetValue(key + ":last", out JsonObject? last) && last is not null ? Task.FromResult(last) : run;
    }

    /// <summary>Hinter <see cref="ColdGate"/>: noch einmal in den Cache schauen, sonst zählen und merken (frisch 30 min, zuletzt
    /// gezählt unbegrenzt — ein paar KB).</summary>
    private async Task<JsonObject> GatedAsync(string key, Func<Task<JsonObject>> compute, CancellationToken ct)
    {
        await ColdGate.WaitAsync(ct);
        try
        {
            if (cache!.TryGetValue(key, out JsonObject? hit) && hit is not null) return hit;
            if (BeforeComputeForTests is { } hold) await hold();
            var res = await compute();
            cache.Set(key, res, CacheFor);
            cache.Set(key + ":last", res, new MemoryCacheEntryOptions { Priority = CacheItemPriority.NeverRemove });
            return res;
        }
        finally { ColdGate.Release(); }
    }

    /// <summary>Alle Spieler der Meldelisten dieser Liga (<see cref="Models.LeaguePlayer"/> mit FIDE-ID); eine unbekannte Liga zählt
    /// 0 Spieler.</summary>
    private static async Task<JsonObject> ComputeLeagueAsync(AppDbContext db, int clubId, int tnr, bool onlySure, CancellationToken ct)
    {
        var fides = await db.LeaguePlayers.AsNoTracking().Where(p => p.Tnr == tnr && p.FideId != null && p.FideId != "")
            .Select(p => p.FideId!).Distinct().ToListAsync(ct);
        return await CountPlayersAsync(db, clubId, fides, onlySure, ct);
    }

    /// <summary>
    /// Partien der Spieler <paramref name="fides"/> je Quelle → <c>{ players, board{ Quelle: n }, boardTotal, online{ Seite: { games,
    /// accounts } }, onlineTotal, onlineAccounts }</c>. Eine Partie zweier dieser Spieler zählt einmal.
    /// </summary>
    public Task<JsonObject> PlayersAsync(int clubId, IReadOnlyCollection<string> fides, bool onlySure, CancellationToken ct) =>
        CountPlayersAsync(db, clubId, fides, onlySure, ct);

    private static async Task<JsonObject> CountPlayersAsync(AppDbContext db, int clubId, IReadOnlyCollection<string> fides, bool onlySure,
        CancellationToken ct)
    {
        var board = new Dictionary<string, int>(StringComparer.Ordinal);
        var seen = new HashSet<ulong>();
        var partials = new PartialCount(await LeaguePartialGames.LoadAsync(db, fides, ct));
        await foreach (var pgn in db.LeaguePlayerProfiles.AsNoTracking().Where(p => fides.Contains(p.FideId)).Select(p => p.Pgn)
                           .AsAsyncEnumerable().WithCancellation(ct))
            CountBoard(pgn, seen, board, partials.Collect);
        await partials.AddToAsync(db, board, ct);
        var club = await db.LeagueClubGames.AsNoTracking().Where(g => g.ClubId == clubId)
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

    private static async Task<JsonObject> ComputeTotalsAsync(AppDbContext db, int clubId, CancellationToken ct)
    {
        var board = new Dictionary<string, int>(StringComparer.Ordinal);
        var seen = new HashSet<ulong>();
        var partials = new PartialCount(await LeaguePartialGames.LoadAsync(db, null, ct));
        await foreach (var pgn in db.LeaguePlayerProfiles.AsNoTracking().Select(p => p.Pgn).AsAsyncEnumerable().WithCancellation(ct))
            CountBoard(pgn, seen, board, partials.Collect);
        await partials.AddToAsync(db, board, ct);
        var club = await db.LeagueClubGames.AsNoTracking().CountAsync(g => g.ClubId == clubId, ct);
        if (club > 0) board[LeagueProfileStore.ClubSource] = club;
        // Erst die Konten (Dev 28.10.: 350), dann deren Partien über den Index (AccountId, ExternalId): Der Filter direkt auf den
        // Partien las die ganze Tabelle (1,2 Mio. Zeilen, 643 MB) samt aller Profile mit PGN (829 MB) — Dev 61 s, so 6,5 s.
        // Nur Spieler von LeagueHub — Konten aus der Spielervorbereitung zählen hier nicht mit (0.637.0).
        var ids = await db.LeagueOnlineAccounts.AsNoTracking()
            .Where(a => db.LeaguePlayers.Any(p => p.FideId == a.FideId) || db.LeaguePlayerProfiles.Any(p => p.FideId == a.FideId))
            .Select(a => a.Id).ToListAsync(ct);
        var online = ids.Count == 0 ? new Dictionary<string, int>() : (await db.LeagueOnlineGames.AsNoTracking()
                .Where(g => ids.Contains(g.AccountId))
                .GroupBy(g => g.Account.Site)
                .Select(g => new { Site = g.Key, Games = g.Select(x => x.ExternalId).Distinct().Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.Site, x => x.Games);
        return new JsonObject
        {
            ["board"] = Rows(board), ["boardTotal"] = board.Values.Sum(),
            ["online"] = Rows(online), ["onlineTotal"] = online.Values.Sum(),
            ["countedAt"] = DateTime.UtcNow.ToString("O"),
        };
    }

    private static JsonArray Rows(Dictionary<string, int> counts) => new(counts.Where(kv => kv.Value > 0)
        .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
        .Select(kv => (JsonNode)new JsonObject { ["key"] = kv.Key, ["label"] = Label(kv.Key), ["games"] = kv.Value }).ToArray());

    /// <summary>
    /// Die Partien EINES gespeicherten PGN zählen — jede, deren Schlüssel noch nicht in <paramref name="seen"/> steht, unter ihrer
    /// Quelle. Liest nur die Kopfzeilen: eine neue Partie beginnt mit einer Kopfzeile nach Zugtext. Der Schlüssel (beide Namen
    /// nur aus Buchstaben + Datum + Runde) liegt als 64-Bit-Prüfsumme in <paramref name="seen"/> statt als Text, und die Zeilen
    /// werden ohne <c>Split</c> gelesen — bei 843 MB PGN (Prod 16.10.2026) machte beides Hunderte MB je Zählung aus.
    /// </summary>
    public static void CountBoard(string pgn, HashSet<ulong> seen, Dictionary<string, int> counts,
        Action<IReadOnlyDictionary<string, string>>? onGame = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        var inMoves = false;
        void Flush()
        {
            if (headers.Count == 0) return;
            onGame?.Invoke(headers);
            if (seen.Add(GameKey(headers)))
            {
                var src = LeagueProfileBuilder.StoredSource(headers);
                counts[src] = counts.GetValueOrDefault(src) + 1;
            }
            headers.Clear();
        }
        foreach (var raw in pgn.AsSpan().EnumerateLines())
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                if (inMoves) { Flush(); inMoves = false; }
                var sp = line.IndexOf(' ');
                var q1 = line.IndexOf('"');
                var q2 = line.LastIndexOf('"');
                if (sp > 1 && q1 > sp && q2 > q1) headers[line[1..sp].ToString()] = line[(q1 + 1)..q2].ToString();
            }
            else inMoves = true;
        }
        Flush();
    }

    /// <summary>Schlüssel einer Partie als FNV-1a-64 über: Weiß und Schwarz nur aus Buchstaben (klein), Datum, Runde — mit
    /// Trennzeichen, damit „ab|c“ ≠ „a|bc“. Kollisionen sind bei einer Million Partien praktisch ausgeschlossen (≈ 3·10⁻⁸).</summary>
    public static ulong GameKey(IReadOnlyDictionary<string, string> headers)
    {
        var h = 14695981039346656037UL;
        void Add(char c) { h ^= c; h *= 1099511628211UL; }
        void Letters(string? s) { if (s is null) return; foreach (var c in s) if (char.IsLetter(c)) Add(char.ToLowerInvariant(c)); }
        void Raw(string? s) { if (s is null) return; foreach (var c in s) Add(c); }
        Letters(headers.GetValueOrDefault("White")); Add('|');
        Letters(headers.GetValueOrDefault("Black")); Add('|');
        Raw(headers.GetValueOrDefault("Date")); Add('|');
        Raw(headers.GetValueOrDefault("Round"));
        return h;
    }

    /// <summary>
    /// Die Teilpartien aus ersten Zügen (<see cref="LeaguePartialGames"/>, 0.725.0) als eigene Zeile „Ligarunde (erste Züge)": je
    /// Brett einmal, ohne die Bretter mit voller Partie — dieselbe Regel wie die Karte. Beim Durchgehen der Karten-PGNs merkt
    /// <see cref="Collect"/> nur die Kopfzeilen von Partien derselben beiden Spieler (Nachnamen oder FIDE-IDs) — sonst nichts.
    /// </summary>
    private sealed class PartialCount(List<LeaguePartialGames.Partial> partials)
    {
        private readonly HashSet<string> _pairs = partials.SelectMany(p => new[]
        {
            "l:" + LeagueProfileBuilder.LastName(p.White) + "|" + LeagueProfileBuilder.LastName(p.Black),
            "f:" + p.WhiteFide + "|" + p.BlackFide,
        }).ToHashSet(StringComparer.Ordinal);
        private readonly List<LeaguePartialGames.FullRef> _refs = new();

        public Action<IReadOnlyDictionary<string, string>>? Collect => partials.Count == 0 ? null : h =>
        {
            var r = LeaguePartialGames.RefOf(h);
            if (_pairs.Contains("l:" + r.WhiteLast + "|" + r.BlackLast)
                || r.WhiteFide is not null && r.BlackFide is not null && _pairs.Contains("f:" + r.WhiteFide + "|" + r.BlackFide))
                _refs.Add(r);
        };

        public async Task AddToAsync(AppDbContext db, Dictionary<string, int> board, CancellationToken ct)
        {
            if (partials.Count == 0) return;
            var n = (await LeaguePartialGames.FilterAsync(db, partials, _refs, ct)).Count;
            if (n > 0) board[LeaguePartialGames.Source] = n;
        }
    }

}
