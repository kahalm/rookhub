using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;
using RookHub.Api.Services.Og;

namespace RookHub.Api.Tests.Og;

/// <summary>Vorschaubild eines geteilten Aufgabenblatts (2026-11-09): Raster aus bis zu sechs Diagrammen, ohne Text/Lösung;
/// widerrufener Link → kein Bild.</summary>
public class OgWorksheetCardTests
{
    private const string Fen = "6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1";

    private static async Task<(OgMetaService Meta, Data.AppDbContext Db)> SetupAsync(int items)
    {
        var db = OgCardTests.NewDb();
        db.Worksheets.Add(new Worksheet
        {
            Id = 990501, UserId = 990501, Name = "Grundmatts U10", ShareToken = "wtok990501",
            Items = Enumerable.Range(0, items).Select(i => new WorksheetItem
            {
                Id = 990510 + i, SortOrder = i, Fen = Fen, Orientation = i % 2 == 0 ? "white" : "black",
                Heading = "Geheime Überschrift", Text = "Begleittext", SolutionMoves = "a1a8",
            }).ToList(),
        });
        await db.SaveChangesAsync();
        return (new OgMetaService(null!, null!, null!, null!, db, NullLogger<OgMetaService>.Instance, worksheets: new WorksheetService(db)), db);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(9)]
    public async Task Worksheet_GridOfUpToSix_Renders1200x630(int items)
    {
        var (meta, db) = await SetupAsync(items);
        using var _ = db;
        var board = await meta.ResolveBoardAsync("worksheet", "wtok990501");
        var card = board!.Card!;
        Assert.Equal(Math.Min(items, OgMetaService.WorksheetBoards), card.Boards.Count);
        Assert.Equal(OgMetaService.EditableMaxAge, board.MaxAge);
        Assert.True(card.Boards[Math.Min(1, card.Boards.Count - 1)].Flip == (card.Boards.Count > 1));
        // Begleittext, Überschrift und Lösung kommen nicht aufs Bild
        Assert.DoesNotContain(card.Lines, l => l.Contains("Geheim") || l.Contains("Begleit") || l.Contains("a8"));
        var png = OgCardTests.Images().RenderCard(card);
        if (Environment.GetEnvironmentVariable("OG_DUMP_DIR") is { Length: > 0 } dir) File.WriteAllBytes(Path.Combine(dir, $"worksheet{items}.png"), png);
        OgCardTests.AssertCardPng(png);
    }

    [Fact]
    public async Task Worksheet_Meta_AndRevokedLink_Nothing()
    {
        var (meta, db) = await SetupAsync(3);
        using var _ = db;
        var page = await meta.ResolvePageAsync("/w/wtok990501", "https://rh.example");
        Assert.Equal("Grundmatts U10", page!.Title);
        Assert.StartsWith("3 chess positions", page.Description);
        Assert.Equal($"https://rh.example/api/og/img/worksheet/wtok990501.png?c={OgMetaService.CardVersion}", page.ImageUrl);

        // Teilen abgeschaltet (= Widerruf): das alte Token zeigt nichts mehr
        db.Worksheets.Single().ShareToken = null;
        await db.SaveChangesAsync();
        Assert.Null(await meta.ResolvePageAsync("/w/wtok990501", "https://rh.example"));
        Assert.Null(await meta.ResolveBoardAsync("worksheet", "wtok990501"));
    }

    [Fact]
    public void ParsePath_Worksheet() => Assert.Equal("worksheet", OgMetaService.ParsePath("/w/abc")!.Value.Kind);
}
