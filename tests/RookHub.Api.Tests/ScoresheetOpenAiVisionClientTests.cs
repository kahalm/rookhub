using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Der Formular-Leser über eine OpenAI-kompatible Schnittstelle (Qwen3-VL auf eigener Hardware).</summary>
public class ScoresheetOpenAiVisionClientTests
{
    private readonly ChatCompletionHandler _handler = new();

    private OpenAiScoresheetVisionClient Client(string? key = "k", int cap = 8000, bool schema = true)
        => new(new HttpClient(_handler),
            new OpenAiScoresheetVisionClient.Settings("http://spark/v1/", "qwen3-vl", key, UseJsonSchema: schema, MaxTokensCap: cap),
            NullLogger.Instance);

    [Fact]
    public async Task ReadAsync_SendsImageAsDataUrl_WithSchemaAndCappedTokens_ReturnsJsonAndUsage()
    {
        _handler.Reply("{\"moves\":[]}", input: 1500, output: 420);

        var result = await Client().ReadAsync(new byte[] { 1, 2, 3 }, "Transcribe the scoresheet.", 64000);

        Assert.Null(result.Error);
        Assert.Equal("{\"moves\":[]}", result.Json);
        Assert.Equal(1500, result.InputTokens);
        Assert.Equal(420, result.OutputTokens);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal("http://spark/v1/chat/completions", request.Url);
        Assert.Equal("Bearer k", request.Authorization);
        var body = request.Body;
        Assert.Equal("qwen3-vl", (string?)body["model"]);
        Assert.Equal(8000, (int?)body["max_tokens"]); // am Deckel des Servers, nicht am Budget-Wunsch
        Assert.Equal("system", (string?)body["messages"]![0]!["role"]);
        Assert.Equal(ScoresheetPrompt.TranscribeSystem, (string?)body["messages"]![0]!["content"]);
        var content = body["messages"]![1]!["content"]!.AsArray();
        Assert.Equal("data:image/jpeg;base64,AQID", (string?)content[0]!["image_url"]!["url"]);
        Assert.Equal("Transcribe the scoresheet.", (string?)content[1]!["text"]);
        Assert.Equal("json_schema", (string?)body["response_format"]!["type"]);
        var required = body["response_format"]!["json_schema"]!["schema"]!["required"]!.AsArray().Select(n => (string?)n);
        Assert.Contains("moves", required);
    }

    [Fact]
    public async Task ReadAsync_ServerRejectsSchema_RetriesOnceWithout_AndStaysWithout()
    {
        _handler.Fail(HttpStatusCode.BadRequest).Reply("{\"moves\":[]}").Reply("{\"moves\":[]}");
        var client = Client();

        var first = await client.ReadAsync(new byte[] { 1 }, "a", 4000);
        var second = await client.ReadAsync(new byte[] { 1 }, "b", 4000);

        Assert.Null(first.Error);
        Assert.Null(second.Error);
        Assert.Equal(3, _handler.Requests.Count);
        Assert.NotNull(_handler.Requests[0].Body["response_format"]);
        Assert.Null(_handler.Requests[1].Body["response_format"]);
        Assert.Null(_handler.Requests[2].Body["response_format"]);
    }

    [Fact]
    public async Task ReadAsync_ThinkBlockAndCodeFence_AreStripped()
    {
        _handler.Reply("<think>first {maybe} Nf3?</think>\nHere it is:\n```json\n{\"moves\":[{\"written\":\"Sf3\"}]}\n```");

        var result = await Client().ReadAsync(new byte[] { 1 }, "x", 4000);

        Assert.Equal("{\"moves\":[{\"written\":\"Sf3\"}]}", result.Json);
    }

    [Fact]
    public async Task ReadAsync_CutAtTokenLimit_IsTruncated_AndUsageStillReported()
    {
        _handler.Reply("{\"moves\":[{\"written\":\"e4\"", finish: "length", output: 4000);

        var result = await Client().ReadAsync(new byte[] { 1 }, "x", 4000);

        Assert.Equal("truncated", result.Error);
        Assert.Null(result.Json);
        Assert.Equal(4000, result.OutputTokens);
    }

    [Fact]
    public async Task ReadAsync_ServerError_IsFailed_AndWithoutKeyNoAuthorization()
    {
        _handler.Fail(HttpStatusCode.InternalServerError);

        var result = await Client(key: null, schema: false).ReadAsync(new byte[] { 1 }, "x", 4000);

        Assert.Equal("failed", result.Error);
        Assert.Null(Assert.Single(_handler.Requests).Authorization);
    }

    [Fact]
    public async Task ReadAsync_NoBaseUrl_IsNotConfigured_AndSendsNothing()
    {
        var client = new OpenAiScoresheetVisionClient(new HttpClient(_handler),
            new OpenAiScoresheetVisionClient.Settings("", "m"), NullLogger.Instance);

        var result = await client.ReadAsync(new byte[] { 1 }, "x", 4000);

        Assert.False(client.IsConfigured);
        Assert.Equal("notConfigured", result.Error);
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData("{\"a\":1}", "{\"a\":1}")]
    [InlineData("text before {\"a\":{\"b\":2}} after", "{\"a\":{\"b\":2}}")]
    [InlineData("<think>{\"a\":1}", null)] // Denkblock ohne Ende: keine Antwort dahinter
    [InlineData("[1,2]", null)]
    [InlineData("{broken", null)]
    [InlineData("", null)]
    public void ExtractJsonObject_TakesOnlyAValidObject(string content, string? expected)
        => Assert.Equal(expected, OpenAiChat.ExtractJsonObject(content));

    [Fact]
    public async Task ContentAsPartList_IsJoined()
    {
        // Manche Server liefern message.content als Liste von Text-Teilen statt als String.
        var reply = new JsonObject
        {
            ["choices"] = new JsonArray
            {
                new JsonObject
                {
                    ["message"] = new JsonObject
                    {
                        ["content"] = new JsonArray
                        {
                            new JsonObject { ["type"] = "text", ["text"] = "{\"mo" },
                            new JsonObject { ["type"] = "text", ["text"] = "ves\":[]}" },
                        },
                    },
                    ["finish_reason"] = "stop",
                },
            },
        };
        _handler.Replies.Enqueue((HttpStatusCode.OK, reply.ToJsonString()));

        var result = await Client().ReadAsync(new byte[] { 1 }, "x", 4000);

        Assert.Equal("{\"moves\":[]}", result.Json);
    }
}
