using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>G2-003: die Bundsteg-Suche darf bei absurder Seitenbreite aus dem PDF nicht hängen, und die Umwandlung
/// reagiert je Seite auf das Abbruch-Token.</summary>
public class EbookReflowGutterTests
{
    /// <summary>Zwei Spalten: je Zeile ein Block links (10–40 % der Breite) und einer rechts (60–90 %).</summary>
    private static List<List<ReflowGlyph>> TwoColumns(double width, int rows = 12)
    {
        var lines = new List<List<ReflowGlyph>>();
        for (var r = 0; r < rows; r++)
            lines.Add(
            [
                new ReflowGlyph(width * 0.10, width * 0.40, 20 + r * 12, 10, "links", "Garamond", false, false),
                new ReflowGlyph(width * 0.60, width * 0.90, 20 + r * 12, 10, "rechts", "Garamond", false, false),
            ]);
        return lines;
    }

    [Fact]
    public void FindGutter_NormalPage_StillFindsColumnGap()
    {
        var gutter = EbookReflowLayout.FindGutter(TwoColumns(400), 400);
        Assert.NotNull(gutter);
        Assert.InRange(gutter!.Value, 160, 240);
    }

    [Theory]
    [InlineData(3e16)]   // vorher Endlosschleife (x += 1 ändert x nicht mehr)
    [InlineData(1e9)]    // vorher 3e8 Schritte × Zeilen
    [InlineData(double.PositiveInfinity)]
    public void FindGutter_HugePageWidth_ReturnsQuicklyWithoutGutter(double width)
    {
        var lines = TwoColumns(double.IsFinite(width) ? width : 1e300);
        var task = Task.Run(() => EbookReflowLayout.FindGutter(lines, width));
        Assert.True(task.Wait(TimeSpan.FromSeconds(10)), "FindGutter hängt bei riesiger Seitenbreite");
        Assert.Null(task.Result);
    }

    [Fact]
    public void FindGutter_MaxSpecWidth_StaysBounded()
    {
        var w = EbookReflowLayout.MaxGutterPageWidth;
        var gutter = EbookReflowLayout.FindGutter(TwoColumns(w), w);
        Assert.NotNull(gutter);
    }

    [Fact]
    public void Blocks_CancelledToken_StopsBeforeTheFirstPage()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var page = new ReflowPage(1, 400, 600, [new ReflowGlyph(40, 45, 50, 10, "a", "Garamond", false, false)]);
        Assert.Throws<OperationCanceledException>(() => EbookReflowLayout.Blocks([page], [], null, cts.Token));
    }
}
