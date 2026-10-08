using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.DTOs;
using RookHub.Api.Services;
using Xunit.Abstractions;

namespace RookHub.Api.Tests;

/// <summary>Zugfolgen-Suche im lokalen Explorer (<see cref="ExplorerPathFinder"/>): ein kleiner gefälschter Explorer-Baum
/// (Sizilianisch über 1.e4 c5 2.Sf3 und über 1.Sf3 c5 2.e4), dazu Schranken, Budget und der Controller.</summary>
public class ExplorerPathFinderTests
{
    private const string Start = ExplorerPathFinder.StartFen;

    /// <summary>1.e4 c5 2.Sf3 d6 — weiß am Zug.</summary>
    private const string SicilianD6 = "rnbqkbnr/pp2pppp/3p4/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 0 3";
    /// <summary>1.e4 c5 2.Sf3 Sc6 — weiß am Zug.</summary>
    private const string SicilianNc6 = "r1bqkbnr/pp1ppppp/2n5/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3";

    /// <summary>Der gefälschte Baum: je Stellung (Brett + Seite) die Züge mit Partienzahl; unbekannte Stellungen haben keine
    /// Partien.</summary>
    private sealed class FakeExplorer
    {
        private readonly Dictionary<string, (string Uci, string San, long Games)[]> _tree = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (string Eco, string Name)> _openings = new(StringComparer.Ordinal);
        public int Calls;

        public FakeExplorer Add(string fenOrMoves, params (string Uci, string San, long Games)[] moves)
        {
            _tree[Key(fenOrMoves)] = moves;
            return this;
        }

        public FakeExplorer Name(string fenOrMoves, string eco, string name)
        {
            _openings[Key(fenOrMoves)] = (eco, name);
            return this;
        }

        /// <summary>FEN oder eine Zugfolge in UCI ab der Grundstellung („e2e4 c7c5").</summary>
        private static string Key(string fenOrMoves)
        {
            if (fenOrMoves.Contains('/')) return ExplorerPathFinder.Pos.Parse(fenOrMoves)!.BoardKey;
            var pos = ExplorerPathFinder.Pos.Parse(Start)!;
            foreach (var m in fenOrMoves.Split(' ', StringSplitOptions.RemoveEmptyEntries)) pos = pos.Apply(m)!;
            return pos.BoardKey;
        }

        /// <summary>Jede Stellung bekommt zusätzlich Springer-Pendelzüge (Sg1-h3-g1, Sb1-a3-b1, Sg8-h6-g8, Sb8-a6-b8) — wertlose
        /// Umwege, die nie zum Ziel führen, aber immer weiter Abfragen kosten.</summary>
        public bool Detours;
        /// <summary>Stellungen, deren Antwort so lange auf sich warten lässt (eine langsame Abfrage auf der Platte).</summary>
        public readonly Dictionary<string, TimeSpan> Slow = new(StringComparer.Ordinal);

        public FakeExplorer SlowAt(string fenOrMoves, TimeSpan delay)
        {
            Slow[Key(fenOrMoves)] = delay;
            return this;
        }

        private static (string Uci, string San, long Games)[] Shuffles(ExplorerPathFinder.Pos pos)
        {
            var b = pos.Board;
            var list = new List<(string, string, long)>();
            void Try(int from, int to, char piece, string uci, string san)
            {
                if (b[from] == piece && b[to] == '.') list.Add((uci, san, 0));
            }
            if (pos.WhiteToMove)
            {
                Try(6, 23, 'N', "g1h3", "Nh3"); Try(23, 6, 'N', "h3g1", "Ng1");
                Try(1, 16, 'N', "b1a3", "Na3"); Try(16, 1, 'N', "a3b1", "Nb1");
            }
            else
            {
                Try(62, 47, 'n', "g8h6", "Nh6"); Try(47, 62, 'n', "h6g8", "Ng8");
                Try(57, 40, 'n', "b8a6", "Na6"); Try(40, 57, 'n', "a6b8", "Nb8");
            }
            return list.ToArray();
        }

