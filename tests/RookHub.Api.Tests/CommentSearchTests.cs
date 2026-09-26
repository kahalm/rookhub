using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>„Frag die Kommentare" (0.536.0): Zerlegen, Einbetten, Suchen (InMemory-Weg) und der Embedding-Client.</summary>
public class CommentSearchTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    // ── Zerlegen ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Chunks_HeaderPlusNumberedMoves_IntroAndBlackMoves()
    {
        var game = new LibraryGame
        {
            White = "Tal", Black = "Botvinnik", Event = "WCh", PlayedOn = new DateOnly(1960, 3, 15), Annotator = "Tal",
            Languages = "en",
            Pgn = "[White \"Tal\"]\n[Black \"Botvinnik\"]\n\n{A wild game.} 1. e4 c6 {The Caro-Kann.} 2. d4 d5 3. Nc3 dxe4 4. Nxe4 Bf5 {Main line.} *",
        };

        var chunk = Assert.Single(CommentChunks.Build(game));

        Assert.Equal(-1, chunk.FromPly);
        Assert.Equal(7, chunk.ToPly);
        Assert.StartsWith("Tal – Botvinnik, WCh 1960 (annotated by Tal)", chunk.Text);
        Assert.Contains("\nIntro: A wild game.", chunk.Text);
        Assert.Contains("\n1... c6: The Caro-Kann.", chunk.Text);
        Assert.Contains("\n4... Bf5: Main line.", chunk.Text);
    }

    [Fact]
    public void Chunks_SplitAtTheTargetLength()
    {
        var comment = new string('x', 500);
        var game = new LibraryGame
        {
            White = "A", Black = "B",
            Pgn = $"1. e4 {{{comment}}} e5 {{{comment}}} 2. Nf3 {{{comment}}} *",
        };

        var chunks = CommentChunks.Build(game);

        Assert.Equal(3, chunks.Count);
        Assert.Equal([0, 1, 2], chunks.Select(c => c.FromPly));
        Assert.All(chunks, c => Assert.StartsWith("A – B", c.Text));
    }

    [Fact]
    public void Chunks_NoComments_NoChunks()
        => Assert.Empty(CommentChunks.Build(new LibraryGame { White = "A", Black = "B", Pgn = "1. e4 e5 *" }));

    // ── Einbetten + Suchen ─────────────────────────────────────────────────────────────────────────

    private sealed class AxisEmbedder : ITextEmbedder
    {
        public bool IsConfigured { get; set; } = true;
        public string Model => "axis";
        public List<bool> Queries { get; } = new();
        public Task<float[][]?> EmbedAsync(IReadOnlyList<string> texts, bool query, CancellationToken ct = default)
        {
            Queries.Add(query);
            return Task.FromResult<float[][]?>(texts.Select(t =>
            {
                var v = new float[CommentEmbedding.Dimensions];
                v[t.Contains("back rank", StringComparison.OrdinalIgnoreCase) ? 0 : 1] = 1;
                return v;
            }).ToArray());
        }
    }

    private CommentSearchService Service(AxisEmbedder embedder)
        => new(_db, embedder, new LibraryGameService(_db, TestServices.GameAnalyses(_db)), NullLogger<CommentSearchService>.Instance);

    [Fact]
    public async Task EmbedThenSearch_FindsTheGame_GroupsByGame_AndSkipsAlreadyEmbedded()
    {
        _db.LibraryGames.AddRange(
            new LibraryGame { White = "Anna", Black = "Berta", CommentedPlies = 1, Score = 50, Pgn = "1. e4 {A quiet start.} e5 *" },
            new LibraryGame { White = "Carla", Black = "Dora", CommentedPlies = 2, Score = 40,
                Pgn = "1. e4 e5 2. Qh5 {The back rank is weak.} Nc6 {Still the back rank.} *" },
            new LibraryGame { White = "X", Black = "Y", CommentedPlies = 0, Score = 99, Pgn = "1. e4 e5 *" });
        await _db.SaveChangesAsync();
        var embedder = new AxisEmbedder();
        var service = Service(embedder);

        var first = await service.EmbedPendingAsync(100, 8, CancellationToken.None);
        Assert.Equal(2, first.Games);
        Assert.Equal(0, (await service.EmbedPendingAsync(100, 8, CancellationToken.None)).Games);
        Assert.All(_db.CommentEmbeddings, e => Assert.Equal(CommentEmbedding.Dimensions * 4, e.Vector.Length));

        var page = await service.SearchAsync(0, "back rank", 10);

        Assert.True(page.Available);
        Assert.Equal("Carla", page.Items[0].Game.White);
        Assert.Equal(2, page.Items.Count);           // gruppiert: eine Zeile je Partie
        Assert.Equal(2, page.Items[0].Matches[0].FromPly);
        Assert.DoesNotContain("Carla – Dora", page.Items[0].Matches[0].Text); // Kopfzeile nicht im Auszug
        Assert.True(embedder.Queries.Last());        // die Frage wurde als Frage eingebettet
    }

    [Fact]
    public async Task Search_WithoutModel_OrTooShort_ReturnsNothing()
    {
        var off = new AxisEmbedder { IsConfigured = false };
        Assert.False((await Service(off).SearchAsync(0, "back rank", 10)).Available);
        var on = new AxisEmbedder();
        Assert.Empty((await Service(on).SearchAsync(0, "ab", 10)).Items);
        Assert.Empty(on.Queries);
    }

    // ── Embedding-Client ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Embedder_AsksForTheDimensions_PrefixesQueries_TruncatesLongerAndNormalizes()
    {
        var handler = new ChatCompletionHandler();
        var longer = string.Join(",", Enumerable.Range(0, 1024).Select(i => i < 2 ? "3" : "0"));
        handler.Raw("{\"data\":[{\"index\":0,\"embedding\":[" + longer + "]}]}");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Embedding:BaseUrl"] = "http://spark/v1", ["Embedding:Model"] = "qwen3-embedding",
        }).Build();
        var embedder = new OpenAiTextEmbedder(new HttpClient(handler), config, NullLogger.Instance);

        var v = await embedder.EmbedAsync(["Grundreihe"], query: true);

        var body = handler.Requests.Single().Body;
        Assert.Equal("http://spark/v1/embeddings", handler.Requests[0].Url);
        Assert.Equal(512, (int?)body["dimensions"]);
        Assert.StartsWith("Instruct:", (string?)body["input"]![0]);
        Assert.EndsWith("Query: Grundreihe", (string?)body["input"]![0]);
        Assert.Equal(CommentEmbedding.Dimensions, v![0].Length);
        Assert.Equal(1.0, Math.Sqrt(v[0].Sum(x => (double)x * x)), 5);
        Assert.Equal(v[0][0], v[0][1], 5);
    }

    /// <summary>vLLM nimmt <c>dimensions</c> nur bei einem als Matryoshka gestarteten Modell (Spark, 2026-09-26:
    /// „does not support Matryoshka embeddings; dimensions must be unset") — dann ohne, gekürzt wird hier.</summary>
    [Fact]
    public async Task Embedder_DimensionsRejected_RetriesWithoutAndRemembers()
    {
        var full = "{\"data\":[{\"index\":0,\"embedding\":[" + string.Join(",", Enumerable.Repeat("1", 2560)) + "]}]}";
        var handler = new ChatCompletionHandler()
            .Fail(System.Net.HttpStatusCode.BadRequest,
                "{\"error\":{\"message\":\"Model 'qwen3-embedding-4b' does not support Matryoshka embeddings; dimensions must be unset\"}}")
            .Raw(full)
            .Raw(full);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Embedding:BaseUrl"] = "http://spark/v1", ["Embedding:Model"] = "qwen3-embedding-4b",
        }).Build();
        var embedder = new OpenAiTextEmbedder(new HttpClient(handler), config, NullLogger.Instance);

        var first = await embedder.EmbedAsync(["Grundreihe"], query: false);
        var second = await embedder.EmbedAsync(["Minoritätsangriff"], query: false);

        Assert.Equal(CommentEmbedding.Dimensions, first![0].Length);
        Assert.Equal(1.0, Math.Sqrt(first[0].Sum(x => (double)x * x)), 5);
        Assert.NotNull(second);
        Assert.Equal(3, handler.Requests.Count);                  // abgelehnt, ohne, und danach gleich ohne
        Assert.NotNull(handler.Requests[0].Body["dimensions"]);
        Assert.Null(handler.Requests[1].Body["dimensions"]);
        Assert.Null(handler.Requests[2].Body["dimensions"]);
    }

    /// <summary>Ohne eingestelltes Modell das erste mit „embed" im Namen — steht ein Sprachmodell davor, ginge die
    /// Anfrage sonst an das falsche.</summary>
    [Fact]
    public async Task Embedder_WithoutModel_PrefersAnEmbeddingModelFromTheList()
    {
        var handler = new ChatCompletionHandler()
            .Raw("{\"data\":[{\"id\":\"qwen3.5-122b\"},{\"id\":\"qwen3-embedding-4b\"}]}")
            .Raw("{\"data\":[{\"index\":0,\"embedding\":[" + string.Join(",", Enumerable.Repeat("1", 512)) + "]}]}");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Embedding:BaseUrl"] = "http://spark/v1",
        }).Build();
        var embedder = new OpenAiTextEmbedder(new HttpClient(handler), config, NullLogger.Instance);

        Assert.NotNull(await embedder.EmbedAsync(["x"], query: false));
        Assert.Equal("qwen3-embedding-4b", (string?)handler.Requests[1].Body["model"]);
        Assert.Equal("qwen3-embedding-4b", embedder.Model);
    }

    [Fact]
    public async Task Embedder_TooShortVectors_AreAnError()
    {
        var handler = new ChatCompletionHandler().Raw("{\"data\":[{\"index\":0,\"embedding\":[1,0,0]}]}");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Embedding:BaseUrl"] = "http://spark/v1", ["Embedding:Model"] = "m",
        }).Build();
        Assert.Null(await new OpenAiTextEmbedder(new HttpClient(handler), config, NullLogger.Instance).EmbedAsync(["x"], query: false));
    }

    [Fact]
    public void VectorMath_RoundTrip_AndCosineLikeMariaDb()
    {
        var v = new float[] { 1, 0, 0 };
        Assert.Equal(v, VectorMath.FromBytes(VectorMath.ToBytes(v)));
        Assert.Equal("0000803F0000000000000000", Convert.ToHexString(VectorMath.ToBytes(v)));   // wie x'0000803f…'
        Assert.Equal(0, VectorMath.CosineDistance(v, v), 6);
        Assert.Equal(1, VectorMath.CosineDistance(v, [0, 1, 0]), 6);
    }
}
