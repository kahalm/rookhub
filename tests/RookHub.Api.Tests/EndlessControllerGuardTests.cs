using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Controllers;
using RookHub.Api.DTOs;

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
}
