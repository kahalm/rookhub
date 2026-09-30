using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Deckel der Lückensuche (Codereview 2026-09-29, N5-001). <c>…/gap</c> und <c>…/gap/propose</c> rechneten bis
/// 12 s bzw. 3 Mio. Knoten im Request-Thread, und nur der globale Deckel von 100/min je Adresse zählte — ein frei
/// registriertes Konto hielt so rund hundert Suchen gleichzeitig am Laufen. Jetzt: Rate-Limit je Konto, eine
/// prozessweite Schranke für gleichzeitige Suchen (sonst sofort 429), kleineres Budget, Abbruch mit der Anfrage.
/// </summary>
public class GapSearchLimitTests : IDisposable
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string AfterFivePlies = "r1bqkbnr/pppp1ppp/2n5/1B2p3/4P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3";
    /// <summary>Zwei weiße Läufer auf WEISSEN Feldern, der schwarzfeldrige fehlt, kein Bauer ist weg: unerreichbar,
    /// aber die Schranke sagt „nah" — die Suche kämmt den ganzen Baum bis zum Budget durch.</summary>
    private const string ImpossibleButNear = "rnbqkbnr/pppppppp/8/8/4P3/3B4/PPPP1PPP/RN1QKBNR w KQkq - 0 1";

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    // ── Rate-Limit je Konto ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(nameof(GameReconstructionController.SolveGap))]
    [InlineData(nameof(GameReconstructionController.ProposeGap))]
    public void GapEndpoints_UseThePerAccountGapPolicy(string action)
    {
        var method = typeof(GameReconstructionController).GetMethod(action)!;
        Assert.Equal(RateLimitPartitions.ReconstructionGapPolicy,
            method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
    }

    [Fact]
    public void GapPolicy_IsRegisteredInProgram()
    {
        var src = File.ReadAllText(ProgramCs());
        Assert.Contains($@"AddPolicy(""{RateLimitPartitions.ReconstructionGapPolicy}"", ctx => RookHub.Api.Services.RateLimitPartitions.ReconstructionGap(ctx, permitScale))", src);
    }

    [Fact]
    public void ReconstructionGap_CapsOneAccount_ButNotItsNeighboursBehindTheSameAddress()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartitions.ReconstructionGap(c, 1));
        var permit = RateLimitPartitions.ReconstructionGapPermitPerMinute;

        Assert.Equal(permit, Acquired(limiter, Context(42), permit + 5));
        Assert.Equal(permit, Acquired(limiter, Context(43), permit));
        Assert.True(permit <= 10);
    }

    // ── Prozessweite Schranke: sofort 429 statt Stau ─────────────────────────────────────────────

    [Fact]
    public async Task SolveGap_WhenAllSlotsAreTaken_IsBusy_AndRunsAgainOnceOneIsFree()
    {
        var gate = new GapSearchGate(2);
        var service = new GameReconstructionService(_db, gate);
        var (id, target) = await GapAsync(service);

        // Zwei Suchen laufen schon (die Plätze sind belegt) — die dritte rechnet NICHT.
        Assert.True(gate.TryEnter());
        Assert.True(gate.TryEnter());
        await Assert.ThrowsAsync<GapSearchBusyException>(() => service.SolveGapAsync(1, id, target, null));

        gate.Exit();
        var dto = await service.SolveGapAsync(1, id, target, null);
        Assert.Equal("Nc6 Bb5", Assert.Single(dto!.Solutions).San);
        // Der Platz ist nach der Suche wieder frei (auch der zweite war nie verloren).
        Assert.True(gate.TryEnter());
        Assert.False(gate.TryEnter());
    }

    [Fact]
    public async Task ProposeGap_WhenBusy_KeepsTheEarlierProposals()
    {
        var gate = new GapSearchGate(1);
        var service = new GameReconstructionService(_db, gate);
        var (id, target) = await GapAsync(service);
        Assert.Equal(1, (await service.ProposeGapAsync(1, id, target, null))!.Inserted);

        Assert.True(gate.TryEnter());
        await Assert.ThrowsAsync<GapSearchBusyException>(() => service.ProposeGapAsync(1, id, target, null));

        var detail = await service.GetAsync(1, id);
        Assert.Equal(3, detail!.Parts.Count);
        Assert.True(detail.Parts[1].Generated);
    }

    [Fact]
    public async Task Controller_AnswersBusyWith429_ForBothSearchEndpoints()
    {
        var gate = new GapSearchGate(1);
        var service = new GameReconstructionService(_db, gate);
        var (id, target) = await GapAsync(service);
        var controller = new GameReconstructionController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1")], "Test")),
                },
            },
        };

        Assert.True(gate.TryEnter());
        var solve = await controller.SolveGap(id, target, null, CancellationToken.None);
        var propose = await controller.ProposeGap(id, target, null, CancellationToken.None);

        Assert.Equal(StatusCodes.Status429TooManyRequests, Assert.IsType<ObjectResult>(solve.Result).StatusCode);
        Assert.Equal(StatusCodes.Status429TooManyRequests, Assert.IsType<ObjectResult>(propose.Result).StatusCode);

        gate.Exit();
        Assert.IsType<OkObjectResult>((await controller.SolveGap(id, target, null, CancellationToken.None)).Result);
    }

    /// <summary>Program.cs registriert nur den Dienst: die Schranke (nicht registriert) und die Lebensdauer kommen über
    /// optionale Parameter — der Container muss ihn trotzdem bauen können.</summary>
    [Fact]
    public void Service_ResolvesFromTheContainer_WithoutARegisteredGate()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddScoped<GameReconstructionService>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GameReconstructionService>());
    }

    [Fact]
    public void SharedGate_HasTwoToFourSlots()
    {
        Assert.InRange(GapSearchGate.DefaultSlots, 2, 4);
    }

    // ── Budget und Abbruch ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultBudget_IsSmall()
    {
        Assert.True(GapSolver.DefaultTimeBudget <= TimeSpan.FromSeconds(4), $"{GapSolver.DefaultTimeBudget}");
        Assert.True(GapSolver.DefaultNodeBudget <= 500_000, $"{GapSolver.DefaultNodeBudget}");
        Assert.True(GapSolver.MaxDeadEntries <= GapSolver.DefaultNodeBudget);
    }

    /// <summary>Das Fehlerszenario: ein Ziel, das die Schranke für nah hält, das aber in zwölf Halbzügen NICHT
    /// erreichbar ist — die Suche läuft bis zum Budget. Vorher 12 s je Aufruf, jetzt das neue Budget (4 s) plus
    /// ein Uhr-Intervall von 1024 Knoten; die Grenze lässt 2 s Luft für einen langsamen CI-Rechner.</summary>
    [Fact]
    public void Solve_ImpossibleButNearTarget_GivesUpWithinTheDefaultBudget()
    {
        var sw = Stopwatch.StartNew();
        var result = GapSolver.Solve(Start, ImpossibleButNear, maxPlies: GapSolver.MaxSearchPlies);
        sw.Stop();

        Assert.True(result.BudgetExhausted);
        Assert.Equal("budget", result.Reason);
        Assert.Empty(result.Solutions);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(6), $"{sw.Elapsed.TotalSeconds:0.0} s bei {result.Nodes} Knoten");
    }

    [Fact]
    public void Solve_CancelledToken_StopsLikeAnExhaustedBudget()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sw = Stopwatch.StartNew();
        var result = GapSolver.Solve(Start, ImpossibleButNear, maxPlies: GapSolver.MaxSearchPlies, ct: cts.Token);
        sw.Stop();

        Assert.True(result.BudgetExhausted);
        Assert.Equal("budget", result.Reason);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"{sw.Elapsed.TotalMilliseconds:0} ms");
    }

    [Fact]
    public async Task SolveGap_PassesTheRequestTokenToTheSearch()
    {
        var service = new GameReconstructionService(_db, new GapSearchGate(1));
        var id = (await service.CreateAsync(1, new ReconstructionHeadRequest { Title = "Abbruch" })).Id;
        await service.AddPartAsync(1, id, new ReconstructionPartRequest { Kind = ReconstructionPartKind.Position, Fen = Start });
        var target = (await service.AddPartAsync(1, id,
            new ReconstructionPartRequest { Kind = ReconstructionPartKind.Position, Fen = ImpossibleButNear }))!.Parts[1].Id;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var sw = Stopwatch.StartNew();
        var dto = await service.SolveGapAsync(1, id, target, GapSolver.MaxSearchPlies, cts.Token);
        sw.Stop();

        Assert.True(dto!.BudgetExhausted);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"{sw.Elapsed.TotalMilliseconds:0} ms");
    }

    // ── Hilfen ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>„e4 e5 Nf3", Lücke, dann die Stellung nach 3. Bb5 — genau ein Weg (Nc6 Bb5).</summary>
    private static async Task<(int Id, int Target)> GapAsync(GameReconstructionService service)
    {
        var id = (await service.CreateAsync(1, new ReconstructionHeadRequest { Title = "Runde 3" })).Id;
        await service.AddPartAsync(1, id, new ReconstructionPartRequest { Kind = ReconstructionPartKind.Moves, Moves = "e4 e5 Nf3" });
        var target = (await service.AddPartAsync(1, id,
            new ReconstructionPartRequest { Kind = ReconstructionPartKind.Position, Fen = AfterFivePlies }))!.Parts[1].Id;
        return (id, target);
    }

    private static HttpContext Context(int userId)
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
