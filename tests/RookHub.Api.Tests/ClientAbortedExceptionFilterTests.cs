using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using RookHub.Api.Filters;

namespace RookHub.Api.Tests;

/// <summary>Browser-Abbruch (<c>RequestAborted</c>) → 499 ohne Error-Log statt 500 (Prod 09.10.2026,
/// <c>/api/explorer/paths</c> mit <c>TaskCanceledException</c> aus dem lokalen Explorer).</summary>
public class ClientAbortedExceptionFilterTests
{
    private static (ExceptionContext Ctx, CapturingLogger<ClientAbortedExceptionFilter> Log) Run(Exception ex, bool aborted, bool handled = false)
    {
        using var cts = new CancellationTokenSource();
        if (aborted) cts.Cancel();
        var http = new DefaultHttpContext { RequestAborted = cts.Token };
        http.Request.Method = "GET";
        http.Request.Path = "/api/explorer/paths";
        var ctx = new ExceptionContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>())
        {
            Exception = ex,
            ExceptionHandled = handled,
        };
        var log = new CapturingLogger<ClientAbortedExceptionFilter>();
        new ClientAbortedExceptionFilter(log).OnException(ctx);
        return (ctx, log);
    }

    [Theory]
    [InlineData(typeof(TaskCanceledException))]
    [InlineData(typeof(OperationCanceledException))]
    public void ClientAbort_Is499_WithoutWarningOrError(Type exceptionType)
    {
        var (ctx, log) = Run((Exception)Activator.CreateInstance(exceptionType)!, aborted: true);
        Assert.True(ctx.ExceptionHandled);
        Assert.Equal(ClientAbortedExceptionFilter.StatusClientClosedRequest, Assert.IsType<StatusCodeResult>(ctx.Result).StatusCode);
        Assert.All(log.Events, e => Assert.Equal(LogLevel.Debug, e.Level));
    }

    [Fact]
    public void CancelWithoutClientAbort_StaysAnError()
    {
        // Eigene Frist / HttpClient-Timeout: kein Browser-Abbruch → weiter in den globalen Handler (500 + Error-Log).
        var (ctx, log) = Run(new TaskCanceledException("timeout"), aborted: false);
        Assert.False(ctx.ExceptionHandled);
        Assert.Null(ctx.Result);
        Assert.Empty(log.Events);
    }

    [Fact]
    public void OtherException_DuringClientAbort_StaysAnError()
    {
        var (ctx, _) = Run(new InvalidOperationException("boom"), aborted: true);
        Assert.False(ctx.ExceptionHandled);
        Assert.Null(ctx.Result);
    }

    [Fact]
    public void AlreadyHandled_KeepsTheEarlierResult()
    {
        var (ctx, _) = Run(new OperationCanceledException(), aborted: true, handled: true);
        Assert.Null(ctx.Result);
    }

    [Fact]
    public void Filter_IsRegisteredGlobally()
    {
        var program = File.ReadAllText(ProgramCs());
        Assert.Contains("o.Filters.Add<RookHub.Api.Filters.ClientAbortedExceptionFilter>()", program);
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
