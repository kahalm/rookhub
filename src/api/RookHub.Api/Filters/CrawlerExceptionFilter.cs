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

            if (statusCode >= 400 && statusCode < 500)
            {
                // Forward 4xx as-is
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
                // 5xx → 502
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

        return Task.CompletedTask;
    }
}
