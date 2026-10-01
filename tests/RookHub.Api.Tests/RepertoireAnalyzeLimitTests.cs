using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Chess;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Deckel der Repertoire-Analyse der Erweiterung (Codereview 2026-09-29, N8-005). <c>refresh: true</c> verwarf vor
/// JEDER Anfrage den Cache — ein Konto ließ so im Takt des globalen Limiters (100/min je Adresse) alle markierten PGNs
/// laden und Halbzug für Halbzug nachspielen, parallele Anfragen bauten dasselbe Set mehrfach gleichzeitig, und der
/// Cache hatte keine Größengrenze. Jetzt: Refresh höchstens einmal je Konto und Minute, ein Neuaufbau je Konto zur
/// Zeit, Deckel für PGN-Menge und Stellungen (Teil-Set mit Hinweis), eigener Cache mit Größengrenze, 30 Anfragen/min
/// je Konto. Nacharbeit: Stellungsdeckel je KONTO über alle Arten, ein vom Cache abgewiesenes Set wird nach einer
/// Verdichtung doch eingestellt, Prozess-Schranke für Neuaufbauten (429 busy), PGN-Deckel über FileSize mit Abstand.
/// </summary>
public class RepertoireAnalyzeLimitTests : IDisposable
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = RepertoireAnalyzeService.CacheSizeLimit });
    private readonly List<AppDbContext> _extraContexts = new();
    /// <summary>Eigene Prozess-Schranke je Test — die geteilte (<see cref="PositionSetBuildGate.Shared"/>) bleibt
    /// anderen Testklassen.</summary>
    private readonly PositionSetBuildGate _gate = new(2, TimeSpan.FromSeconds(5));

    public RepertoireAnalyzeLimitTests()
    {
        _db = NewContext();
    }

    public void Dispose()
    {
        foreach (var c in _extraContexts) c.Dispose();
        _db.Dispose();
        _cache.Dispose();
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options);

    /// <summary>Eine Anfrage = eigener DbContext + eigene Dienst-Instanz, geteilt sind nur Cache und Schranke (wie in
    /// der API).</summary>
    private RepertoireAnalyzeService Request(IMemoryCache? cache = null, PositionSetBuildGate? gate = null,
        int positionsPerAccount = RepertoireAnalyzeService.MaxPositionsPerAccount)
    {
        var db = NewContext();
        lock (_extraContexts) _extraContexts.Add(db);
        return new RepertoireAnalyzeService(db, cache ?? _cache, buildGate: gate ?? _gate)
        {
            PositionsPerAccount = positionsPerAccount,
        };
    }

    private async Task<(int UserId, int RepertoireId)> SeedAsync(params string[] pgns)
    {
        var user = new AppUser { Username = "u", Email = "u@x.y", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        var repId = await AddRepertoireAsync(user.Id, RepertoireKind.Opening, pgns);
        return (user.Id, repId);
    }

    private async Task<int> AddRepertoireAsync(int userId, RepertoireKind kind, params string[] pgns)
    {
        var rep = new Repertoire { UserId = userId, Name = kind.ToString(), Kind = kind };
        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();
        foreach (var pgn in pgns) await AddFileAsync(rep.Id, pgn);
        return rep.Id;
    }

    /// <summary>Direkt in die Datenbank — ohne <see cref="RepertoireService"/>, also OHNE Invalidate des Caches.
    /// <paramref name="fileSize"/> wie gespeichert (Bytes); der Deckel misst daran, nicht am Text.</summary>
    private async Task AddFileAsync(int repertoireId, string pgn, long? fileSize = null)
    {
        _db.RepertoireFiles.Add(new RepertoireFile { RepertoireId = repertoireId, FileName = "f.pgn", PgnContent = pgn, FileSize = fileSize ?? pgn.Length });
        await _db.SaveChangesAsync();
    }

    /// <summary>13 Stellungen: Grundstellung + 6 in der Variante + 6 in der Hauptlinie.</summary>
    private const string Pgn13 = "[Event \"a\"]\n\n1. e4 (1. d4 d5 2. c4 e6 3. Nc3 Nf6) 1... e5 2. Nf3 Nc6 3. Bb5 a6 *";
    /// <summary>13 Stellungen, keine davon (außer der Grundstellung) in <see cref="Pgn13"/>.</summary>
    private const string Other13 = "[Event \"b\"]\n\n1. c4 e5 2. Nc3 Nf6 3. g3 d5 4. cxd5 Nxd5 5. Bg2 Nb6 6. Nf3 Nc6 *";

    private static AnalyzeGameRequestDto Moves(bool refresh, params string[] moves) =>
        new() { Moves = moves.ToList(), Kind = RepertoireKind.Opening, Refresh = refresh };

    // ── refresh: höchstens ein Neuaufbau je Konto und Minute ─────────────────────────────────────

    [Fact]
    public async Task Refresh_RebuildsOnce_ThenUsesTheCacheUntilTheCooldownIsOver()
    {
        var (userId, repId) = await SeedAsync("[Event \"a\"]\n\n1. e4 e5 *");
        Assert.Equal(1, (await Request().AnalyzeAsync(userId, Moves(false))).RepertoireFileCount);

        await AddFileAsync(repId, "[Event \"b\"]\n\n1. d4 d5 *");
        var first = await Request().AnalyzeAsync(userId, Moves(true, "d4", "d5"));
        Assert.Equal(2, first.RepertoireFileCount);   // der erste Refresh baut neu
        Assert.Equal(-1, first.Deviation);

        await AddFileAsync(repId, "[Event \"c\"]\n\n1. c4 c5 *");
        var second = await Request().AnalyzeAsync(userId, Moves(true, "c4", "c5"));
        Assert.Equal(2, second.RepertoireFileCount);  // der zweite in derselben Minute liest den Cache
        Assert.Equal(0, second.Deviation);
    }

    [Fact]
    public async Task Refresh_CooldownIsPerAccount_AndInvalidateStillRebuilds()
    {
        var (userId, repId) = await SeedAsync("[Event \"a\"]\n\n1. e4 e5 *");
        await Request().AnalyzeAsync(userId, Moves(true));

        // Andere Art desselben Kontos: kein zweiter Refresh-Neuaufbau in der Minute, aber ein leerer Cache wird gebaut.
        var other = Request();
        await other.AnalyzeAsync(userId, new AnalyzeGameRequestDto { Kind = RepertoireKind.Endgame, Refresh = true });
        Assert.Equal(1, other.SetsBuilt);
        var again = Request();
        await again.AnalyzeAsync(userId, new AnalyzeGameRequestDto { Kind = RepertoireKind.Endgame, Refresh = true });
        Assert.Equal(0, again.SetsBuilt);

        // Upload/Löschen/Ändern verwerfen den Cache weiter sofort.
        await AddFileAsync(repId, "[Event \"b\"]\n\n1. d4 d5 *");
        var service = Request();
        service.Invalidate(userId);
        Assert.Equal(2, (await service.AnalyzeAsync(userId, Moves(true))).RepertoireFileCount);
    }

    // ── Single-Flight: ein Neuaufbau je Konto zur Zeit ───────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ParallelRequests_BuildTheSetExactlyOnce(bool refresh)
    {
        // Genug Züge, dass sich die Neuaufbauten ohne Sperre sicher überlappen würden.
        var (userId, _) = await SeedAsync(LongGamePgn(60), LongGamePgn(60, "Nf3"));
        var services = Enumerable.Range(0, 10).Select(_ => Request()).ToList();

        var results = await Task.WhenAll(services.Select(s =>
            Task.Run(() => s.AnalyzeAsync(userId, Moves(refresh, "e4", "e5")))));

        Assert.Equal(1, services.Sum(s => s.SetsBuilt));
        Assert.All(results, r => Assert.Equal(2, r.RepertoireFileCount));
    }

    // ── Eingabedeckel: Teil-Set mit Hinweis ──────────────────────────────────────────────────────

    [Fact]
    public async Task TooMuchPgn_BuildsAPartialSet_AndSaysSo()
    {
        var (userId, repId) = await SeedAsync("[Event \"a\"]\n\n1. e4 e5 *");
        // Gemessen wird an FileSize (Bytes, wie gespeichert) — die Texte bleiben dafür in der Datenbank.
        await AddFileAsync(repId, "[Event \"big\"]\n\n1. d4 d5 *", RepertoireAnalyzeService.MaxPgnBytesPerSet);
        await AddFileAsync(repId, "[Event \"c\"]\n\n1. c4 c5 *");

        var d4 = await Request().AnalyzeAsync(userId, Moves(false, "d4", "d5"));
        var c4 = await Request().AnalyzeAsync(userId, Moves(false, "c4", "c5"));

        Assert.True(d4.RepertoireTruncated);
        Assert.Equal(3, d4.RepertoireFileCount);   // alle markierten Dateien — „keine Eröffnungen" hieße, es gäbe keine
        Assert.Equal(0, d4.Deviation);             // die Datei, die nicht mehr passt, bleibt draußen — die kleinen danach nicht
        Assert.Equal(-1, c4.Deviation);
    }

    /// <summary>Chessable-Importe hängen ohne Grenze an eine Datei an und tragen Kommentare mehrsprachig: der alte
    /// Deckel (16 Mio. Zeichen) ließ solche Dateien ganz draußen. Jetzt mit Abstand darüber, in Portionen gelesen.</summary>
    [Fact]
    public async Task FilesAboveTheOldSixteenMegabyteCap_StillCountFully()
    {
        var (userId, repId) = await SeedAsync();
        await AddFileAsync(repId, "[Event \"big\"]\n\n1. d4 d5 *", 20L * 1024 * 1024);
        await AddFileAsync(repId, "[Event \"big2\"]\n\n1. c4 c5 *", 20L * 1024 * 1024);

        var r = await Request().AnalyzeAsync(userId, Moves(false, "c4", "c5"));

        Assert.False(r.RepertoireTruncated);
        Assert.Equal(2, r.RepertoireFileCount);
        Assert.Equal(-1, r.Deviation);
    }

    [Fact]
    public async Task NormalRepertoire_IsNotTruncated()
    {
        var (userId, _) = await SeedAsync("[Event \"a\"]\n\n1. e4 e5 2. Nf3 (2. Bc4) 2... Nc6 *");
        var r = await Request().AnalyzeAsync(userId, Moves(false, "e4", "e5", "Bc4"));
        Assert.False(r.RepertoireTruncated);
        Assert.Equal(-1, r.Deviation);
    }

    [Fact]
    public void PositionCap_StopsTheWalk_EvenInsideVariations()
    {
        var pgn = new List<string> { "[Event \"a\"]\n\n1. e4 (1. d4 d5 2. c4 e6 3. Nc3 Nf6) 1... e5 2. Nf3 Nc6 3. Bb5 a6 *" };

        var full = RepertoireAnalyzeService.BuildPositionSet(pgn, out var fullCapped);
        var capped = RepertoireAnalyzeService.BuildPositionSet(pgn, out var wasCapped, maxPositions: 5);

        Assert.False(fullCapped);
        Assert.Equal(13, full.Count);   // Grundstellung + 6 Variante + 6 Hauptlinie
        Assert.True(wasCapped);
        Assert.Equal(5, capped.Count);
        Assert.Subset(full, capped);
    }

    // ── Stellungsdeckel je KONTO über alle Arten ─────────────────────────────────────────────────

    /// <summary>Ein Konto allein durfte den Cache mit vier vollen Sets (je Art eines) so weit füllen, dass er das fünfte
    /// abwies — dann baute jede Anfrage neu. Jetzt teilen sich alle Arten eines Kontos einen Deckel.</summary>
    [Fact]
    public async Task AccountCap_IsSharedByAllKindsOfTheAccount()
    {
        var (userId, _) = await SeedAsync(Pgn13);
        await AddRepertoireAsync(userId, RepertoireKind.Endgame, Other13);

        var opening = await Request(positionsPerAccount: 20).AnalyzeAsync(userId, Moves(false));
        var endgame = await Request(positionsPerAccount: 20).AnalyzeAsync(userId,
            new AnalyzeGameRequestDto { Kind = RepertoireKind.Endgame, Moves = new() { "c4", "e5", "Nc3", "Nf6", "g3", "d5", "cxd5", "Nxd5", "Bg2", "Nb6" } });

        Assert.False(opening.RepertoireTruncated);
        Assert.True(endgame.RepertoireTruncated);   // 20 − 13 = 7 Stellungen bleiben für das Endspiel
        Assert.Equal(6, endgame.Deviation);         // Grundstellung + 6 Halbzüge

        // Verworfen (Upload/Löschen) → wer zuerst kommt, bekommt den Platz.
        Request().Invalidate(userId);
        var endgameFirst = await Request(positionsPerAccount: 20).AnalyzeAsync(userId, new AnalyzeGameRequestDto { Kind = RepertoireKind.Endgame });
        var openingSecond = await Request(positionsPerAccount: 20).AnalyzeAsync(userId, Moves(false));
        Assert.False(endgameFirst.RepertoireTruncated);
        Assert.True(openingSecond.RepertoireTruncated);
    }

    /// <summary>Der Enum-Konverter nimmt auch Zahlen außerhalb von <see cref="RepertoireKind"/> an — solche Arten zählen
    /// zum Deckel des Kontos und werden mit verworfen.</summary>
    [Fact]
    public async Task AccountCap_CountsKindsOutsideTheEnum_AndInvalidateDropsThem()
    {
        var (userId, _) = await SeedAsync(Other13);
        await AddRepertoireAsync(userId, (RepertoireKind)7, Pgn13);

        await Request(positionsPerAccount: 20).AnalyzeAsync(userId, new AnalyzeGameRequestDto { Kind = (RepertoireKind)7 });
        var opening = await Request(positionsPerAccount: 20).AnalyzeAsync(userId, Moves(false));
        Assert.True(opening.RepertoireTruncated);

        Request().Invalidate(userId);
        var again = Request(positionsPerAccount: 20);
        await again.AnalyzeAsync(userId, new AnalyzeGameRequestDto { Kind = (RepertoireKind)7 });
        Assert.Equal(1, again.SetsBuilt);
    }

    // ── Vom Cache abgewiesenes Set ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Maßstäblich verkleinert (Grenze 100, belegt 94 — unter dem Niedrigwasser von 95, also verdichtet der Cache
    /// selbst nicht): ein Set mit 13 Stellungen passt nicht hinein und wurde still abgewiesen, jede weitere Anfrage
    /// baute neu. Jetzt wird verdichtet und das Set doch eingestellt — die zweite Anfrage baut nichts.
    /// </summary>
    [Fact]
    public async Task RejectedCacheEntry_IsStoredAfterCompaction_SoTheNextRequestBuildsNothing()
    {
        using var small = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        small.Set("fremdes-konto", new object(), new MemoryCacheEntryOptions { Size = 94 });
        var (userId, _) = await SeedAsync(Pgn13);

        var first = Request(small);
        var r1 = await first.AnalyzeAsync(userId, Moves(false, "e4", "e5"));
        var second = Request(small);
        var r2 = await second.AnalyzeAsync(userId, Moves(false, "d4", "d5"));
        var third = Request(small);
        await third.AnalyzeAsync(userId, Moves(false));

        Assert.Equal(1, first.SetsBuilt);
        Assert.Equal(0, second.SetsBuilt);
        Assert.Equal(0, third.SetsBuilt);
        Assert.Equal(-1, r1.Deviation);
        Assert.Equal(-1, r2.Deviation);
        Assert.False(small.TryGetValue("fremdes-konto", out _));   // verdrängt: am längsten nicht gelesen
    }

    /// <summary>Auch die Sperrmarke des Refresh kann in einem randvollen Cache abgewiesen werden — dann galt die Minute
    /// nicht, und jeder Refresh baute neu.</summary>
    [Fact]
    public async Task RefreshCooldown_HoldsEvenInAFullCache()
    {
        using var full = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        full.Set("fremdes-konto", new object(), new MemoryCacheEntryOptions { Size = 100 });
        var (userId, _) = await SeedAsync(Pgn13);

        var first = Request(full);
        await first.AnalyzeAsync(userId, Moves(true, "e4"));
        var second = Request(full);
        await second.AnalyzeAsync(userId, Moves(true, "e4"));

        Assert.Equal(1, first.SetsBuilt);
        Assert.Equal(0, second.SetsBuilt);
    }

    // ── Prozess-Schranke für Neuaufbauten ────────────────────────────────────────────────────────

    /// <summary>Die Sperre je Konto ließ N Konten N Kerne binden. Jetzt rechnen im Prozess nur wenige Neuaufbauten
    /// zugleich; wer keinen Platz bekommt, bekommt nach kurzem Warten 429 — ein gecachtes Set gilt weiter.</summary>
    [Fact]
    public async Task ProcessGate_Busy_NoBuild_ButCachedSetsStillServe()
    {
        var gate = new PositionSetBuildGate(1, TimeSpan.FromMilliseconds(50));
        var (userId, _) = await SeedAsync(Pgn13);

        Assert.True(await gate.TryEnterAsync());   // ein anderes Konto baut gerade
        var blocked = Request(gate: gate);
        await Assert.ThrowsAsync<PositionSetBusyException>(() => blocked.AnalyzeAsync(userId, Moves(false, "e4")));
        Assert.Equal(0, blocked.SetsBuilt);
        gate.Exit();

        Assert.Equal(-1, (await Request(gate: gate).AnalyzeAsync(userId, Moves(false, "e4"))).Deviation);

        // Wieder belegt: ohne refresh gilt der Cache, MIT refresh ebenfalls (der alte Eintrag statt 429) ...
        Assert.True(await gate.TryEnterAsync());
        var cached = Request(gate: gate);
        Assert.Equal(-1, (await cached.AnalyzeAsync(userId, Moves(true, "e4"))).Deviation);
        Assert.Equal(0, cached.SetsBuilt);
        gate.Exit();

        // ... und der Refresh ist dabei nicht verbraucht worden.
        var refreshed = Request(gate: gate);
        await refreshed.AnalyzeAsync(userId, Moves(true, "e4"));
        Assert.Equal(1, refreshed.SetsBuilt);
    }

    [Fact]
    public async Task AnalyzeGame_AnswersBusyWith429()
    {
        var gate = new PositionSetBuildGate(1, TimeSpan.FromMilliseconds(50));
        var (userId, _) = await SeedAsync(Pgn13);
        Assert.True(await gate.TryEnterAsync());
        try
        {
            var controller = new ExtensionController(null!, Request(gate: gate), null!, null!, null!, null!, null!, null!,
                null!, null!, null!, null!, null!, NullLogger<ExtensionController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = Context(userId) },
            };

            var result = await controller.AnalyzeGame(Moves(false, "e4"));

            var busy = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(StatusCodes.Status429TooManyRequests, busy.StatusCode);
            Assert.Contains("busy", System.Text.Json.JsonSerializer.Serialize(busy.Value));
        }
        finally { gate.Exit(); }
    }

    /// <summary>Der Partie-Rückblick läuft an der Policy der Erweiterung vorbei — auch er braucht für einen Neuaufbau
    /// einen Platz. Ohne Platz kommen diesmal keine Buchzüge, der Rückblick selbst scheitert nicht.</summary>
    [Fact]
    public async Task BookPlies_WhenTheGateIsBusy_ComeBackEmpty_InsteadOfFailing()
    {
        var gate = new PositionSetBuildGate(1, TimeSpan.FromMilliseconds(50));
        var (userId, _) = await SeedAsync(Pgn13);
        var board = new ChessBoard();
        var fens = new List<string>();
        foreach (var san in new[] { "e4", "e5", "Nf3" }) { board.Move(san); fens.Add(board.ToFen()); }

        Assert.True(await gate.TryEnterAsync());
        Assert.Empty(await Request(gate: gate).BookPliesAsync(userId, fens));
        gate.Exit();

        Assert.Equal(new List<int> { 0, 1, 2 }, await Request(gate: gate).BookPliesAsync(userId, fens));
    }

    // ── Eigener Cache mit Größengrenze ───────────────────────────────────────────────────────────

    /// <summary>Die API baut den Dienst mit dem eigenen, begrenzten Cache — nicht mit dem allgemeinen. Mit SizeLimit
    /// wirft jeder Set ohne Size; der Refresh-Weg setzt zwei Einträge (Sperrmarke + Set).</summary>
    [Fact]
    public async Task DependencyInjection_UsesTheKeyedSizeLimitedCache()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddMemoryCache();
        services.AddKeyedSingleton<IMemoryCache>(RepertoireAnalyzeService.CacheServiceKey, (_, _) =>
            new MemoryCache(new MemoryCacheOptions { SizeLimit = RepertoireAnalyzeService.CacheSizeLimit }));
        services.AddScoped<RepertoireAnalyzeService>();
        using var provider = services.BuildServiceProvider();
        var (userId, _) = await SeedAsync("[Event \"a\"]\n\n1. e4 e5 *");

        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<RepertoireAnalyzeService>()
            .AnalyzeAsync(userId, Moves(true, "e4", "e5"));

        Assert.Equal(-1, result.Deviation);
        var keyed = (MemoryCache)provider.GetRequiredKeyedService<IMemoryCache>(RepertoireAnalyzeService.CacheServiceKey);
        Assert.Equal(2, keyed.Count);
        Assert.Equal(0, ((MemoryCache)provider.GetRequiredService<IMemoryCache>()).Count);
    }

    [Fact]
    public void KeyedCache_IsRegisteredInProgram_WithTheSizeLimit()
    {
        var src = File.ReadAllText(ProgramCs());
        Assert.Contains("AddKeyedSingleton<Microsoft.Extensions.Caching.Memory.IMemoryCache>(RepertoireAnalyzeService.CacheServiceKey", src);
        Assert.Contains("SizeLimit = RepertoireAnalyzeService.CacheSizeLimit", src);
    }

    // ── Rate-Limit je Konto ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnalyzeGame_UsesThePerAccountPolicy()
    {
        var method = typeof(ExtensionController).GetMethod(nameof(ExtensionController.AnalyzeGame))!;
        Assert.Equal(RateLimitPartitions.ExtensionAnalyzePolicy,
            method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
    }

    [Fact]
    public void ExtensionAnalyzePolicy_IsRegisteredInProgram()
    {
        var src = File.ReadAllText(ProgramCs());
        Assert.Contains($@"AddPolicy(""{RateLimitPartitions.ExtensionAnalyzePolicy}"", ctx => RookHub.Api.Services.RateLimitPartitions.ExtensionAnalyze(ctx, permitScale))", src);
    }

    [Fact]
    public void ExtensionAnalyze_CapsOneAccount_ButNotItsNeighboursBehindTheSameAddress()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartitions.ExtensionAnalyze(c, 1));
        var permit = RateLimitPartitions.ExtensionAnalyzePermitPerMinute;

        Assert.Equal(permit, Acquired(limiter, Context(42), permit + 5));
        Assert.Equal(permit, Acquired(limiter, Context(43), permit));
        Assert.True(permit < RateLimitPartitions.GlobalPermitPerMinute);
    }

    private static int Acquired(PartitionedRateLimiter<HttpContext> limiter, HttpContext ctx, int attempts)
    {
        var ok = 0;
        for (var i = 0; i < attempts; i++)
        {
            using var lease = limiter.AttemptAcquire(ctx);
            if (lease.IsAcquired) ok++;
        }
        return ok;
    }

    private static HttpContext Context(int userId)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "Test")),
        };
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.1");
        return ctx;
    }

    /// <summary>Springer hin und her — legale, lange Hauptlinie (viele Halbzüge, wenige Stellungen).</summary>
    private static string LongGamePgn(int plies, string first = "e4")
    {
        var cycle = new[] { "Nf3", "Nf6", "Ng1", "Ng8" };
        var moves = new List<string> { first, first == "e4" ? "e5" : "d5" };
        for (var i = 0; moves.Count < plies; i++) moves.Add(cycle[i % 4]);
        var text = string.Join(' ', moves.Select((m, i) => i % 2 == 0 ? $"{i / 2 + 1}. {m}" : m));
        return $"[Event \"{first}\"]\n\n{text} *";
    }

    private static string ProgramCs([CallerFilePath] string thisFile = "")
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
