using System.Text;
using System.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Services.Og;

namespace RookHub.Api.Controllers;

/// <summary>
/// Open-Graph-/Twitter-Card-Vorschauen für öffentliche Routen (geteilte Partie <c>/g/…</c>,
/// Puzzles <c>/puzzles/…</c>, Turnier <c>/t/…</c>). nginx leitet diese Pfade an <see cref="Render"/>,
/// das die echte SPA-index.html mit stellungsspezifischen Meta-Tags anreichert (Mensch bekommt die
/// normale SPA, Crawler liest die Tags). Das Brett-Bild liefert <see cref="Image"/> als PNG.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/og")]
public class OgController : ControllerBase
{
    private readonly OgMetaService _meta;
    private readonly OgImageService _images;
    private readonly OgIndexHtmlProvider _index;

    public OgController(OgMetaService meta, OgImageService images, OgIndexHtmlProvider index)
    {
        _meta = meta;
        _images = images;
        _index = index;
    }

    /// <summary>
    /// Liefert die SPA-index.html — mit OG-Meta-Tags angereichert, wenn der Original-Pfad (nginx-Header
    /// <c>X-Original-URI</c>, sonst <c>?path=</c>) eine vorschaubare Route ist. Sonst unverändert, sodass
    /// die SPA normal lädt.
    /// </summary>
    [HttpGet("render")]
    public async Task<IActionResult> Render(CancellationToken ct)
    {
        var path = Request.Headers["X-Original-URI"].ToString();
        if (string.IsNullOrWhiteSpace(path)) path = Request.Query["path"].ToString();

        var html = await _index.GetIndexHtmlAsync(ct);
        if (string.IsNullOrEmpty(html))
            return StatusCode(StatusCodes.Status502BadGateway, "frontend unavailable");

        var page = await _meta.ResolvePageAsync(path, BaseUrl(), ct);
        if (page is not null)
            html = Inject(html, page);

        // Kurz cachen: Crawler dürfen frische Tags bekommen, ohne den API bei jedem Hard-Load zu treffen.
        Response.Headers.CacheControl = "public, max-age=300";
        return Content(html, "text/html; charset=utf-8");
    }

    /// <summary>Rendert das Brett-Bild (PNG 1200×630) für ein (kind, id).</summary>
    [HttpGet("img/{kind}/{id}.png")]
    public async Task<IActionResult> Image(string kind, string id, CancellationToken ct)
    {
        var board = await _meta.ResolveBoardAsync(kind, id, ct);
        if (board is null) return NotFound();

        var png = _images.RenderBoard(board.Fen, board.Flip);
        // Unveränderlich je (kind,id,Stellung) → aggressiv cachen.
        Response.Headers.CacheControl = "public, max-age=604800, immutable";
        return File(png, "image/png");
    }

    private string BaseUrl()
    {
        var proto = Request.Headers["X-Forwarded-Proto"].ToString();
        if (string.IsNullOrWhiteSpace(proto)) proto = Request.Scheme;
        var host = Request.Host.HasValue ? Request.Host.Value : "localhost";
        return $"{proto}://{host}";
    }

    private static string Inject(string html, OgPage page)
    {
        var sb = new StringBuilder();
        sb.Append("<meta property=\"og:type\" content=\"").Append(Esc(page.Type)).Append("\">\n");
        sb.Append("<meta property=\"og:site_name\" content=\"RookHub\">\n");
        sb.Append("<meta property=\"og:title\" content=\"").Append(Esc(page.Title)).Append("\">\n");
        sb.Append("<meta property=\"og:description\" content=\"").Append(Esc(page.Description)).Append("\">\n");
        sb.Append("<meta property=\"og:image\" content=\"").Append(Esc(page.ImageUrl)).Append("\">\n");
        sb.Append("<meta property=\"og:image:width\" content=\"1200\">\n");
        sb.Append("<meta property=\"og:image:height\" content=\"630\">\n");
        sb.Append("<meta property=\"og:url\" content=\"").Append(Esc(page.CanonicalUrl)).Append("\">\n");
        sb.Append("<meta name=\"twitter:card\" content=\"summary_large_image\">\n");
        sb.Append("<meta name=\"twitter:title\" content=\"").Append(Esc(page.Title)).Append("\">\n");
        sb.Append("<meta name=\"twitter:description\" content=\"").Append(Esc(page.Description)).Append("\">\n");
        sb.Append("<meta name=\"twitter:image\" content=\"").Append(Esc(page.ImageUrl)).Append("\">\n");
        sb.Append("<link rel=\"canonical\" href=\"").Append(Esc(page.CanonicalUrl)).Append("\">\n");

        var block = sb.ToString();
        var idx = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return idx < 0 ? html : html.Insert(idx, block);
    }

    private static string Esc(string s) => HttpUtility.HtmlAttributeEncode(s) ?? string.Empty;
}
