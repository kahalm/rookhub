using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Sprachen, in die ein Kurs uebersetzt werden darf. Handgespiegelt: Gegenstueck ist <c>SUPPORTED_LANGS</c> in
/// <c>src/frontend/app/src/app/core/locale.service.ts</c>, festgehalten in <c>locale.service.spec.ts</c> mit DERSELBEN
/// literalen Liste — beide Tests bewusst ohne Bezug auf die Gegenseite, damit ein Fehler nicht mitwandert.
/// </summary>
public class CourseTranslationLanguagesTests
{
    [Fact]
    public void Supported_IsTheLiteralListOfTheTwentyFiveUiLanguages()
    {
        Assert.Equal(new[]
        {
            "en", "de", "hr", "es", "fr", "it", "pt", "nl", "sv", "pl", "cs", "ro", "hu",
            "el", "tr", "ru", "uk", "ar", "fa", "hi", "id", "vi", "zh", "ja", "ko",
        }, CourseTranslationLanguages.Supported);
    }

    [Theory]
    [InlineData("de", "de")]
    [InlineData(" DE ", "de")]
    [InlineData("ko", "ko")]
    [InlineData("pt-br", null)]
    [InlineData("und", null)]
    [InlineData("da", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalize_OnlyTheSupportedLanguages(string? input, string? expected)
        => Assert.Equal(expected, CourseTranslationLanguages.Normalize(input));

    [Fact]
    public void ParseList_KeepsOrder_DropsUnknownAndDuplicates()
    {
        Assert.Equal(new[] { "de", "en" }, CourseTranslationLanguages.ParseList(" de, en ,xx;DE"));
        Assert.Empty(CourseTranslationLanguages.ParseList(""));
        Assert.Empty(CourseTranslationLanguages.ParseList(null));
    }
}
