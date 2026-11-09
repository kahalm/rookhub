using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Authorization;
using RookHub.Api.Controllers.League;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests.League;

/// <summary>Teilen-Link EINER Vereinspartie (2026-11-09, <c>/cg/{token}</c>): nur diese Partie, Widerruf, Anonymisierung, kein Schreiben. Spieler erfunden.</summary>
public class ClubGameLinksTests : IDisposable
{
    private const string RealName = "Ackermann, Anna";
    private const string Pgn = "[Event \"Landesliga\"]\n[White \"Testdorf\"]\n[Black \"Brunner, Bert\"]\n[Result \"1-0\"]\n[WhiteElo \"1800\"]\n\n1. e4 e5 1-0\n";
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private ClubGameLinks Links() => new(_db, new LeagueClubService(_db, NullLogger<LeagueClubService>.Instance), new ClubGameSummaries(_db), new ClubGameComparisons(_db, null!));

    private async Task<LeagueClubGame> GameAsync(bool anon, string pgn = Pgn, int clubId = TestClubs.HomeId, string moves = "h1")
    {
        if (!await _db.LeagueClubs.AnyAsync(c => c.Id == TestClubs.HomeId)) await TestClubs.SeedAsync(_db);
        var g = new LeagueClubGame { ClubId = clubId, Year = 2026, White = anon ? "Testdorf" : "Ackermann, Anna", Black = "Brunner, Bert", Result = "1-0", Plies = 2,
            Pgn = pgn, MovesHash = moves, Anonymized = anon, Event = anon ? "Landesliga Runde 2" : "Landesliga", WhiteRealName = anon ? RealName : null, WhiteRealFide = anon ? "1234567" : null,
            WhiteElo = anon ? 1800 : 1500 };
        _db.LeagueClubGames.Add(g);
        await _db.SaveChangesAsync();
        return g;
    }

    private async Task SummaryAsync(int id, string markdown)
    {
        _db.ClubGameSummaries.Add(new ClubGameSummary { ClubGameId = id, Status = ClubGameSummaryStatus.Done, Markdown = markdown, MovesHash = "h1", DoneAt = DateTime.UtcNow,
            RequestedAt = DateTime.UtcNow, Model = "fable" });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Create_IsIdempotent_ReadShowsOnlyThatGame_RevokeKillsTheLink()
    {
        var g = await GameAsync(false);
        var other = await GameAsync(false, moves: "h2");
        var token = await Links().CreateAsync(TestClubs.Home, g.Id, default);
        Assert.Equal(token, await Links().CreateAsync(TestClubs.Home, g.Id, default));
        var shared = await Links().ReadAsync(token!, default);
        Assert.Equal(("Ackermann, Anna", "Landesliga", Pgn), (shared!.White, shared.Event!, shared.Pgn));
        Assert.Null((await _db.LeagueClubGames.AsNoTracking().FirstAsync(x => x.Id == other.Id)).ShareToken);
        Assert.DoesNotContain(typeof(ClubGameLinks.SharedClubGame).GetProperties(), p => p.Name is "Id" or "ClubId" or "ShareToken" or "LeagueGameId");

        Assert.True(await Links().RevokeAsync(TestClubs.Home, g.Id, default));
        Assert.Null(await Links().ReadAsync(token!, default));
        Assert.Null(await Links().EvalsAsync(token!, default));
        Assert.Null(await Links().SummaryAsync(token!, default));
    }

    [Fact]
    public async Task InvalidToken_IsNotFound_ArchivedGame_KillsLink()
    {
        var g = await GameAsync(false);
        var token = await Links().CreateAsync(TestClubs.Home, g.Id, default);
        var c = new ClubGameLinkController(Links());
        Assert.IsType<NotFoundResult>(await c.Get("nix", default));
        Assert.IsType<NotFoundResult>(await c.Summary("", default));
        Assert.IsType<NotFoundResult>(await c.Comparisons("nix", default));
        Assert.IsType<OkObjectResult>(await c.Get(token!, default));
        Assert.Null(await Links().ReadAsync(token!.ToUpperInvariant() + "x", default));
        g.ArchivedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await new ClubGameLinkController(Links()).Get(token, default));
    }

