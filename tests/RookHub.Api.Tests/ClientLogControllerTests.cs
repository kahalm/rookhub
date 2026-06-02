using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Controllers;
using RookHub.Api.DTOs;
using Xunit;

namespace RookHub.Api.Tests;

public class ClientLogControllerTests
{
    private static ClientLogController CreateController(TestLogger<ClientLogController> logger)
    {
        var ctrl = new ClientLogController(logger)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        ctrl.ControllerContext.HttpContext.Request.Headers.UserAgent = "TestAgent/1.0";
        return ctrl;
    }

    [Fact]
    public void Post_LogsStructuredEvent_AndReturnsNoContent()
    {
        var logger = new TestLogger<ClientLogController>();
        var ctrl = CreateController(logger);

        var result = ctrl.Post(new ClientLogDto { Kind = "engine_analysis_crash", Detail = "boom", Url = "/puzzles" });

        Assert.IsType<NoContentResult>(result);
        Assert.Contains(logger.Messages, m => m.Contains("ClientLog") && m.Contains("engine_analysis_crash") && m.Contains("boom"));
    }

    [Fact]
    public void Post_MissingKind_ReturnsBadRequest_AndLogsNothing()
    {
        var logger = new TestLogger<ClientLogController>();
        var ctrl = CreateController(logger);

        var result = ctrl.Post(new ClientLogDto { Kind = "  " });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(logger.Messages);
    }

    [Fact]
    public void Post_TruncatesOverlongDetail()
    {
        var logger = new TestLogger<ClientLogController>();
        var ctrl = CreateController(logger);
        var longDetail = new string('x', 1000);

        var result = ctrl.Post(new ClientLogDto { Kind = "k", Detail = longDetail });

        Assert.IsType<NoContentResult>(result);
        Assert.DoesNotContain(logger.Messages, m => m.Contains(longDetail));   // 500er-Cap greift
    }
}
