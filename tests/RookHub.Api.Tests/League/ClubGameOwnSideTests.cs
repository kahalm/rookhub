using RookHub.Api.Models;
using RookHub.Api.Services.League;
using Xunit;

namespace RookHub.Api.Tests.League;

/// <summary>0.907.0, Wunsch 2026-11-08: „wenn ich aus der League eine Partie öffne, zeig sie standardmäßig aus der Sicht des Schwazers
/// (Brettrichtung + Fehler nachspielen)" — die Einzelansicht einer Vereinspartie nennt die Seite des eigenen Vereins.</summary>
public class ClubGameOwnSideTests
{
    private static LeagueClubGame Game(string white, string black, string? whiteFide = null, string? blackReal = null) =>
        new() { White = white, Black = black, WhiteFide = whiteFide, BlackRealName = blackReal };

    [Fact]
    public void OwnSide_IsTheReplacedSide()
    {
        var club = TestClubs.Home;   // AnonName „Testdorf"
        Assert.Equal("white", ClubGameReader.OwnSideOf(club, Game("Testdorf", "Hengl, Philip")));
        Assert.Equal("black", ClubGameReader.OwnSideOf(club, Game("Hengl, Philip", "Testdorf")));
        // mit internem Klarnamen auch dann, wenn der Anzeigename schon anders lautet
        Assert.Equal("black", ClubGameReader.OwnSideOf(club, Game("Hengl, Philip", "X", blackReal: "intern")));
    }

    [Fact]
    public void OwnSide_NullWhenNoneOrBoth()
    {
        var club = TestClubs.Home;
        Assert.Null(ClubGameReader.OwnSideOf(club, Game("Binder, Moriz", "Hengl, Philip")));
        Assert.Null(ClubGameReader.OwnSideOf(club, Game("Testdorf", "Testdorf")));
        // ein echter Spieler, der zufällig so heißt wie der Ersatzname, aber eine FIDE-ID hat, ist kein Ersatz
        Assert.Null(ClubGameReader.OwnSideOf(club, Game("Testdorf", "Hengl, Philip", whiteFide: "123")));
    }
}
