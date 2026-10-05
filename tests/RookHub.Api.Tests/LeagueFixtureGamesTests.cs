using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>Paarungen einer gespielten Begegnung samt Partie (0.673.0).</summary>
public class LeagueFixtureGamesTests : IDisposable
{
    private readonly AppDbContext _db;

    public LeagueFixtureGamesTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _db.LeagueRounds.Add(new LeagueRound { Tnr = 1, Round = 2, Date = new DateOnly(2026, 10, 4) });
        // Brett 1: Heim Weiß, Vereinspartie mit „Schwaz"; Brett 2: Heim Schwarz, Partie in der Spielerkarte;
        // Brett 3: keine Partie; Brett 4: kampflos.
        _db.LeagueGames.AddRange(
            Board(1, "Hess, Max", "24656666", "Binder, Moriz", "1616951", "w", "½ - ½"),
            Board(2, "Kruckenhauser, Arthur", "1642812", "Tafertshofer, Matthias", "1270012", "s", "½ - ½"),
            Board(3, "Ciolek, Andreas", "12906727", "Gruber, Michael", "1611143", "w", "1 - 0"),
            Board(4, "Stichter, Constantin", "34603140", null, null, "w", "+ - -", forfeit: 1),
            new LeagueGame { Tnr = 1, Round = 2, Board = 1, HomeTeam = "Andere", AwayTeam = "Fremde", HomePlayer = "X", AwayPlayer = "Y",
                HomeColor = "w", Result = "1 - 0" });
        _db.LeagueClubGames.AddRange(
            new LeagueClubGame { Id = 10, Year = 2026, White = "Hess, Max", WhiteFide = "24656666", Black = "Schwaz",
                Pgn = "[White \"Hess, Max\"]\n[Black \"Schwaz\"]\n\n1. e4 e5 1/2-1/2", MovesHash = "a" },
            new LeagueClubGame { Id = 11, Year = 2025, White = "Ciolek, Andreas", WhiteFide = "12906727", Black = "Schwaz",
                Pgn = "alt", MovesHash = "b" },   // anderes Jahr
            new LeagueClubGame { Id = 12, Year = 2026, White = "Schwaz", Black = "Ciolek, Andreas", BlackFide = "12906727",
                Pgn = "falsche Farbe", MovesHash = "c" });
        _db.LeaguePlayerProfiles.Add(new LeaguePlayerProfile { FideId = "1270012", Name = "Tafertshofer, Matthias",
            Pgn = "[Event \"TMM\"]\n[Date \"2026.10.04\"]\n[White \"Tafertshofer, Matthias\"]\n[Black \"Kruckenhauser, Arthur\"]\n[Result \"1/2-1/2\"]\n\n1. d4 d5 1/2-1/2\n\n"
                + "[Event \"Alt\"]\n[Date \"2025.10.04\"]\n[White \"Tafertshofer, Matthias\"]\n[Black \"Kruckenhauser, Arthur\"]\n[Result \"1-0\"]\n\n1. c4 1-0\n" });
        _db.SaveChanges();
    }

    private static LeagueGame Board(int b, string home, string? hf, string? away, string? af, string color, string result, int forfeit = 0) =>
        new() { Tnr = 1, Round = 2, Board = b, HomeTeam = "Schach Ohne Grenzen", AwayTeam = "Schwaz", HomePlayer = home, HomeFide = hf,
            AwayPlayer = away, AwayFide = af, HomeColor = color, Result = result, Forfeit = forfeit, HomeElo = 1800 + b };

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ForFixture_paarungenMitFarbenUndPartien()
    {
        var list = await new LeagueFixtureGames(_db).ForFixtureAsync(1, 2, "Schwaz", default);

        Assert.Equal(new[] { 1, 2, 3, 4 }, list.Select(p => p.Board));
        Assert.Equal(("Hess, Max", "Binder, Moriz", "club", (int?)10), (list[0].White, list[0].Black, list[0].Source, list[0].ClubGameId));
        Assert.Equal(("Tafertshofer, Matthias", "Kruckenhauser, Arthur", "profile"), (list[1].White, list[1].Black, list[1].Source));
        Assert.Contains("1. d4 d5", list[1].Pgn);
        Assert.Equal(1802, list[1].BlackElo);
        Assert.Null(list[2].Pgn);   // falsches Jahr bzw. falsche Farbe
        Assert.True(list[3].Forfeit);
        Assert.Null(list[3].Pgn);
    }

    [Fact]
    public async Task ForFixture_ungespielteRunde_leer()
    {
        Assert.Empty(await new LeagueFixtureGames(_db).ForFixtureAsync(1, 3, "Schwaz", default));
    }

    [Fact]
    public void SideMatches_SchwazNurFuerDenEigenenVerein()
    {
        Assert.True(LeagueFixtureGames.SideMatches("Schwaz", null, "Binder, Moriz", "1616951", "Schwaz 1"));
        Assert.False(LeagueFixtureGames.SideMatches("Schwaz", null, "Hess, Max", "24656666", "Schach Ohne Grenzen"));
        Assert.False(LeagueFixtureGames.SideMatches("Hess, Max", "999", "Hess, Max", "24656666", "X"));   // FIDE-ID schlägt den Namen
    }
}
