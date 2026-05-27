using System.Net;

namespace RookHub.Api.Exceptions;

public class CrawlerRequestException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? ResponseBody { get; }

    public CrawlerRequestException(HttpStatusCode statusCode, string? responseBody)
        : base($"Crawler returned {(int)statusCode}: {responseBody}")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
