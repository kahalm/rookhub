using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// „Frag die Kommentare" (0.536.0): semantische Suche über die Anmerkungen des Rohbestands (<see cref="LibraryGame"/>).
/// Die Kommentare liegen als Textstücke mit Vektor in <see cref="CommentEmbedding"/> (<see cref="CommentChunks"/>,
/// eingebettet von <see cref="EmbedPendingAsync"/> — Schritt <c>embed</c> in <c>tools/LibraryImport</c>); eine Frage wird
/// mit demselben Modell eingebettet (<see cref="ITextEmbedder"/>, mehrsprachig: eine deutsche Frage findet englische
/// Kommentare) und über den Kosinus-Index von MariaDB (≥ 11.7, <c>VEC_DISTANCE_COSINE</c>) gesucht.
/// </summary>
public sealed class CommentSearchService
{
    /// <summary>So viele Stücke holt die Vektorsuche; daraus werden die Partien gruppiert.</summary>
    public const int Candidates = 200;
    public const int MaxTake = 50;

    private readonly AppDbContext _db;
    private readonly ITextEmbedder _embedder;
    private readonly LibraryGameService _library;
    private readonly ILogger<CommentSearchService> _logger;

    public CommentSearchService(AppDbContext db, ITextEmbedder embedder, LibraryGameService library,
        ILogger<CommentSearchService> logger)
    {
        _db = db;
        _embedder = embedder;
        _library = library;
        _logger = logger;
    }

    public bool Available => _embedder.IsConfigured;

    /// <summary>Eine Zeile der Vektorsuche — eine Klasse mit Settern, weil <c>SqlQueryRaw</c> sie so befüllt.</summary>
    private sealed class Hit
    {
        public long Id { get; set; }
        public int LibraryGameId { get; set; }
        public int FromPly { get; set; }
        public string Text { get; set; } = string.Empty;
        public double Distance { get; set; }
    }

    public async Task<LibrarySemanticPageDto> SearchAsync(int userId, string? query, int take, CancellationToken ct = default)
    {
        var page = new LibrarySemanticPageDto { Available = Available };
        if (!Available) return page;
        page.Indexed = await _db.CommentEmbeddings.LongCountAsync(ct);
        var q = (query ?? "").Trim();
        if (q.Length < 3 || page.Indexed == 0) return page;
        take = Math.Clamp(take, 1, MaxTake);

        var vectors = await _embedder.EmbedAsync(new[] { q }, query: true, ct);
        if (vectors is not { Length: 1 }) return page;
        var hits = await NearestAsync(vectors[0], Candidates, ct);

        var byGame = hits.GroupBy(h => h.LibraryGameId)
            .Select(g => (GameId: g.Key, Best: g.Min(h => h.Distance), Hits: g.OrderBy(h => h.Distance).Take(2).ToList()))
            .OrderBy(g => g.Best).Take(take).ToList();
        var ids = byGame.Select(g => g.GameId).ToList();
        var games = await _db.LibraryGames.AsNoTracking()
            .Where(g => ids.Contains(g.Id) && g.Status != LibraryGameStatus.Duplicate && g.Status != LibraryGameStatus.Rejected)
            .Select(g => new LibraryGameDto
            {
                Id = g.Id, White = g.White, Black = g.Black, WhiteElo = g.WhiteElo, BlackElo = g.BlackElo,
                Result = g.Result, Event = g.Event, PlayedOn = g.PlayedOn, Eco = g.Eco,
                PlyCount = g.PlyCount, Annotator = g.Annotator, CommentedPlies = g.CommentedPlies,
                CommentChars = g.CommentChars, Languages = g.Languages, Score = g.Score, SourceTitle = g.SourceTitle,
            })
            .ToListAsync(ct);
        await _library.MarkKnownAsync(userId, games, ct);
        var gameById = games.ToDictionary(g => g.Id);
        foreach (var g in byGame)
        {
            if (!gameById.TryGetValue(g.GameId, out var dto)) continue;
            page.Items.Add(new LibrarySemanticHitDto
            {
                Game = dto,
                Matches = g.Hits.Select(h => new LibrarySemanticMatchDto
                {
                    FromPly = h.FromPly, Text = Snippet(h.Text), Score = Math.Round(Math.Max(0, 1 - h.Distance), 3),
                }).ToList(),
            });
        }
        return page;
    }

