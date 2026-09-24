using RookHub.Api.DTOs;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Das PGN einer gespeicherten Partie — zeichengenau. Es liegt in der Datenbank und hängt an einem
/// öffentlichen Teilen-Link (<c>/g/{token}</c>); geprüft wurde es bis 0.499.7 nirgends direkt.
/// </summary>
public class SavedGamePgnTests
{
    /// <summary>Volle Kopfzeile. Zwei Eigenheiten, die BLEIBEN: das Anführungszeichen im
    /// Spielernamen wird durch ein Apostroph ERSETZT (nicht maskiert wie sonst im PGN), und ein
    /// unplausibles Elo (hier 50) fällt ganz weg statt als Müll-Header dazustehen.</summary>
    [Fact]
    public void BuildPgn_Golden_FullHeadersAndMoveNumbers()
    {
        var dto = new SaveGameInputDto
        {
            Source = "lichess",
            White = "Al\"ice",
            Black = "Bob",
            SourceUrl = "https://lichess.org/abc",
            PlayedAt = new DateTime(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc),
            WhiteElo = 1832,
            BlackElo = 50,
        };

        Assert.Equal(
            "[Event \"RepCheck saved game\"]\n" +
            "[Site \"https://lichess.org/abc\"]\n" +
            "[Date \"2026.09.21\"]\n" +
            "[White \"Al'ice\"]\n" +
            "[Black \"Bob\"]\n" +
            "[Result \"1-0\"]\n" +
            "[WhiteElo \"1832\"]\n" +
            "\n" +
            "1. e4 c5 2. Nf3 d6 1-0",
            SavedGameService.BuildPgn(new List<string> { "e4", "c5", "Nf3", "d6" }, dto, "1-0"));
    }

    /// <summary>Ohne Angaben: „?" bzw. das leere Datum, und ein Zugtext, der NUR aus dem Ergebnis
    /// besteht — ohne führendes Leerzeichen (anders als beim Kurs-Export, der seins selbst setzt).</summary>
    [Fact]
    public void BuildPgn_Golden_NoHeadersNoMoves()
        => Assert.Equal(
            "[Event \"RepCheck saved game\"]\n[Site \"?\"]\n[Date \"????.??.??\"]\n[White \"?\"]\n[Black \"?\"]\n[Result \"*\"]\n\n*",
            SavedGameService.BuildPgn(new List<string>(), new SaveGameInputDto { Source = "chess.com" }, "*"));

    /// <summary>Die Bedenkzeit steht als letzter Header (0.526.0), in genau der Schreibweise der Plattform.</summary>
    [Fact]
    public void BuildPgn_TimeControl_StehtHinterDenElos()
    {
        var pgn = SavedGameService.BuildPgn(new List<string> { "e4" },
            new SaveGameInputDto { Source = "chess.com", WhiteElo = 1632, TimeControl = "180+2" }, "*");
        Assert.Contains("[WhiteElo \"1632\"]\n[TimeControl \"180+2\"]\n\n", pgn);
    }

    /// <summary>
    /// Der Wert geht ungeprüft in einen PGN-Header und in eine Spalte — deshalb wird alles verworfen,
    /// was nicht wie eine Bedenkzeit aussieht (statt es zu speichern und später zu raten).
    /// </summary>
    [Theory]
    [InlineData("600", "600")]
    [InlineData("180+2", "180+2")]
    [InlineData("1/86400", "1/86400")]
    [InlineData("-", "-")]
    [InlineData("  300+0 ", "300+0")]
    public void CleanTimeControl_NimmtDieBekanntenFormen(string raw, string erwartet)
        => Assert.Equal(erwartet, SavedGameService.CleanTimeControl(raw));

    [Theory]
    [InlineData("3 + 2")]          // die ANZEIGE, nicht das Format
    [InlineData("blitz")]
    [InlineData("180+2\"]\n[X \"y")]  // Versuch, einen Header unterzuschieben
    [InlineData("")]
    [InlineData(null)]
    public void CleanTimeControl_VerwirftAllesAndere(string? raw)
        => Assert.Null(SavedGameService.CleanTimeControl(raw));
}
