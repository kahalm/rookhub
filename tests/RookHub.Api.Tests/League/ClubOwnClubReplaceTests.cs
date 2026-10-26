using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs.League;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests.League;

/// <summary>
/// „Spieler des eigenen Vereins werden durch den Vereinsnamen ersetzt" gilt auf ALLEN drei Wegen in die Vereins-Datenbank
/// (Codereview 2026-09-29 F7-008, Entscheidung 2026-10-25 „a: überall erzwingen"): PGN-Import (mit und ohne Konto),
/// einzelne Partie/Formular-Übernahme (<c>AddGame</c>) und Korrigieren. Vorher erzwang nur das Korrigieren die Ersetzung;
/// beim Hochladen genügte <c>replace:false</c>, und der Spieler stand mit Name, FIDE-ID und Elo in der Datenbank — obwohl
/// die Teilen-Seite „wird immer ersetzt" zusichert. Dieselbe Seite ergibt jetzt auf jedem Weg dieselbe Zeile.
/// </summary>
public class ClubOwnClubReplaceTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly DateTime Now = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
    public void Dispose() => _db.Dispose();

    private LeagueClubService Club() => new(_db, NullLogger<LeagueClubService>.Instance, () => Now);

    private const string Moves = "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be3 e5 7. Nb3 Be6 8. f3 Be7 9. Qd2 O-O 1-0";

    /// <summary>„Binder" spielt für den eigenen Verein (Mannschaft Testdorf), „Hengl" für einen fremden.</summary>
    private async Task<int> SeedAsync()
    {
        foreach (var (team, name, fide) in new[] { ("Testdorf", "Binder, Moriz", "111"), ("Absam", "Hengl, Philip", "222") })
            _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 7, Team = team, Name = name, NameKey = LeagueNames.NameKey(name), FideId = fide });
        var u = new AppUser { Username = "uploader", Email = "u@test", PasswordHash = "x" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u.Id;
    }

    private static string Pgn(string white, string black) =>
        $"[Event \"Vereinsmeisterschaft\"]\n[Date \"2024.05.12\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n[WhiteElo \"1900\"]\n[Result \"1-0\"]\n\n{Moves}\n";

    private static List<LeagueClubImportGameDecision> KeepNames() => new()
    {
        new() { Index = 1, White = new() { Replace = false }, Black = new() { Replace = false } },
    };

    /// <summary>Die eigene Seite ist ersetzt: Vereinsname, keine FIDE-ID, keine Elo, keine Veranstaltung.</summary>
    private static void AssertOwnSideReplaced(LeagueClubGame g)
    {
        Assert.Equal((TestClubs.Home.AnonName, (string?)null, (int?)null), (g.White, g.WhiteFide, g.WhiteElo));
        Assert.Equal(("Hengl, Philip", "222"), (g.Black, g.BlackFide));
        Assert.True(g.Anonymized);
        Assert.Null(g.Event);
    }

    [Fact]
    public async Task Import_WithAccount_ReplaceFalse_StillReplacesTheOwnClubPlayer()
    {
        var me = await SeedAsync();
        var r = await Club().ImportPgnAsync(TestClubs.Home, me, Pgn("Binder, Moriz", "Hengl, Philip"), KeepNames());
        Assert.Equal(1, r.Added);
        AssertOwnSideReplaced(await _db.LeagueClubGames.SingleAsync());
    }

    [Fact]
    public async Task Import_WithoutAccount_ReplaceFalse_StillReplacesTheOwnClubPlayer()
    {
        await SeedAsync();
        var r = await Club().ImportPgnAsync(TestClubs.Home, null, Pgn("Binder, Moriz", "Hengl, Philip"), KeepNames());
        Assert.Equal(1, r.Added);
        AssertOwnSideReplaced(await _db.LeagueClubGames.SingleAsync());
    }

    [Fact]
    public async Task Import_ChosenByFide_ReplaceFalse_StillReplacesTheOwnClubPlayer()
    {
        var me = await SeedAsync();
        var r = await Club().ImportPgnAsync(TestClubs.Home, me, Pgn("Unbekannt, X", "Hengl, Philip"), new List<LeagueClubImportGameDecision>
        {
            new() { Index = 1, White = new() { Fide = "111", Replace = false }, Black = new() },
        });
        Assert.Equal(1, r.Added);
        AssertOwnSideReplaced(await _db.LeagueClubGames.SingleAsync());
    }

    [Fact]
    public async Task AddGame_ScoresheetOrManual_ReplaceFalse_StillReplacesTheOwnClubPlayer()
    {
        var me = await SeedAsync();
        var (game, reason, _) = await Club().AddGameAsync(TestClubs.Home, me, new LeagueClubGameRequest
        {
            Moves = new() { "e4", "c5", "Nf3" }, White = "Binder, Moriz", WhiteElo = 1900, Black = "Hengl, Philip",
            WhiteReplace = false, BlackReplace = false, Event = "Vereinsmeisterschaft",
        });
        Assert.Null(reason);
        AssertOwnSideReplaced(game!);
    }

    [Fact]
    public async Task Update_ToAnOwnClubPlayer_ReplaceFalse_Replaces_LikeTheOtherWays()
    {
        var me = await SeedAsync();
        var club = Club();
        await club.ImportPgnAsync(TestClubs.Home, me, Pgn("Gast, Ein", "Hengl, Philip"), null);
        var g = await _db.LeagueClubGames.AsNoTracking().SingleAsync();
        Assert.False(g.Anonymized);

        var (updated, reason) = await club.UpdateAsync(TestClubs.Home, me, false, g.Id,
            new LeagueClubGameUpdateRequest { White = new() { Fide = "111", Replace = false } });
        Assert.Null(reason);
        Assert.Equal((TestClubs.Home.AnonName, (string?)null), (updated!.White, updated.WhiteFide));
        Assert.True(updated.Anonymized);
    }

    [Fact]
    public async Task ForeignPlayer_ReplaceFalse_KeepsTheName()
    {
        var me = await SeedAsync();
        var (game, reason, _) = await Club().AddGameAsync(TestClubs.Home, me, new LeagueClubGameRequest
        {
            Moves = new() { "e4", "c5", "Nf3" }, White = "Gast, Ein", Black = "Hengl, Philip", WhiteReplace = false, BlackReplace = false,
        });
        Assert.Null(reason);
        Assert.Equal(("Gast, Ein", "Hengl, Philip", false), (game!.White, game.Black, game.Anonymized));
    }
}
