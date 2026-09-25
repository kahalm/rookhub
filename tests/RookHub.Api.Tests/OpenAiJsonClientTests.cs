using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Tipps und Übersetzungen über ein Sprachmodell auf eigener Hardware (OpenAI-kompatibel, DGX Spark).</summary>
public class OpenAiJsonClientTests
{
    private readonly ChatCompletionHandler _handler = new();

    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    private OpenAiJsonClient Client(IConfiguration config) => new(new HttpClient(_handler), config, NullLogger.Instance);

    [Fact]
    public async Task Hints_WithoutModel_TakesTheFirstServedModel_SendsSchema_AndThinkingOff()
    {
        _handler.Raw("{\"data\":[{\"id\":\"qwen3.5-122b\"},{\"id\":\"other\"}]}")
            .Reply("{\"hint1\":\"a\",\"hint2\":\"b\",\"hint3\":\"c\"}");
        var client = Client(Config(("TextLlm:BaseUrl", "http://spark/v1"), ("TextLlm:ApiKey", "k")));

        var json = await client.GenerateHintsJsonAsync("sys", "puzzle");

        Assert.Equal("{\"hint1\":\"a\",\"hint2\":\"b\",\"hint3\":\"c\"}", json);
        Assert.Equal("http://spark/v1/models", _handler.Requests[0].Url);
        var body = _handler.Requests[1].Body;
        Assert.Equal("qwen3.5-122b", (string?)body["model"]);
        Assert.Equal("Bearer k", _handler.Requests[1].Authorization);
        Assert.Equal("sys", (string?)body["messages"]![0]!["content"]);
        Assert.Equal("puzzle", (string?)body["messages"]![1]!["content"]);
        Assert.False((bool?)body["chat_template_kwargs"]!["enable_thinking"]);
        Assert.True((bool?)body["stream"]);
        var required = body["response_format"]!["json_schema"]!["schema"]!["required"]!.AsArray().Select(n => (string?)n);
        Assert.Equal(["hint1", "hint2", "hint3"], required);
        Assert.Equal("qwen3.5-122b", client.TranslationModel);

        // Das Modell wird nur einmal nachgefragt.
        _handler.Reply("{\"hint1\":\"a\",\"hint2\":\"b\",\"hint3\":\"c\"}");
        await client.GenerateHintsJsonAsync("sys", "puzzle 2");
        Assert.Equal(3, _handler.Requests.Count);
    }

    [Fact]
    public async Task Translation_WithConfiguredModel_AndThinkingOn_UsesTheTranslationSchema()
    {
        _handler.Reply("Here: {\"items\":[{\"ply\":3,\"text\":\"Gut.\"}]}");
        var client = Client(Config(("TextLlm:BaseUrl", "http://spark/v1"), ("TextLlm:Model", "gpt-oss-120b"),
            ("TextLlm:Thinking", "true")));

        var json = await client.TranslateCommentsJsonAsync("sys", "{\"items\":[]}");

        Assert.Equal("{\"items\":[{\"ply\":3,\"text\":\"Gut.\"}]}", json);
        var request = Assert.Single(_handler.Requests); // kein /models — das Modell ist eingestellt
        Assert.Equal("gpt-oss-120b", (string?)request.Body["model"]);
        Assert.Null(request.Body["chat_template_kwargs"]);
        Assert.Contains("items", request.Body["response_format"]!["json_schema"]!["schema"]!["required"]!.AsArray().Select(n => (string?)n));
        Assert.Equal("gpt-oss-120b", client.TranslationModel);
    }

    [Fact]
    public async Task ServerError_OrCutOff_IsNull()
    {
        _handler.Fail(System.Net.HttpStatusCode.InternalServerError).Reply("{\"hint1\":\"a\"", finish: "length");
        var client = Client(Config(("TextLlm:BaseUrl", "http://spark/v1"), ("TextLlm:Model", "m")));

        Assert.Null(await client.GenerateHintsJsonAsync("s", "u"));
        Assert.Null(await client.GenerateHintsJsonAsync("s", "u"));
    }

    [Fact]
    public void Create_OwnHardwareWinsOverClaude_AndWithoutBothEverythingIsOff()
    {
        var both = TextJsonClients.Create(Config(("TextLlm:BaseUrl", "http://spark/v1"), ("Anthropic:TextApiKey", "sk")),
            NullLoggerFactory.Instance, new HttpClient(_handler));
        Assert.IsType<OpenAiJsonClient>(both);
        Assert.True(both.IsConfigured);

        var claude = TextJsonClients.Create(Config(("Anthropic:TextApiKey", "sk")), NullLoggerFactory.Instance);
        Assert.IsType<ClaudeJsonClient>(claude);
        Assert.True(claude.IsConfigured);

        // Der Konto-Schlüssel allein schaltet nichts ein (0.533.1).
        var none = TextJsonClients.Create(Config(("Anthropic:ApiKey", "sk")), NullLoggerFactory.Instance);
        Assert.False(none.IsConfigured);
    }
}
