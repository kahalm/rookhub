using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>EndlessController-Eingangs-Guards (nur im Controller, nicht im Service): Session-ID-Regex
/// auf anonymen Endpoints (IDOR) + die Count-Klemmen (DoS). Alle Branches schließen VOR dem Service-
/// Aufruf kurz (BadRequest), daher genügt ein null-Service. Die EndlessProgressService-Logik ist separat
/// getestet.</summary>
public class EndlessControllerGuardTests
{
    private static EndlessController Controller()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "1") }, "test"));
        return new EndlessController(null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } }
        };
    }

    private static List<RecordEndlessSessionDto> Sessions(int n)
        => Enumerable.Range(0, n).Select(_ => new RecordEndlessSessionDto()).ToList();

    [Fact]
    public async Task ArchiveSessions_RejectsEmpty()
    {
        var res = await Controller().ArchiveSessions(new ArchiveSessionsDto { SessionIds = new(), Archive = true });
        Assert.IsType<BadRequestObjectResult>(res.Result);
    }

    [Fact]
    public async Task ArchiveSessions_RejectsMoreThan100()
    {
        var res = await Controller().ArchiveSessions(new ArchiveSessionsDto { SessionIds = Enumerable.Range(0, 101).ToList() });
        Assert.IsType<BadRequestObjectResult>(res.Result);
    }

    [Fact]
    public async Task BulkImportSessions_RejectsMoreThan50()
    {
        var res = await Controller().BulkImportSessions(new BulkImportSessionDto { Sessions = Sessions(51) });
        Assert.IsType<BadRequestObjectResult>(res.Result);
    }

    [Fact]
    public async Task BulkImportAnonymousSessions_RejectsMoreThan50()
    {
        var res = await Controller().BulkImportAnonymousSessions(new BulkImportAnonymousSessionDto { SessionId = new string('a', 36), Sessions = Sessions(51) });
        Assert.IsType<BadRequestObjectResult>(res.Result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-short")]                       // unter der 32-Zeichen-Mindestlänge (IDOR-Härtung)
    public async Task GetAnonymousProgress_RejectsInvalidSessionId(string sessionId)
    {
        var res = await Controller().GetAnonymousProgress(sessionId);
        Assert.IsType<BadRequestObjectResult>(res.Result);
    }

    // --- Anonyme Senke (A2-001): enger Spielstand-Deckel + Gesamtdeckel → 400 ---

    private const string Sid = "00000000-0000-0000-0000-000000000001";

    /// <summary>Spielstand in der Form von <c>syncActiveGameToServer</c> (Endless-Frontend).</summary>
    private static string RealisticActiveGameState(int puzzles) => JsonSerializer.Serialize(new
    {
        lives = 1, solved = puzzles, level = puzzles, chainIndex = puzzles,
        seed = "3f2b8c1e-5d4a-4e6f-9a7b-1c2d3e4f5a6b", currentMinRating = 2400, maxRatingReached = 2615,
        sessionSeconds = 5400, mistakes = new[] { 1450, 2210 },
        puzzleAttempts = Enumerable.Range(0, puzzles).Select(i => new
        {
            puzzleNumber = i + 1, puzzleId = 3_000_000 + i, lichessId = "a1B2c", rating = 1000 + 15 * i,
            solved = i % 7 != 0, startedAt = 1_727_700_000_000L + i * 30_000L, endedAt = 1_727_700_020_000L + i * 30_000L,
        }),
    });

    private static bool DataAnnotationsValid(object dto)
        => Validator.TryValidateObject(dto, new ValidationContext(dto), new List<ValidationResult>(), validateAllProperties: true);

    /// <summary>Validierung auf dem Weg, den [ApiController] nimmt (MVC-Metadaten, nicht TypeDescriptor) —
    /// der Deckel sitzt auf einem überschriebenen Property, und beide Wege müssen ihn sehen.</summary>
    private static bool MvcValid(object dto)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        using var sp = services.BuildServiceProvider();
        var ctx = new ActionContext(new DefaultHttpContext { RequestServices = sp }, new RouteData(), new ActionDescriptor());
        sp.GetRequiredService<IObjectModelValidator>().Validate(ctx, validationState: null, prefix: string.Empty, dto);
        return ctx.ModelState.IsValid;
    }

    [Fact]
    public void AnonymousProgressDto_RejectsMegabyteState_AcceptsRealisticOne()
    {
        var huge = new SaveAnonymousProgressDto { SessionId = Sid, ActiveGameState = new string('x', 1_000_000) };
        Assert.False(DataAnnotationsValid(huge));
        Assert.False(MvcValid(huge));

        var real = RealisticActiveGameState(400);
        Assert.True(real.Length <= SaveAnonymousProgressDto.MaxActiveGameStateLength, $"400 Puzzles = {real.Length} Zeichen");
        var ok = new SaveAnonymousProgressDto { SessionId = Sid, ActiveGameState = real };
        Assert.True(DataAnnotationsValid(ok));
        Assert.True(MvcValid(ok));
    }

    [Fact]
    public void AccountProgressDto_KeepsItsMegabyteLimit()
    {
        // Nur der anonyme Pfad ist enger — der Konto-Pfad bleibt wie er war.
        var dto = new SaveEndlessProgressDto { ActiveGameState = new string('x', 1_000_000) };
        Assert.True(DataAnnotationsValid(dto));
        Assert.True(MvcValid(dto));
    }

    [Fact]
    public void AnonymousProgressDto_OverrideStillFeedsTheBaseProperty()
    {
        // Der Dienst liest den Stand über den BASIS-Typ (ApplyProgressDto(SaveEndlessProgressDto)) —
        // ein verdeckendes „new" statt „override" ließe dort null ankommen.
        var dto = JsonSerializer.Deserialize<SaveAnonymousProgressDto>(
            $"{{\"sessionId\":\"{Sid}\",\"activeGameState\":\"{{}}\"}}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("{}", ((SaveEndlessProgressDto)dto).ActiveGameState);
    }

    [Fact]
    public async Task AnonymousWrites_WhenSinkFull_Return400WithReason()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new EndlessProgressService(db, NullLogger<EndlessProgressService>.Instance)
        { AnonymousProgressRowsCap = 0, AnonymousSessionRowsCap = 0 };
        var controller = new EndlessController(service);

        var progress = await controller.SaveAnonymousProgress(new SaveAnonymousProgressDto { SessionId = Sid });
        var session = await controller.RecordAnonymousSession(new RecordAnonymousSessionDto { SessionId = Sid });
        var bulk = await controller.BulkImportAnonymousSessions(new BulkImportAnonymousSessionDto { SessionId = Sid, Sessions = Sessions(1) });

        foreach (var result in new[] { progress.Result, session.Result, bulk.Result })
        {
            var bad = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("anonymousStorageFull", bad.Value!.GetType().GetProperty("reason")!.GetValue(bad.Value));
        }
        Assert.Equal(0, await db.EndlessProgresses.CountAsync());
        Assert.Equal(0, await db.EndlessSessions.CountAsync());
    }
}
