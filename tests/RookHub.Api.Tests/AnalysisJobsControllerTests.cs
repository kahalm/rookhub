using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Verdrahtung des Auftrags-Controllers: die Admin-Rolle des Tokens muss beim Service ankommen — an ihr hängt,
/// ob Tiefe/Linien/Engine eines Auftrags auf der Haus-Engine geändert werden dürfen (Codereview 2026-09-29, A4-001).
/// </summary>
public class AnalysisJobsControllerTests : IDisposable
{
    private const string START = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private readonly AppDbContext _db;
    private readonly AnalysisJobsController _controller;

    public AnalysisJobsControllerTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!",
        }).Build();
        _controller = new AnalysisJobsController(new AnalysisJobService(_db, new EncryptionService(config)), new AnalysisJobLive());
    }

    public void Dispose() => _db.Dispose();

    private void SetUser(int userId, bool admin)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
        };
    }

    private async Task<AnalysisJob> HouseJobAsync(int userId)
    {
        var job = new AnalysisJob
        {
            UserId = userId, EngineOwnerUserId = 77, Fen = START, EngineId = "eei_haus",
            TargetDepth = 20, MultiPv = 5, Status = AnalysisJobStatus.Queued,
        };
        _db.AnalysisJobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    [Fact]
    public async Task Update_HausAuftrag_TiefeHoch_NichtAdmin400_Admin200()
    {
        var job = await HouseJobAsync(5);

        SetUser(5, admin: false);
        var denied = await _controller.Update(job.Id, new UpdateAnalysisJobRequest { TargetDepth = 60 }, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(denied.Result);
        Assert.Equal(20, (await _db.AnalysisJobs.AsNoTracking().SingleAsync()).TargetDepth);

        SetUser(5, admin: true);
        var allowed = await _controller.Update(job.Id, new UpdateAnalysisJobRequest { TargetDepth = 30 }, CancellationToken.None);
        var dto = Assert.IsType<AnalysisJobDto>(Assert.IsType<OkObjectResult>(allowed.Result).Value);
        Assert.Equal(30, dto.TargetDepth);
    }
}
