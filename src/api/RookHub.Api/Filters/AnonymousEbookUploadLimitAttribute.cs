using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using RookHub.Api.Exceptions;
using RookHub.Api.Services;
using RookHub.Api.Services.Ebooks;

namespace RookHub.Api.Filters;

/// <summary>
/// Rumpfgrenze für ANONYME E-Book-Uploads, bevor der Rumpf gelesen wird (F3-009). <c>[RequestSizeLimit]</c> am Upload gilt
/// für alle (1,1 GB, Angemeldete dürfen 1 GiB hochladen); ohne Konto sind aber nur <see cref="EbookOptions.AnonymousQuotaBytes"/>
/// (300 MB) erlaubt — geprüft wurde das bisher erst in <see cref="EbookService.UploadAsync"/>, nachdem das Model-Binding den
/// ganzen Multipart-Rumpf in eine Temp-Datei des Containers geschrieben hatte. Dieser Ressourcen-Filter läuft NACH
/// <c>[RequestSizeLimit]</c> (Order 900) und VOR dem Model-Binding:
/// <list type="bullet">
/// <item>angemeldet → nichts (wie bisher),</item>
/// <item>anonym ohne gültige <c>X-Visitor-Id</c> oder bei abgeschaltetem anonymem Upload → dieselbe 403 wie der Dienst
/// (<see cref="ApiErrorCodes.EbookAnonymousUploadDisabled"/>), ohne den Rumpf zu lesen,</item>
/// <item>anonym mit <c>Content-Length</c> über der Grenze → 413 (<see cref="ApiErrorCodes.EbookFileTooLarge"/>), ohne zu lesen,</item>
/// <item>sonst wird <see cref="IHttpMaxRequestBodySizeFeature"/> auf die Grenze gesenkt — auch ein Rumpf ohne
/// Längenangabe (chunked) bricht dort ab.</item>
/// </list>
/// nginx bleibt bei einem Limit für alle (<c>location ^~ /api/ebooks</c>): es kann nicht nach Anmeldung unterscheiden, und
/// mit <c>proxy_request_buffering off</c> puffert es nichts — die 413 der API beendet die Anfrage.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AnonymousEbookUploadLimitAttribute : Attribute, IAsyncResourceFilter, IOrderedFilter
{
    /// <summary>Platz für den Multipart-Rahmen und die Metadaten-Felder neben der Datei.</summary>
    public const long MultipartSlackBytes = 1024 * 1024;

    /// <summary>Nach <c>RequestSizeLimitAttribute</c> (900), damit dessen 1,1 GB hier überschrieben werden.</summary>
    public int Order => 1000;

    /// <summary>Grenze des ganzen Rumpfs für einen anonymen Upload; <c>null</c> = anonymer Upload nicht erlaubt.</summary>
    internal static long? AnonymousRequestLimit(EbookOptions options, string? visitorHeader)
    {
        var quota = options.AnonymousUploads ? Math.Max(0, options.AnonymousQuotaBytes) : 0;
        if (quota <= 0 || EbookCaller.Anonymous(visitorHeader).AnonId == null) return null;
        return Math.Min(options.MaxFileBytes, quota) + MultipartSlackBytes;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var options = http.RequestServices.GetService<IOptions<EbookOptions>>()?.Value;
        // Ohne Upload-Wurzel antwortet der Dienst wie bisher („nicht eingerichtet“).
        if (http.User?.Identity?.IsAuthenticated != true && !string.IsNullOrWhiteSpace(options?.Root))
        {
            var limit = AnonymousRequestLimit(options, http.Request.Headers["X-Visitor-Id"].FirstOrDefault());
            if (limit == null)
            {
                context.Result = new ObjectResult(ApiErrorResponses.Body(EbookService.AnonymousUploadDisabledMessage, ApiErrorCodes.EbookAnonymousUploadDisabled))
                    { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }
            if (http.Request.ContentLength > limit)
            {
                var maxFile = limit.Value - MultipartSlackBytes;
                context.Result = new ObjectResult(ApiErrorResponses.Body($"The file is larger than {maxFile / (1024 * 1024)} MB.", ApiErrorCodes.EbookFileTooLarge))
                    { StatusCode = StatusCodes.Status413PayloadTooLarge };
                return;
            }
            var feature = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = limit;
        }
        await next();
    }
}
