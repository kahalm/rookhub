using RookHub.Api.Services;
using SkiaSharp;

namespace RookHub.Api.Tests;

public class ScoresheetOrientationTests
{
    /// <summary>Kästen eines aufrechten Formulars: zwei Spalten à 20 Züge, Zug n in Zeile n, Weiß links von Schwarz.</summary>
    private static List<(int No, string Color, double X, double Y)> Upright(int moves = 30)
    {
        var list = new List<(int, string, double, double)>();
        for (var n = 1; n <= moves; n++)
        {
            var col = (n - 1) / 20;
            var row = (n - 1) % 20;
            list.Add((n, "w", 100 + col * 500, 200 + row * 50));
            list.Add((n, "b", 250 + col * 500, 200 + row * 50));
        }
        return list;
    }

    /// <summary>Die Mittelpunkte als Lesung mit Kästen.</summary>
    private static ScoresheetTranscription T(IEnumerable<(int No, string Color, double X, double Y)> centers) => new()
    {
        Moves = centers.Select(c => new ScoresheetTranscription.Entry
        {
            MoveNumber = c.No, Color = c.Color, Written = "e4",
            Box = new List<int> { (int)c.X - 20, (int)c.Y - 15, (int)c.X + 20, (int)c.Y + 15 },
        }).ToList(),
    };

    [Fact]
    public void Upright_NeedsNoTurn()
        => Assert.Equal(0, ScoresheetOrientation.Detect(T(Upright())));

    /// <summary>Wie LeagueHub-Formular 24 (2026-10-05): der Kopf des Formulars zeigt nach RECHTS, die Zugnummern laufen
    /// nach links, Schwarz steht UNTER Weiß — im Uhrzeigersinn gedreht fotografiert, zurück um 270°.</summary>
    [Fact]
    public void TurnedClockwise_NeedsTwoSeventy()
        => Assert.Equal(270, ScoresheetOrientation.Detect(T(Upright().Select(c => (c.No, c.Color, 2000 - c.Y, c.X)))));

    /// <summary>Kopf nach LINKS (gegen den Uhrzeigersinn gedreht): zurück um 90°.</summary>
    [Fact]
    public void TurnedCounterClockwise_NeedsNinety()
        => Assert.Equal(90, ScoresheetOrientation.Detect(T(Upright().Select(c => (c.No, c.Color, c.Y, 2000 - c.X)))));

    [Fact]
    public void UpsideDown_NeedsOneEighty()
        => Assert.Equal(180, ScoresheetOrientation.Detect(T(Upright().Select(c => (c.No, c.Color, 2000 - c.X, 1500 - c.Y)))));

    [Fact]
    public void TooFewBoxes_OrNone_NeverTurns()
    {
        Assert.Equal(0, ScoresheetOrientation.Detect(T(Upright(3).Select(c => (c.No, c.Color, c.Y, 2000 - c.X)))));
        Assert.Equal(0, ScoresheetOrientation.Detect(new ScoresheetTranscription
            { Moves = Enumerable.Range(1, 20).Select(i => new ScoresheetTranscription.Entry { MoveNumber = i, Written = "e4" }).ToList() }));
    }

    [Fact]
    public void Rotate_TurnsThePixels()
    {
        using var bmp = new SKBitmap(40, 30);
        using (var c = new SKCanvas(bmp)) { c.Clear(SKColors.White); c.DrawRect(0, 0, 10, 10, new SKPaint { Color = SKColors.Black }); }
        using var img = SKImage.FromBitmap(bmp);
        var jpeg = img.Encode(SKEncodedImageFormat.Jpeg, 95).ToArray();

        var turned = ScoresheetImage.Rotate(jpeg, 90)!;
        Assert.Equal((30, 40), ScoresheetImage.Size(turned));
        using var back = SKBitmap.Decode(turned);
        Assert.True(back.GetPixel(25, 5).Red < 80);                // die schwarze Ecke oben links steht jetzt oben rechts
        Assert.True(back.GetPixel(5, 5).Red > 180);
        Assert.Null(ScoresheetImage.Rotate(jpeg, 45));
    }
}