    /// <summary>Die nächsten Stücke: in MariaDB über den Vektor-Index (die ORDER-BY-Form muss genau
    /// <c>VEC_DISTANCE_COSINE(Spalte, Konstante)</c> sein, sonst wird er nicht benutzt), sonst (InMemory, Tests) in C#.</summary>
    private async Task<List<Hit>> NearestAsync(float[] query, int k, CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
        {
            var all = await _db.CommentEmbeddings.AsNoTracking().ToListAsync(ct);
            return all.Select(e => new Hit
                {
                    Id = e.Id, LibraryGameId = e.LibraryGameId, FromPly = e.FromPly, Text = e.Text,
                    Distance = VectorMath.CosineDistance(query, VectorMath.FromBytes(e.Vector)),
                })
                .OrderBy(h => h.Distance).Take(k).ToList();
        }
        var q = new MySqlParameter("@q", MySqlDbType.VarBinary) { Value = VectorMath.ToBytes(query) };
        var limit = new MySqlParameter("@k", k);
        return await _db.Database.SqlQueryRaw<Hit>(
            // `Vector` in Backticks: VECTOR ist in MariaDB ein Schlüsselwort.
            "SELECT `Id`, `LibraryGameId`, `FromPly`, `Text`, VEC_DISTANCE_COSINE(`Vector`, @q) AS `Distance` " +
            "FROM `CommentEmbeddings` ORDER BY VEC_DISTANCE_COSINE(`Vector`, @q) LIMIT @k", q, limit).ToListAsync(ct);
    }

    private static string Snippet(string text)
    {
        // Die Kopfzeile steht in der Trefferzeile ohnehin — der Auszug zeigt die Kommentare.
        var nl = text.IndexOf('\n');
        var body = nl >= 0 ? text[(nl + 1)..] : text;
        return body.Length <= 420 ? body : body[..420] + "…";
    }

    // ── Vorbereiten (tools/LibraryImport embed) ───────────────────────────────────────────────────

    public sealed record EmbedResult(int Games, int Chunks, int Failed);

    /// <summary>
    /// Bettet die Kommentare von bis zu <paramref name="maxGames"/> noch nicht eingebetteten Partien ein (kommentierte
    /// zuerst nach Note). Je Aufruf ans Modell <paramref name="batch"/> Stücke; eine Partie wird ganz oder gar nicht
    /// gespeichert, damit „schon eingebettet" (= es gibt ein Stück) nie eine halbe Partie meint.
    /// </summary>
    public async Task<EmbedResult> EmbedPendingAsync(int maxGames, int batch, CancellationToken ct)
    {
        if (!Available) return new(0, 0, 0);
        var ids = await _db.LibraryGames.AsNoTracking()
            .Where(g => g.CommentedPlies > 0 && g.Status != LibraryGameStatus.Duplicate && g.Status != LibraryGameStatus.Rejected
                && !_db.CommentEmbeddings.Any(e => e.LibraryGameId == g.Id))
            .OrderByDescending(g => g.Score).ThenBy(g => g.Id)
            .Select(g => g.Id).Take(maxGames).ToListAsync(ct);
        int games = 0, chunksSaved = 0, failed = 0;
        foreach (var slice in ids.Chunk(Math.Max(1, batch / 4)))
        {
            var rows = await _db.LibraryGames.AsNoTracking().Where(g => slice.Contains(g.Id)).ToListAsync(ct);
            var pending = rows.Select(g => (Game: g, Chunks: CommentChunks.Build(g))).Where(x => x.Chunks.Count > 0).ToList();
            var texts = pending.SelectMany(p => p.Chunks.Select(c => c.Text)).ToList();
            var vectors = new List<float[]>();
            foreach (var part in texts.Chunk(Math.Max(1, batch)))
            {
                var v = await _embedder.EmbedAsync(part, query: false, ct);
                if (v == null) { vectors.Clear(); break; }
                vectors.AddRange(v);
            }
            if (vectors.Count != texts.Count)
            {
                failed += pending.Count;
                _logger.LogWarning("Embedding: {Count} Partien nicht eingebettet (Modell antwortete nicht vollständig)", pending.Count);
                continue;
            }
            var i = 0;
            foreach (var (game, chunks) in pending)
            {
                foreach (var c in chunks)
                    _db.CommentEmbeddings.Add(new CommentEmbedding
                    {
                        LibraryGameId = game.Id, FromPly = c.FromPly, ToPly = c.ToPly, Text = c.Text,
                        Vector = VectorMath.ToBytes(vectors[i++]), Model = _embedder.Model,
                    });
                games++;
                chunksSaved += chunks.Count;
            }
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
        }
        return new(games, chunksSaved, failed);
    }
}