        public async Task<ExplorerPositionStats?> Fetch(string fen, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            var pos = ExplorerPathFinder.Pos.Parse(fen)!;
            var key = pos.BoardKey;
            if (Slow.TryGetValue(key, out var delay)) await Task.Delay(delay, ct);
            var moves = _tree.TryGetValue(key, out var m) ? m : Array.Empty<(string Uci, string San, long Games)>();
            if (Detours)
            {
                // Je Pendelzug 3 % der Partien der Stellung (mindestens 10) — selten, aber über der 1-%-Schwelle.
                var share = Math.Max(10, moves.Sum(x => x.Games) * 3 / 100);
                moves = moves.Concat(Shuffles(pos).Select(x => (x.Uci, x.San, share))).ToArray();
            }
            var list = moves.Select(x => new ExplorerMoveStat(x.Uci, x.San, x.Games, null, null, x.Games, 0, 0)).ToList();
            var total = list.Sum(x => x.Games);
            _openings.TryGetValue(key, out var o);
            return new ExplorerPositionStats(total, list, total, 0, 0, o.Name, o.Eco);
        }
    }

    private static FakeExplorer Sicilian() => new FakeExplorer()
        .Add("", ("e2e4", "e4", 600), ("g1f3", "Nf3", 300), ("d2d4", "d4", 100))
        .Add("e2e4", ("c7c5", "c5", 500), ("e7e5", "e5", 100))
        .Add("e2e4 c7c5", ("g1f3", "Nf3", 400), ("b1c3", "Nc3", 100))
        .Add("e2e4 c7c5 g1f3", ("d7d6", "d6", 200), ("b8c6", "Nc6", 150), ("e7e6", "e6", 50))
        .Add("g1f3", ("c7c5", "c5", 150), ("d7d5", "d5", 150))
        .Add("g1f3 c7c5", ("e2e4", "e4", 100), ("g2g3", "g3", 50))
        .Add("e2e4 c7c5 g1f3 d7d6", ("d2d4", "d4", 240))
        .Name("e2e4 c7c5 g1f3 d7d6", "B50", "Sicilian Defense: Modern Variations");

