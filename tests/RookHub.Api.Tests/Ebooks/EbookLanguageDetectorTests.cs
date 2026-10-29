using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.Tests.Ebooks;

/// <summary>Buch-Übersetzung (0.876.0): die Sprache eines Buchs am Text — gemischt/unklar bleibt ohne Sprache.</summary>
public class EbookLanguageDetectorTests
{
    private static List<string> Book(string paragraph, int times = 60) => Enumerable.Repeat(paragraph, times).ToList();

    private const string German = "Weiß hat die bessere Stellung, aber der Läufer auf e3 ist nicht so stark, wie es aussieht. " +
        "Schwarz muss sich jetzt mit dem Damenflügel beschäftigen und kann noch auf Gegenspiel hoffen.";
    private const string English = "White has the better position, but the bishop on e3 is not as strong as it looks. " +
        "Black should now turn to the queenside and can still hope for counterplay after this move.";
    private const string Spanish = "Las blancas tienen una posición mejor, pero el alfil de e3 no es tan fuerte como parece. " +
        "Las negras deben jugar por el flanco de dama y todavía hay contrajuego para ellas después de esta jugada.";
    private const string Russian = "Белые имеют лучшую позицию, но слон на e3 не так силён, как кажется. " +
        "Чёрные должны теперь заняться ферзевым флангом и могут ещё надеяться на контригру.";
    private const string Croatian = "Bijeli ima bolju poziciju, ali lovac na e3 nije tako jak kako izgleda. " +
        "Crni sada mora igrati na daminom krilu i može se nadati protuigri nakon ovog poteza, jer je to vrlo važno.";

    [Theory]
    [InlineData(German, "de")]
    [InlineData(English, "en")]
    [InlineData(Spanish, "es")]
    [InlineData(Russian, "ru")]
    [InlineData(Croatian, "hr")]
    public void Detect_FromTheText(string paragraph, string expected)
    {
        var r = EbookLanguageDetector.Detect(Book(paragraph));
        Assert.Equal(expected, r.Language);
        Assert.Equal(EbookLanguageDetector.SourceText, r.Source);
    }

    [Fact]
    public void Detect_OpfHintDoesNotOverruleTheText()
    {
        // Viele EPUB-Werkzeuge schreiben „en“ als Vorgabe ins OPF — der Text ist spanisch.
        Assert.Equal("es", EbookLanguageDetector.Detect(Book(Spanish), "en").Language);
    }

    [Fact]
    public void Detect_MixedHalfAndHalf_IsUnclear()
    {
        var book = Book(German, 30).Concat(Book(English, 30)).ToList();
        var r = EbookLanguageDetector.Detect(book);
        Assert.Null(r.Language);
        Assert.Equal(EbookLanguageDetector.SourceUnclear, r.Source);
    }

    [Fact]
    public void Detect_OnlyMovesOrNoText_IsNoText()
    {
        Assert.Equal(EbookLanguageDetector.SourceNoText, EbookLanguageDetector.Detect(Book("1.e4 e5 2.Nf3 Nc6 3.Bb5 a6 4.Ba4 Nf6 5.O-O Be7", 400)).Source);
        Assert.Equal(EbookLanguageDetector.SourceNoText, EbookLanguageDetector.Detect([]).Source);
    }

    [Fact]
    public void Detect_EnglishPrefaceInASpanishBook_StaysSpanish()
    {
        var book = Book(English, 3).Concat(Book(Spanish, 80)).ToList();
        Assert.Equal("es", EbookLanguageDetector.Detect(book).Language);
    }

    [Theory]
    [InlineData("en-US", "en")]
    [InlineData("ger", "de")]
    [InlineData("DE", "de")]
    [InlineData("und", null)]
    [InlineData("", null)]
    public void NormalizeHint(string hint, string? expected) => Assert.Equal(expected, EbookLanguageDetector.NormalizeHint(hint));
}
