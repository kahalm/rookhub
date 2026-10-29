using RookHub.Api.Services.Ebooks;

namespace RookHub.Api.Tests.Ebooks;

public class EbookNotationLettersTests
{
    private static readonly string[] GermanBook =
    [
        "<p>1.e4 e5 2.Sf3 Sc6 3.Lb5 a6 4.La4 Sf6 5.0-0 Le7 6.Te1 b5 7.Lb3 d6 8.c3 0-0</p>",
        "<p>Nach 9.h3 Sb8 10.d4 Sbd7 steht Weiß gut; Dd1-e2 und Txe4 kommen in Frage. 34...e1=D</p>",
    ];

    [Fact]
    public void For_GermanBookToEnglish_ConvertsPieceLettersOnlyInMoves()
    {
        var convert = EbookNotationLetters.For("de", "en", GermanBook);
        Assert.NotNull(convert);
        Assert.Equal("<p>1.e4 e5 2.Nf3 Nc6 3.Bb5 a6 4.Ba4 Nf6 5.0-0 Be7 6.Re1 b5 7.Bb3 d6 8.c3 0-0</p>", convert!(GermanBook[0]));
        Assert.Equal("<p>Nach 9.h3 Nb8 10.d4 Nbd7 steht Weiß gut; Qd1-e2 und Rxe4 kommen in Frage. 34...e1=Q</p>", convert(GermanBook[1]));
    }

    [Fact]
    public void Convert_LeavesAttributesAndWordsAlone()
    {
        var convert = EbookNotationLetters.For("de", "en", GermanBook)!;
        const string html = "<p><img src=\"Sf3.png\" alt=\"Sa3\"/> Sand, Lemberg und D-Zug, dann Sf3.</p>";
        Assert.Equal("<p><img src=\"Sf3.png\" alt=\"Sa3\"/> Sand, Lemberg und D-Zug, dann Nf3.</p>", convert(html));
    }

    [Fact]
    public void For_SpanishBookPrintingEnglishNotation_DoesNothing()
    {
        // „R“ ist im Spanischen der König — ein Buch mit englischer Notation darf nicht umgestellt werden.
        var english = new[] { "<p>1.e4 e5 2.Nf3 Nc6 3.Bb5 a6 4.Ba4 Nf6 5.O-O Be7 6.Re1 b5 7.Bb3 d6 8.Qe2 Rb8</p>" };
        Assert.Null(EbookNotationLetters.For("es", "en", english));
    }

    [Fact]
    public void For_SpanishNotation_MapsKingAndRookCorrectly()
    {
        var spanish = new[] { "<p>1.e4 e5 2.Cf3 Cc6 3.Ab5 a6 4.Aa4 Cf6 5.Re2 Td8 6.Dd1 Ae7</p>" };
        var convert = EbookNotationLetters.For("es", "en", spanish);
        Assert.NotNull(convert);
        Assert.Equal("<p>1.e4 e5 2.Nf3 Nc6 3.Bb5 a6 4.Ba4 Nf6 5.Ke2 Rd8 6.Qd1 Be7</p>", convert!(spanish[0]));
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("de", "fr")]
    [InlineData(null, "en")]
    public void For_NoConversionNeeded_ReturnsNull(string? source, string target)
        => Assert.Null(EbookNotationLetters.For(source, target, GermanBook));
}