    private static ExplorerPathFinder Finder(FakeExplorer fake) => new(fake.Fetch, new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task Find_SicilianD6_FindsBothMoveOrders_RankedByEstimatedGames()
    {
        var fake = Sicilian();
        var r = await Finder(fake).FindAsync(SicilianD6, 20, CancellationToken.None);

        Assert.False(r.Truncated);
        Assert.Equal(240, r.Games);
        Assert.Equal("B50", r.Opening?.Eco);
        Assert.Equal(2, r.Paths.Count);
        Assert.Equal(new[] { "e4", "c5", "Nf3", "d6" }, r.Paths[0].Moves);
        Assert.Equal(new[] { "e2e4", "c7c5", "g1f3", "d7d6" }, r.Paths[0].Uci);
        // 1000 · 600/1000 · 500/600 · 400/500 · 200/400 = 200
        Assert.Equal(200, r.Paths[0].EstGames);
        Assert.Equal(new[] { "Nf3", "c5", "e4", "d6" }, r.Paths[1].Moves);
        // 1.Sf3 c5 2.e4 ist dieselbe Stellung wie 1.e4 c5 2.Sf3 — dort 200 von 400 mit d6:
        // 1000 · 300/1000 · 150/300 · 100/150 · 200/400 = 50
        Assert.Equal(50, r.Paths[1].EstGames);
        Assert.Equal(200.0 / 240, r.Paths[0].Share, 6);
    }

    [Fact]
    public async Task Find_Transposition_ExpandsThePositionOnce_ButKeepsBothPaths()
    {
        var fake = Sicilian()
            .Add("e2e4 c7c5 g1f3 b8c6", ("d2d4", "d4", 1));
        var r = await Finder(fake).FindAsync(SicilianNc6, 20, CancellationToken.None);

        Assert.Equal(2, r.Paths.Count);
        Assert.Equal(new[] { "e4", "c5", "Nf3", "Nc6" }, r.Paths[0].Moves);
        Assert.Equal(new[] { "Nf3", "c5", "e4", "Nc6" }, r.Paths[1].Moves);
        // Jede Stellung wird höchstens einmal abgefragt (Zielstellung + jede erweiterte einmal).
        Assert.Equal(r.Queries, fake.Calls);
        Assert.True(r.Searched <= r.Queries);
    }

    [Fact]
    public async Task Find_WrongSideToMove_FindsNothing()
    {
        // Dasselbe Brett, aber Schwarz am Zug: nach einer geraden Zahl Halbzügen ist immer Weiß dran.
        var r = await Finder(Sicilian()).FindAsync(SicilianD6.Replace(" w ", " b "), 20, CancellationToken.None);
        Assert.Empty(r.Paths);
    }

    [Fact]
    public async Task Find_FarAwayPosition_IsCutByTheDistanceBound()
    {
        var fake = Sicilian();
        // Nur die Könige: 30 Felder anders, in 4 Halbzügen höchstens 2·4 + 4 = 12.
        var r = await Finder(fake).FindAsync("4k3/8/8/8/8/8/8/4K3 w - - 0 1", 4, CancellationToken.None);
        Assert.Empty(r.Paths);
        Assert.Equal(0, r.Searched);
        Assert.Equal(2, fake.Calls);   // nur die Zielstellung selbst — mit beiden Seiten am Zug (0 Partien → Gegenprobe)
    }

    [Fact]
    public async Task Find_QueryBudget_EndsTruncated()
    {
        var fake = Sicilian();
        var finder = Finder(fake);
        finder.MaxQueries = 2;
        var r = await finder.FindAsync(SicilianD6, 20, CancellationToken.None);
        Assert.True(r.Truncated);
        Assert.True(fake.Calls <= 2 + ExplorerPathFinder.Parallelism);
    }

    [Fact]
    public async Task Find_SecondCall_ComesFromMemory()
    {
        var fake = Sicilian();
        var finder = Finder(fake);
        await finder.FindAsync(SicilianD6, 20, CancellationToken.None);
        var first = fake.Calls;
        var r = await finder.FindAsync(SicilianD6, 20, CancellationToken.None);
        Assert.Equal(first, fake.Calls);
        Assert.Equal(0, r.Queries);
        Assert.Equal(2, r.Paths.Count);
    }

    [Fact]
    public async Task Find_FewPaths_StopsLongBeforeTheBudget_DespiteWorthlessDetours()
    {
        // Drei Wege (1.e4 c5 2.Sf3, 1.Sf3 c5 2.e4, 1.e4 c5 2.Sc3 … Sf3 gibt es nicht — also zwei plus der über d4 nicht),
        // dazu überall Springer-Pendelzüge. Der Abbruch „zehn Funde" greift nie, die Schwelle von 0,5 % der besten muss tragen.
        var fake = Sicilian();
        fake.Detours = true;
        var finder = Finder(fake);
        var r = await finder.FindAsync(SicilianD6, 20, CancellationToken.None);

        Assert.False(r.Truncated);
        Assert.True(r.Queries < 60, $"{r.Queries} Abfragen");
        Assert.Equal(new[] { "e4", "c5", "Nf3", "d6" }, r.Paths[0].Moves);
        Assert.Equal(new[] { "Nf3", "c5", "e4", "d6" }, r.Paths[1].Moves);
    }

    [Fact]
    public async Task Find_SlowQueryInFlight_DoesNotFillTheOtherSlotsWithDetours()
    {
        // Die Abfrage nach 1.Sf3 c5 (hohe Schätzung) hängt 300 ms an der „Platte". Bis 0.723.1 zählte sie beim Abbruch mit,
        // und die übrigen sieben Plätze holten derweil Pendelzug um Pendelzug, bis das Budget erreicht war.
        var fake = Sicilian().SlowAt("g1f3 c7c5", TimeSpan.FromMilliseconds(300));
        fake.Detours = true;
        var finder = Finder(fake);
        finder.MinShareOfBest = ExplorerPathFinder.MinRelative;   // nur die Regel „unterwegs zählt nicht" prüfen
        var r = await finder.FindAsync(SicilianD6, 20, CancellationToken.None);

        Assert.False(r.Truncated);
        Assert.True(r.Queries < 70, $"{r.Queries} Abfragen");
        Assert.Equal(2, r.Paths.Count(p => p.Moves.SequenceEqual(new[] { "e4", "c5", "Nf3", "d6" })
                                           || p.Moves.SequenceEqual(new[] { "Nf3", "c5", "e4", "d6" })));
    }

    [Fact]
    public async Task Find_WrongSide_ReportsGamesWithTheOtherSideToMove()
    {
        var fake = Sicilian().Add("e2e4 c7c5 g1f3 d7d6", ("d2d4", "d4", 5000));
        var r = await Finder(fake).FindAsync(SicilianD6.Replace(" w ", " b "), 20, CancellationToken.None);
        Assert.Equal(0, r.Games);
        Assert.Equal(5000, r.OtherSideGames);

        // Gut bekannte Stellung mit Zugfolgen: keine Gegenprobe.
        var ok = await Finder(fake).FindAsync(SicilianD6, 20, CancellationToken.None);
        Assert.Equal(5000, ok.Games);
        Assert.NotEmpty(ok.Paths);
        Assert.Null(ok.OtherSideGames);
    }

    [Fact]
    public void OtherSideFen_FlipsTheSide_AndDropsEnPassant()
    {
        var pos = ExplorerPathFinder.Pos.Parse("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2")!;
        var flipped = ExplorerPathFinder.OtherSideFen(pos).Split(' ');
        Assert.Equal("b", flipped[1]);
        Assert.Equal("-", flipped[3]);
    }

    private sealed class TestClock : Microsoft.Extensions.Internal.ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public async Task Find_PathCache_KeepsAnswers24Hours_LochfinderKeyOnlyOne()
    {
        var clock = new TestClock();
        var shared = new MemoryCache(new MemoryCacheOptions { Clock = clock });
        var paths = new MemoryCache(new MemoryCacheOptions { Clock = clock, SizeLimit = ExplorerPathFinder.CacheSizeLimit });
        var fake = Sicilian();
        var finder = new ExplorerPathFinder(fake.Fetch, shared, null, paths);
        var first = await finder.FindAsync(SicilianD6, 20, CancellationToken.None);
        Assert.True(first.Queries > 0);

        clock.UtcNow += TimeSpan.FromHours(2);   // Lochfinder-Schlüssel (1 h) abgelaufen, eigener Speicher nicht
        Assert.False(shared.TryGetValue(RepertoireExplorerService.LocalMemoryKey(ExplorerPathFinder.Query,
            RepertoireReach.Key(ExplorerPathFinder.StartFen)), out _));
        var warm = await finder.FindAsync(SicilianD6, 20, CancellationToken.None);
        Assert.Equal(0, warm.Queries);
        Assert.Equal(first.Paths.Count, warm.Paths.Count);

        clock.UtcNow += TimeSpan.FromHours(23);   // 25 h nach dem ersten Lauf
        var cold = await finder.FindAsync(SicilianD6, 20, CancellationToken.None);
        Assert.Equal(first.Queries, cold.Queries);
    }

    [Theory]
    [InlineData("")]
    [InlineData("kein fen")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQ1BNR w - - 0 1")]   // kein weißer König
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNP w - - 0 1")]   // Bauer auf der Grundreihe
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR x - - 0 1")]
    public async Task Find_InvalidFen_Throws(string fen)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Finder(Sicilian()).FindAsync(fen, 20, CancellationToken.None));
    }

