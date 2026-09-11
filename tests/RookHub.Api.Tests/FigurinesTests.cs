using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Figurenzeichen der Sammlung. Ohne diese Aufloesung liest sich ein Kommentar als
/// „I can't win the pawn due to the h7+ trick" — das Zeichen dazwischen ist ein Codepunkt aus dem
/// privaten Unicode-Bereich und ohne die ChessBase-Schrift unsichtbar. Am Bestand gemessen:
/// 45 von 101 Partien einer Stichprobe tragen solche Zeichen.
/// </summary>
public class FigurinesTests
{
    private const string Koenig = "";
    private const string Dame = "";
    private const string Turm = "";
    private const string Laeufer = "";
    private const string Springer = "";
    private const string Bauer = "";

    /// <summary>Der Satz, an dem die Zuordnung belegt wurde — das griechische Geschenk.</summary>
    [Fact]
    public void Apply_englisch_setztDieBuchstabenEin()
    {
        var text = $"I can't win the pawn due to the {Laeufer}h7+ trick.";

        Assert.Equal("I can't win the pawn due to the Bh7+ trick.", Figurines.Apply(text, "en"));
    }

    /// <summary>Derselbe Springer heisst englisch N und deutsch S — deshalb entscheidet die
    /// Sprache des Satzes, nicht das Zeichen.</summary>
    [Fact]
    public void Apply_deutsch_nimmtDieDeutschenBuchstaben()
    {
        var text = $"nach ...{Springer}c6 und {Turm}dg1";

        Assert.Equal("nach ...Sc6 und Tdg1", Figurines.Apply(text, "de"));
        Assert.Equal("nach ...Nc6 und Rdg1", Figurines.Apply(text, "en"));
    }

    [Fact]
    public void Apply_kenntDieUebrigenSprachen()
    {
        Assert.Equal("Cf3", Figurines.Apply($"{Springer}f3", "fr"));
        Assert.Equal("Pf3", Figurines.Apply($"{Springer}f3", "nl"));
        Assert.Equal("Hf3", Figurines.Apply($"{Springer}f3", "hu"));
    }

    /// <summary>Der Bauer bekommt als einziger ein WORT: in der Notation traegt er keinen
    /// Buchstaben, und „Black's P structure" waere weder Deutsch noch Englisch.</summary>
    [Fact]
    public void Apply_bauerWirdZumWort()
    {
        Assert.Equal("Black's pawn structure", Figurines.Apply($"Black's {Bauer} structure", "en"));
        Assert.Equal("die Bauer-Struktur", Figurines.Apply($"die {Bauer}-Struktur", "de"));
    }

    /// <summary>„und" heisst „Sprache nicht bestimmbar" — dann gelten die englischen Buchstaben,
    /// die im Quelltext der meisten Partien ohnehin stehen.</summary>
    [Fact]
    public void Apply_unbekannteSprache_nimmtEnglisch()
    {
        Assert.Equal("Kb1", Figurines.Apply($"{Koenig}b1", "und"));
        Assert.Equal("Qa5", Figurines.Apply($"{Dame}a5", null));
    }

    /// <summary>Mehr als die Haelfte der Partien traegt kein solches Zeichen — die sollen nicht
    /// einmal kopiert werden.</summary>
    [Fact]
    public void Contains_gewoehnlicherText_istFalsch()
    {
        Assert.False(Figurines.Contains("A quiet move: 21. Rd1 Kd7 and Black holds."));
        Assert.False(Figurines.Contains(""));
        Assert.True(Figurines.Contains($"21. {Turm}d1"));
    }

    [Fact]
    public void Apply_ohneZeichen_gibtDenselbenText()
    {
        const string text = "Nothing to do here. 1. e4 e5 2. Nf3";

        Assert.Equal(text, Figurines.Apply(text, "en"));
    }
}
