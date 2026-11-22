using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers.Games;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.League;
using RookHub.Api.Services.TrainingVideos;

namespace RookHub.Api.Tests.Games;

/// <summary>„Teilen-Link → volle Seite“ (2026-11-22, <see cref="ShareLinkTargets"/>): nur wer die Sache auf dem normalen Weg sieht, bekommt
/// einen Weg; alles andere ist dasselbe 404 ohne Id.</summary>
public class ShareLinkTargetsTests : IDisposable
{
    private const int Owner = 10, Member = 11, Stranger = 12, Admin = 13;
    private const string GameToken = "own-token-aaaaaaaaaaaaaaaa", AnonToken = "anon-token-aaaaaaaaaaaaaa",
        ClubToken = "club-token-aaaaaaaaaaaaaa", LessonToken = "lessonTokenAAAAAAAAAAAAAAA", RecToken = "rec-token-aaaaaaaaaaaaaaa";
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private readonly Microsoft.AspNetCore.DataProtection.IDataProtectionProvider _protection = new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider();
    private LeagueGameShares LeagueGames() => new(_db, new LeagueFixtureGames(_db), _protection);
    private ShareLinkTargets Targets() => new(_db, new LeagueClubResolver(_db), new TrainingLessonShares(_db), LeagueGames());

    private async Task<(int Own, int Club, int ForeignClub, int Lesson, int Rec)> SeedAsync(bool lessonRevoked = false, bool lessonExpired = false)
    {
        await TestClubs.SeedAsync(_db);
        _db.Groups.Add(new Group { Id = 77, Name = "Testdorf" });
        _db.LeagueClubMembers.Add(new LeagueClubMember { ClubId = TestClubs.HomeId, GroupId = 77 });
        _db.UserGroups.Add(new UserGroup { UserId = Member, GroupId = 77 });
        var own = new SavedGame { UserId = Owner, Source = "pgn", Pgn = "1. e4 e5 *", ShareToken = GameToken, AnonShareToken = AnonToken };
        var club = new LeagueClubGame { ClubId = TestClubs.HomeId, Year = 2026, White = "A", Black = "B", Result = "1-0", Pgn = "1. e4 1-0", MovesHash = "h1", ShareToken = ClubToken };
        var foreign = new LeagueClubGame { ClubId = TestClubs.OtherId, Year = 2026, White = "C", Black = "D", Result = "0-1", Pgn = "1. d4 0-1", MovesHash = "h2", ShareToken = "foreign-token-aaaaaaaaaaa" };
        var lesson = new TrainingLesson { UserId = Owner, Date = new DateOnly(2026, 11, 1), ImportedAt = DateTime.UtcNow };
        var rec = new GameReconstruction { UserId = Owner, Title = "R", ShareToken = RecToken };
        _db.AddRange(own, club, foreign, lesson, rec);
        await _db.SaveChangesAsync();
        _db.TrainingLessonShares.Add(new TrainingLessonShare { LessonId = lesson.Id, Token = LessonToken, CreatedAt = DateTime.UtcNow,
            ExpiresAt = lessonExpired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddDays(7), RevokedAt = lessonRevoked ? DateTime.UtcNow : null });
        await _db.SaveChangesAsync();
        return (own.Id, club.Id, foreign.Id, lesson.Id, rec.Id);
    }

    [Fact]
    public async Task OwnGame_OnlyOwner_GetsGamePage_ForBothLinks()
    {
        var (own, _, _, _, _) = await SeedAsync();
        Assert.Equal($"/games/{own}", await Targets().ResolveAsync(Owner, false, false, "g", GameToken, null));
        Assert.Equal($"/games/{own}", await Targets().ResolveAsync(Owner, false, false, "ga", AnonToken, null));
        Assert.Null(await Targets().ResolveAsync(Stranger, false, true, "g", GameToken, null));
        Assert.Null(await Targets().ResolveAsync(Admin, true, true, "g", GameToken, null));   // /games/{id} zeigt auch Admins nur eigene
        Assert.Null(await Targets().ResolveAsync(Stranger, false, false, "ga", AnonToken, null));
        // Die Tokens sind nicht austauschbar: der normale Link ist kein anonymer.
        Assert.Null(await Targets().ResolveAsync(Owner, false, false, "ga", GameToken, null));
    }

