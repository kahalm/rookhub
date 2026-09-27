using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der ankerunabhaengige Kern (<see cref="CommentTranslator"/>) in seinen KURS-Varianten. Das Verhalten fuer
/// Partien halten die unveraenderten <see cref="CommentTranslationServiceTests"/> fest.
/// </summary>
public class CommentTranslatorTests
{
    private readonly FakeCourseTranslator _llm = new();
    private CommentTranslator Translator() => new(_llm, NullLogger.Instance);

    [Fact]
    public async Task CourseLine_LaengeZaehltNurProsa_TitelDarfKuerzerWerden()
    {
        var prose = string.Join(" ", Enumerable.Repeat("Langer Satz ueber die Stellung.", 10));
        _llm.Answer = $$"""{"items":[{"ply":-3,"text":"T"},{"ply":0,"text":"{{prose}}"}]}""";

        var result = await Translator().TranslateAsync(
            [(-3, "Ein sehr langer Linientitel mit vielen Worten darin"), (0, prose)],
            "en", "de", TranslationSubject.CourseLine, 1);

        Assert.NotNull(result);
        Assert.Equal("T", result![-3]);
    }

    [Fact]
    public async Task CourseLine_ZuKurzeProsa_Verworfen()
    {
        var prose = string.Join(" ", Enumerable.Repeat("Langer Satz ueber die Stellung.", 10));
        _llm.Answer = """{"items":[{"ply":0,"text":"Kurz."}]}""";

        Assert.Null(await Translator().TranslateAsync([(0, prose)], "en", "de", TranslationSubject.CourseLine, 1));
    }

    /// <summary>Kurze Prosa schwankt zwischen den Sprachen zu stark — unter der Mindestmenge wird die Laenge
    /// nicht geprueft („Stark!" fuer „Excellent!" ist richtig).</summary>
    [Fact]
    public async Task CourseLine_KurzeProsa_OhneLaengenpruefung()
    {
        _llm.Answer = """{"items":[{"ply":0,"text":"Stark!"}]}""";

        var result = await Translator().TranslateAsync([(0, "Excellent!")], "en", "de", TranslationSubject.CourseLine, 1);

        Assert.Equal("Stark!", result![0]);
    }

    [Fact]
    public async Task CourseLine_UngefragterSchluessel_WirdNichtUebernommen()
    {
        _llm.Answer = """{"items":[{"ply":0,"text":"Zug"},{"ply":7,"text":"erfunden"}]}""";

        var result = await Translator().TranslateAsync([(0, "Move")], "en", "de", TranslationSubject.CourseLine, 1);

        Assert.Equal(new[] { 0 }, result!.Keys);
    }

    [Fact]
    public async Task CourseLine_AntwortInFalscherSprache_Verworfen()
    {
        _llm.Answer = """{"items":[{"ply":0,"text":"The white knight is better than the black bishop and the position is very good now."}]}""";

        Assert.Null(await Translator().TranslateAsync([(0, "Irgendwas")], "en", "de", TranslationSubject.CourseLine, 1));
    }

    [Fact]
    public async Task Chapters_OhneLaengenpruefung_AuftragNenntKapitel()
    {
        _llm.Answer = """{"items":[{"ply":0,"text":"A"},{"ply":1,"text":"B"}]}""";

        var result = await Translator().TranslateAsync(
            [(0, "The long chapter name one"), (1, "The long chapter name two")],
            "en", "de", TranslationSubject.CourseChapters, 5);

        Assert.Equal("A", result![0]);
        Assert.Contains("name of one chapter", _llm.Calls[0].System);
        Assert.Contains("Game references", _llm.Calls[0].System);
    }

    /// <summary>Kapitelnamen sind kurz und voller Namen — eine Liste deutscher Ueberschriften mit englischen
    /// Eroeffnungsnamen liest sich als englisch. Die Sprachpruefung faellt dort weg, sonst bliebe das Kapitel fuer immer
    /// offen; „jeder Eintrag beantwortet" gilt weiter.</summary>
    [Fact]
    public async Task Chapters_OhneSprachpruefung_AberJederEintragPflicht()
    {
        _llm.Answer = """{"items":[{"ply":0,"text":"The Najdorf: the main line is the best"},{"ply":1,"text":"The English Attack and the Scheveningen"}]}""";
        var items = new List<(int, string)> { (0, "The Najdorf main line"), (1, "English Attack, Scheveningen") };

        var result = await Translator().TranslateAsync(items, "en", "de", TranslationSubject.CourseChapters, 5);

        Assert.Equal("The Najdorf: the main line is the best", result![0]);

        _llm.Answer = """{"items":[{"ply":0,"text":"Najdorf"}]}""";
        Assert.Null(await Translator().TranslateAsync(items, "en", "de", TranslationSubject.CourseChapters, 5));
    }

    /// <summary>Der Auftrag fuer Partien ist der von vor dem Umbau — auch bei „und" —, seit 0.551.1 mit der Regel zu
    /// den Anfuehrungszeichen (ein gerades " beendet den JSON-String, siehe <c>ZitatMitGerademAnfuehrungszeichen_*</c>).</summary>
    [Fact]
    public void GamePrompt_UnveraendertGegenueberVorher()
    {
        var expected = """
            You translate chess annotations from und to de.

            Rules:
            - Translate ONLY the prose. Leave every move, evaluation symbol and coordinate exactly as it
              is (German piece letters: K D T L S). Never add, remove or reorder moves.
            - Keep the author's voice: an annotation is a person explaining a game, not a report.
            - Do not explain, summarise or improve. If a sentence is wrong, it stays wrong.
            - Keep one entry per input entry, with the same ply number.
            - Never write the straight double quote character inside a text. For quotations use the
              typographic quotation marks of the target language (German „…“, French «…», English “…”).
            """;
        Assert.Equal(expected, CommentTranslator.SystemPrompt("und", "de", TranslationSubject.Game));
        Assert.StartsWith(expected, CommentTranslator.SystemPrompt("en", "de", TranslationSubject.CourseLine)
            .Replace("from en ", "from und "));
    }

