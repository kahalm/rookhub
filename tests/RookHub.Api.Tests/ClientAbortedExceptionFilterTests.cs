using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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

    // ── Abgebrochener/kaputter Rumpf: Kestrels BadHttpRequestException (Dev 28.10.2026, /api/puzzles/random-batch) ──

    [Fact]
    public void BadRequestBody_ClientAborted_Is499_Debug()
    {
        var (ctx, log) = Run(new BadHttpRequestException("Unexpected end of request content.", 400), aborted: true);
        Assert.True(ctx.ExceptionHandled);
        Assert.Equal(499, Assert.IsType<StatusCodeResult>(ctx.Result).StatusCode);
        Assert.All(log.Events, e => Assert.Equal(LogLevel.Debug, e.Level));
    }

    [Theory]
    [InlineData(400, "Malformed request.")]
    [InlineData(413, "Request body too large.")]
    public void BadRequestBody_ClientStillThere_KeepsKestrelStatus_WithMessage(int status, string message)
    {
        var (ctx, log) = Run(new BadHttpRequestException("kaputt", status), aborted: false);
        Assert.True(ctx.ExceptionHandled);
        var result = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(JsonSerializer.Serialize(new { message }), JsonSerializer.Serialize(result.Value));
        Assert.All(log.Events, e => Assert.Equal(LogLevel.Debug, e.Level));
    }

    [Fact]
    public void BadRequestBody_AsInnerException_IsHandledToo()
    {
        var wrapped = new InvalidOperationException("outer",
            new IOException("io", new BadHttpRequestException("Unexpected end of request content.", 400)));
        var (ctx, _) = Run(wrapped, aborted: true);
        Assert.True(ctx.ExceptionHandled);
        Assert.Equal(499, Assert.IsType<StatusCodeResult>(ctx.Result).StatusCode);
    }

    /// <summary>
    /// Über die echte MVC-Pipeline: der Rumpf bricht beim Model-Binding ab (STJ-Formatter → BodyModelBinder wirft die
    /// BadHttpRequestException weiter). Die Sonde-Middleware steht da, wo <c>UseSerilogRequestLogging</c> steht: läuft
    /// eine Ausnahme bis zu ihr durch, schreibt Serilog „responded 500" auf Error — genau das passierte ohne den Fix.
    /// </summary>
    [Theory]
    [InlineData(true, 499)]
    [InlineData(false, 400)]
    public async Task Pipeline_AbortedBodyDuringModelBinding_IsNot500(bool aborted, int expected)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers(o => o.Filters.Add<ClientAbortedExceptionFilter>())
            .ConfigureApplicationPartManager(apm =>
            {
                apm.ApplicationParts.Clear();
                apm.ApplicationParts.Add(new AssemblyPart(typeof(ClientAbortProbeController).Assembly));
            });
        await using var app = builder.Build();
        app.UseExceptionHandler(e => e.Run(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Task.CompletedTask;
        }));
        Exception? escaped = null;
        app.Use(async (ctx, next) =>
        {
            try { await next(); }
            catch (Exception ex) { escaped = ex; throw; }
        });
        using var cts = new CancellationTokenSource();
        app.Use((ctx, next) =>
        {
            ctx.Request.Body = new BreakingBodyStream();
            if (aborted) { cts.Cancel(); ctx.RequestAborted = cts.Token; }
            return next();
        });
        app.MapControllers();
        await app.StartAsync();

        var response = await app.GetTestClient().PostAsync("/probe-abort/batch",
            new StringContent("{\"count\":5}", Encoding.UTF8, "application/json"));

        Assert.Null(escaped);
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
    }

    [Fact]
    public void Filter_IsRegisteredGlobally()
    {
        var program = StartupSource.All();
        Assert.Contains("o.Filters.Add<RookHub.Api.Filters.ClientAbortedExceptionFilter>()", program);
    }
}

/// <summary>Sonde für <see cref="ClientAbortedExceptionFilterTests.Pipeline_AbortedBodyDuringModelBinding_IsNot500"/>.</summary>
[ApiController]
[Route("probe-abort")]
public class ClientAbortProbeController : ControllerBase
{
    public sealed record BatchRequest(int Count);

    [HttpPost("batch")] public IActionResult Batch([FromBody] BatchRequest request) => Ok(request.Count);
}

/// <summary>Rumpf, der wie Kestrel bei vorzeitig geschlossenem Upload abbricht.</summary>
internal sealed class BreakingBodyStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => 0; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw Abort();
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw Abort();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw Abort();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    private static BadHttpRequestException Abort() => new("Unexpected end of request content.", StatusCodes.Status400BadRequest);
}
