using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Authorization;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>
/// Aufstellungen je Runde + erste Züge je Partie (2026-10-08): Zugprüfung/Normalisierung, Rechte (Leser, Beitragender, Verwalter,
/// fremder Verein, fremder Eintrag), 404 ohne Paarung und das Lesen im Aufstellungs-Endpunkt. Spieler und Vereine erfunden.
/// </summary>
public class LeagueGameMovesTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
    public void Dispose() => _db.Dispose();

    private const int Tnr = 4711, Round = 2;
    private const int Admin = 1, HomeMember = 2, OtherMember = 3, Reader = 4, HomeMember2 = 5;
    private const int HomeGroup = 10, OtherGroup = 11;

    private async Task SeedAsync()
    {
        await TestClubs.SeedAsync(_db);
        _db.Groups.AddRange(new Group { Id = HomeGroup, Name = "Testdorf" }, new Group { Id = OtherGroup, Name = "Weiler" });
        _db.LeagueClubMembers.AddRange(new LeagueClubMember { ClubId = TestClubs.HomeId, GroupId = HomeGroup },
            new LeagueClubMember { ClubId = TestClubs.OtherId, GroupId = OtherGroup });
        foreach (var (id, admin) in new[] { (Admin, true), (HomeMember, false), (OtherMember, false), (Reader, false), (HomeMember2, false) })
            _db.AppUsers.Add(new AppUser { Id = id, Username = $"u{id}", Email = $"u{id}@t", PasswordHash = "x", IsAdmin = admin });
        _db.UserGroups.AddRange(new UserGroup { UserId = HomeMember, GroupId = HomeGroup }, new UserGroup { UserId = OtherMember, GroupId = OtherGroup },
            new UserGroup { UserId = Reader, GroupId = HomeGroup }, new UserGroup { UserId = HomeMember2, GroupId = HomeGroup });
        _db.LeagueRounds.Add(new LeagueRound { Tnr = Tnr, Round = Round, Date = new DateOnly(2026, 10, 11) });
        _db.LeagueRounds.Add(new LeagueRound { Tnr = Tnr, Round = 3, Date = new DateOnly(2026, 11, 8) });
        _db.LeagueMatches.AddRange(
            new LeagueMatch { Tnr = Tnr, Round = Round, MatchNo = 1, Home = "Testdorf", Away = "Bergheim", HomePts = 2.5, AwayPts = 1.5 },
            new LeagueMatch { Tnr = Tnr, Round = Round, MatchNo = 2, Home = "Talhausen", Away = "Seewinkel", HomePts = 2, AwayPts = 2 },
            new LeagueMatch { Tnr = Tnr, Round = 3, MatchNo = 1, Home = "Bergheim", Away = "Testdorf" });
        void Game(int match, int board, string home, string away, string? hp, string? ap, string color, string result, int forfeit = 0) =>
            _db.LeagueGames.Add(new LeagueGame { Tnr = Tnr, Round = Round, MatchNo = match, Board = board, HomeTeam = home, AwayTeam = away,
                HomePlayer = hp, AwayPlayer = ap, HomeColor = color, Result = result, Forfeit = forfeit, HomeElo = 1800 + board, AwayElo = 1700 + board });
        Game(1, 1, "Testdorf", "Bergheim", "Ackermann, Anna", "Brunner, Bert", "w", "1 - 0");
        Game(1, 2, "Testdorf", "Bergheim", "Clauss, Carl", "Dorn, Dora", "s", "½ - ½");
        Game(1, 3, "Testdorf", "Bergheim", "Eder, Emil", null, "w", "+ - -", forfeit: 1);
        Game(2, 1, "Talhausen", "Seewinkel", "Fink, Franz", "Gruber, Gerd", "w", "0 - 1");
        await _db.SaveChangesAsync();
    }

    private LeagueGameMoves Service() => new(_db, () => Now);

    private LeagueLineupsController Controller(int user, bool admin = false, int? club = null, params string[] perms)
    {
        var c = new LeagueLineupsController(Service(), new LeagueClubResolver(_db)).As(user, admin, club);
        var id = (ClaimsIdentity)c.ControllerContext.HttpContext.User.Identity!;
        foreach (var p in perms) id.AddClaim(new Claim(PermissionAuthorizationHandler.PermissionClaimType, p));
        return c;
    }

    // ---- Zugprüfung -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("e4 c5 Nf3 d6", "e4 c5 Nf3 d6")]
    [InlineData("1.e4 c5 2.Nf3 d6", "e4 c5 Nf3 d6")]
    [InlineData("1. e4 c5 2. Nf3 2... d6 *", "e4 c5 Nf3 d6")]
    [InlineData("1.e4 e5 2.Sf3 Sc6 3.Lb5 a6 4.La4 Sf6 5.0-0 Le7 6.Te1", "e4 e5 Nf3 Nc6 Bb5 a6 Ba4 Nf6 O-O Be7 Re1")]
    [InlineData("1.d4 d5 2.c4 dxc4 3.Dd1a4+", null)]
    [InlineData("1.e4 {Kommentar} e5 (1...c5 2.Nf3) 2.Nf3 1-0", "e4 e5 Nf3")]
    [InlineData("  ", "")]
    public void Parse_NormalizesNumbersGermanLettersAndComments(string text, string? expected)
    {
        var p = LeagueGameMoves.Parse(text);
        if (expected is null) { Assert.Null(p.Sans); return; }
        Assert.Equal(expected, string.Join(' ', p.Sans!));
    }

    [Fact]
    public void Parse_GermanPromotion()
    {
        Assert.Equal("Q", LeagueGameMoves.English("e8=D")[^1..]);
        Assert.Equal("exd8=N+", LeagueGameMoves.English("exd8=S+"));
        Assert.Equal("Bb5", LeagueGameMoves.English("Bb5"));   // englischer Läufer bleibt
    }

    [Fact]
    public void Parse_IllegalMove_NamesTheFirstWrongOneAsWritten()
    {
        var p = LeagueGameMoves.Parse("1.e4 e5 2.Sf3 Sc6 3.Ke3");
        Assert.Null(p.Sans);
        Assert.Equal(("illegal", "Ke3", 5), (p.Reason, p.Move, p.Ply));
    }

    [Fact]
    public void Parse_HalfMoveLimit()
    {
        var sixty = string.Join(' ', Enumerable.Repeat("Nf3 Nf6 Ng1 Ng8", 15));
        Assert.Equal(60, LeagueGameMoves.Parse(sixty).Sans!.Count);
        var p = LeagueGameMoves.Parse(sixty + " Nf3");
        Assert.Equal("tooLong", p.Reason);
    }

    // ---- Speichern + Rechte ---------------------------------------------------------------------------------

    [Fact]
    public async Task Save_OwnMatch_StoresEnglishSan_WithClubAndUser()
    {
        await SeedAsync();
        var r = await Controller(HomeMember, perms: Permissions.LeagueContribute)
            .SaveMoves(Tnr, Round, 1, 1, new() { Moves = "1.e4 c5 2.Sf3" }, default);
        Assert.Equal("e4 c5 Nf3", (string?)((OkObjectResult)r).Value!.GetType().GetProperty("moves")!.GetValue(((OkObjectResult)r).Value));
        var row = await _db.LeagueGameMoves.SingleAsync();
        Assert.Equal((Tnr, Round, 1, 1, "e4 c5 Nf3", TestClubs.HomeId, (int?)HomeMember, Now),
            (row.Tnr, row.Round, row.MatchNo, row.Board, row.Moves, row.ClubId, row.UpdatedByUserId, row.UpdatedAt));
    }

    [Fact]
    public async Task Save_IllegalMove_400WithMoveAndPly()
    {
        await SeedAsync();
        var r = await Controller(HomeMember, perms: Permissions.LeagueContribute)
            .SaveMoves(Tnr, Round, 1, 1, new() { Moves = "e4 e5 Ke3" }, default);
        var bad = Assert.IsType<BadRequestObjectResult>(r);
        var v = bad.Value!;
        Assert.Equal("illegal", v.GetType().GetProperty("reason")!.GetValue(v));
        Assert.Equal("Ke3", v.GetType().GetProperty("move")!.GetValue(v));
        Assert.Equal(3, v.GetType().GetProperty("ply")!.GetValue(v));
        Assert.Empty(_db.LeagueGameMoves);
    }

    [Fact]
    public async Task Save_UnknownPairing_404()
    {
        await SeedAsync();
        Assert.IsType<NotFoundResult>(await Controller(HomeMember, perms: Permissions.LeagueContribute)
            .SaveMoves(Tnr, Round, 1, 9, new() { Moves = "e4" }, default));
        Assert.IsType<NotFoundResult>(await Controller(Admin, admin: true, club: TestClubs.HomeId)
            .SaveMoves(Tnr, 7, 1, 1, new() { Moves = "e4" }, default));
    }

    [Fact]
    public async Task Save_ForeignMatch_403ForContributor_OkForManager()
    {
        await SeedAsync();
        var r = await Controller(HomeMember, perms: Permissions.LeagueContribute)
            .SaveMoves(Tnr, Round, 2, 1, new() { Moves = "d4" }, default);
        Assert.Equal(403, ((ObjectResult)r).StatusCode);
        Assert.IsType<OkObjectResult>(await Controller(HomeMember, perms: [Permissions.LeagueContribute, Permissions.LeagueManage])
            .SaveMoves(Tnr, Round, 2, 1, new() { Moves = "d4" }, default));
        Assert.IsType<OkObjectResult>(await Controller(Admin, admin: true, club: TestClubs.HomeId)
            .SaveMoves(Tnr, Round, 2, 1, new() { Moves = "d4 d5" }, default));
    }

    [Fact]
    public async Task Save_OtherClubsMember_CannotWriteOurMatch_NorUseOurClub()
    {
        await SeedAsync();
        // als SK Weiler: Begegnung Testdorf – Bergheim ist fremd
        var r = await Controller(OtherMember, perms: Permissions.LeagueContribute)
            .SaveMoves(Tnr, Round, 1, 1, new() { Moves = "e4" }, default);
        Assert.Equal(403, ((ObjectResult)r).StatusCode);
        // ?club= eines Vereins, zu dem das Konto nicht gehört
        var foreign = await Controller(OtherMember, club: TestClubs.HomeId, perms: Permissions.LeagueContribute)
            .SaveMoves(Tnr, Round, 1, 1, new() { Moves = "e4" }, default);
        Assert.Equal(403, ((ObjectResult)foreign).StatusCode);
        Assert.Empty(_db.LeagueGameMoves);
    }

    [Fact]
    public async Task Save_SomeoneElsesEntry_OnlyManagerMayChangeOrDelete()
    {
        await SeedAsync();
        await Controller(HomeMember, perms: Permissions.LeagueContribute).SaveMoves(Tnr, Round, 1, 1, new() { Moves = "e4" }, default);
        var r = await Controller(HomeMember2, perms: Permissions.LeagueContribute).SaveMoves(Tnr, Round, 1, 1, new() { Moves = "d4" }, default);
        Assert.Equal(403, ((ObjectResult)r).StatusCode);
        Assert.Equal(403, ((ObjectResult)await Controller(HomeMember2, perms: Permissions.LeagueContribute)
            .DeleteMoves(Tnr, Round, 1, 1, default)).StatusCode);
        // der Eintragende selbst ändert und löscht (leere Züge)
        Assert.IsType<OkObjectResult>(await Controller(HomeMember, perms: Permissions.LeagueContribute)
            .SaveMoves(Tnr, Round, 1, 1, new() { Moves = "1.e4 e5" }, default));
        Assert.Equal("e4 e5", (await _db.LeagueGameMoves.SingleAsync()).Moves);
        Assert.IsType<OkObjectResult>(await Controller(HomeMember, perms: Permissions.LeagueContribute)
            .SaveMoves(Tnr, Round, 1, 1, new() { Moves = "" }, default));
        Assert.Empty(_db.LeagueGameMoves);
        // ein Verwalter löscht einen fremden Eintrag
        await Controller(HomeMember, perms: Permissions.LeagueContribute).SaveMoves(Tnr, Round, 1, 1, new() { Moves = "c4" }, default);
        Assert.IsType<NoContentResult>(await Controller(HomeMember2, perms: [Permissions.LeagueContribute, Permissions.LeagueManage])
            .DeleteMoves(Tnr, Round, 1, 1, default));
        Assert.Empty(_db.LeagueGameMoves);
    }

    [Fact]
    public async Task Save_ForfeitBoard_400NoGame()
    {
        await SeedAsync();
        var r = await Controller(HomeMember, perms: Permissions.LeagueContribute).SaveMoves(Tnr, Round, 1, 3, new() { Moves = "e4" }, default);
        Assert.Equal("noGame", ((BadRequestObjectResult)r).Value!.GetType().GetProperty("reason")!.GetValue(((BadRequestObjectResult)r).Value));
    }

    [Fact]
    public async Task Moves_SurviveRefreshOfLeagueGames_NaturalKey()
    {
        await SeedAsync();
        await Controller(HomeMember, perms: Permissions.LeagueContribute).SaveMoves(Tnr, Round, 1, 2, new() { Moves = "d4 Nf6" }, default);
        // „Daten aktualisieren" legt die Zeile neu an (andere Id) — die Züge hängen am Schlüssel, nicht an der Id
        var old = await _db.LeagueGames.SingleAsync(g => g.MatchNo == 1 && g.Board == 2);
        _db.LeagueGames.Remove(old);
        _db.LeagueGames.Add(new LeagueGame { Tnr = Tnr, Round = Round, MatchNo = 1, Board = 2, HomeTeam = "Testdorf", AwayTeam = "Bergheim",
            HomePlayer = "Clauss, Carl", AwayPlayer = "Dorn, Dora", HomeColor = "s", Result = "½ - ½" });
        await _db.SaveChangesAsync();
        var l = await Service().LineupsAsync(TestClubs.Home, Tnr, Round, Reader, false, false);
        Assert.Equal("d4 Nf6", l!.Matches[0].Boards.Single(b => b.Board == 2).Moves);
    }

    // ---- Aufstellungen lesen --------------------------------------------------------------------------------

    [Fact]
    public async Task Lineups_AllMatchesOfTheRound_WithMovesAndEditFlags()
    {
        await SeedAsync();
        await Controller(HomeMember, perms: Permissions.LeagueContribute).SaveMoves(Tnr, Round, 1, 1, new() { Moves = "e4 c5" }, default);
        var ok = Assert.IsType<OkObjectResult>(await Controller(HomeMember, perms: [Permissions.LeagueView, Permissions.LeagueContribute])
            .Lineups(Tnr, Round, default));
        var l = Assert.IsType<LeagueGameMoves.Lineups>(ok.Value);
        Assert.Equal("2026-10-11", l.Date);
        Assert.True(l.CanEdit);
        Assert.Equal(new[] { "Testdorf", "Talhausen" }, l.Matches.Select(m => m.Home));
        var own = l.Matches[0];
        Assert.True(own.Own);
        Assert.Equal((2.5, 1.5), (own.HomePts, own.AwayPts));
        Assert.Equal(new[] { 1, 2, 3 }, own.Boards.Select(b => b.Board));
        Assert.Equal("e4 c5", own.Boards[0].Moves);
        Assert.Equal(("Ackermann, Anna", 1801, "w", "1 - 0"), (own.Boards[0].HomePlayer, own.Boards[0].HomeElo, own.Boards[0].HomeColor, own.Boards[0].Result));
        Assert.Equal(new[] { true, true, false }, own.Boards.Select(b => b.CanEditMoves));   // Brett 3 kampflos
        Assert.False(l.Matches[1].Own);
        Assert.False(l.Matches[1].Boards[0].CanEditMoves);   // fremde Begegnung
    }

    [Fact]
    public async Task Lineups_CarryFideIdsOfThePairing_EmptyIsNull()
    {
        // 0.727.2: der Name in der Aufstellung öffnet die Spielerkarte — dafür braucht die Oberfläche die FIDE-IDs der Paarung.
        await SeedAsync();
        var g1 = await _db.LeagueGames.SingleAsync(g => g.MatchNo == 1 && g.Board == 1);
        g1.HomeFide = "1610001";
        g1.AwayFide = " ";
        var g2 = await _db.LeagueGames.SingleAsync(g => g.MatchNo == 1 && g.Board == 2);
        g2.AwayFide = "1610002";
        await _db.SaveChangesAsync();
        var l = (await Service().LineupsAsync(TestClubs.Home, Tnr, Round, Reader, false, false))!;
        var b = l.Matches[0].Boards;
        Assert.Equal(("1610001", (string?)null), (b[0].HomeFide, b[0].AwayFide));
        Assert.Equal(((string?)null, "1610002"), (b[1].HomeFide, b[1].AwayFide));
        Assert.Null(l.Matches[1].Boards[0].HomeFide);
    }

    [Fact]
    public async Task Lineups_Reader_SeesMoves_ButCannotEdit_OthersEntryNotEditable()
    {
        await SeedAsync();
        await Controller(HomeMember, perms: Permissions.LeagueContribute).SaveMoves(Tnr, Round, 1, 1, new() { Moves = "e4" }, default);
        var reader = (LeagueGameMoves.Lineups)((OkObjectResult)await Controller(Reader, perms: Permissions.LeagueView).Lineups(Tnr, Round, default)).Value!;
        Assert.False(reader.CanEdit);
        Assert.Equal("e4", reader.Matches[0].Boards[0].Moves);
        Assert.All(reader.Matches.SelectMany(m => m.Boards), b => Assert.False(b.CanEditMoves));
        var other = (LeagueGameMoves.Lineups)((OkObjectResult)await Controller(HomeMember2, perms: Permissions.LeagueContribute).Lineups(Tnr, Round, default)).Value!;
        Assert.False(other.Matches[0].Boards[0].CanEditMoves);   // trug HomeMember ein
        Assert.True(other.Matches[0].Boards[1].CanEditMoves);
    }

    [Fact]
    public async Task Lineups_FutureRound_MatchWithoutBoards_UnknownRound404()
    {
        await SeedAsync();
        var l = (LeagueGameMoves.Lineups)((OkObjectResult)await Controller(HomeMember, perms: Permissions.LeagueView).Lineups(Tnr, 3, default)).Value!;
        var m = Assert.Single(l.Matches);
        Assert.Equal(("Bergheim", "Testdorf"), (m.Home, m.Away));
        Assert.Empty(m.Boards);
        Assert.IsType<NotFoundResult>(await Controller(HomeMember, perms: Permissions.LeagueView).Lineups(Tnr, 9, default));
    }

    // ---- Partie je Brett (0.724.0: „wenn ich die Partie hab, nicht Züge eingeben lassen, sondern die Partie ausweisen") -----

    private const string GamePgn = "[White \"Ackermann, Anna\"]\n[Black \"Brunner, Bert\"]\n[Result \"1-0\"]\n\n"
        + "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be2 e5 7. Nb3 Be7 8. O-O O-O 9. Be3 Be6 10. Qd2 Nbd7 11. a4 Qc7 1-0";

    private async Task<LeagueClubGame> LinkedClubGameAsync(int clubId = TestClubs.HomeId, DateTime? archived = null)
    {
        var lg = await _db.LeagueGames.SingleAsync(g => g.MatchNo == 1 && g.Board == 1);
        var c = new LeagueClubGame { ClubId = clubId, Year = 2026, White = "Ackermann, Anna", Black = "Brunner, Bert", Result = "1-0",
            Plies = 22, Pgn = GamePgn, MovesHash = "h1", UploadedByUserId = HomeMember, LeagueGameId = lg.Id, ArchivedAt = archived };
        LeagueGameLinks.Set(c, lg);
        _db.LeagueClubGames.Add(c);
        await _db.SaveChangesAsync();
        return c;
    }

    private async Task<LeagueGameMoves.Lineups> LineupsAs(int user, params string[] perms) =>
        (LeagueGameMoves.Lineups)((OkObjectResult)await Controller(user, perms: perms).Lineups(Tnr, Round, default)).Value!;

    // 0.739.0, Wunsch 2026-10-10: „die laufende Aufstellung schon sehen — weiß ja die Paarungen" — chess-results hat das Brett
    // noch leer, eine Vereinspartie ist ihm zugeordnet → Spieler aus ihr; die eigene Seite nur für angemeldete Mitglieder.
    [Fact]
    public async Task Lineups_EmptyBoardWithAssignedClubGame_ProvisionalFromTheGame_OwnSideOnlyForMembers()
    {
        await SeedAsync();
        var lg = new LeagueGame { Tnr = Tnr, Round = Round, MatchNo = 1, Board = 4, HomeTeam = "Testdorf", AwayTeam = "Bergheim",
            HomeColor = "w", Result = "" };
        _db.LeagueGames.Add(lg);
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = Tnr, Team = "Bergheim", Name = "Ober, Olga", FideId = "222", EloI = 1900 });
        await _db.SaveChangesAsync();
        // Testdorf hatte an Brett 4 Schwarz — die Partie: Ober (Bergheim) mit Weiß gegen „Testdorf" (intern: Hess)
        var c = new LeagueClubGame { ClubId = TestClubs.HomeId, Year = 2026, White = "Ober, Olga", WhiteFide = "222", Black = "Testdorf",
            BlackRealName = "Hess, Max", BlackRealFide = "111", Anonymized = true, Result = "0-1", Plies = 22, Pgn = GamePgn,
            MovesHash = "h4", UploadedByUserId = HomeMember };
        LeagueGameLinks.Set(c, lg);
        _db.LeagueClubGames.Add(c);
        await _db.SaveChangesAsync();

        var b = (await LineupsAs(HomeMember, Permissions.LeagueView)).Matches[0].Boards.Single(x => x.Board == 4);
        Assert.Equal(("Hess, Max", "111", "Ober, Olga", "222", (int?)1900, "s", "1 - 0", true),
            (b.HomePlayer, b.HomeFide, b.AwayPlayer, b.AwayFide, b.AwayElo, b.HomeColor, b.Result, b.Provisional));
        Assert.Equal(c.Id, b.Game!.ClubGameId);

        // über einen Teilen-Link (revealOwn aus) bleibt die eigene Seite „Testdorf"
        var fixtures = new LeagueFixtureGames(_db);
        var shared = (await fixtures.ForFixtureAsync(TestClubs.Home, Tnr, Round, "Testdorf", default)).Single(p => p.Board == 4);
        Assert.Equal(("Ober, Olga", "Testdorf", "0 - 1", true), (shared.White, shared.Black, shared.Result, shared.Provisional));
        Assert.Null(shared.BlackFide);
        var member = (await fixtures.ForFixtureAsync(TestClubs.Home, Tnr, Round, "Testdorf", default, HomeMember, revealOwn: true))
            .Single(p => p.Board == 4);
        Assert.Equal("Hess, Max", member.Black);
    }

    [Fact]
    public async Task Lineups_LinkedClubGame_ShownAsGame_NoMoveEntry_OldEntryKept()
    {
        await SeedAsync();
        await Controller(HomeMember, perms: Permissions.LeagueContribute).SaveMoves(Tnr, Round, 1, 1, new() { Moves = "e4 c5" }, default);
        var c = await LinkedClubGameAsync();
        var l = await LineupsAs(HomeMember, Permissions.LeagueView, Permissions.LeagueContribute);
        var board = l.Matches[0].Boards[0];
        var g = Assert.IsType<LeagueGameMoves.LineupGame>(board.Game);
        Assert.Equal(("club", (int?)c.Id, 22, "1-0", "Ackermann, Anna", "Brunner, Bert", true),
            (g.Source, g.ClubGameId, g.Plies, g.Result, g.White, g.Black, g.CanEdit));
        Assert.Equal("e4 c5 Nf3 d6 d4 cxd4 Nxd4 Nf6 Nc3 a6", string.Join(' ', g.FirstMoves));
        Assert.False(board.CanEditMoves);          // keine Zug-Eingabe bei vorhandener Partie
        Assert.Equal("e4 c5", board.Moves);       // alter Handeintrag kommt weiter mit
        Assert.True(board.CanDeleteMoves);        // … und darf von dem, der ihn eintrug, gelöscht werden
        Assert.Null(l.Matches[0].Boards[1].Game);
        Assert.True(l.Matches[0].Boards[1].CanEditMoves);
        // ein anderer Beitragender des Vereins sieht die Partie, darf sie aber nicht bearbeiten (nicht hochgeladen)
        var other = await LineupsAs(HomeMember2, Permissions.LeagueView, Permissions.LeagueContribute);
        Assert.False(other.Matches[0].Boards[0].Game!.CanEdit);
        Assert.False(other.Matches[0].Boards[0].CanDeleteMoves);   // fremder Eintrag
    }

    [Fact]
    public async Task Lineups_ClubGame_OtherClubDoesNotSeeIt_ArchivedNeverCounts()
    {
        await SeedAsync();
        await LinkedClubGameAsync();
        var foreign = await LineupsAs(OtherMember, Permissions.LeagueView);
        Assert.All(foreign.Matches.SelectMany(m => m.Boards), b => Assert.Null(b.Game));

        _db.LeagueClubGames.Single().ArchivedAt = Now;
        await _db.SaveChangesAsync();
        var l = await LineupsAs(HomeMember, Permissions.LeagueView, Permissions.LeagueContribute);
        Assert.Null(l.Matches[0].Boards[0].Game);
        Assert.True(l.Matches[0].Boards[0].CanEditMoves);
    }

    [Fact]
    public async Task Lineups_ProfileGame_NearRoundDate_SourceProfile()
    {
        await SeedAsync();
        var lg = await _db.LeagueGames.SingleAsync(g => g.MatchNo == 2 && g.Board == 1);
        (lg.HomeFide, lg.AwayFide) = ("9911", "9922");
        _db.LeaguePlayerProfiles.Add(new LeaguePlayerProfile { FideId = "9922", Name = "Gruber, Gerd",
            Pgn = "[Event \"Liga\"]\n[Date \"2026.10.12\"]\n[White \"Fink, Franz\"]\n[Black \"Gruber, Gerd\"]\n[Result \"0-1\"]\n\n1. d4 d5 2. c4 e6 0-1\n\n" });
        await _db.SaveChangesAsync();
        var l = await LineupsAs(Reader, Permissions.LeagueView);
        var g = Assert.IsType<LeagueGameMoves.LineupGame>(l.Matches[1].Boards[0].Game);
        Assert.Equal(("profile", (int?)null, 4, "0-1", false), (g.Source, g.ClubGameId, g.Plies, g.Result, g.CanEdit));
        Assert.Equal(new[] { "d4", "d5", "c4", "e6" }, g.FirstMoves);
    }

    [Fact]
    public async Task ForGames_GuessedClubGame_GoesToOneBoardOnly()
    {
        await SeedAsync();
        // zweite Brettpaarung mit denselben Nachnamen in einer anderen Begegnung — die geratene Partie gehört nur EINEM Brett
        _db.LeagueGames.Add(new LeagueGame { Tnr = Tnr, Round = Round, MatchNo = 2, Board = 2, HomeTeam = "Talhausen", AwayTeam = "Seewinkel",
            HomePlayer = "Ackermann, Arno", AwayPlayer = "Brunner, Berta", HomeColor = "w", Result = "1 - 0" });
        _db.LeagueClubGames.Add(new LeagueClubGame { ClubId = TestClubs.HomeId, Year = 2026, White = "Ackermann, Anna", Black = "Brunner, Bert",
            Result = "1-0", Plies = 22, Pgn = GamePgn, MovesHash = "h2" });
        await _db.SaveChangesAsync();
        var games = await _db.LeagueGames.Where(g => g.Tnr == Tnr && g.Round == Round).ToListAsync();
        var found = await new LeagueFixtureGames(_db).ForGamesAsync(TestClubs.Home, Tnr, Round, games, default);
        Assert.Single(found.Values, p => p.Source == "club");
    }

    // ---- Rechte an den Endpunkten (Attribute wertet ein direkter Controller-Aufruf nicht aus) ------------------

    [Theory]
    [InlineData(nameof(LeagueLineupsController.Lineups), "GET", Permissions.LeagueView)]
    [InlineData(nameof(LeagueLineupsController.SaveMoves), "PUT", Permissions.LeagueContribute)]
    [InlineData(nameof(LeagueLineupsController.DeleteMoves), "DELETE", Permissions.LeagueContribute)]
    public void Endpoints_CarryTheirPermission(string action, string verb, string permission)
    {
        var m = typeof(LeagueLineupsController).GetMethod(action)!;
        Assert.Contains(verb, m.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods));
        var perm = Assert.Single(m.GetCustomAttributes<HasPermissionAttribute>());
        Assert.Equal(PermissionPolicyProvider.Prefix + permission, perm.Policy);
        Assert.NotNull(typeof(LeagueLineupsController).GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
    }
}