    [Fact]
    public async Task ForeignClub_CannotShare_NorRevoke()
    {
        var g = await GameAsync(false);
        Assert.Null(await Links().CreateAsync(TestClubs.Other, g.Id, default));
        Assert.False(await Links().RevokeAsync(TestClubs.Other, g.Id, default));
        Assert.Equal((false, (string?)null), await Links().TokenAsync(TestClubs.Other, g.Id, default));
        Assert.Null(await Links().CreateAsync(TestClubs.Home, 424242, default));
    }

    [Fact]
    public async Task AnonymizedGame_ExposesNoRealName_InJsonOrPgn_NoEventNoEloOfReplacedSide()
    {
        var g = await GameAsync(true, Pgn);
        var token = await Links().CreateAsync(TestClubs.Home, g.Id, default);
        var shared = (await Links().ReadAsync(token!, default))!;
        var json = JsonSerializer.Serialize(shared);
        Assert.DoesNotContain("Ackermann", json);
        Assert.DoesNotContain("Anna", json);
        Assert.DoesNotContain("1234567", json);
        Assert.Equal(("Testdorf", (int?)null, (string?)null), (shared.White, shared.WhiteElo, shared.Event));
        Assert.DoesNotContain("Landesliga", shared.Pgn);
        Assert.DoesNotContain("WhiteElo", shared.Pgn);
        Assert.Contains("1. e4 e5", shared.Pgn);
    }

    [Fact]
    public async Task Summary_OnlyDoneText_NoModel_RemovesRoundAndBoard_OfAnonymizedGame()
    {
        var g = await GameAsync(true);
        await SummaryAsync(g.Id, "Eine Partie aus der 1. Klasse, Runde 3, Brett 5. Weiß gewann.");
        var token = await Links().CreateAsync(TestClubs.Home, g.Id, default);
        var s = await Links().SummaryAsync(token!, default);
        Assert.Equal(("done", (string?)null, (string?)null, false), (s!.Status, s.Model, s.Error, s.RequestedAt != null));
        Assert.DoesNotContain("Runde", s.Markdown!);
        Assert.DoesNotContain("Brett", s.Markdown!);
        Assert.Contains("1. Klasse", s.Markdown!);
    }

    [Theory]
    [InlineData("Am Ende gewann Ackermann klar.")]
    [InlineData("Am Ende gewann ackermann klar.")]
    [InlineData("Anna spielte stark.")]
    [InlineData("FIDE 1234567 dazu.")]
    public async Task Summary_OfAnonymizedGame_WithInternalName_IsWithheld(string text)
    {
        var g = await GameAsync(true);
        await SummaryAsync(g.Id, text);
        var token = await Links().CreateAsync(TestClubs.Home, g.Id, default);
        var s = await Links().SummaryAsync(token!, default);
        Assert.Equal(("none", (string?)null), (s!.Status, s.Markdown));
    }

    [Fact]
    public async Task Summary_NoneYet_And_NormalGame_KeepsText()
    {
        var g = await GameAsync(false);
        var token = await Links().CreateAsync(TestClubs.Home, g.Id, default);
        Assert.Equal("none", (await Links().SummaryAsync(token!, default))!.Status);
        await SummaryAsync(g.Id, "Runde 3 gegen Brunner.");
        Assert.Equal("Runde 3 gegen Brunner.", (await Links().SummaryAsync(token!, default))!.Markdown);
    }

    [Fact]
    public void Endpoints_ManagingNeedsLeagueView_LinkHasOnlyGets()
    {
        foreach (var m in new[] { nameof(LeagueClubOverviewController.Share), nameof(LeagueClubOverviewController.CreateShare), nameof(LeagueClubOverviewController.RevokeShare) })
            Assert.Contains(PermissionPolicyProvider.Prefix + Permissions.LeagueView, typeof(LeagueClubOverviewController).GetMethod(m)!.GetCustomAttribute<HasPermissionAttribute>()!.Policy!);
        Assert.NotNull(typeof(ClubGameLinkController).GetCustomAttribute<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>());
        var verbs = typeof(ClubGameLinkController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>()).SelectMany(a => a.HttpMethods).Distinct();
        Assert.Equal(["GET"], verbs);
    }

    [Fact]
    public async Task Comparisons_WithoutAnalysis_AreEmpty_AndUnknownTokenNull()
    {
        var g = await GameAsync(false);
        var token = await Links().CreateAsync(TestClubs.Home, g.Id, default);
        Assert.Empty((await Links().ComparisonsAsync(token!, default))!);
        Assert.Null(await Links().ComparisonsAsync("nix", default));
    }
}
