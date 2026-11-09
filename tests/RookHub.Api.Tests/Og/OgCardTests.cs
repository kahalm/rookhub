using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers.Og;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Games;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.Og;
using RookHub.Api.Services.Repertoires;
using SkiaSharp;

namespace RookHub.Api.Tests.Og;

/// <summary>Vorschaubilder mit Text (2026-11-09, docs/api/link-vorschau.md): Partie, markierte Stellung, rekonstruierte Partie,
/// Repertoire-Linie — Rendering, Meta-Texte, Rechte (ungültiger/abgelaufener Link → kein Bild).</summary>
public class OgCardTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string Pgn = "[Event \"Tiroler Landesliga\"]\n[White \"Hess, Max\"]\n[Black \"Berger, Anna\"]\n[Result \"1-0\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 1-0";

    internal static AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    internal static OgImageService Images() => new(new TestLogger<OgImageService>());

    internal static void AssertCardPng(byte[] png)
    {
        using var bmp = SKBitmap.Decode(png);
        Assert.NotNull(bmp);
        Assert.Equal(1200, bmp.Width);
        Assert.Equal(630, bmp.Height);
    }

    private static SharedGameDto Game(string? pgn = Pgn) => new()
    {
        Source = "lichess", White = "Hess, Max", Black = "Berger, Anna", WhiteElo = 1826, BlackElo = 1712, Result = "1-0",
        Pgn = pgn ?? string.Empty, PlayedAt = new DateTime(2026, 10, 3),
    };

    // ── Rendering ────────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderCard_Game_Is1200x630_AndDrawsText()
    {
        var card = OgMetaService.GameCard(Game(), null, new double?[] { 50, 60, 40, 70, 80 });
        var png = Images().RenderCard(card);
        if (Environment.GetEnvironmentVariable("OG_DUMP_DIR") is { Length: > 0 } dir) File.WriteAllBytes(Path.Combine(dir, "game.png"), png);
        AssertCardPng(png);
        using var bmp = SKBitmap.Decode(png);
        // rechts vom Brett steht heller Text (Spielernamen) — die Schrift ist eingebettet
        var bright = Enumerable.Range(690, 400).Count(x => Enumerable.Range(95, 70).Any(y => bmp.GetPixel(x, y).Red > 200));
        Assert.True(bright > 30, $"helle Spalten in der Spielerzeile: {bright}");
    }

    [Fact]
    public void RenderCard_EmptyOrBrokenData_StillAPng_NoException()
    {
        var images = Images();
        AssertCardPng(images.RenderCard(new OgCard()));
        AssertCardPng(images.RenderCard(new OgCard { Boards = [new OgDiagram("kaputt/xx", true, "z9")], Title = "", Lines = ["", "  "] }));
        AssertCardPng(images.RenderCard(new OgCard { Brand = OgBrand.LeagueHub, Table = new OgTable(null, []) }));
        AssertCardPng(images.RenderCard(new OgCard { Title = new string('W', 400), Players = [new OgPlayer(true, new string('x', 300), 9999)] }));
    }

    [Fact]
    public void RenderCard_LastMove_TintsBothSquares()
    {
        var images = Images();
        var plain = images.RenderCard(new OgCard { Boards = [new OgDiagram(StartFen)] });
        var marked = images.RenderCard(new OgCard { Boards = [new OgDiagram(StartFen, false, "e2e4")] });
        Assert.NotEqual(plain, marked);
        Assert.Equal(new HashSet<(int, int)> { (4, 6), (4, 4) }, OgImageService.LastMoveSquares("e2e4"));
        Assert.Empty(OgImageService.LastMoveSquares("z9z9"));
        Assert.Empty(OgImageService.LastMoveSquares(null));
    }

    [Theory]
    [InlineData("1/2-1/2", "½–½")]
    [InlineData("1-0", "1–0")]
    [InlineData("0-1", "0–1")]
    [InlineData("2½:3½", "2½:3½")]
    public void PrettyResult(string raw, string shown) => Assert.Equal(shown, OgImageService.PrettyResult(raw));

    // ── Partie ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void GameCard_EndPosition_PlayersResultEvent_LastMove()
    {
        var card = OgMetaService.GameCard(Game(), null, null);
        Assert.Equal("Chess game", card.Kicker);
        Assert.Equal(new[] { "Hess, Max", "Berger, Anna" }, card.Players.Select(p => p.Name));
        Assert.Equal(new int?[] { 1826, 1712 }, card.Players.Select(p => p.Elo));
        Assert.Equal("1-0", card.Result);
        Assert.Equal("Tiroler Landesliga · 3 Oct 2026", Assert.Single(card.Lines));
        Assert.Equal("a7a6", card.Boards[0].LastMove);
        Assert.StartsWith("r1bqkbnr/1ppp1ppp/p1n5/1B2p3/", card.Boards[0].Fen);
    }

    [Fact]
    public void GameCard_MarkedPosition_AfterPly_WithMoveLabel()
    {
        var card = OgMetaService.GameCard(Game(), 3, null);
        Assert.Equal("Position after 2. Nf3", card.Kicker);
        Assert.Equal("g1f3", card.Boards[0].LastMove);
        Assert.StartsWith("rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b", card.Boards[0].Fen);
        Assert.Equal("Position after 1… e5", OgMetaService.GameCard(Game(), 2, null).Kicker);
        // jenseits der Partie: Endstellung wie ohne ply
        Assert.Equal("Chess game", OgMetaService.GameCard(Game(), 99, null).Kicker);
    }

    [Fact]
    public void GameCard_BrokenPgn_FallsBackToStart_NoException()
    {
        var card = OgMetaService.GameCard(Game("das ist kein pgn"), 5, null);
        Assert.Equal(StartFen, card.Boards[0].Fen);
        Assert.Null(card.Boards[0].LastMove);
        AssertCardPng(Images().RenderCard(card));
    }

    [Theory]
    [InlineData("[Event \"Rated Blitz game\"]", null)]
    [InlineData("[Event \"Casual Rapid game\"]", null)]
    [InlineData("[Event \"Live Chess\"]", null)]
    [InlineData("[Event \"?\"]", null)]
    [InlineData("[Event \"Open Innsbruck\"]", "Open Innsbruck")]
    public void EventOf_SkipsMeaninglessNames(string tag, string? expected) => Assert.Equal(expected, OgMetaService.EventOf(tag + "\n\n1. e4 *"));

    [Theory]
    [InlineData("/r/abc", "recon", "abc")]
    [InlineData("/l/xyz?utm=1", "line", "xyz")]
    public void ParsePath_ReconAndLine(string path, string kind, string id)
    {
        var r = OgMetaService.ParsePath(path);
        Assert.Equal(kind, r!.Value.Kind);
        Assert.Equal(id, r.Value.Id);
    }

    [Theory]
    [InlineData("/r")]
    [InlineData("/r/abc/extra")]
    [InlineData("/l")]
    public void ParsePath_ReconAndLine_NeedExactlyOneToken(string path) => Assert.Null(OgMetaService.ParsePath(path));

    [Fact]
    public void CacheControlFor_TokenLinksShort_FixedContentImmutable()
    {
        Assert.Equal("public, max-age=604800, immutable", OgController.CacheControlFor(null));
        Assert.Equal("public, max-age=600", OgController.CacheControlFor(TimeSpan.FromMinutes(10)));
    }

    // ── Rekonstruierte Partie + Repertoire-Linie: nur über einen gültigen Link ────────

    private static OgMetaService Meta(AppDbContext db) => new(null!, null!, null!, null!, db, NullLogger<OgMetaService>.Instance,
        reconstructions: new GameReconstructionService(db), sharedLines: new SharedLineService(db));

    [Fact]
    public async Task Recon_ValidToken_CardAndMeta_InvalidToken_Nothing()
    {
        using var db = NewDb();
        db.GameReconstructions.Add(new GameReconstruction
        {
            Id = 990101, UserId = 990101, Title = "Vereinsmeisterschaft", White = "Hess", Black = "Berger", Result = "0-1",
            ShareToken = "rtok990101",
            Parts = [new GameReconstructionPart { Id = 990102, Ordinal = 0, Kind = ReconstructionPartKind.Moves, Moves = "e4 e5 Nf3" }],
        });
        await db.SaveChangesAsync();
        var meta = Meta(db);

        var page = await meta.ResolvePageAsync("/r/rtok990101", "https://rh.example");
        Assert.NotNull(page);
        Assert.Equal("Hess – Berger", page!.Title);
        Assert.Equal($"https://rh.example/api/og/img/recon/rtok990101.png?c={OgMetaService.CardVersion}", page.ImageUrl);
        var board = await meta.ResolveBoardAsync("recon", "rtok990101");
        Assert.NotNull(board?.Card);
        Assert.Equal(OgMetaService.EditableMaxAge, board!.MaxAge);
        AssertCardPng(Images().RenderCard(board.Card!));

        Assert.Null(await meta.ResolvePageAsync("/r/unbekannt", "https://rh.example"));
        Assert.Null(await meta.ResolveBoardAsync("recon", "unbekannt"));
    }

    [Fact]
    public async Task Line_ValidToken_Card_ExpiredOrUnknown_Nothing()
    {
        using var db = NewDb();
        db.SharedLines.AddRange(
            new SharedLine { Id = 990201, OwnerUserId = 990201, Title = "Spanisch", RepertoireName = "Weiß 1.e4", Pgn = "1. e4 e5 2. Nf3 Nc6 3. Bb5 *",
                ShareToken = "ltok990201", ExpiresAt = DateTime.UtcNow.AddDays(3) },
            new SharedLine { Id = 990202, OwnerUserId = 990201, Title = "Alt", Pgn = "1. d4 *", ShareToken = "ltok990202",
                ExpiresAt = DateTime.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();
        var meta = Meta(db);

        var page = await meta.ResolvePageAsync("/l/ltok990201", "https://rh.example");
        Assert.Equal("Spanisch", page!.Title);
        Assert.Contains("Weiß 1.e4", page.Description);
        var board = await meta.ResolveBoardAsync("line", "ltok990201");
        Assert.Equal("Repertoire line", board!.Card!.Kicker);
        Assert.Equal("f1b5", board.Card.Boards[0].LastMove);
        Assert.Equal(OgMetaService.ExpiringMaxAge, board.MaxAge);

        // abgelaufen: weder Seite noch Bild
        Assert.Null(await meta.ResolvePageAsync("/l/ltok990202", "https://rh.example"));
        Assert.Null(await meta.ResolveBoardAsync("line", "ltok990202"));
        Assert.Null(await meta.ResolveBoardAsync("line", "nix"));
    }
}
