using RookHub.Api.Controllers;
using RookHub.Api.DTOs;
using RookHub.Api.Services.Og;

namespace RookHub.Api.Tests;

/// <summary>Tests für die Link-Vorschau (Open Graph): Pfad-Parsing + Brett-Bild-Rendering.</summary>
public class OgTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    [Theory]
    [InlineData("/g/abc123", "game", "abc123")]
    [InlineData("/t/12345", "tournament", "12345")]
    [InlineData("/puzzles/987", "puzzle", "987")]
    [InlineData("/puzzles/book/42", "book", "42")]
    [InlineData("/puzzles/daily/20260707", "daily", "20260707")]
    [InlineData("/puzzles/daily/today", "daily", "today")]
    [InlineData("/g/tok?utm=x", "game", "tok")]
    public void ParsePath_KnownRoutes_ReturnsKindAndId(string path, string kind, string id)
    {
        var result = OgMetaService.ParsePath(path);
        Assert.NotNull(result);
        Assert.Equal(kind, result!.Value.Kind);
        Assert.Equal(id, result.Value.Id);
    }

    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/puzzles")]          // Modus-Auswahl, keine konkrete Stellung
    [InlineData("/puzzles/endless")]  // keine feste Stellung
    [InlineData("/friends/5/stats")]
    [InlineData("")]
    [InlineData(null)]
    public void ParsePath_NonPreviewRoutes_ReturnsNull(string? path)
    {
        Assert.Null(OgMetaService.ParsePath(path));
    }

    [Fact]
    public void FenCharToFile_MapsColorAndRole()
    {
        Assert.Equal("wN", OgImageService.FenCharToFile('N'));
        Assert.Equal("bQ", OgImageService.FenCharToFile('q'));
        Assert.Equal("wP", OgImageService.FenCharToFile('P'));
        Assert.Null(OgImageService.FenCharToFile('1'));
    }

    [Fact]
    public void RenderBoard_ProducesValidPng()
    {
        var svc = new OgImageService(new TestLogger<OgImageService>());
        var png = svc.RenderBoard(StartFen);

        Assert.NotNull(png);
        Assert.True(png.Length > PngSignature.Length);
        Assert.Equal(PngSignature, png[..PngSignature.Length]);
    }

    [Fact]
    public void RenderBoard_DifferentPositionsDiffer_AndFlipChangesOutput()
    {
        var svc = new OgImageService(new TestLogger<OgImageService>());
        var empty = svc.RenderBoard("8/8/8/8/8/8/8/8 w - - 0 1");
        var start = svc.RenderBoard(StartFen);
        var startFlipped = svc.RenderBoard(StartFen, flip: true);

        Assert.NotEqual(empty, start);          // Figuren werden tatsächlich gezeichnet
        Assert.NotEqual(start, startFlipped);   // Perspektive wirkt sich aus
    }

    [Fact]
    public void RenderBoard_IsCached_ReturnsSameInstance()
    {
        var svc = new OgImageService(new TestLogger<OgImageService>());
        var a = svc.RenderBoard(StartFen);
        var b = svc.RenderBoard(StartFen);
        Assert.Same(a, b);
    }

    // ── Bewertungskurve im Vorschaubild (0.541.0) ────────────────────────────────────────────────

    private static GameEvalsDto Evals(string status = "done") => new()
    {
        Status = status, Total = 4, AnalysisId = 17, Refined = 3,
        Plies =
        {
            new GameEvalPlyDto { Ply = 0, Cp = 30 },
            new GameEvalPlyDto { Ply = 1, Cp = -250 },
            // Halbzug 2 nicht gerechnet → Lücke
            new GameEvalPlyDto { Ply = 3, Cp = 1500 },
        },
        Final = new GameEvalScoreDto { Mate = -2 },
    };

    [Fact]
    public void CurveOf_DoneAnalysis_LinearToTenPawns_MateAtTheEdge_GapsStayGaps()
    {
        Assert.Equal(new double?[] { 51.5, 37.5, null, 100, 0 }, OgMetaService.CurveOf(Evals()));
        Assert.Equal("17-3", OgMetaService.CurveVersion(Evals()));
    }

    [Theory]
    [InlineData("running")]
    [InlineData("pending")]
    [InlineData("none")]
    public void CurveOf_NoFinishedAnalysis_NoCurve_NoNewImageAddress(string status)
    {
        // Eine halbe Kurve sähe im geteilten Bild wie das Ende der Partie aus.
        Assert.Null(OgMetaService.CurveOf(Evals(status)));
        Assert.Null(OgMetaService.CurveVersion(Evals(status)));
    }

    [Fact]
    public void RenderBoard_WithCurve_IsAValidPng_DiffersFromTheBoardAlone_AndIsCachedPerCurve()
    {
        var svc = new OgImageService(new TestLogger<OgImageService>());
        var curve = OgMetaService.CurveOf(Evals());
        var plain = svc.RenderBoard(StartFen);
        var withCurve = svc.RenderBoard(StartFen, curve: curve);
        var otherCurve = svc.RenderBoard(StartFen, curve: new double?[] { 50, 90, 10 });

        Assert.Equal(PngSignature, withCurve[..PngSignature.Length]);
        Assert.NotEqual(plain, withCurve);
        Assert.NotEqual(withCurve, otherCurve);
        Assert.Same(withCurve, svc.RenderBoard(StartFen, curve: OgMetaService.CurveOf(Evals())));
    }

    [Fact]
    public void SmoothPath_GoesThroughEveryPoint_AndNeverOvershootsAnExtreme()
    {
        var points = new[] { new SkiaSharp.SKPoint(0, 50), new SkiaSharp.SKPoint(10, 10), new SkiaSharp.SKPoint(20, 10), new SkiaSharp.SKPoint(30, 90) };
        using var path = OgImageService.SmoothPath(points);

        Assert.Equal(points[^1], path.LastPoint);
        // Monoton: die Kurve bleibt im Band der Punkte (10..90), rundet aber zwischen ihnen.
        Assert.InRange(path.TightBounds.Top, 9.99f, 10.01f);
        Assert.InRange(path.TightBounds.Bottom, 89.99f, 90.01f);
    }

    // ── Zwei Seiten, zwei Shells (RookHub + turnier.oberschmid.homes) ──────────────────────────

    [Fact]
    public void ConfiguredBaseUrl_TurnierSite_PrefersTurnierBaseUrl()
    {
        // Ein /t/{id}-Link lebt auf der Turnierseite — og:url darf nicht auf RookHub zeigen,
        // wo es die Route seit der Trennung nicht mehr gibt.
        var url = OgController.ConfiguredBaseUrl("turnier", "https://rookhub.example", "https://turnier.example");
        Assert.Equal("https://turnier.example", url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rookhub")]
    public void ConfiguredBaseUrl_OtherSites_UseAppBaseUrl(string? site)
    {
        var url = OgController.ConfiguredBaseUrl(site, "https://rookhub.example", "https://turnier.example");
        Assert.Equal("https://rookhub.example", url);
    }

    [Fact]
    public void ConfiguredBaseUrl_TurnierWithoutOwnConfig_FallsBackToAppBaseUrl()
    {
        var url = OgController.ConfiguredBaseUrl("turnier", "https://rookhub.example", null);
        Assert.Equal("https://rookhub.example", url);
    }

    [Fact]
    public void BuildCandidates_ConfiguredUrlComesFirst_AndIsDeduped()
    {
        var candidates = OgIndexHtmlProvider.BuildCandidates("http://turnier:8080/", "http://turnier:8080", "http://rookhub-turnier:8080");
        Assert.Equal(new[] { "http://turnier:8080/index.html", "http://rookhub-turnier:8080/index.html" }, candidates);
    }

    [Fact]
    public void BuildCandidates_WithoutConfiguration_KeepsDefaults()
    {
        var candidates = OgIndexHtmlProvider.BuildCandidates(null, "http://frontend:8080", "http://rookhub-frontend:8080");
        Assert.Equal(new[] { "http://frontend:8080/index.html", "http://rookhub-frontend:8080/index.html" }, candidates);
    }
}