    [Fact]
    public void Pos_Castling_AsKingTakesRook_AndEnPassant()
    {
        var italian = ExplorerPathFinder.Pos.Parse("r1bqk1nr/pppp1ppp/2n5/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4")!;
        var castled = italian.Apply("e1h1")!;
        Assert.Equal("r1bqk1nr/pppp1ppp/2n5/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQ1RK1 b kq", castled.BoardKey + " " + castled.ToFen().Split(' ')[2]);
        Assert.Equal("e1g1", ExplorerPathFinder.Pos.StandardUci(italian, "e1h1"));
        Assert.Equal("e1g1", ExplorerPathFinder.Pos.StandardUci(italian, "e1g1"));
        Assert.Equal(castled.BoardKey, italian.Apply("e1g1")!.BoardKey);

        var ep = ExplorerPathFinder.Pos.Parse("rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3")!.Apply("e5f6")!;
        Assert.Equal("rnbqkbnr/ppp1p1pp/5P2/3p4/8/8/PPPP1PPP/RNBQKBNR b", ep.BoardKey);
        Assert.Contains(" e3 ", ExplorerPathFinder.Pos.Parse(Start)!.Apply("e2e4")!.ToFen());
    }

    [Fact]
    public void Bounds_PawnsDoNotGoBack()
    {
        // Ziel: weißer Bauer auf e4; aktuell steht er schon auf e5 und kein anderer kommt hin → unerreichbar.
        var target = ExplorerPathFinder.Pos.Parse("rnbqkbnr/pppp1ppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 1")!;
        var bounds = new ExplorerPathFinder.Bounds(target, 20);
        var ahead = ExplorerPathFinder.Pos.Parse("rnbqkbnr/pppp1ppp/8/4P3/8/8/PPPP1PPP/RNBQKBNR w KQkq - 0 1")!;
        Assert.False(bounds.Feasible(ahead, 2, out _));
        Assert.True(bounds.Feasible(ExplorerPathFinder.Pos.Parse(Start)!, 0, out var need));
        Assert.Equal(1, need);   // e2-e4 als Doppelschritt (der fehlende schwarze e-Bauer wird geschlagen)
    }

