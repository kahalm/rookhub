using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers.Og;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Og;

namespace RookHub.Api.Tests.Og;

/// <summary>Link-Vorschau der geteilten Vereinspartie <c>/cg/{token}</c> (2026-11-22, vorher keinerlei og-Tags): Partie- und
/// Trainingskarte wie bei <c>/g/</c>; eine anonymisierte Partie zeigt nur den Anonymisierungs-Namen — Klarname, interne FIDE-ID, Event
/// und Elo der ersetzten Seite erreichen weder Tags noch Bild. Spieler erfunden.</summary>
public class OgClubGameTests : IDisposable
{
    private const string RealName = "Ackermann, Anna";
    private const string RealFide = "1234567";
    private const string Base = "https://rh.example";
    // Das gespeicherte PGN trägt den Klarnamen (so kommt es vom Formular) — die Vorschau darf es nie roh lesen.
    private const string RawPgn = "[Event \"Landesliga Runde 2\"]\n[White \"Ackermann, Anna\"]\n[Black \"Brunner, Bert\"]\n[Result \"1-0\"]\n"
        + "[WhiteElo \"1800\"]\n[BlackElo \"1750\"]\n[WhiteFideId \"1234567\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 1-0\n";

    private readonly AppDbContext _db = OgCardTests.NewDb();
    public void Dispose() => _db.Dispose();

    private OgMetaService Meta() => new(null!, null!, null!, null!, _db, NullLogger<OgMetaService>.Instance,
        clubGames: new ClubGameLinks(_db, new LeagueClubService(_db, NullLogger<LeagueClubService>.Instance), new ClubGameSummaries(_db),
            new ClubGameComparisons(_db, null!)));

    private async Task<string> ShareAsync(bool anon, string token = "cgtok990701")
    {
        await TestClubs.SeedAsync(_db);
        _db.LeagueClubGames.Add(new LeagueClubGame
        {
            ClubId = TestClubs.HomeId, Year = 2026, White = anon ? "Testdorf" : "Hess, Max", Black = "Brunner, Bert", Result = "1-0", Plies = 6,
            Pgn = RawPgn, MovesHash = token, Anonymized = anon, Event = "Landesliga Runde 2", WhiteElo = 1800, BlackElo = 1750,
            WhiteRealName = anon ? RealName : null, WhiteRealFide = anon ? RealFide : null, ShareToken = token,
        });
        await _db.SaveChangesAsync();
        return token;
    }

    [Theory]
    [InlineData("/cg/abc_DEF-1", "cgame", "abc_DEF-1")]
    [InlineData("/cg/abc_DEF-1?train=white", "cgame", "abc_DEF-1")]
    [InlineData("/cg/abc_DEF-1/summary", "cgame", "abc_DEF-1")]
    public void ParsePath_ClubGameLink(string path, string kind, string id)
    {
        var r = OgMetaService.ParsePath(path);
        Assert.Equal((kind, id), (r!.Value.Kind, r.Value.Id));
        Assert.Null(OgMetaService.ParsePath("/cg"));
        Assert.Null(OgMetaService.ParsePath(path, OgIndexHtmlProvider.LeagueHubSite));   // nur auf RookHub
    }

