using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Models;
using RookHub.Api.Services.Og;
using RookHub.Api.Services.Puzzles;

namespace RookHub.Api.Tests.Og;

/// <summary>Vorschaubilder für Puzzles, Wochenpost und geteilte Stellung (2026-11-09): Diagramm aus Sicht des Lösers nach dem
/// Setup-Zug, „X to move", keine Lösung; unveröffentlichter Wochenpost → kein Bild.</summary>
public class OgPuzzleCardTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    [Fact]
    public void SolverPosition_Classic_AfterSetupMove_FromSolversSide()
    {
        var (fen, last) = OgMetaService.SolverPosition(StartFen, "e2e4 e7e5 g1f3", 0);
        Assert.StartsWith("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b", fen);
        Assert.Equal("e2e4", last);

        var card = OgMetaService.PuzzleCard(StartFen, "e2e4 e7e5 g1f3", 0, "Puzzle #990301 · Rating 1500", null, ["Fork"]);
        Assert.False(card.ToMoveWhite);
        Assert.True(card.Boards[0].Flip);           // Schwarz löst → Brett gedreht
        Assert.Equal("e2e4", card.Boards[0].LastMove);
        // Die Lösung (e7e5) steht nirgends auf der Karte.
        Assert.DoesNotContain(card.Lines, l => l.Contains("e5") || l.Contains("e7e5"));
    }

    [Fact]
    public void SolverPosition_TrainingFen_AndPrelude_AndIllegalMove()
    {
        Assert.Equal((StartFen, (string?)null), OgMetaService.SolverPosition(StartFen, "e2e4", -1));
        var (fen, last) = OgMetaService.SolverPosition(StartFen, "e2e4 e7e5 g1f3", 1);   // Vorspiel e4, Setup e5
        Assert.StartsWith("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w", fen);
        Assert.Equal("e7e5", last);
        // nicht spielbar (Buch-FENs sind nicht immer legal): die Stellung bleibt, keine Ausnahme
        Assert.Equal((StartFen, (string?)null), OgMetaService.SolverPosition(StartFen, "e2e5", 0));
        Assert.Equal((StartFen, (string?)null), OgMetaService.SolverPosition(StartFen, "", 0));
    }

    [Theory]
    [InlineData("mateIn2 fork short", "Mate in 2 · Fork")]
    [InlineData("advancedPawn endgame crushing", "Advanced pawn")]
    [InlineData("long veryLong", null)]
    [InlineData(null, null)]
    public void Themes_Humanized_WithoutQuietOnes(string? raw, string? shown) => Assert.Equal(shown, OgMetaService.Themes(raw));

    [Fact]
    public void PuzzleCard_Renders1200x630()
    {
        var card = OgMetaService.PuzzleCard(StartFen, "e2e4 e7e5", 0, "Puzzle #990302 · Rating 1850", null, ["Mate in 2 · Fork"]);
        var png = OgCardTests.Images().RenderCard(card);
        if (Environment.GetEnvironmentVariable("OG_DUMP_DIR") is { Length: > 0 } dir) File.WriteAllBytes(Path.Combine(dir, "puzzle.png"), png);
        OgCardTests.AssertCardPng(png);
    }

    // ── geteilte Stellung /analysis?fen= ─────────────────────────────────────────────

    [Fact]
    public void PositionId_RoundTrip_WithOrientation()
    {
        const string fen = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3";
        var id = OgMetaService.PositionId(fen, "black");
        Assert.StartsWith("b-", id);
        Assert.DoesNotContain('/', id!);
        Assert.Equal((fen, true), OgMetaService.ParsePositionId(id!));
        Assert.Equal((fen, false), OgMetaService.ParsePositionId(OgMetaService.PositionId(fen, null)!));
    }

    [Theory]
    [InlineData("/analysis?fen=r1bqkbnr%2Fpppp1ppp%2F2n5%2F4p3%2F4P3%2F5N2%2FPPPP1PPP%2FRNBQKB1R+w+KQkq+-+2+3&orientation=black", true)]
    [InlineData("/analysis?fen=8/8/8/8/8/8/8/K6k%20b%20-%20-%200%201", false)]
    public void ParsePath_AnalysisWithFen_IsPosition(string path, bool flip)
    {
        var r = OgMetaService.ParsePath(path);
        Assert.Equal("position", r!.Value.Kind);
        Assert.Equal(flip, OgMetaService.ParsePositionId(r.Value.Id)!.Value.Flip);
    }

    [Theory]
    [InlineData("/analysis")]
    [InlineData("/analysis?fen=")]
    [InlineData("/analysis?fen=<script>")]
    [InlineData("/analysis?fen=rnbqkbnr%2Fpppppppp%20w")]
    [InlineData("/analysis/games/5?fen=8/8/8/8/8/8/8/K6k%20w")]
    public void ParsePath_AnalysisWithoutValidFen_NoPreview(string path) => Assert.Null(OgMetaService.ParsePath(path));

    [Theory]
    [InlineData("x-abc")]
    [InlineData("w-!!!")]
    [InlineData("w-PHNjcmlwdD4")]   // „<script>"
    [InlineData("")]
    public void ParsePositionId_Garbage_Null(string id) => Assert.Null(OgMetaService.ParsePositionId(id));

    [Fact]
    public async Task Position_PageAndBoard_WithoutDatabase()
    {
        var meta = new OgMetaService(null!, null!, null!, null!, null!, NullLogger<OgMetaService>.Instance);
        var page = await meta.ResolvePageAsync("/analysis?fen=8/8/8/8/8/8/8/K6k%20b%20-%20-%200%201", "https://rh.example");
        Assert.Equal("Chess position · Black to move", page!.Title);
        Assert.StartsWith("https://rh.example/api/og/img/position/w-", page.ImageUrl);
        var id = page.ImageUrl.Split("/position/")[1].Split(".png")[0];
        var board = await meta.ResolveBoardAsync("position", id);
        Assert.False(board!.Card!.ToMoveWhite);
        OgCardTests.AssertCardPng(OgCardTests.Images().RenderCard(board.Card));
        Assert.Null(await meta.ResolveBoardAsync("position", "w-kaputt"));
    }

    // ── Wochenpost: nur veröffentlicht ────────────────────────────────────────────────

    [Fact]
    public async Task Weekly_PublishedOnly()
    {
        using var db = OgCardTests.NewDb();
        db.WeeklyPosts.AddRange(
            new WeeklyPost { Id = 990401, Title = "Wochenpost 45", FileName = "w45.pgn", ScheduledAt = DateTime.UtcNow.AddDays(-1),
                PgnContent = "[FEN \"6k1/5ppp/8/8/8/8/5PPP/R5K1 w - - 0 1\"]\n[SetUp \"1\"]\n\n1. Ra8# *" },
            new WeeklyPost { Id = 990402, Title = "Geheim", FileName = "w46.pgn", ScheduledAt = DateTime.UtcNow.AddDays(3), PgnContent = "" });
        await db.SaveChangesAsync();
        var meta = new OgMetaService(null!, null!, null!, null!, db, NullLogger<OgMetaService>.Instance,
            weekly: new WeeklyPostService(db, NullLogger<WeeklyPostService>.Instance));

        var page = await meta.ResolvePageAsync("/weekly/990401", "https://rh.example");
        Assert.Equal("Wochenpost 45", page!.Title);
        var board = await meta.ResolveBoardAsync("weekly", "990401");
        Assert.Equal("Weekly post", board!.Card!.Kicker);
        Assert.Equal(OgMetaService.ExpiringMaxAge, board.MaxAge);
        OgCardTests.AssertCardPng(OgCardTests.Images().RenderCard(board.Card));

        // noch nicht veröffentlicht: weder Titel noch Bild
        Assert.Null(await meta.ResolvePageAsync("/weekly/990402", "https://rh.example"));
        Assert.Null(await meta.ResolveBoardAsync("weekly", "990402"));
        Assert.Null(await meta.ResolveBoardAsync("weekly", "abc"));
    }
}
