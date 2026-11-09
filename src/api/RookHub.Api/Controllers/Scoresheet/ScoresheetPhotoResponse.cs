using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace RookHub.Api.Controllers.Scoresheet;

/// <summary>
/// Formular-Fotos ausliefern (0.919.1): <c>private, no-cache</c> + ETag aus dem Inhalt. Vorher galt <c>max-age=3600</c>
/// unter derselben Adresse — wurde das Foto danach aufrecht gedreht (Lesung, Formular 110 am 09.11.2026), zeigte der
/// Browser bis zu einer Stunde das alte, quer liegende. So fragt er jedes Mal nach und bekommt 304, solange es gleich ist.
/// </summary>
public static class ScoresheetPhotoResponse
{
    public static FileContentResult File(ControllerBase c, byte[] data, string contentType, string? downloadName = null)
    {
        c.Response.Headers.CacheControl = "private, no-cache";
        var tag = new EntityTagHeaderValue("\"" + Convert.ToHexString(SHA256.HashData(data), 0, 16) + "\"");
        var result = new FileContentResult(data, contentType) { EntityTag = tag };
        if (downloadName != null) result.FileDownloadName = downloadName;
        return result;
    }
}
