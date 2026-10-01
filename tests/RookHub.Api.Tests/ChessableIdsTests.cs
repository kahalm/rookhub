using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// EINE Form fuer Chessable-Kennungen (<see cref="ChessableIds"/>, Codereview 2026-09-29, A3-013). Vorher galten fuer
/// die oid zwei Regeln: „int &gt; 0" im Extension-Ingest/Linien-Cache/piratechess, „hoechstens 32 Ziffern" in den
/// Senken — dort waren „00123" und „123" zwei Zeilen fuer dieselbe Linie, und eine 20-stellige oid wurde gespeichert,
/// fiel spaeter aber still aus dem Cache-Weg.
/// </summary>
public class ChessableIdsTests : IDisposable
{
    private readonly AppDbContext _db;

    public ChessableIdsTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    // Spiegel von piratechess BrowserCourseAssembler.TryParseOid:
    //   int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out oid) && oid > 0
    [Theory]
    [InlineData("1", "1")]
    [InlineData("36730415", "36730415")]
    [InlineData("2147483647", "2147483647")]
    [InlineData("00123", "123")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("0", null)]
    [InlineData("-1", null)]
    [InlineData("+1", null)]
    [InlineData(" 1", null)]
    [InlineData("1 ", null)]
    [InlineData("1.0", null)]
    [InlineData("1e3", null)]
    [InlineData("2147483648", null)]
    [InlineData("12345678901234567890", null)]
    [InlineData("١٢٣", null)]   // arabisch-indische Ziffern: kein ASCII
    [InlineData("abc", null)]
    public void Oid_RegelWiePiratechess_KanonischOhneFuehrendeNullen(string? raw, string? canonical)
    {
        Assert.Equal(canonical is not null, ChessableIds.TryParseOid(raw, out _));
        Assert.Equal(canonical, ChessableIds.CanonicalOid(raw));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("228856", true)]
    [InlineData("123456789012", true)]
    [InlineData("1234567890123", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData(" ", false)]
    [InlineData("12a", false)]
    [InlineData("-1", false)]
    public void Bid_HoechstensZwoelfAsciiZiffern(string? bid, bool valid) =>
        Assert.Equal(valid, ChessableIds.IsValidBid(bid));

    private async Task<AppUser> CreateUserAsync()
    {
        var user = new AppUser { Username = "ids", PasswordHash = "x", CreatedAt = DateTime.UtcNow };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task ReviewLines_FuehrendeNullen_DieselbeZeile_UeberlangeOidAbgewiesen()
    {
        var u = await CreateUserAsync();
        var svc = new ChessableReviewLineService(_db, new PgnImportService(_db));

        await svc.UpsertBatchAsync(u.Id, "228856", new() { new() { Oid = "00123", Json = "{\"v\":1}" } });
        await svc.UpsertBatchAsync(u.Id, "228856", new() { new() { Oid = " 123 ", Json = "{\"v\":2}" } });
        var stored = await svc.UpsertBatchAsync(u.Id, "228856", new() { new() { Oid = "12345678901234567890", Json = "{\"v\":3}" } });

        Assert.Equal(0, stored);
        var row = await _db.ChessableReviewLines.SingleAsync();
        Assert.Equal("123", row.Oid);
        Assert.Equal("{\"v\":2}", row.Json);
    }

    [Fact]
    public async Task AnonReviewLines_FuehrendeNullen_DieselbeZeile()
    {
        var svc = new ChessableReviewLineService(_db, new PgnImportService(_db));

        await svc.UpsertAnonBatchAsync("42", "228856", new() { new() { Oid = "0077", Json = "{\"v\":1}" } });
        await svc.UpsertAnonBatchAsync("42", "228856", new() { new() { Oid = "77", Json = "{\"v\":2}" } });
        await svc.UpsertAnonBatchAsync("42", "228856", new() { new() { Oid = "0", Json = "{\"v\":3}" } });

        var row = await _db.AnonymousChessableReviewLines.SingleAsync();
        Assert.Equal("77", row.Oid);
        Assert.Equal("{\"v\":2}", row.Json);
    }

    [Fact]
    public async Task ProblemMoves_FuehrendeNullen_DieselbeZeile_UeberlangeOidAbgewiesen()
    {
        var u = await CreateUserAsync();
        var svc = new ChessableProblemMoveService(_db);

        await svc.UpsertBatchAsync(u.Id, "128648", new() { new() { Oid = "020733115", NHard = 3 } });
        await svc.UpsertBatchAsync(u.Id, "128648", new() { new() { Oid = "20733115", NHard = 9 } });
        var written = await svc.UpsertBatchAsync(u.Id, "128648", new() { new() { Oid = "99999999999999999999", NHard = 1 } });

        Assert.Equal(0, written);
        var row = await _db.ChessableProblemMoves.SingleAsync();
        Assert.Equal("20733115", row.Oid);
        Assert.Equal(9, row.NHard);
    }

    [Fact]
    public async Task SessionMoves_SpeichernKanonischeOid_UeberlangeOidAbgewiesen()
    {
        var u = await CreateUserAsync();
        var svc = new ChessableSessionMoveService(_db);

        var stored = await svc.AppendBatchAsync(u.Id, "228856", new()
        {
            new ChessableSessionMoveEntryDto { Oid = "0036729913", Moves = Json("""[{"mid":0,"wrong":[]}]""") },
            new ChessableSessionMoveEntryDto { Oid = "12345678901234567890", Moves = Json("""[{"mid":0,"wrong":[]}]""") },
        });

        Assert.Equal(1, stored);
        Assert.Equal("36729913", (await _db.ChessableSessionMoves.SingleAsync()).Oid);
    }
}