    // ---- Controller ----

    private static ExplorerController Controller() => new(null!)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "7") }, "Test")),
            },
        },
    };

    private static string ReasonOf(IActionResult? r)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(r);
        return (string)bad.Value!.GetType().GetProperty("reason")!.GetValue(bad.Value)!;
    }

    [Fact]
    public async Task Controller_NoLocalExplorer_Is400()
    {
        var finder = new ExplorerPathFinder(null, new MemoryCache(new MemoryCacheOptions()));
        var r = await Controller().Paths(SicilianD6, 20, "local", finder, CancellationToken.None);
        Assert.Equal("noLocalExplorer", ReasonOf(r.Result));
    }

    [Fact]
    public async Task Controller_InvalidFen_And_OnlineSource_Are400()
    {
        var finder = Finder(Sicilian());
        Assert.Equal("invalidFen", ReasonOf((await Controller().Paths("8/8/8 w", 20, "local", finder, CancellationToken.None)).Result));
        Assert.Equal("onlyLocal", ReasonOf((await Controller().Paths(SicilianD6, 20, "online", finder, CancellationToken.None)).Result));
    }

    [Fact]
    public async Task Controller_Ok_ReturnsPaths()
    {
        var r = await Controller().Paths(SicilianD6, null, null, Finder(Sicilian()), CancellationToken.None);
        var dto = Assert.IsType<ExplorerPathsResultDto>(Assert.IsType<OkObjectResult>(r.Result).Value);
        Assert.Equal(2, dto.Paths.Count);
    }

    [Fact]
    public void Controller_Paths_IsAuthorizedAndRateLimited()
    {
        Assert.NotNull(typeof(ExplorerController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).FirstOrDefault());
        var limit = typeof(ExplorerController).GetMethod(nameof(ExplorerController.Paths))!
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), false).Cast<EnableRateLimitingAttribute>().Single();
        Assert.Equal(RateLimitPartitions.ExplorerPathsPolicy, limit.PolicyName);
        Assert.Contains($@"AddPolicy(""{RateLimitPartitions.ExplorerPathsPolicy}""", File.ReadAllText(ProgramCs()));
    }

    private static string ProgramCs([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile);
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "src", "api", "RookHub.Api", "Program.cs");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        Assert.Fail("Program.cs nicht gefunden");
        return "";
    }
}