    [Fact]
    public async Task ClubLink_NeedsLeagueView_AndMembership_ForeignClubAndRevokedGetNothing()
    {
        var (_, club, foreign, _, _) = await SeedAsync();
        Assert.Equal($"/club-games/{club}", await Targets().ResolveAsync(Member, false, true, "cg", ClubToken, null));
        Assert.Null(await Targets().ResolveAsync(Member, false, false, "cg", ClubToken, null));   // ohne league.view
        Assert.Null(await Targets().ResolveAsync(Stranger, false, true, "cg", ClubToken, null));  // kein Mitglied
        Assert.Null(await Targets().ResolveAsync(Member, false, true, "cg", "foreign-token-aaaaaaaaaaa", null));   // fremder Verein
        Assert.Equal($"/club-games/{foreign}", await Targets().ResolveAsync(Admin, true, true, "cg", "foreign-token-aaaaaaaaaaa", null));

        var g = await _db.LeagueClubGames.FirstAsync(x => x.Id == club);
        g.ShareToken = null;   // widerrufen
        await _db.SaveChangesAsync();
        Assert.Null(await Targets().ResolveAsync(Member, false, true, "cg", ClubToken, null));
    }

    [Fact]
    public async Task LeagueShareLink_ClubGame_ById_SameRules()
    {
        var (_, club, foreign, _, _) = await SeedAsync();
        Assert.Equal($"/club-games/{club}", await Targets().ResolveAsync(Member, false, true, "s", "irgendwas", club));
        Assert.Null(await Targets().ResolveAsync(Member, false, true, "s", "irgendwas", foreign));
        Assert.Null(await Targets().ResolveAsync(Stranger, false, true, "s", "irgendwas", club));
        Assert.Null(await Targets().ResolveAsync(Member, false, false, "s", "irgendwas", club));
        Assert.Null(await Targets().ResolveAsync(Member, false, true, "s", "irgendwas", 999999));
        Assert.Null(await Targets().ResolveAsync(Member, false, true, "s", "irgendwas", null));
    }

    [Fact]
    public async Task Lesson_OwnerOrAdmin_WhileValid_Reconstruction_OwnerOnly()
    {
        var (_, _, _, lesson, rec) = await SeedAsync();
        Assert.Equal($"/training-videos/{lesson}", await Targets().ResolveAsync(Owner, false, false, "tl", LessonToken, null));
        Assert.Equal($"/training-videos/{lesson}", await Targets().ResolveAsync(Admin, true, false, "tl", LessonToken, null));
        Assert.Null(await Targets().ResolveAsync(Stranger, false, false, "tl", LessonToken, null));
        Assert.Equal($"/reconstruct/{rec}", await Targets().ResolveAsync(Owner, false, false, "r", RecToken, null));
        Assert.Null(await Targets().ResolveAsync(Stranger, false, false, "r", RecToken, null));
    }

    /// <summary>Wunsch 2026-11-22: ein abgelaufener (bzw. widerrufener) Link führt das Konto der Stunde und Admins trotzdem auf die
    /// Stunde — sie haben normalen Zugriff. Für alle anderen bleibt er abgelaufen.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Lesson_ExpiredOrRevokedLink_OwnerAndAdminStillRedirected_OthersNot(bool expired, bool revoked)
    {
        var (_, _, _, lesson, _) = await SeedAsync(lessonRevoked: revoked, lessonExpired: expired);
        Assert.Equal($"/training-videos/{lesson}", await Targets().ResolveAsync(Owner, false, false, "tl", LessonToken, null));
        Assert.Equal($"/training-videos/{lesson}", await Targets().ResolveAsync(Admin, true, false, "tl", LessonToken, null));
        Assert.Null(await Targets().ResolveAsync(Stranger, false, false, "tl", LessonToken, null));
        Assert.Null(await Targets().ResolveAsync(Owner, false, false, "tl", "unknownTokenAAAAAAAAAAAA", null));
    }

