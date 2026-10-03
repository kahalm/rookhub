using RookHub.Api.Services.Club;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Kreis ums Gesicht (<see cref="ClubFace"/>). Die Werte für <c>Normalize</c> sind LITERAL und stehen genauso im
/// Browser-Test <c>src-clubhub/app/core/face.spec.ts</c> (<c>clampFace</c>) — die beiden Seiten rechnen dieselbe Regel,
/// und keiner der Tests importiert die Gegenseite. <c>Square</c> gibt es nur am Server (dort wird geschnitten).
/// </summary>
public class ClubFaceTests
{
    [Theory]
    // x, y, r, Breite, Höhe → x, y, r
    [InlineData(0.5, 0.4, 0.28, 900, 1200, 0.5, 0.4, 0.28)]          // passt: bleibt
    [InlineData(0.05, 0.02, 0.3, 900, 1200, 0.3, 0.225, 0.3)]        // über den Rand links oben: hineingeschoben
    [InlineData(0.99, 0.99, 0.9, 1200, 800, 0.6667, 0.5, 0.5)]       // zu groß: größter Kreis, der passt; rechts unten hinein
    [InlineData(0.5, 0.5, 0.001, 1000, 1000, 0.5, 0.5, 0.05)]        // zu klein: Mindestgröße
    [InlineData(0, 0, 0.5, 1200, 800, 0.3333, 0.5, 0.5)]
    public void Normalize_HoltDenKreisInsBild(double x, double y, double r, int w, int h, double ex, double ey, double er)
    {
        var f = ClubFace.Normalize(x, y, r, w, h);
        Assert.Equal((ex, ey, er), f!.Value);
    }

    [Fact]
    public void Normalize_OhneZahlenOderOhneBild_GibtEsKeinenKreis()
    {
        Assert.Null(ClubFace.Normalize(double.NaN, 0.5, 0.2, 900, 1200));
        Assert.Null(ClubFace.Normalize(0.5, double.PositiveInfinity, 0.2, 900, 1200));
        Assert.Null(ClubFace.Normalize(0.5, 0.5, double.NaN, 900, 1200));
        Assert.Null(ClubFace.Normalize(0.5, 0.5, 0.2, 0, 1200));
    }

    [Theory]
    // x, y, r, Breite, Höhe → links, oben, Kante (Pixel)
    [InlineData(0.5, 0.4, 0.28, 900, 1200, 198, 228, 504)]
    [InlineData(0.3, 0.225, 0.3, 900, 1200, 0, 0, 540)]
    [InlineData(0.6667, 0.5, 0.5, 1200, 800, 400, 0, 800)]
    [InlineData(0.5, 0.5, 0.05, 1000, 1000, 450, 450, 100)]
    public void Square_IstDasQuadratUmDenKreis_GanzImBild(double x, double y, double r, int w, int h, int left, int top, int size)
    {
        Assert.Equal((left, top, size), ClubFace.Square(x, y, r, w, h));
    }
}
