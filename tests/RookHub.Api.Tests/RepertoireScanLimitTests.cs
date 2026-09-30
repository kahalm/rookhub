using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Chess;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Deckel des Baummodus und der Ähnlichkeitssuche im Repertoire (Codereview 2026-09-29, N7-001). Beide spielten je
/// Anfrage ALLE lesbaren Linien nach (gecacht war nur das Parsen), ohne Zeitgrenze und nur hinter dem globalen Deckel
/// von 100/min je Adresse — ein Konto mit großem Bestand band so den API-Prozess. Jetzt: Rate-Limit je Konto,
/// Zeitbudget je Anfrage mit <c>truncated</c>, Abbruch mit der Anfrage, und der Brett-Walk hört nach dem Deckel je
/// Linie auf, statt die Linie stumm zu Ende zu spielen.
/// </summary>
public class RepertoireScanLimitTests : IDisposable
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    /// <summary>Damengambit-Abtauschvariante nach 9.Qc2 Te8 (wie in <see cref="RepertoireSimilarityServiceTests"/>).</summary>
    private const string Carlsbad = "r1bqr1k1/pp1nbppp/2p2n2/3p2B1/3P4/2NBPN2/PPQ2PPP/R3K2R w KQ - 7 10";

    private const string CarlsbadPgn =
        "[Event \"Repertoire\"]\n[White \"Damengambit: Abtauschvariante\"]\n[Black \"Damengambit\"]\n\n" +
        "1. d4 d5 2. c4 e6 3. Nc3 Nf6 4. cxd5 exd5 5. Bg5 c6 6. e3 Be7 7. Bd3 O-O 8. Nf3 Nbd7 9. Qc2 Re8 *\n";
    private const string NimzoPgn =
        "[Event \"Repertoire\"]\n[White \"Nimzoindisch: Rubinstein\"]\n[Black \"Nimzoindisch\"]\n\n" +
        "1. d4 Nf6 2. c4 e6 3. Nc3 Bb4 4. e3 O-O 5. Bd3 d5 6. cxd5 exd5 7. Nge2 Re8 8. O-O c6 9. Ng3 Nbd7 *\n";

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose() { _db.Dispose(); _cache.Dispose(); }

    // ── Rate-Limit je Konto ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(nameof(RepertoireController.PositionTree))]
    [InlineData(nameof(RepertoireController.SimilarPositions))]
    public void ScanEndpoints_UseThePerAccountScanPolicy(string action)
    {
        var method = typeof(RepertoireController).GetMethod(action)!;
        Assert.Equal(RateLimitPartitions.RepertoireScanPolicy,
            method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
    }

    /// <summary>Bewusst ohne eigene Policy: sie liest den gecachten Index, und das Panel lädt bei jedem Schritt durch
    /// eine Partie neu — ein enges Fenster zeigte beim Durchklicken Fehler.</summary>
    [Fact]
    public void PositionLookup_ReadsTheCachedIndex_AndStaysOnTheGlobalLimiter()
    {
        var method = typeof(RepertoireController).GetMethod(nameof(RepertoireController.PositionLookup))!;
        Assert.Null(method.GetCustomAttribute<EnableRateLimitingAttribute>());
    }

    [Fact]
    public void ScanPolicy_IsRegisteredInProgram()
    {
        var src = File.ReadAllText(ProgramCs());
        Assert.Contains($@"AddPolicy(""{RateLimitPartitions.RepertoireScanPolicy}"", ctx => RookHub.Api.Services.RateLimitPartitions.RepertoireScan(ctx, permitScale))", src);
    }

    [Fact]
    public void RepertoireScan_CapsOneAccount_ButNotItsNeighboursBehindTheSameAddress()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartitions.RepertoireScan(c, 1));
        var permit = RateLimitPartitions.RepertoireScanPermitPerMinute;

        Assert.InRange(permit, 20, 30);
        Assert.Equal(permit, Acquired(limiter, Context(42), permit + 5));
        Assert.Equal(permit, Acquired(limiter, Context(43), permit));
    }

    // ── Zeitbudget der Ähnlichkeitssuche ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Similar_WithinBudget_ComparesEverything_AndIsNotTruncated()
    {
        var userId = await SeedAsync();
        var svc = new RepertoireSimilarityService(new RepertoireLineSource(_db, _cache));

        var res = await svc.FindAsync(userId, new SimilarPositionsRequestDto { Fen = Carlsbad }, default);

        Assert.False(res.Truncated);
        Assert.True(res.Compared > 0);
        Assert.Contains(res.Matches, m => m.LineName.StartsWith("Nimzoindisch"));
    }

    [Fact]
    public async Task Similar_BudgetSpent_AnswersTruncated_InsteadOfScanningOn()
    {
        var userId = await SeedAsync();
        var svc = new RepertoireSimilarityService(new RepertoireLineSource(_db, _cache)) { Budget = TimeSpan.Zero };

        var res = await svc.FindAsync(userId, new SimilarPositionsRequestDto { Fen = Carlsbad }, default);

        Assert.True(res.Truncated);
        Assert.Equal(0, res.Compared);
        Assert.Empty(res.Matches);
    }

    [Fact]
    public void SimilarBudget_DefaultsToSeconds_NotTheNginxTimeout()
    {
        var svc = new RepertoireSimilarityService(new RepertoireLineSource(_db, _cache));
        Assert.InRange(svc.Budget, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
    }

    // ── Zeitbudget und Abbruch des Baummodus ─────────────────────────────────────────────────────

    [Fact]
    public async Task Tree_WithinBudget_IsComplete_AndNotTruncated()
    {
        var userId = await SeedAsync();
        var svc = new RepertoirePositionLookupService(new RepertoireLineSource(_db, _cache), _cache);

        var res = await svc.TreeAsync(userId, Start, 4, default);

        Assert.False(res.Truncated);
        Assert.Equal(2, res.Repertoires.Count);
        Assert.All(res.Repertoires, r => Assert.False(r.Truncated));
    }

    [Fact]
    public async Task Tree_BudgetSpent_AnswersTruncated_InsteadOfWalkingOn()
    {
        var userId = await SeedAsync();
        var svc = new RepertoirePositionLookupService(new RepertoireLineSource(_db, _cache), _cache) { TreeBudget = TimeSpan.Zero };

        var res = await svc.TreeAsync(userId, Start, 4, default);

        Assert.True(res.Truncated);
        Assert.Empty(res.Repertoires);
        Assert.InRange(new RepertoirePositionLookupService(new RepertoireLineSource(_db, _cache), _cache).TreeBudget,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
    }

    /// <summary>Der Baummodus sah den CancellationToken nie: nach dem Abbruch der Anfrage (nginx nach 60 s, Tab zu)
    /// rechnete er zu Ende. Der Linien-Cache ist warm — sonst bräche schon das Laden ab.</summary>
    [Fact]
    public async Task Tree_CancelledRequest_StopsWalking()
    {
        var userId = await SeedAsync();
        var lines = new RepertoireLineSource(_db, _cache);
        await lines.GetGamesAsync(userId, default);
        var svc = new RepertoirePositionLookupService(lines, _cache);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.TreeAsync(userId, Start, 4, cts.Token));
    }

    // ── Brett-Walk: nach dem letzten gemeldeten Zug nichts mehr spielen ──────────────────────────

    /// <summary>Vorher spielte der Walk nach <c>visit == false</c> die Linie samt Varianten stumm zu Ende — der Deckel
    /// von 400 Stellungen je Linie begrenzte nur die Meldung, nicht die Arbeit. Beobachtet über die Schlagzüge des
    /// Bretts: hinter dem Stopp darf keiner mehr fallen, und das Brett steht wieder auf der Ausgangsstellung.</summary>
    [Fact]
    public void WalkPositions_AfterTheVisitorStops_PlaysNoFurtherMoves_AndRestoresTheBoard()
    {
        var game = PgnMoveTree.ParseSections(
            "[Event \"x\"]\n\n1. e4 d5 (1... e5 2. Nf3 Nc6 3. Bb5 a6 4. Bxc6 dxc6) 2. exd5 Qxd5 3. Nc3 Qxg2 *\n").Single();
        var board = new ChessBoard();
        var captures = 0;
        board.OnCaptured += (_, _) => captures++;
        var visited = new List<string>();

        RepertoireLineSource.WalkPositions(board, game.Moves, 0, (fen, _) =>
        {
            visited.Add(fen);
            return false;                      // nach der ersten Stellung reicht es
        });

        Assert.Single(visited);
        Assert.Equal(0, captures);
        Assert.Equal(Start, board.ToFen());
    }

    [Fact]
    public void WalkPositions_WithoutStop_StillWalksEveryPositionAndCapture()
    {
        var game = PgnMoveTree.ParseSections(
            "[Event \"x\"]\n\n1. e4 d5 (1... e5 2. Nf3 Nc6 3. Bb5 a6 4. Bxc6 dxc6) 2. exd5 Qxd5 3. Nc3 Qxg2 *\n").Single();
        var board = new ChessBoard();
        var captures = 0;
        board.OnCaptured += (_, _) => captures++;
        var visited = 0;

        RepertoireLineSource.WalkPositions(board, game.Moves, 0, (_, _) => { visited++; return true; });

        Assert.Equal(6 + 7, visited);          // Hauptlinie 6 Halbzüge, Variante 7
        Assert.Equal(5, captures);             // Bxc6 dxc6 | exd5 Qxd5 Qxg2
        Assert.Equal(Start, board.ToFen());
    }

    // ── Hilfen ───────────────────────────────────────────────────────────────────────────────────

    private async Task<int> SeedAsync()
    {
        var user = new AppUser { Username = "scan", Email = "scan@x.y", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        foreach (var (name, pgn) in new[] { ("Damengambit", CarlsbadPgn), ("Nimzo", NimzoPgn) })
        {
            var rep = new Repertoire { UserId = user.Id, Name = name, Kind = RepertoireKind.Opening };
            _db.Repertoires.Add(rep);
            await _db.SaveChangesAsync();
            _db.RepertoireFiles.Add(new RepertoireFile { RepertoireId = rep.Id, FileName = "rep.pgn", PgnContent = pgn, FileSize = pgn.Length });
        }
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private static DefaultHttpContext Context(int userId)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"));
        return ctx;
    }

    private static int Acquired(PartitionedRateLimiter<HttpContext> limiter, HttpContext ctx, int tries)
    {
        var ok = 0;
        for (var i = 0; i < tries; i++)
        {
            using var lease = limiter.AttemptAcquire(ctx);
            if (lease.IsAcquired) ok++;
        }
        return ok;
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