    [Fact]
    public async Task Page_Anonymized_ShowsAnonName_NeverTheRealName()
    {
        var token = await ShareAsync(anon: true);
        var page = await Meta().ResolvePageAsync($"/cg/{token}", Base);

        Assert.NotNull(page);
        Assert.Equal("Testdorf – Brunner, Bert (Elo 1750)", page!.Title);        // Elo der ersetzten Seite fällt weg
        Assert.Equal("1-0 · Partie auf RookHub nachspielen", page.Description);  // kein Event (Runde!) bei anonymisierten
        Assert.Equal($"{Base}/api/og/img/cgame/{token}.png?c={OgMetaService.CardVersion}", page.ImageUrl);
        Assert.Equal($"{Base}/cg/{token}", page.CanonicalUrl);
        AssertNoRealName(OgController.Inject("<html><head></head></html>", page));

        var board = await Meta().ResolveBoardAsync("cgame", token);
        Assert.NotNull(board?.Card);
        Assert.Equal(new[] { "Testdorf", "Brunner, Bert" }, board!.Card!.Players.Select(p => p.Name));
        Assert.Null(board.Card.Players[0].Elo);
        AssertNoRealName(string.Join("|", board.Card.Lines.Append(board.Card.Kicker ?? "").Append(board.Card.Title ?? "")));
        Assert.False(board.Card.Boards[0].Flip);                                 // eigene Seite = Weiß
        Assert.Equal(TimeSpan.FromHours(1), board.MaxAge);                       // widerrufbar: nicht eine Woche „immutable"
        var png = OgCardTests.Images().RenderCard(board.Card);
        if (Environment.GetEnvironmentVariable("OG_DUMP_DIR") is { Length: > 0 } dir) File.WriteAllBytes(Path.Combine(dir, "cgame.png"), png);
        OgCardTests.AssertCardPng(png);
    }

    [Theory]
    [InlineData("white", "Testdorf")]
    [InlineData("black", "Brunner, Bert")]
    public async Task TrainPage_Anonymized_TrainCard_WithAnonName(string side, string who)
    {
        var token = await ShareAsync(anon: true);
        var page = await Meta().ResolvePageAsync($"/cg/{token}?train={side}", Base);

        Assert.Equal(OgMetaService.TrainTitle, page!.Title);
        Assert.Equal($"Testdorf – Brunner, Bert: replay the mistakes of {who} and find the better moves.", page.Description);
        Assert.Equal($"{Base}/api/og/img/cgtrain/{side}-{token}.png?l=en", page.ImageUrl);
        AssertNoRealName(OgController.Inject("<html><head></head></html>", page));

        var board = await Meta().ResolveBoardAsync("cgtrain", $"{side}-{token}");
        Assert.NotNull(board?.Train);
        Assert.Equal($"Mistakes by {who}", board!.Train!.Note);
        Assert.Equal(side == "black", board.Flip);
        AssertNoRealName(board.Train.Note!);
        OgCardTests.AssertCardPng(OgCardTests.Images().RenderBoard(board.Fen, board.Flip, board.Curve, board.Train));
    }

    [Fact]
    public async Task Page_NotAnonymized_ShowsNamesEloAndEvent_LikeTheSharedGame()
    {
        var token = await ShareAsync(anon: false);
        var page = await Meta().ResolvePageAsync($"/cg/{token}", Base);
        Assert.Equal("Hess, Max (Elo 1800) – Brunner, Bert (Elo 1750)", page!.Title);
        Assert.Equal("1-0 · Landesliga Runde 2 · Partie auf RookHub nachspielen", page.Description);
    }

    [Fact]
    public async Task InvalidOrRevokedLink_NoPageNoImage()
    {
        var token = await ShareAsync(anon: true);
        var meta = Meta();
        Assert.Null(await meta.ResolvePageAsync("/cg/gibtsnicht", Base));
        Assert.Null(await meta.ResolveBoardAsync("cgame", "gibtsnicht"));
        Assert.Null(await meta.ResolveBoardAsync("cgtrain", $"white-gibtsnicht"));
        Assert.Null(await meta.ResolveBoardAsync("cgtrain", $"green-{token}"));

        var g = await _db.LeagueClubGames.FirstAsync(x => x.ShareToken == token);
        g.ShareToken = null;                                                      // widerrufen
        await _db.SaveChangesAsync();
        Assert.Null(await meta.ResolvePageAsync($"/cg/{token}", Base));
        Assert.Null(await meta.ResolveBoardAsync("cgame", token));
        Assert.Null(await meta.ResolveBoardAsync("cgtrain", $"white-{token}"));
    }

    private static void AssertNoRealName(string text)
    {
        Assert.DoesNotContain("Ackermann", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Anna", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(RealFide, text);
        Assert.DoesNotContain("Runde", text);
    }
}