    /// <summary>LeagueHub <c>/partie/{token}</c> (versiegelte Marke, Wunsch 2026-11-22): wer die Vereinspartie normal sieht (<c>league.view</c>
    /// + Verein der Partie, Admin jeder), springt nach <c>/club-games/{id}</c> — auch nach Ablauf; sonst nichts.</summary>
    [Fact]
    public async Task LeaguePartieLink_MemberWithLeagueView_GetsClubGame_EvenExpired_OthersNothing()
    {
        var (_, club, _, _, _) = await SeedAsync();
        const int tnr = 4711, round = 2;
        _db.LeagueRounds.Add(new LeagueRound { Tnr = tnr, Round = round, Date = new DateOnly(2026, 10, 11) });
        var lg = new LeagueGame { Tnr = tnr, Round = round, MatchNo = 1, Board = 1, HomeTeam = "Testdorf", AwayTeam = "Bergheim",
            HomePlayer = "A", AwayPlayer = "B", HomeColor = "w", Result = "1 - 0" };
        _db.LeagueGames.Add(lg);
        await _db.SaveChangesAsync();
        var row = await _db.LeagueClubGames.SingleAsync(g => g.Id == club);
        row.Pgn = "[White \"A\"]\n[Black \"B\"]\n[Result \"1-0\"]\n\n1. e4 e5 1-0";
        LeagueGameLinks.Set(row, lg);
        await _db.SaveChangesAsync();
        var made = await LeagueGames().CreateAsync(TestClubs.Home, tnr, round, "Testdorf", 1, default);
        Assert.NotNull(made);
        var token = made!.Value.Token;

        Assert.Equal($"/club-games/{club}", await Targets().ResolveAsync(Member, false, true, "lp", token, null));
        Assert.Equal($"/club-games/{club}", await Targets().ResolveAsync(Admin, true, true, "lp", token, null));
        Assert.Null(await Targets().ResolveAsync(Member, false, false, "lp", token, null));     // ohne league.view
        Assert.Null(await Targets().ResolveAsync(Stranger, false, true, "lp", token, null));    // anderer Verein
        Assert.Null(await Targets().ResolveAsync(Member, false, true, "lp", "x" + token[1..], null));
        Assert.Null(await Targets().ResolveAsync(Member, false, true, "lp", "", null));

        // Ein Brett ohne Vereinspartie (Partie aus einer Spielerkarte) hat kein Gegenstück — dieselbe Marke ohne Partie-Id.
        Assert.Null(new LeagueGameShares(_db, new LeagueFixtureGames(_db), new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider())
            .ClubGameOf(token));                                                                  // fremder Schlüsselbund: kaputt
    }

    [Theory]
    [InlineData("x", GameToken)]
    [InlineData("g", "")]
    [InlineData("g", null)]
    [InlineData("cg", "unbekannt")]
    [InlineData(null, GameToken)]
    public async Task UnknownKindOrToken_IsNull(string? kind, string? token)
    {
        await SeedAsync();
        Assert.Null(await Targets().ResolveAsync(Owner, true, true, kind, token, null));
    }

    [Fact]
    public async Task Controller_OkWithPath_Or404WithoutBody_AndRequiresLogin()
    {
        var (_, club, _, _, _) = await SeedAsync();
        var ok = Assert.IsType<OkObjectResult>(await new ShareLinkTargetController(Targets()).As(Member, admin: false).WithLeagueView()
            .Target("cg", ClubToken, null, default));
        Assert.Contains($"/club-games/{club}", System.Text.Json.JsonSerializer.Serialize(ok.Value));
        // Ohne league.view (kein Claim): dasselbe 404 wie ein unbekannter Link
        Assert.IsType<NotFoundResult>(await new ShareLinkTargetController(Targets()).As(Member).Target("cg", ClubToken, null, default));
        Assert.IsType<NotFoundResult>(await new ShareLinkTargetController(Targets()).As(Stranger).WithLeagueView().Target("cg", ClubToken, null, default));
        Assert.IsType<NotFoundResult>(await new ShareLinkTargetController(Targets()).As(Member).WithLeagueView().Target("cg", "weg", null, default));
        // lp (LeagueHub /partie) fragt league.view wie cg/s: ohne Claim 404 (auch bei kaputter Marke ohnehin)
        Assert.IsType<NotFoundResult>(await new ShareLinkTargetController(Targets()).As(Member).Target("lp", "kaputt", null, default));

        // Gäste: [Authorize] an der Klasse, kein [AllowAnonymous] an der Aktion → 401 vor jeder Auskunft.
        Assert.NotNull(typeof(ShareLinkTargetController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(typeof(ShareLinkTargetController).GetMethod(nameof(ShareLinkTargetController.Target))!.GetCustomAttribute<AllowAnonymousAttribute>());
    }
}

internal static class ShareLinkTargetsTestExtensions
{
    /// <summary>Das Konto hat <c>league.view</c> als Claim (ohne <c>PermissionResolver</c> liest <c>HasPermissionAsync</c> den Claim).</summary>
    public static T WithLeagueView<T>(this T controller) where T : ControllerBase
    {
        var id = (System.Security.Claims.ClaimsIdentity)controller.ControllerContext.HttpContext.User.Identity!;
        id.AddClaim(new System.Security.Claims.Claim(RookHub.Api.Authorization.PermissionAuthorizationHandler.PermissionClaimType, Permissions.LeagueView));
        return controller;
    }
}