    /// <summary>Der Fehler vom 2026-09-27: das Modell schliesst ein deutsches Zitat mit dem geraden " — das beendet den
    /// JSON-String, der Text endet mitten im Zitat. Auch wenn damit zufaellig alle Eintraege beantwortet sind (der
    /// abgeschnittene war der letzte), darf nichts gespeichert werden.</summary>
    [Fact]
    public async Task ZitatMitGerademAnfuehrungszeichen_KursLinie_Verworfen()
    {
        _llm.Answer = """{"items":[{"ply":-2,"text":"Da Sie Ihren König nicht absichtlich angreifen lassen dürfen, besteht ein Zustand des „Patts"}]}""";

        Assert.Null(await Translator().TranslateAsync(
            [(-2, "Since you can't put your king on an attacked square, a state of “stalemate” exists.")],
            "en", "de", TranslationSubject.CourseLine, 1));
    }

    [Fact]
    public async Task ZitatMitGerademAnfuehrungszeichen_Partie_Verworfen()
    {
        // Lang genug fuer die Laengenpruefung — verworfen wird allein wegen des offenen Zitats.
        _llm.Answer = """{"items":[{"ply":3,"text":"Kortschnoj schrieb dazu: „Bedroht mit Tf3."}]}""";

        Assert.Null(await Translator().TranslateAsync(
            [(3, "Korchnoi wrote: “Threatening Rf3.”")], "en", "de", TranslationSubject.Game, 1));
    }

    [Fact]
    public async Task GeschlossenesZitat_Angenommen()
    {
        _llm.Answer = """{"items":[{"ply":0,"text":"Der „Kraftfeld“-Turm hält den König fest."}]}""";

        var result = await Translator().TranslateAsync([(0, "The “force field” rook holds the king.")], "en", "de",
            TranslationSubject.CourseLine, 1);

        Assert.Equal("Der „Kraftfeld“-Turm hält den König fest.", result![0]);
    }

    /// <summary>Chessable trennt einen Satz manchmal MITTEN im Zitat auf zwei Zuege auf — dann ist schon die Vorlage
    /// offen, und die Uebersetzung darf es auch sein.</summary>
    [Fact]
    public async Task OffenesZitatInDerVorlage_Angenommen()
    {
        _llm.Answer = """{"items":[{"ply":0,"text":"Der Turm baut ein „Kraftfeld"}]}""";

        var result = await Translator().TranslateAsync([(0, "The rook builds a “force field")], "en", "de",
            TranslationSubject.CourseLine, 1);

        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("ein Zustand des „Patts", true)]
    [InlineData("ein Zustand des „Patts“.", false)]
    [InlineData("ein Zustand des „Patts”.", false)]
    [InlineData("le « pat", true)]
    [InlineData("le « pat » est là", false)]
    [InlineData("der »Patt« ist da", false)]
    [InlineData("the “stalemate", true)]
    [InlineData("the “stalemate” is there", false)]
    [InlineData("ohne Zitat", false)]
    [InlineData("", false)]
    public void EndsInsideQuote_ErkenntOffeneZitate(string text, bool expected)
        => Assert.Equal(expected, CommentTranslator.EndsInsideQuote(text));

    /// <summary>Das Modell streut: derselbe Auftrag geht beim zweiten Mal oft glatt. Ein abgeschnittenes Zitat bekommt
    /// deshalb EINEN zweiten Versuch — und der gilt.</summary>
    [Fact]
    public async Task ZitatAbgeschnitten_ZweiterVersuchGilt()
    {
        _llm.Answers.Enqueue("""{"items":[{"ply":0,"text":"Der Turm baut ein „Kraftfeld"}]}""");
        _llm.Answers.Enqueue("""{"items":[{"ply":0,"text":"Der Turm baut ein „Kraftfeld“."}]}""");

        var result = await Translator().TranslateAsync([(0, "The rook builds a “force field”.")], "en", "de",
            TranslationSubject.CourseLine, 1);

        Assert.Equal("Der Turm baut ein „Kraftfeld“.", result![0]);
        Assert.Equal(2, _llm.Calls.Count);
    }

    [Fact]
    public async Task FehlendeEintraege_ZweiterVersuch_DannVerworfen()
    {
        _llm.Answer = """{"items":[{"ply":0,"text":"Zug"}]}""";

        Assert.Null(await Translator().TranslateAsync([(0, "Move"), (1, "Other")], "en", "de",
            TranslationSubject.CourseLine, 1));
        Assert.Equal(CommentTranslator.MaxAttemptsPerChunk, _llm.Calls.Count);
    }

    /// <summary>Ein ABGEBROCHENER Aufruf wird nicht wiederholt — der zweite Versuch gilt nur der Spur des
    /// Anfuehrungszeichen-Fehlers.</summary>
    [Fact]
    public async Task AbgebrochenerAufruf_KeinZweiterVersuch()
    {
        _llm.Fail = true;

        Assert.Null(await Translator().TranslateAsync([(0, "Move")], "en", "de", TranslationSubject.CourseLine, 1));
        Assert.Single(_llm.Calls);
    }
}
