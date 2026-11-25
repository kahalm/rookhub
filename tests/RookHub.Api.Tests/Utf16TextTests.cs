using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services;
using RookHub.Api.Services.Library;
using RookHub.Api.Services.Scoresheet;
using Xunit;

namespace RookHub.Api.Tests;

/// <summary>
/// Einzelne UTF-16-Surrogate (1.011.1): Buch-Übersetzung 911 brach auf Prod ab mit „Cannot read invalid UTF-16 JSON text as
/// string. Invalid surrogate value: '0xDC4B'.“ — das Modell schrieb <c>\udc4b</c> ohne obere Hälfte in seine JSON-Antwort.
/// </summary>
public class Utf16TextTests
{
    /// <summary>Genau die Prod-Ausnahme: Parse lässt das Escape durch, GetString wirft — und zwar KEINE JsonException.</summary>
    [Fact]
    public void Reproduction_GetStringThrowsInvalidOperation_NotJsonException()
    {
        using var doc = JsonDocument.Parse("""{"t":"a\udc4bb"}""");
        var ex = Assert.Throws<InvalidOperationException>(() => doc.RootElement.GetProperty("t").GetString());
        Assert.Contains("0xDC4B", ex.Message);
    }

    [Theory]
    [InlineData("""{"t":"a\udc4bb"}""", "a�b")]                 // untere Hälfte allein (Prod)
    [InlineData("""{"t":"a\ud83d b"}""", "a� b")]               // obere Hälfte allein
    [InlineData("""{"t":"a\ud83d"}""", "a�")]                   // obere Hälfte am Stringende
    [InlineData("""{"t":"\udc4b\ud83d"}""", "��")]         // verkehrte Reihenfolge
    [InlineData("""{"t":"a👋b"}""", "a👋b")]               // vollständiges Paar bleibt
    [InlineData("""{"t":"a\\udc4bb"}""", "a\\udc4bb")]               // Backslash als Text, kein Escape
    [InlineData("""{"t":"aä\"b"}""", "aä\"b")]                  // andere Escapes unverändert
    public void SanitizeJsonEscapes_LoneSurrogateEscapes_BecomeReplacementChar(string json, string expected)
    {
        using var doc = JsonDocument.Parse(Utf16Text.SanitizeJsonEscapes(json));
        Assert.Equal(expected, doc.RootElement.GetProperty("t").GetString());
    }

    [Fact]
    public void ReplaceLoneSurrogates_KeepsPairs_ReplacesHalves()
    {
        Assert.Equal("a�b👋�", Utf16Text.ReplaceLoneSurrogates("a\uDC4Bb👋\uD83D"));
        var clean = "nichts zu tun 👋";
        Assert.Same(clean, Utf16Text.ReplaceLoneSurrogates(clean));
        Assert.False(Utf16Text.HasLoneSurrogate(clean));
        Assert.True(Utf16Text.HasLoneSurrogate("x\uDC4B"));
    }

    /// <summary>Kürzen nach Länge darf ein Paar nicht zerschneiden (sonst bleibt eine einzelne Hälfte stehen).</summary>
    [Fact]
    public void Truncate_NeverSplitsASurrogatePair()
    {
        var text = "ab👋cd";   // a b \uD83D \uDC4B c d
        Assert.Equal("ab", Utf16Text.Truncate(text, 3));
        Assert.Equal("ab👋", Utf16Text.Truncate(text, 4));
        Assert.Equal("ab", Utf16Text.Truncate(text, 2));
        Assert.Equal(text, Utf16Text.Truncate(text, 99));
        Assert.False(Utf16Text.HasLoneSurrogate(Utf16Text.Truncate(text, 3)));
    }

    // ── CommentTranslator ───────────────────────────────────────────────────────────────────────

    /// <summary>Vorher warf <c>Parse</c> die InvalidOperationException durch bis in den Buchlauf.</summary>
    [Fact]
    public async Task Translator_ResponseWithLoneSurrogateEscape_IsReadNotThrown()
    {
        var llm = new FakeCourseTranslator { Answer = """{"items":[{"ply":0,"text":"Hello \udc4b there, my friend."}]}""" };
        var translator = new CommentTranslator(llm, NullLogger.Instance);

        var result = await translator.TranslateAsync([(0, "Hallo überall, mein Freund.")], "de", "en", TranslationSubject.EbookText, 1);

        Assert.NotNull(result);
        Assert.Equal("Hello � there, my friend.", result![0]);
    }

    [Fact]
    public void Parse_LoneSurrogateEscape_OtherEntriesSurvive()
    {
        var parsed = CommentTranslator.Parse("""{"items":[{"ply":1,"text":"ok"},{"ply":2,"text":"x\udc4b"}]}""");
        Assert.Equal([(1, "ok"), (2, "x�")], parsed);
    }

    /// <summary>Quelle mit einer einzelnen Hälfte (kaputtes Buch): der Auftrag ist gültiges JSON mit U+FFFD.</summary>
    [Fact]
    public void UserPrompt_LoneSurrogateInSource_IsReplacedBeforeSending()
    {
        var prompt = CommentTranslator.UserPrompt([(0, "Weiß \uDC4B gewinnt"), (1, "Paar 👋 bleibt")]);
        using var doc = JsonDocument.Parse(prompt);
        var texts = doc.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("text").GetString()).ToList();
        Assert.Equal(["Weiß � gewinnt", "Paar 👋 bleibt"], texts);
    }

    // ── OpenAiChat (Strom / ganze Antwort) ──────────────────────────────────────────────────────

    [Fact]
    public void OpenAiChat_TextOf_LoneSurrogateEscape_ReplacementInsteadOfThrow()
    {
        using var doc = JsonDocument.Parse("""{"content":"{\"items\":[]} \udc4b"}""");
        Assert.Equal("{\"items\":[]} �", OpenAiChat.TextOf(doc.RootElement.GetProperty("content")));
    }
}
