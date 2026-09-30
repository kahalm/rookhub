using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
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
/// je Konto.
/// </summary>
public class RepertoireAnalyzeLimitTests : IDisposable
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = RepertoireAnalyzeService.CacheSizeLimit });
    private readonly List<AppDbContext> _extraContexts = new();

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

    /// <summary>Eine Anfrage = eigener DbContext + eigene Dienst-Instanz, geteilt ist nur der Cache (wie in der API).</summary>
    private RepertoireAnalyzeService Request()
    {
        var db = NewContext();
        lock (_extraContexts) _extraContexts.Add(db);
        return new RepertoireAnalyzeService(db, _cache);
    }

    private async Task<(int UserId, int RepertoireId)> SeedAsync(params string[] pgns)
    {
        var user = new AppUser { Username = "u", Email = "u@x.y", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        var rep = new Repertoire { UserId = user.Id, Name = "Opening", Kind = RepertoireKind.Opening };
        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();
        foreach (var pgn in pgns) await AddFileAsync(rep.Id, pgn);
        return (user.Id, rep.Id);
    }

    /// <summary>Direkt in die Datenbank — ohne <see cref="RepertoireService"/>, also OHNE Invalidate des Caches.</summary>
    private async Task AddFileAsync(int repertoireId, string pgn)
    {
        _db.RepertoireFiles.Add(new RepertoireFile { RepertoireId = repertoireId, FileName = "f.pgn", PgnContent = pgn, FileSize = pgn.Length });
        await _db.SaveChangesAsync();
    }

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
        var huge = "[Event \"big\"]\n\n1. d4 d5 {" + new string('x', (int)RepertoireAnalyzeService.MaxPgnCharsPerSet) + "} *";
        var (userId, _) = await SeedAsync("[Event \"a\"]\n\n1. e4 e5 *", huge, "[Event \"c\"]\n\n1. c4 c5 *");

        var d4 = await Request().AnalyzeAsync(userId, Moves(false, "d4", "d5"));
        var c4 = await Request().AnalyzeAsync(userId, Moves(false, "c4", "c5"));

        Assert.True(d4.RepertoireTruncated);
        Assert.Equal(2, d4.RepertoireFileCount);   // die Datei, die nicht mehr passt, bleibt draußen — die kleinen danach nicht
        Assert.Equal(0, d4.Deviation);
        Assert.Equal(-1, c4.Deviation);
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
