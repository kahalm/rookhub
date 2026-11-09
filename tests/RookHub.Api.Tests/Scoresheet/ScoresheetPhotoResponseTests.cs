using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Controllers.Scoresheet;

namespace RookHub.Api.Tests.Scoresheet;

/// <summary>0.919.1 (Formular 110, 09.11.2026): ein gedrehtes Foto unter derselben Adresse darf nicht eine Stunde aus dem
/// Browser-Speicher kommen — <c>no-cache</c> + ETag aus dem Inhalt.</summary>
public class ScoresheetPhotoResponseTests
{
    private sealed class Dummy : ControllerBase { }

    private static (FileContentResult Result, HttpResponse Response) Serve(byte[] data, string? name = null)
    {
        var c = new Dummy { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        return (ScoresheetPhotoResponse.File(c, data, "image/jpeg", name), c.Response);
    }

    [Fact]
    public void File_RevalidatesEveryTime_AndTheETagFollowsTheContent()
    {
        var (a, response) = Serve(new byte[] { 1, 2, 3 });
        var (same, _) = Serve(new byte[] { 1, 2, 3 });
        var (turned, _) = Serve(new byte[] { 3, 2, 1 }, "formular.jpg");

        Assert.Equal("private, no-cache", response.Headers.CacheControl.ToString());
        Assert.Equal(a.EntityTag!.Tag, same.EntityTag!.Tag);
        Assert.NotEqual(a.EntityTag.Tag, turned.EntityTag!.Tag);
        Assert.Equal("", a.FileDownloadName);
        Assert.Equal("formular.jpg", turned.FileDownloadName);
    }
}
