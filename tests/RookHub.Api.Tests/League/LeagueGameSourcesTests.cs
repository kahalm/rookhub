using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests.League;

/// <summary>Partien im Bestand je Quelle (0.626.0) — Startseite von LeagueHub.</summary>
public class LeagueGameSourcesTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private static string Game(string white, string black, string date, string round, params string[] extra) =>
        $"[Event \"Liga\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n[Date \"{date}\"]\n[Round \"{round}\"]\n"
        + string.Concat(extra.Select(e => e + "\n")) + "\n1. e4 e5 2. Nf3 1-0\n\n";

    // Muster gegen Huber steht in BEIDEN Profilen (je Spieler gespeichert) — einmal mit Komma, einmal ohne.
    private static readonly string MusterPgn =
        Game("Muster, Max", "Huber, Franz", "2024.03.01", "1", "[WhiteFideId \"222\"]")                      // Lumbra
        + Game("Muster, Max", "Gast, Gerd", "2023.01.01", "2", "[LeagueSource \"Mega\"]", "[WhiteFideId \"222\"]")
        + Game("Muster, Max", "Mair, Moritz", "2022.05.05", "3")                                               // chess-results
        + Game("Muster, Max", "Mair, Moritz", "2022.05.05", "4")                                               // andere Runde
        + Game("Muster, Max", "Senn, Sepp", "2025.09.01", "1", "[LeagueSource \"Lichess-Übertragung\"]");
    private static readonly string HuberPgn =
        Game("Muster Max", "Huber Franz", "2024.03.01", "1")                                                   // dieselbe Partie
        + Game("Huber, Franz", "Neu, Nina", "2024.04.01", "1", "[LeagueSource \"Mega\"]");

    [Fact]
    public void CountBoard_EachGameOnce_SourceByTheCardsRule()
    {
        var seen = new HashSet<ulong>();
        var counts = new Dictionary<string, int>();
        LeagueGameSources.CountBoard(MusterPgn, seen, counts);
        LeagueGameSources.CountBoard(HuberPgn, seen, counts);
        Assert.Equal(new Dictionary<string, int> { ["Lumbra"] = 1, ["Mega"] = 2, ["chess-results"] = 2, ["Lichess-Übertragung"] = 1 }, counts);
        LeagueGameSources.CountBoard("", seen, counts);                                                        // leeres Profil
        Assert.Equal(6, counts.Values.Sum());
    }

    [Fact]
    public void GameKey_NamesOnlyLetters_FieldsSeparated()
    {
        static Dictionary<string, string> H(string w, string b, string d = "2024.03.01", string r = "1") =>
            new() { ["White"] = w, ["Black"] = b, ["Date"] = d, ["Round"] = r };
        Assert.Equal(LeagueGameSources.GameKey(H("Muster, Max", "Huber, Franz")), LeagueGameSources.GameKey(H("muster max", "HUBER FRANZ")));
        Assert.NotEqual(LeagueGameSources.GameKey(H("Ab", "C")), LeagueGameSources.GameKey(H("A", "Bc")));   // Trenner zählt
        Assert.NotEqual(LeagueGameSources.GameKey(H("A", "B", r: "1")), LeagueGameSources.GameKey(H("A", "B", r: "2")));
        Assert.Equal(LeagueGameSources.GameKey(H("A", "B")), LeagueGameSources.GameKey(new Dictionary<string, string>
            { ["Black"] = "B", ["White"] = "A", ["Round"] = "1", ["Date"] = "2024.03.01", ["Event"] = "egal" }));
    }

    /// <summary>Zählt, wie oft die Gesamtzählung in DIESEN Cache geschrieben wird (instanzlokal — kein statischer Zähler,
    /// den parallele Testklassen mitbenutzen würden).</summary>
    private sealed class CountingCache(IMemoryCache inner) : IMemoryCache
    {
        public int Writes;
        public ICacheEntry CreateEntry(object key)
        {
            if (key is not string k || !k.EndsWith(":last", StringComparison.Ordinal)) Interlocked.Increment(ref Writes);   // „zuletzt gezählt" je Zählung dazu
            return inner.CreateEntry(key);
        }
        public void Dispose() => inner.Dispose();
        public void Remove(object key) => inner.Remove(key);
        public bool TryGetValue(object key, out object? value) => inner.TryGetValue(key, out value);
    }

    // 16.10.2026: Vier gleichzeitige Anfragen bei leerem Cache rechneten je für sich alle Profile durch (Dev: OutOfMemory
    // bei 6 GB). Jetzt rechnet eine, die übrigen warten und nehmen das Ergebnis aus dem Cache.
    [Fact]
    public async Task Get_ConcurrentColdRequests_CountOnce()
    {
        var name = Guid.NewGuid().ToString();
        DbContextOptions<AppDbContext> opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options;
        await using (var seed = new AppDbContext(opts))
        {
            seed.LeaguePlayerProfiles.AddRange(
                new LeaguePlayerProfile { FideId = "222", Name = "Muster, Max", Pgn = MusterPgn },
                new LeaguePlayerProfile { FideId = "333", Name = "Huber, Franz", Pgn = HuberPgn });
            await seed.SaveChangesAsync();
        }
        var cache = new CountingCache(new MemoryCache(new MemoryCacheOptions()));
        var contexts = Enumerable.Range(0, 4).Select(_ => new AppDbContext(opts)).ToList();
        try
        {
            var results = await Task.WhenAll(contexts.Select(db => Task.Run(() => new LeagueGameSources(db, cache).GetAsync(TestClubs.HomeId, default))));
            Assert.All(results, r => Assert.Equal(6, r["boardTotal"]!.GetValue<int>()));
            Assert.Equal(1, cache.Writes);
        }
        finally { foreach (var db in contexts) await db.DisposeAsync(); }
    }

    /// <summary>Ein Dienst wie im Betrieb: Scope-Fabrik über eine eigene In-Memory-DB mit Muster + Huber (Gesamt 6 Brettpartien).</summary>
    private static async Task<(ServiceProvider Sp, string Name)> BackgroundDb()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name));
        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var seed = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        seed.LeaguePlayerProfiles.AddRange(
            new LeaguePlayerProfile { FideId = "222", Name = "Muster, Max", Pgn = MusterPgn },
            new LeaguePlayerProfile { FideId = "333", Name = "Huber, Franz", Pgn = HuberPgn });
        seed.LeaguePlayers.Add(new LeaguePlayer { Tnr = 1, Team = "Kufstein", Name = "Muster, Max", NameKey = "muster, max", FideId = "222" });
        await seed.SaveChangesAsync();
        return (sp, name);
    }

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 500 && !done(); i++) await Task.Delay(10);
        Assert.True(done());
    }

    // 28.10.2026 (Dev): Die Gesamtzählung dauerte länger als nginx wartet (60 s). Der Abbruch der Anfrage brach auch die
    // Zählung ab, der Cache blieb leer, jede weitere Anfrage begann von vorn — 28 von 28 Anfragen 499. Jetzt: Antwort nach dem
    // Zeitbudget mit „pending" (der Gegner-Block ist trotzdem da), die Zählung läuft im Hintergrund zu Ende und landet im Cache.
    [Fact]
    public async Task Get_SlowColdCount_AnswersPending_AndFinishesInBackgroundDespiteAbort()
    {
        var (sp, _) = await BackgroundDb();
        await using var _sp = sp;
        var cache = new MemoryCache(new MemoryCacheOptions());
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var computes = 0;
        LeagueGameSources Svc(AppDbContext db) => new(db, cache, sp.GetRequiredService<IServiceScopeFactory>())
        {
            Budget = TimeSpan.FromMilliseconds(50),
            BeforeComputeForTests = () => { Interlocked.Increment(ref computes); return hold.Task; },
        };
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var r = await Svc(db).GetAsync(TestClubs.HomeId, default, new[] { "333" }, leagueTnr: 1);
            Assert.True(r["pending"]!.GetValue<bool>());
            Assert.Equal(0, r["boardTotal"]!.GetValue<int>());
            Assert.Null(r["league"]);
            Assert.Equal(2, r["opponent"]!["boardTotal"]!.GetValue<int>());            // Huber: 2 Partien, sofort

            // nginx gibt auf (Anfrage abgebrochen) — die Zählung läuft trotzdem weiter, und nur einmal.
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Svc(db).GetAsync(TestClubs.HomeId, cts.Token, leagueTnr: 1));
        }
        finally { hold.TrySetResult(); }

        await Until(() => cache.TryGetValue(LeagueGameSources.TotalsKey(TestClubs.HomeId), out _)
                          && cache.TryGetValue(LeagueGameSources.LeagueKey(TestClubs.HomeId, 1, false), out _));
        using var scope2 = sp.CreateScope();
        var done = await Svc(scope2.ServiceProvider.GetRequiredService<AppDbContext>()).GetAsync(TestClubs.HomeId, default, leagueTnr: 1);
        Assert.Null(done["pending"]);
        Assert.Equal(6, done["boardTotal"]!.GetValue<int>());
        Assert.Equal(5, done["league"]!["boardTotal"]!.GetValue<int>());                // nur Muster
        Assert.Equal(2, computes);                                                      // Gesamt + Liga, je einmal
    }

    // Nach 30 min kommt die letzte Zählung sofort; die neue rechnet im Hintergrund und gilt ab der nächsten Anfrage.
    [Fact]
    public async Task Get_Expired_ServesLastCountAtOnce_AndRefreshesInBackground()
    {
        var (sp, _) = await BackgroundDb();
        await using var _sp = sp;
        var cache = new MemoryCache(new MemoryCacheOptions());
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        LeagueGameSources Svc() => new(db, cache, sp.GetRequiredService<IServiceScopeFactory>()) { Budget = TimeSpan.FromSeconds(5) };
        Assert.Equal(6, (await Svc().GetAsync(TestClubs.HomeId, default))["boardTotal"]!.GetValue<int>());

        db.LeaguePlayerProfiles.Add(new LeaguePlayerProfile { FideId = "444", Name = "Neu, Nora", Pgn = Game("Neu, Nora", "X, Y", "2021.01.01", "1") });
        await db.SaveChangesAsync();
        cache.Remove(LeagueGameSources.TotalsKey(TestClubs.HomeId));                   // 30 min vorbei
        var stale = await Svc().GetAsync(TestClubs.HomeId, default);
        Assert.Equal(6, stale["boardTotal"]!.GetValue<int>());
        Assert.Null(stale["pending"]);

        await Until(() => cache.TryGetValue(LeagueGameSources.TotalsKey(TestClubs.HomeId), out _));
        Assert.Equal(7, (await Svc().GetAsync(TestClubs.HomeId, default))["boardTotal"]!.GetValue<int>());
    }

    [Fact]
    public void Labels()
    {
        Assert.Equal("ChessBase-Megabase", LeagueGameSources.Label("Mega"));
        Assert.Equal("Lichess-Übertragungen", LeagueGameSources.Label("Lichess-Übertragung"));
        Assert.Equal("Vereins-Datenbank", LeagueGameSources.Label("Verein"));
        Assert.Equal("chess.com", LeagueGameSources.Label("chess.com"));
        Assert.Equal("Eigene Sammlung", LeagueGameSources.Label("Eigene Sammlung"));
    }

    [Fact]
    public async Task Get_BoardClubAndOnline_SortedWithTotals_AndCached()
    {
        _db.LeaguePlayerProfiles.AddRange(
            new LeaguePlayerProfile { FideId = "222", Name = "Muster, Max", Pgn = MusterPgn },
            new LeaguePlayerProfile { FideId = "333", Name = "Huber, Franz", Pgn = HuberPgn });
        _db.LeagueClubGames.Add(new LeagueClubGame { ClubId = TestClubs.HomeId, White = "Schwaz", Black = "Gast", Pgn = "1. d4 *", MovesHash = "h" });
        var li1 = new LeagueOnlineAccount { FideId = "222", Site = "lichess", UserName = "a", Url = "u", Confidence = "sicher" };
        var li2 = new LeagueOnlineAccount { FideId = "333", Site = "lichess", UserName = "b", Url = "u", Confidence = "sicher" };
        var cc = new LeagueOnlineAccount { FideId = "222", Site = "chess.com", UserName = "c", Url = "u", Confidence = "sicher" };
        _db.LeagueOnlineAccounts.AddRange(li1, li2, cc);
        await _db.SaveChangesAsync();
        LeagueOnlineGame G(LeagueOnlineAccount a, string id) => new()
        {
            AccountId = a.Id, FideId = a.FideId, ExternalId = id, PlayedAt = DateTime.UtcNow, Speed = "blitz", Result = "1-0", Line = "e4", Moves = "e4",
        };
        // „x1" spielten die beiden Lichess-Konten GEGENEINANDER — eine Partie, zwei Zeilen.
        _db.LeagueOnlineGames.AddRange(G(li1, "x1"), G(li2, "x1"), G(li1, "x2"), G(li2, "x3"), G(cc, "c1"));
        await _db.SaveChangesAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var r = await new LeagueGameSources(_db, cache).GetAsync(TestClubs.HomeId, default);
        Assert.Equal(7, r["boardTotal"]!.GetValue<int>());
        var board = r["board"]!.AsArray().Select(x => ((string)x!["label"]!, x["games"]!.GetValue<int>())).ToList();
        Assert.Equal(new[] { ("ChessBase-Megabase", 2), ("chess-results", 2), ("Lichess-Übertragungen", 1), ("Lumbra", 1), ("Vereins-Datenbank", 1) }, board);
        Assert.Equal(4, r["onlineTotal"]!.GetValue<int>());
        var online = r["online"]!.AsArray().Select(x => ((string)x!["key"]!, (string)x["label"]!, x["games"]!.GetValue<int>())).ToList();
        Assert.Equal(new[] { ("lichess", "Lichess", 3), ("chess.com", "chess.com", 1) }, online);

        // 30 min im Speicher: eine neue Partie zählt erst danach.
        _db.LeagueOnlineGames.Add(G(cc, "c2"));
        await _db.SaveChangesAsync();
        Assert.Equal(4, (await new LeagueGameSources(_db, cache).GetAsync(TestClubs.HomeId, default))["onlineTotal"]!.GetValue<int>());
        Assert.Equal(5, (await new LeagueGameSources(_db).GetAsync(TestClubs.HomeId, default))["onlineTotal"]!.GetValue<int>());
    }

    [Fact]
    public async Task Opponent_CountsOnlyThesePlayers_SharedGameOnce_OnlineWithAccounts()
    {
        _db.LeaguePlayerProfiles.AddRange(
            new LeaguePlayerProfile { FideId = "222", Name = "Muster, Max", Pgn = MusterPgn },
            new LeaguePlayerProfile { FideId = "333", Name = "Huber, Franz", Pgn = HuberPgn },
            new LeaguePlayerProfile { FideId = "999", Name = "Fremd, Fritz", Pgn = Game("Fremd, Fritz", "X, Y", "2020.01.01", "1") });
        _db.LeagueClubGames.AddRange(
            new LeagueClubGame { ClubId = TestClubs.HomeId, White = "Schwaz", Black = "Huber, Franz", BlackFide = "333", Pgn = "1. d4 *", MovesHash = "a" },
            new LeagueClubGame { ClubId = TestClubs.HomeId, White = "Schwaz", Black = "Fremd, Fritz", BlackFide = "999", Pgn = "1. e4 *", MovesHash = "b" });
        var sure = new LeagueOnlineAccount { FideId = "222", Site = "lichess", UserName = "a", Url = "u", Confidence = "sicher" };
        var unsure = new LeagueOnlineAccount { FideId = "333", Site = "chess.com", UserName = "b", Url = "u", Confidence = "wahrscheinlich" };
        _db.LeagueOnlineAccounts.AddRange(sure, unsure);
        await _db.SaveChangesAsync();
        _db.LeagueOnlineGames.AddRange(
            new LeagueOnlineGame { AccountId = sure.Id, FideId = "222", ExternalId = "l1", PlayedAt = DateTime.UtcNow, Speed = "blitz", Result = "1-0", Line = "e4", Moves = "e4" },
            new LeagueOnlineGame { AccountId = sure.Id, FideId = "222", ExternalId = "l2", PlayedAt = DateTime.UtcNow, Speed = "blitz", Result = "1-0", Line = "e4", Moves = "e4" },
            new LeagueOnlineGame { AccountId = unsure.Id, FideId = "333", ExternalId = "c1", PlayedAt = DateTime.UtcNow, Speed = "blitz", Result = "1-0", Line = "e4", Moves = "e4" });
        await _db.SaveChangesAsync();

        // Gegner = Muster + Huber (+ leere/doppelte Einträge der Meldeliste): ihre gemeinsame Partie einmal, Fremd nicht.
        var r = await new LeagueGameSources(_db).GetAsync(TestClubs.HomeId, default, new[] { "222", "333", null, "", "222" });
        var o = r["opponent"]!.AsObject();
        Assert.Equal(2, o["players"]!.GetValue<int>());
        Assert.Equal(7, o["boardTotal"]!.GetValue<int>());                          // 6 aus den PGN + 1 Vereinspartie von Huber
        Assert.Equal(1, o["board"]!["Verein"]!.GetValue<int>());
        Assert.Equal(2, o["board"]!["Mega"]!.GetValue<int>());
        Assert.Equal((2, 1), (o["online"]!["lichess"]!["games"]!.GetValue<int>(), o["online"]!["lichess"]!["accounts"]!.GetValue<int>()));
        Assert.Equal((1, 1), (o["online"]!["chess.com"]!["games"]!.GetValue<int>(), o["online"]!["chess.com"]!["accounts"]!.GetValue<int>()));
        Assert.Equal((3, 2), (o["onlineTotal"]!.GetValue<int>(), o["onlineAccounts"]!.GetValue<int>()));
        Assert.Equal(9, r["boardTotal"]!.GetValue<int>());                          // Gesamt bleibt Gesamt

        // Teilen-Link: online nur gesicherte Konten — das unsichere chess.com-Konto zählt nicht, auch nicht als Konto.
        var shared = (await new LeagueGameSources(_db).GetAsync(TestClubs.HomeId, default, new[] { "222", "333" }, onlySure: true))["opponent"]!;
        Assert.Null(shared["online"]!["chess.com"]);
        Assert.Equal((2, 1), (shared["onlineTotal"]!.GetValue<int>(), shared["onlineAccounts"]!.GetValue<int>()));

        // Ohne Gegner kein Block; ein Gegner ohne Daten zählt 0 und hat kein Konto.
        Assert.Null((await new LeagueGameSources(_db).GetAsync(TestClubs.HomeId, default))["opponent"]);
        var none = (await new LeagueGameSources(_db).GetAsync(TestClubs.HomeId, default, new[] { "4711" }))["opponent"]!;
        Assert.Equal((1, 0, 0), (none["players"]!.GetValue<int>(), none["boardTotal"]!.GetValue<int>(), none["onlineAccounts"]!.GetValue<int>()));
        // Höchstens 40 Spieler.
        var many = (await new LeagueGameSources(_db).GetAsync(TestClubs.HomeId, default, Enumerable.Range(1, 100).Select(i => i.ToString())))["opponent"]!;
        Assert.Equal(LeagueGameSources.MaxOpponentPlayers, many["players"]!.GetValue<int>());
    }

    [Fact]
    public async Task League_CountsAllRostersOfThatLeague_Cached_UnknownLeagueIsEmpty()
    {
        _db.LeaguePlayers.AddRange(
            new LeaguePlayer { Tnr = 1, Team = "Kufstein", Name = "Muster, Max", NameKey = "muster, max", FideId = "222" },
            new LeaguePlayer { Tnr = 1, Team = "Kufstein", Name = "Muster, Max", NameKey = "muster, max", FideId = "222" },  // zweimal gemeldet
            new LeaguePlayer { Tnr = 1, Team = "Schwaz", Name = "Huber, Franz", NameKey = "huber, franz", FideId = "333" },
            new LeaguePlayer { Tnr = 1, Team = "Schwaz", Name = "Ohne, Fide", NameKey = "ohne, fide", FideId = null },
            new LeaguePlayer { Tnr = 2, Team = "Absam", Name = "Fremd, Fritz", NameKey = "fremd, fritz", FideId = "999" });
        _db.LeaguePlayerProfiles.AddRange(
            new LeaguePlayerProfile { FideId = "222", Name = "Muster, Max", Pgn = MusterPgn },
            new LeaguePlayerProfile { FideId = "333", Name = "Huber, Franz", Pgn = HuberPgn },
            new LeaguePlayerProfile { FideId = "999", Name = "Fremd, Fritz", Pgn = Game("Fremd, Fritz", "X, Y", "2020.01.01", "1") });
        await _db.SaveChangesAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var r = await new LeagueGameSources(_db, cache).GetAsync(TestClubs.HomeId, default, new[] { "333" }, leagueTnr: 1);
        var l = r["league"]!;
        Assert.Equal((2, 6), (l["players"]!.GetValue<int>(), l["boardTotal"]!.GetValue<int>()));   // Fremd (Liga 2) zählt nicht
        Assert.Equal((1, 2), (r["opponent"]!["players"]!.GetValue<int>(), r["opponent"]!["boardTotal"]!.GetValue<int>()));
        Assert.Equal(7, r["boardTotal"]!.GetValue<int>());

        // 30 min gemerkt — ein neuer Spieler der Liga zählt erst danach; die Begegnung ist immer frisch.
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 1, Team = "Schwaz", Name = "Fremd, Fritz", NameKey = "fremd, fritz", FideId = "999" });
        await _db.SaveChangesAsync();
        Assert.Equal(2, (await new LeagueGameSources(_db, cache).GetAsync(TestClubs.HomeId, default, leagueTnr: 1))["league"]!["players"]!.GetValue<int>());
        Assert.Equal(3, (await new LeagueGameSources(_db).GetAsync(TestClubs.HomeId, default, leagueTnr: 1))["league"]!["players"]!.GetValue<int>());
        Assert.Equal(0, (await new LeagueGameSources(_db).GetAsync(TestClubs.HomeId, default, leagueTnr: 4711))["league"]!["players"]!.GetValue<int>());
        Assert.Null((await new LeagueGameSources(_db).GetAsync(TestClubs.HomeId, default))["league"]);
    }
}