/// <summary>Gegen den ECHTEN lokalen Explorer, nur mit <c>ROOKHUB_TEST_EXPLORER_URL</c> (z. B. http://127.0.0.1:9002/) — lesend.</summary>
public class ExplorerPathFinderLiveTests(ITestOutputHelper output)
{
    public sealed class LiveFactAttribute : FactAttribute
    {
        public LiveFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ROOKHUB_TEST_EXPLORER_URL")))
                Skip = "ROOKHUB_TEST_EXPLORER_URL fehlt — Messung nur lokal gegen den echten Explorer.";
        }
    }

    [LiveFact]
    public async Task Najdorf_After5a6_IsFoundInTheRealExplorer()
    {
        var url = Environment.GetEnvironmentVariable("ROOKHUB_TEST_EXPLORER_URL")!.TrimEnd('/') + "/";
        var local = new LocalExplorerClient(new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(30) },
            NullLogger<LocalExplorerClient>.Instance);
        var finder = new ExplorerPathFinder(local, new MemoryCache(new MemoryCacheOptions()), new MemoryCache(new MemoryCacheOptions { SizeLimit = ExplorerPathFinder.CacheSizeLimit }), NullLogger<ExplorerPathFinder>.Instance);
        if (int.TryParse(Environment.GetEnvironmentVariable("ROOKHUB_TEST_EXPLORER_MAXQ"), out var maxq)) finder.MaxQueries = maxq;
        var fen = Environment.GetEnvironmentVariable("ROOKHUB_TEST_EXPLORER_FEN")
                  ?? "rnbqkb1r/1p2pppp/p2p1n2/8/3NP3/2N5/PPP2PPP/R1BQKB1R w KQkq - 0 6";
        var clock = Stopwatch.StartNew();
        var r = await finder.FindAsync(fen, 20, CancellationToken.None);
        output.WriteLine($"{clock.Elapsed.TotalSeconds:0.00} s, {r.Queries} Abfragen, {r.Searched} Stellungen, truncated={r.Truncated}, games={r.Games}, opening={r.Opening?.Eco} {r.Opening?.Name}");
        foreach (var p in r.Paths) output.WriteLine($"  {p.EstGames,10}  {p.Share:P1}  {string.Join(' ', p.Moves)}");
        Assert.NotEmpty(r.Paths);
        Assert.Equal(new[] { "e4", "c5", "Nf3", "d6", "d4", "cxd4", "Nxd4", "Nf6", "Nc3", "a6" }, r.Paths[0].Moves);
    }

    /// <summary>Messung je Stellung (Abfragen, Zeit, Wege) — `ROOKHUB_TEST_EXPLORER_SHARE` setzt <see cref="ExplorerPathFinder.MinShareOfBest"/>
    /// für Vergleiche. Ein zweiter Lauf mit DEMSELBEN Speicher zeigt den warmen Fall.</summary>
    [LiveTheory]
    [InlineData("alapin", "rnbqkbnr/pp3ppp/4p3/2pp4/4P3/2P2N2/PP1P1PPP/RNBQKB1R w KQkq - 0 1")]
    [InlineData("najdorf", "rnbqkb1r/1p2pppp/p2p1n2/8/3NP3/2N5/PPP2PPP/R1BQKB1R w KQkq - 0 6")]
    [InlineData("berlin", "r1bqkb1r/pppp1ppp/2n2n2/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4")]
    [InlineData("qgd", "rnbqkb1r/ppp2ppp/4pn2/3p4/2PP4/2N5/PP2PPPP/R1BQKBNR w KQkq - 2 4")]
    [InlineData("italienisch-weiss", "r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/3P1N2/PPP2PPP/RNBQK2R w KQkq - 0 1")]
    public async Task Measure(string name, string fen)
    {
        var url = Environment.GetEnvironmentVariable("ROOKHUB_TEST_EXPLORER_URL")!.TrimEnd('/') + "/";
        var local = new LocalExplorerClient(new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(30) },
            NullLogger<LocalExplorerClient>.Instance);
        var memory = new MemoryCache(new MemoryCacheOptions());
        var finder = new ExplorerPathFinder(local, memory, new MemoryCache(new MemoryCacheOptions { SizeLimit = ExplorerPathFinder.CacheSizeLimit }), NullLogger<ExplorerPathFinder>.Instance);
        if (double.TryParse(Environment.GetEnvironmentVariable("ROOKHUB_TEST_EXPLORER_SHARE"),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var share))
            finder.MinShareOfBest = share;
        for (var run = 1; run <= 2; run++)
        {
            var clock = Stopwatch.StartNew();
            var r = await finder.FindAsync(fen, 20, CancellationToken.None);
            output.WriteLine($"{name} Lauf {run}: {clock.Elapsed.TotalSeconds:0.00} s, {r.Queries} Abfragen, {r.Searched} Stellungen, " +
                             $"truncated={r.Truncated}, games={r.Games}, otherSide={r.OtherSideGames}, Wege={r.Paths.Count}");
            if (run == 1) foreach (var p in r.Paths) output.WriteLine($"  {p.EstGames,10}  {p.Share:P2}  {string.Join(' ', p.Moves)}");
        }
    }

    public sealed class LiveTheoryAttribute : TheoryAttribute
    {
        public LiveTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ROOKHUB_TEST_EXPLORER_URL")))
                Skip = "ROOKHUB_TEST_EXPLORER_URL fehlt — Messung nur lokal gegen den echten Explorer.";
        }
    }
}
