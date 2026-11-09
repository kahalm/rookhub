using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Authorization;
using RookHub.Api.Controllers.League;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests.League;

/// <summary>Link auf EINE Ligapartie (2026-11-09): nur diese Partie, Ablauf, Marke, Anonymisierung, Recht. Spieler erfunden.</summary>
public class LeagueGameSharesTests : IDisposable
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private const int Tnr = 4711, Round = 2;
    private const string Pgn = "[White \"Ackermann, Anna\"]\n[Black \"Brunner, Bert\"]\n[Result \"1-0\"]\n\n1. e4 e5 1-0";
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
    private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();
    public void Dispose() => _db.Dispose();

    private LeagueGameShares Shares() => new(_db, new LeagueFixtureGames(_db), _protection, _clock);

    private void Board(int board, string home, string? away, string result = "1 - 0", int forfeit = 0, int match = 1) =>
        _db.LeagueGames.Add(new LeagueGame { Tnr = Tnr, Round = Round, MatchNo = match, Board = board, HomeTeam = "Testdorf", AwayTeam = "Bergheim",
            HomePlayer = home, AwayPlayer = away, HomeColor = "w", Result = result, Forfeit = forfeit, HomeElo = 1800, AwayElo = 1700 });

    private async Task<LeagueClubGame> SeedAsync(bool anonymized = false)
    {
        await TestClubs.SeedAsync(_db);
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = Tnr, Name = "Landesliga", Season = "2026/27" });
        _db.LeagueRounds.Add(new LeagueRound { Tnr = Tnr, Round = Round, Date = new DateOnly(2026, 10, 11) });
        Board(1, "Ackermann, Anna", "Brunner, Bert");
        Board(2, "Clauss, Carl", "Dorn, Dora");            // keine Partie
        Board(3, "Eder, Emil", null, "+ - -", forfeit: 1);  // kampflos
        var c = new LeagueClubGame { ClubId = TestClubs.HomeId, Year = 2026, White = anonymized ? "Testdorf" : "Ackermann, Anna", Black = "Brunner, Bert",
            WhiteRealFide = anonymized ? "1" : null, WhiteRealName = anonymized ? "Ackermann, Anna" : null, Anonymized = anonymized, Result = "1-0", Plies = 2, Pgn = Pgn, MovesHash = "h" };
        _db.LeagueClubGames.Add(c);
        await _db.SaveChangesAsync();
        var g = await _db.LeagueGames.SingleAsync(x => x.Board == 1);
        c.LeagueGameId = g.Id;
        await _db.SaveChangesAsync();
        return c;
    }

    [Fact]
    public async Task CreateAndRead_ShowsOnlyThatGame_ExpiresThreeMonthsAfterToday()
    {
        await SeedAsync();
        var made = await Shares().CreateAsync(TestClubs.Home, Tnr, Round, "Bergheim", 1, default);   // auch über die Gastmannschaft
        Assert.Equal(new DateOnly(2027, 2, 9), made!.Value.Expires);
        var g = await Shares().ReadAsync(made.Value.Token, default);
        Assert.Equal(("Ackermann, Anna", "Brunner, Bert", Pgn, 1, "Testdorf", "2027-02-09"), (g!.White, g.Black, g.Pgn, g.Board!.Value, g.Home!, g.Expires));
        Assert.Equal("Landesliga", g.League);
    }

    [Theory]
    [InlineData(2)]   // Paarung ohne Partie
    [InlineData(3)]   // kampflos
    [InlineData(9)]   // Brett gibt es nicht
    public async Task Create_WithoutGameAtBoard_IsRefused(int board)
    {
        await SeedAsync();
        Assert.Null(await Shares().CreateAsync(TestClubs.Home, Tnr, Round, "Testdorf", board, default));
    }

    [Fact]
    public async Task Read_ExpiredTamperedOrForeignKeyring_IsNull()
    {
        await SeedAsync();
        var token = (await Shares().CreateAsync(TestClubs.Home, Tnr, Round, "Testdorf", 1, default))!.Value.Token;
        Assert.NotNull(await Shares().ReadAsync(token, default));
        Assert.Null(await Shares().ReadAsync("x" + token[1..], default));
        Assert.Null(await Shares().ReadAsync("nix!", default));
        Assert.Null(await new LeagueGameShares(_db, new LeagueFixtureGames(_db), new EphemeralDataProtectionProvider(), _clock).ReadAsync(token, default));
        _clock.Now = new DateTimeOffset(2027, 2, 9, 23, 0, 0, TimeSpan.Zero);
        Assert.NotNull(await Shares().ReadAsync(token, default));                                     // letzter gültiger Tag
        _clock.Now = new DateTimeOffset(2027, 2, 10, 0, 0, 0, TimeSpan.Zero);
        Assert.Null(await Shares().ReadAsync(token, default));
        Assert.IsType<NotFoundResult>(await new LeagueGameShareController(Shares()).Get(token, default));
    }

    [Fact]
    public async Task Read_GameMovedOrArchived_DoesNotShowAnother()
    {
        var c = await SeedAsync();
        var token = (await Shares().CreateAsync(TestClubs.Home, Tnr, Round, "Testdorf", 1, default))!.Value.Token;
        // Die Partie wandert ab; an Brett 1 hängt jetzt eine andere Vereinspartie.
        var other = await _db.LeagueGames.SingleAsync(x => x.Board == 2);
        c.LeagueGameId = other.Id;
        var replacement = new LeagueClubGame { ClubId = TestClubs.HomeId, Year = 2026, White = "Ackermann, Anna", Black = "Brunner, Bert", Result = "1-0", Plies = 2,
            Pgn = "1. d4 d5 1-0", MovesHash = "r", LeagueGameId = (await _db.LeagueGames.SingleAsync(x => x.Board == 1)).Id };
        _db.LeagueClubGames.Add(replacement);
        await _db.SaveChangesAsync();
        Assert.Null(await Shares().ReadAsync(token, default));
    }

    [Fact]
    public async Task Read_AnonymizedClubGame_KeepsNamesAnonymous_NoBoardNoTeams()
    {
        await SeedAsync(anonymized: true);
        var token = (await Shares().CreateAsync(TestClubs.Home, Tnr, Round, "Testdorf", 1, default))!.Value.Token;
        var g = await Shares().ReadAsync(token, default);
        Assert.Equal(("Testdorf", (int?)null, "Brunner, Bert", (int?)1700), (g!.White!, g.WhiteElo, g.Black!, g.BlackElo));
        Assert.Equal(((int?)null, (string?)null, (string?)null), (g.Board, g.Home, g.Away));
        Assert.DoesNotContain("Ackermann", System.Text.Json.JsonSerializer.Serialize(g with { Pgn = "" }));
    }

    [Fact]
    public void Endpoints_CreateNeedsLeagueView_ReadIsAnonymous()
    {
        var create = typeof(LeagueLineupsController).GetMethod(nameof(LeagueLineupsController.CreateGameShare))!;
        Assert.Contains(PermissionPolicyProvider.Prefix + Permissions.LeagueView, create.GetCustomAttribute<HasPermissionAttribute>()!.Policy!);
        Assert.NotNull(typeof(LeagueGameShareController).GetCustomAttribute<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>());
    }

    [Fact]
    public async Task Create_ByUserOutsideAnyClub_IsNotOk()
    {
        await SeedAsync();
        _db.AppUsers.Add(new AppUser { Id = 77, Username = "u", Email = "u@t", PasswordHash = "x" });
        await _db.SaveChangesAsync();
        var c = new LeagueLineupsController(new LeagueGameMoves(_db, () => DateTime.UtcNow), new LeagueClubResolver(_db)).As(77);
        var r = await c.CreateGameShare(new(Tnr, Round, "Testdorf", 1), Shares(), default);
        Assert.IsNotType<OkObjectResult>(r);
    }
}
