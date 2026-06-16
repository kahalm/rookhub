using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RookHub.Api.Exceptions;

namespace RookHub.Api.Filters;

public class CrawlerExceptionFilter : IAsyncExceptionFilter
{
    private readonly ILogger<CrawlerExceptionFilter> _logger;

    public CrawlerExceptionFilter(ILogger<CrawlerExceptionFilter> logger)
    {
        _logger = logger;
    }

    public Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.Exception is CrawlerRequestException crawlerEx)
        {
            var statusCode = (int)crawlerEx.StatusCode;
            _logger.LogWarning("Crawler returned {StatusCode}: {Body}", statusCode, crawlerEx.ResponseBody);

            // 4xx sowie die Gateway-Status des Crawlers (502 Upstream weg, 503 Crawler ueberlastet,
            // 504 chess-results.com-Timeout — vom UpstreamErrorMiddleware des Crawlers gemappt) werden
            // 1:1 inkl. Body durchgereicht, damit der Aufrufer die echte Fehlerursache sieht (statt alles
            // pauschal als 502 zu maskieren). Nur uneindeutige 5xx (500/501/…) werden auf 502 normalisiert.
            var passThrough = (statusCode >= 400 && statusCode < 500)
                || statusCode is 502 or 503 or 504;
            if (passThrough)
            {
                object body;
                try
                {
                    body = JsonSerializer.Deserialize<JsonElement>(crawlerEx.ResponseBody ?? "{}");
                }
                catch
                {
                    body = new { message = crawlerEx.ResponseBody ?? "Crawler request failed." };
                }
                context.Result = new ObjectResult(body) { StatusCode = statusCode };
            }
            else
            {
                // Sonstige 5xx → 502
                context.Result = new ObjectResult(new { message = "Crawler service error." }) { StatusCode = 502 };
            }
            context.ExceptionHandled = true;
        }
        else if (context.Exception is HttpRequestException)
        {
            _logger.LogWarning(context.Exception, "Crawler connectivity error");
            context.Result = new ObjectResult(new { message = "Crawler service unavailable." }) { StatusCode = 502 };
            context.ExceptionHandled = true;
        }
        else if (context.Exception is TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning("Crawler request timed out");
            context.Result = new ObjectResult(new { message = "Crawler request timed out." }) { StatusCode = 504 };
            context.ExceptionHandled = true;
        }

        return Task.CompletedTask;
    }
}
