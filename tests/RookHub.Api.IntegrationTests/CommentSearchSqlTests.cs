using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// „Frag die Kommentare" (0.536.0) gegen echtes MariaDB: der Vektor geht als <c>byte[]</c> in eine Spalte
/// <c>VECTOR(512)</c>, und die Suche läuft als rohes SQL über <c>VEC_DISTANCE_COSINE</c> mit dem Kosinus-Index — beides
/// sieht InMemory nicht (dort rechnet der Dienst in C#). Die CI-MariaDB (<c>mariadb:11</c>) muss dafür ≥ 11.7 sein.
/// </summary>
public class CommentSearchSqlTests(CommentSearchFixture fixture) : IAsyncLifetime, IClassFixture<CommentSearchFixture>
{
    private readonly List<AppDbContext> _contexts = new();

    public async Task InitializeAsync() => await fixture.ResetAsync();

    public async Task DisposeAsync()
    {
        foreach (var db in _contexts) await db.DisposeAsync();
    }

    private AppDbContext Fresh()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(fixture.Schema.ConnectionString, new MySqlServerVersion(new Version(11, 0, 0))).Options);
        _contexts.Add(db);
        return db;
    }

    /// <summary>Thema „Grundreihe" → Achse 0, alles andere → Achse 1.</summary>
    private sealed class AxisEmbedder : ITextEmbedder
    {
        public bool IsConfigured => true;
        public string Model => "axis";
        public Task<float[][]?> EmbedAsync(IReadOnlyList<string> texts, bool query, CancellationToken ct = default)
            => Task.FromResult<float[][]?>(texts.Select(t =>
            {
                var v = new float[CommentEmbedding.Dimensions];
                v[t.Contains("back rank", StringComparison.OrdinalIgnoreCase) ? 0 : 1] = 1;
                return v;
            }).ToArray());
    }

    private static CommentSearchService Search(AppDbContext db)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Encryption:Key"] = "TestEncryptionKey32CharsLong!!!!",
        }).Build();
        var analyses = new GameAnalysisService(db, new AnalysisJobService(db, new EncryptionService(config)),
            new CommentSetService(db, NullLogger<CommentSetService>.Instance), NullLogger<GameAnalysisService>.Instance);
        return new CommentSearchService(db, new AxisEmbedder(), new LibraryGameService(db, analyses),
            NullLogger<CommentSearchService>.Instance);
    }

    [MySqlFact]
    public async Task Embed_StoresVectorsInTheVectorColumn_AndSearchFindsTheRightGame()
    {
        var db = Fresh();
        db.LibraryGames.AddRange(
            new LibraryGame
            {
                White = "Anna", Black = "Berta", CommentedPlies = 1, Score = 50,
                Pgn = "[White \"Anna\"]\n[Black \"Berta\"]\n\n1. e4 {A quiet start.} e5 2. Nf3 Nc6 *",
            },
            new LibraryGame
            {
                White = "Carla", Black = "Dora", CommentedPlies = 1, Score = 40,
                Pgn = "[White \"Carla\"]\n[Black \"Dora\"]\n\n1. e4 e5 2. Qh5 {The back rank is weak later on.} Nc6 *",
            });
        await db.SaveChangesAsync();

        var result = await Search(db).EmbedPendingAsync(100, 16, CancellationToken.None);
        Assert.Equal(2, result.Games);

        var page = await Search(Fresh()).SearchAsync(0, "back rank weakness", 10);

        Assert.Equal(2, page.Indexed);
        Assert.Equal("Carla", page.Items[0].Game.White);
        Assert.Equal(2, page.Items[0].Matches[0].FromPly);
        Assert.Contains("back rank", page.Items[0].Matches[0].Text);
        Assert.True(page.Items[0].Matches[0].Score > page.Items[1].Matches[0].Score);
    }
}
