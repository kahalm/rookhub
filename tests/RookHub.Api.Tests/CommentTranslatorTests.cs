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

    /// <summary>Der Auftrag fuer Partien ist WOERTLICH der von vor dem Umbau — auch bei „und".</summary>
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
            """;
        Assert.Equal(expected, CommentTranslator.SystemPrompt("und", "de", TranslationSubject.Game));
        Assert.StartsWith(expected, CommentTranslator.SystemPrompt("en", "de", TranslationSubject.CourseLine)
            .Replace("from en ", "from und "));
    }
}
