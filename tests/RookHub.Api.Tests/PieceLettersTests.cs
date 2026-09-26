using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Die Zuege einer Uebersetzung tragen die Figurenbuchstaben der Zielsprache — und die
/// Prosa drumherum bleibt unangetastet.</summary>
public class PieceLettersTests
{
    [Theory]
    // Der einfache Zug, der Schlag, die Praezisierung, das Schachgebot.
    [InlineData("Eine weitere Fortsetzung ist 5... Nd7", "Eine weitere Fortsetzung ist 5... Sd7")]
    [InlineData("nach Bxe4 steht Weiss besser", "nach Lxe4 steht Weiss besser")]
    [InlineData("6. O-O-O Bb7 7. f3 Nbd7", "6. O-O-O Lb7 7. f3 Sbd7")]
    [InlineData("R1e2 und Qh4+", "T1e2 und Dh4+")]
    [InlineData("der Bauer geht nach e8=Q", "der Bauer geht nach e8=D")]
    // Prosa ist kein Zug: es fehlt das Zielfeld.
    [InlineData("Bad Wiessee 1997, [Rabinovich,Al]", "Bad Wiessee 1997, [Rabinovich,Al]")]
    [InlineData("Be careful, Nobody expected that", "Be careful, Nobody expected that")]
    // Was das Modell selbst schon umgestellt hat, bleibt stehen: „S" gibt es im Englischen nicht.
    [InlineData("Sf3 war besser", "Sf3 war besser")]
    public void EnglischNachDeutsch(string quelle, string erwartet)
        => Assert.Equal(erwartet, PieceLetters.Convert(quelle, "en", "de"));

    /// <summary>Aus dem Franzoesischen ist „R" der Koenig und „T" der Turm — wer stumpf das englische
    /// Alphabet nimmt, macht aus dem Koenig einen Turm.</summary>
    [Theory]
    [InlineData("Cf3 suivi de Fb5", "Sf3 suivi de Lb5")]
    [InlineData("Rg1 et Td1", "Kg1 et Td1")]
    public void FranzoesischNachDeutsch(string quelle, string erwartet)
        => Assert.Equal(erwartet, PieceLetters.Convert(quelle, "fr", "de"));

    [Fact]
    public void NiederlaendischesPaard_wirdSpringer()
        => Assert.Equal("Sf3", PieceLetters.Convert("Pf3", "nl", "de"));

    /// <summary>Unbekannte oder unbestimmbare Sprachen bleiben unangetastet — ein englischer Zug ist
    /// besser als ein zerschriebener.</summary>
    [Theory]
    [InlineData("und", "de")]
    [InlineData("en", "xx")]
    [InlineData("de", "de")]
    public void OhneVerlaesslichesAlphabet_bleibtAlles(string von, string nach)
        => Assert.Equal("Nf3 und Bb5", PieceLetters.Convert("Nf3 und Bb5", von, nach));
}
