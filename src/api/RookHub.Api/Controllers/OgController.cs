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
    private readonly IConfiguration _config;

    public OgController(OgMetaService meta, OgImageService images, OgIndexHtmlProvider index, IConfiguration config)
    {
        _meta = meta;
        _images = images;
        _index = index;
        _config = config;
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

        // Welche der beiden Seiten fragt? Der nginx setzt X-Og-Site anhand des Hosts. Davon haengt
        // BEIDES ab: welche SPA-Shell ausgeliefert wird und auf welche Domain og:url zeigt.
        var site = Request.Headers["X-Og-Site"].ToString();

        var html = await _index.GetIndexHtmlAsync(site, ct);
        if (string.IsNullOrEmpty(html))
            return StatusCode(StatusCodes.Status502BadGateway, "frontend unavailable");

        var page = await _meta.ResolvePageAsync(path, BaseUrl(site), ct);
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

    private string BaseUrl(string? site = null) => ResolveBaseUrl(ConfiguredBaseUrl(site), Request);

    /// <summary>
    /// Konfigurierte Basis-URL der anfragenden Seite: die Turnierseite laeuft unter eigener Domain,
    /// ein <c>/t/{id}</c>-Link dort darf nicht auf RookHub zeigen (dort gibt es die Route nicht mehr).
    /// Ohne eigene Konfiguration faellt die Turnierseite auf <c>App:BaseUrl</c> zurueck.
    /// </summary>
    private string? ConfiguredBaseUrl(string? site)
        => ConfiguredBaseUrl(site, _config["App:BaseUrl"], _config["App:TurnierBaseUrl"]);

    internal static string? ConfiguredBaseUrl(string? site, string? appBaseUrl, string? turnierBaseUrl)
    {
        if (string.Equals(site, OgIndexHtmlProvider.TurnierSite, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(turnierBaseUrl))
            return turnierBaseUrl;
        return appBaseUrl;
    }

    /// <summary>
    /// Basis-URL für og:url/og:image: bevorzugt die konfigurierte <c>App:BaseUrl</c> (dieselbe Quelle
    /// wie die Passwort-Reset-Links) — die Antwort wird mit <c>Cache-Control: public</c> gecacht, ein
    /// aus dem Host-Header gebauter Wert wäre hinter einem CDN eine Cache-Poisoning-Fläche (Angreifer
    /// schickt einen fremden Host-Header, der vergiftete og:image-Link landet im Cache für alle).
    /// Fallback ohne Konfiguration bleibt wie bisher der Request (Dev/Docker ohne .env).
    /// </summary>
    internal static string ResolveBaseUrl(string? configuredBaseUrl, HttpRequest request)
    {
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
            return configuredBaseUrl.TrimEnd('/');

        var proto = request.Headers["X-Forwarded-Proto"].ToString();
        if (string.IsNullOrWhiteSpace(proto)) proto = request.Scheme;
        var host = request.Host.HasValue ? request.Host.Value : "localhost";
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
