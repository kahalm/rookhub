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
}
