using System.Text.Json.Nodes;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Der gemeinsame Teil von Nacherzählung, Roast und Fehler-Erklärung (A6-018): Antwort lesen, zwei Versuche,
/// nur Züge aus den Fakten.</summary>
public class GroundedTextTests
{
    private sealed class FakeLlm : IClaudeJsonClient
    {
        public bool IsConfigured => true;
        public bool IsLocal => true;
        public string TranslationModel => "fake";
        public Queue<string?> Answers { get; } = new();
        public List<(string Purpose, string User, int MaxTokens)> Calls { get; } = new();
        public Task<string?> GenerateHintsJsonAsync(string s, string u, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> TranslateCommentsJsonAsync(string s, string u, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> CompleteJsonAsync(string purpose, string system, string user, JsonNode schema, int maxTokens, CancellationToken ct = default)
        {
            Calls.Add((purpose, user, maxTokens));
            return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : null);
        }
    }

    private static readonly JsonNode Schema = JsonNode.Parse("{}")!;

    [Theory]
    [InlineData("{\"recap\":\"  Gut.  \"}", "Gut.")]
    [InlineData("{\"recap\":\"12345\"}", "12345")]
    [InlineData("{\"recap\":\"123456\"}", null)]   // länger als der Deckel
    [InlineData("{\"recap\":\"   \"}", null)]
    [InlineData("{\"roast\":\"Gut.\"}", null)]     // falscher Schlüssel
    [InlineData("{\"recap\":5}", null)]
    [InlineData("kein JSON", null)]
    [InlineData(null, null)]
    public void TextOf_TrimmedText_UnderTheKey_WithinTheCap(string? json, string? expected)
        => Assert.Equal(expected, GroundedText.TextOf(json, "recap", 5));

    [Fact]
    public async Task WriteAsync_ForeignMoveIsRetriedWithTheHint_ThenAccepted()
    {
        var llm = new FakeLlm();
        llm.Answers.Enqueue("{\"roast\":\"Nach 5.Bb5 war es vorbei.\"}");
        llm.Answers.Enqueue("{\"roast\":\"Nach 4.Qxf7# war es vorbei.\"}");
        var foreign = 0;

        var text = await GroundedText.WriteAsync(llm, "roast", "sys", "facts", "\n\nHINT", Schema, 1500, 2000,
            ["e4", "Qxf7#"], () => foreign++, CancellationToken.None);

        Assert.Equal("Nach 4.Qxf7# war es vorbei.", text);
        Assert.Equal(1, foreign);
        Assert.Equal([("roast", "facts", 1500), ("roast", "facts\n\nHINT", 1500)], llm.Calls);
    }

    [Fact]
    public async Task WriteAsync_TwoUselessAnswers_IsNull_AndOnlyForeignMovesAreReported()
    {
        var llm = new FakeLlm();
        llm.Answers.Enqueue(null);
        llm.Answers.Enqueue("{\"recap\":\"Nach 5.Bb5 war es vorbei.\"}");
        var foreign = 0;

        Assert.Null(await GroundedText.WriteAsync(llm, "recap", "sys", "facts", "!", Schema, 800, 600, ["e4"],
            () => foreign++, CancellationToken.None));
        Assert.Equal(2, llm.Calls.Count);
        Assert.Equal(1, foreign);
    }
}
