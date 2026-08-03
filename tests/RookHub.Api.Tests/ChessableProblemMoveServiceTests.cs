using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Upsert der „schwierigen Züge": getList-Batches (nur nHard) und getGame-Batches (Zug-Details +
/// lastReviewed) ergänzen sich, ohne die Felder des jeweils anderen zu überschreiben; leeres
/// thisUser-Objekt löscht alte Fehlzüge; Müll (kein Objekt, zu groß, kaputte oid) wird ignoriert.
/// </summary>
public class ChessableProblemMoveServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ChessableProblemMoveService _svc;

    public ChessableProblemMoveServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _svc = new ChessableProblemMoveService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<AppUser> CreateUserAsync()
    {
        var user = new AppUser { Username = "u1", PasswordHash = "x", CreatedAt = DateTime.UtcNow };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task Upsert_ListThenGame_FieldsComplementEachOther()
    {
        var u = await CreateUserAsync();

        // 1) Kapitel-Liste: nur nHard.
        await _svc.UpsertBatchAsync(u.Id, "128648", new()
        {
            new ChessableProblemMoveEntryDto { Oid = "20733115", NHard = 9 },
        });
        var row = await _db.ChessableProblemMoves.SingleAsync();
        Assert.Equal(9, row.NHard);
        Assert.Null(row.ProblemMovesJson);

        // 2) getGame: Zug-Details + lastReviewed — nHard bleibt stehen.
        await _svc.UpsertBatchAsync(u.Id, "128648", new()
        {
            new ChessableProblemMoveEntryDto
            {
                Oid = "20733115",
                ProblemMoves = Json("""{"4":{"b":[{"move":"dxe4","total":1}]}}"""),
                LastReviewed = "2026-06-13 13:29:39",
            },
        });
        row = await _db.ChessableProblemMoves.SingleAsync();
        Assert.Equal(9, row.NHard);
        Assert.Contains("dxe4", row.ProblemMovesJson);
        Assert.Equal(new DateTime(2026, 6, 13, 13, 29, 39, DateTimeKind.Utc), row.LastReviewedAt);

        // 3) Erneute Kapitel-Liste (nHard gesunken) — Zug-Details bleiben stehen.
        await _svc.UpsertBatchAsync(u.Id, "128648", new()
        {
            new ChessableProblemMoveEntryDto { Oid = "20733115", NHard = 2 },
        });
        row = await _db.ChessableProblemMoves.SingleAsync();
        Assert.Equal(2, row.NHard);
        Assert.Contains("dxe4", row.ProblemMovesJson);
    }

    [Fact]
    public async Task Upsert_EmptyThisUser_ClearsOldMistakes()
    {
        var u = await CreateUserAsync();
        await _svc.UpsertBatchAsync(u.Id, "128648", new()
        {
            new ChessableProblemMoveEntryDto { Oid = "1", ProblemMoves = Json("""{"4":{"b":[]}}""") },
        });
        await _svc.UpsertBatchAsync(u.Id, "128648", new()
        {
            new ChessableProblemMoveEntryDto { Oid = "1", ProblemMoves = Json("{}") },
        });
        Assert.Equal("{}", (await _db.ChessableProblemMoves.SingleAsync()).ProblemMovesJson);
    }

    [Fact]
    public async Task Upsert_IgnoresGarbage()
    {
        var u = await CreateUserAsync();
        var written = await _svc.UpsertBatchAsync(u.Id, "128648", new()
        {
            new ChessableProblemMoveEntryDto { Oid = "kein-digit", NHard = 3 },
            new ChessableProblemMoveEntryDto { Oid = "", NHard = 3 },
            new ChessableProblemMoveEntryDto { Oid = "7", ProblemMoves = Json("[1,2]") },   // kein Objekt
            new ChessableProblemMoveEntryDto { Oid = "7", LastReviewed = "never" },
        });
        Assert.Equal(1, written);   // die zwei „7"-Einträge kollabieren auf den letzten
        var row = await _db.ChessableProblemMoves.SingleAsync();
        Assert.Equal("7", row.Oid);
        Assert.Null(row.ProblemMovesJson);    // Array wurde verworfen
        Assert.Null(row.LastReviewedAt);      // "never" → null
    }

    [Fact]
    public void NormalizeJson_RejectsOversized()
    {
        var big = "{\"a\":\"" + new string('x', ChessableProblemMoveService.MaxJsonLength) + "\"}";
        Assert.Null(ChessableProblemMoveService.NormalizeJson(Json(big)));
        Assert.Equal("{}", ChessableProblemMoveService.NormalizeJson(Json("{}")));
    }

    [Fact]
    public void ParseLastReviewed_HandlesNeverAndTimestamps()
    {
        Assert.Null(ChessableProblemMoveService.ParseLastReviewed("never"));
        Assert.Null(ChessableProblemMoveService.ParseLastReviewed(null));
        Assert.Null(ChessableProblemMoveService.ParseLastReviewed("unfug"));
        Assert.Equal(new DateTime(2024, 7, 29, 12, 28, 44, DateTimeKind.Utc),
            ChessableProblemMoveService.ParseLastReviewed("2024-07-29 12:28:44"));
    }
}
