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
        // gpt-oss ignoriert den Vorlagen-Schalter und hört auf reasoning_effort — beide gehen mit (0.597.2).
        Assert.Equal("low", (string?)body["reasoning_effort"]);
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
        Assert.Null(request.Body["reasoning_effort"]);
        Assert.Contains("items", request.Body["response_format"]!["json_schema"]!["schema"]!["required"]!.AsArray().Select(n => (string?)n));
        Assert.Equal("gpt-oss-120b", client.TranslationModel);
    }

    [Fact]
    public async Task Reply_LosesTheInvisibleTypographyOfGptOss_ButKeepsDashes()
    {
        // gpt-oss setzt geschützte Bindestriche, weiche Trennstriche und schmale geschützte Leerzeichen — keine Quelle
        // enthält die; sie brechen Suche und Kopieren. Gedankenstriche stehen auch in den Quellen und bleiben.
        _handler.Reply("{\"items\":[{\"ply\":1,\"text\":\"h\u2011Bauer, Bewer\u00ADtung \u22120,5 nach 12\u202Fs \u2014 g\u2010Linie\"}]}");
        var client = Client(Config(("TextLlm:BaseUrl", "http://spark/v1"), ("TextLlm:Model", "openai/gpt-oss-120b")));

        var json = await client.TranslateCommentsJsonAsync("sys", "{}");

        Assert.Equal("{\"items\":[{\"ply\":1,\"text\":\"h-Bauer, Bewertung -0,5 nach 12 s \u2014 g-Linie\"}]}", json);
        Assert.Same("plain", OpenAiJsonClient.PlainTypography("plain")); // ohne Fund keine Kopie
    }

    [Fact]
    public async Task StalledStream_IsGivenUpAfterTheIdleTimeout_InsteadOfWaitingForever()
    {
        // 01.10.2026: die Spark nahm Anfragen an, schickte nichts mehr und liess die Verbindung offen — Bibliothek und
        // Kurse standen sieben Stunden still, ohne eine Fehlerzeile. Der HttpClient-Timeout deckt bei einem Strom nur
        // die Kopfzeilen; die Frist gilt deshalb je gelesener Zeile.
        _handler.Stall();
        var client = Client(Config(("TextLlm:BaseUrl", "http://spark/v1"), ("TextLlm:Model", "m"),
            ("TextLlm:StreamIdleSeconds", "0.2")));

        var call = client.TranslateCommentsJsonAsync("sys", "{}");

        Assert.Null(await call.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task StalledStream_ACancelledCallStillCancels_NotATimeout()
    {
        _handler.Stall();
        var client = Client(Config(("TextLlm:BaseUrl", "http://spark/v1"), ("TextLlm:Model", "m"),
            ("TextLlm:StreamIdleSeconds", "30")));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.TranslateCommentsJsonAsync("sys", "{}", cts.Token).WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Unreachable_AfterThreeTransportFailuresInARow_AndAnyAnswerClearsIt()
    {
        var client = Client(Config(("TextLlm:BaseUrl", "http://spark/v1"), ("TextLlm:Model", "m")));
        Assert.False(client.IsUnreachable);

        // Ein Server, der antwortet (auch mit 500 oder einem abgeschnittenen Text), ist erreichbar.
        _handler.Fail(System.Net.HttpStatusCode.InternalServerError).Fail(System.Net.HttpStatusCode.InternalServerError)
            .Fail(System.Net.HttpStatusCode.InternalServerError).Reply("{\"hint1\":\"a\"", finish: "length");
        for (var i = 0; i < 4; i++) Assert.Null(await client.GenerateHintsJsonAsync("s", "u"));
        Assert.False(client.IsUnreachable);

        // Verbindung abgelehnt: erst der dritte Fehlschlag in Folge zaehlt als „weg".
        _handler.Refuse().Refuse();
        Assert.Null(await client.GenerateHintsJsonAsync("s", "u"));
        Assert.Null(await client.GenerateHintsJsonAsync("s", "u"));
        Assert.False(client.IsUnreachable);
        _handler.Fail(System.Net.HttpStatusCode.ServiceUnavailable);   // der Proxy antwortet, das Modell dahinter nicht
        Assert.Null(await client.GenerateHintsJsonAsync("s", "u"));
        Assert.True(client.IsUnreachable);

        // Die naechste Antwort — gleich welche — hebt es auf.
        _handler.Reply("{\"hint1\":\"a\",\"hint2\":\"b\",\"hint3\":\"c\"}");
        Assert.NotNull(await client.GenerateHintsJsonAsync("s", "u"));
        Assert.False(client.IsUnreachable);
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

    // ── Gleichzeitige Anfragen (A6-005) ────────────────────────────────────────────────────────────

    /// <summary>Hält jede Anfrage fest, bis <see cref="Release"/> fällt, und merkt sich, wie viele zugleich drin waren.</summary>
    private sealed class BlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _inFlight;
        private int _maxInFlight;
        public int InFlight => Volatile.Read(ref _inFlight);
        public int MaxInFlight => Volatile.Read(ref _maxInFlight);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var now = Interlocked.Increment(ref _inFlight);
            int seen;
            while ((seen = Volatile.Read(ref _maxInFlight)) < now && Interlocked.CompareExchange(ref _maxInFlight, now, seen) != seen) { }
            try { await Release.Task.WaitAsync(ct); }
            finally { Interlocked.Decrement(ref _inFlight); }
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(ChatCompletionHandler.Stream("{\"hint1\":\"a\",\"hint2\":\"b\",\"hint3\":\"c\"}", "stop", 1, 1),
                    System.Text.Encoding.UTF8, "text/event-stream"),
            };
        }
    }

    [Theory]
    [InlineData("2", 2)]
    [InlineData(null, OpenAiJsonClient.DefaultMaxConcurrent)]
    public async Task HoechstensMaxConcurrentAnfragenZugleich_DieUebrigenWarten(string? configured, int cap)
    {
        var handler = new BlockingHandler();
        var values = new List<(string, string)> { ("TextLlm:BaseUrl", "http://spark/v1"), ("TextLlm:Model", "m") };
        if (configured != null) values.Add(("TextLlm:MaxConcurrent", configured));
        var client = new OpenAiJsonClient(new HttpClient(handler), Config(values.ToArray()), NullLogger.Instance);

        var calls = Enumerable.Range(0, cap + 3).Select(i => client.GenerateHintsJsonAsync("s", "u" + i)).ToList();
        for (var i = 0; i < 200 && handler.InFlight < cap; i++) await Task.Delay(10);
        await Task.Delay(100);   // Zeit für eine Anfrage zu viel, falls die Grenze fehlt
        Assert.Equal(cap, handler.InFlight);

        handler.Release.SetResult();
        var answers = await Task.WhenAll(calls);
        Assert.All(answers, a => Assert.Equal("{\"hint1\":\"a\",\"hint2\":\"b\",\"hint3\":\"c\"}", a));
        Assert.Equal(cap, handler.MaxInFlight);
    }
}
