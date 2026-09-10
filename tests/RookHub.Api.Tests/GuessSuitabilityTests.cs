using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Eignungsnote sortiert 130 000 Partien danach, welche als Punktepartie etwas hergibt. Sie
/// entscheidet nichts — aber sie muss die Reihenfolge richtig herum legen.
/// </summary>
public class GuessSuitabilityTests
{
    /// <summary>Die durchgaengig erklaerte Meisterpartie mittlerer Laenge.</summary>
    private static int Ideal() => GuessSuitability.Score(
        plyCount: 80, commentedPlies: 26, commentChars: 26 * 200, variationCount: 25,
        result: "1-0", whiteElo: 2650, blackElo: 2700);

    [Fact]
    public void Score_idealePartie_liegtGanzOben()
        => Assert.InRange(Ideal(), 90, 100);

    /// <summary>Der Kern der Sache: eine Partie, die nur am Schluss redet, ist als Punktepartie
    /// stumm — auch wenn genauso viele Zeichen darin stehen.</summary>
    [Fact]
    public void Score_dichteSchlaegtMenge()
    {
        var spread = GuessSuitability.Score(80, 24, 4800, 10, "1-0", 2600, 2600);
        var lumped = GuessSuitability.Score(80, 3, 4800, 10, "1-0", 2600, 2600);

        Assert.True(spread > lumped + 20, $"verteilt {spread} gegen geballt {lumped}");
    }

    /// <summary>„gut!" ist kein Kommentar.</summary>
    [Fact]
    public void Score_kurzeKommentare_zaehlenWeniger()
    {
        var rich = GuessSuitability.Score(80, 20, 20 * 200, 10, "1-0", 2600, 2600);
        var thin = GuessSuitability.Score(80, 20, 20 * 8, 10, "1-0", 2600, 2600);

        Assert.True(rich > thin, $"ausfuehrlich {rich} gegen knapp {thin}");
    }

    [Fact]
    public void Score_zuKurzUndZuLang_verlierenPunkte()
    {
        var right = GuessSuitability.Score(80, 20, 4000, 10, "1-0", 2600, 2600);
        var tooShort = GuessSuitability.Score(24, 6, 1200, 10, "1-0", 2600, 2600);
        var tooLong = GuessSuitability.Score(230, 57, 11400, 10, "1-0", 2600, 2600);

        Assert.True(right > tooShort);
        Assert.True(right > tooLong);
    }

    /// <summary>Im kuratierten Bestand uebernimmt man die Seite des GEWINNERS. Beim Remis gibt es
    /// keinen, und die Seitenwahl faellt auf die Bewertung zurueck.</summary>
    [Fact]
    public void Score_entschiedenePartie_stehtVorDemRemis()
    {
        var decisive = GuessSuitability.Score(80, 20, 4000, 10, "1-0", 2600, 2600);
        var draw = GuessSuitability.Score(80, 20, 4000, 10, "1/2-1/2", 2600, 2600);

        Assert.True(decisive > draw);
    }

    /// <summary>Eine Partie ohne Elo-Angabe ist nicht schwach, sondern unbekannt — alte
    /// Meisterpartien tragen nie eine. Sie darf deshalb nicht wie eine Anfaengerpartie behandelt
    /// werden.</summary>
    [Fact]
    public void Score_ohneEloZahlen_bleibtInDerMitte()
    {
        var unknown = GuessSuitability.Score(80, 20, 4000, 10, "1-0", null, null);
        var weak = GuessSuitability.Score(80, 20, 4000, 10, "1-0", 1500, 1500);
        var strong = GuessSuitability.Score(80, 20, 4000, 10, "1-0", 2700, 2700);

        Assert.True(unknown > weak);
        Assert.True(unknown < strong);
    }

    [Fact]
    public void Score_ohneKommentare_bleibtWeitUnten()
    {
        var mute = GuessSuitability.Score(80, 0, 0, 0, "1-0", 2600, 2600);

        Assert.True(mute < 40, $"ohne ein Wort: {mute}");
        Assert.True(mute < Ideal() - 40);
    }

    [Fact]
    public void Score_ohneZuege_istNull()
    {
        Assert.Equal(0, GuessSuitability.Score(0, 0, 0, 0, "1-0", 2600, 2600));
        Assert.Equal(0, GuessSuitability.Score(null, null, null, null, null, null, null));
    }

    /// <summary>Die Note ist eine Zahl von 0 bis 100 — auch bei absurden Eingaben.</summary>
    [Fact]
    public void Score_bleibtImRahmen()
    {
        var extreme = GuessSuitability.Score(80, 999, 9_000_000, 9999, "1-0", 3500, 3500);

        Assert.InRange(extreme, 0, 100);
    }
}
