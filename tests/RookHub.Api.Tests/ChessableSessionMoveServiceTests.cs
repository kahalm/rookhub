using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Append der Sitzungszüge: jeder Durchlauf einer Linie wird als EIGENE Zeile abgelegt (kein
/// Upsert — Historie bleibt); Müll (kein Array, leeres Array, zu groß, kaputte oid) wird
/// ignoriert; der per-User-Deckel trimmt die ältesten Zeilen.
/// </summary>
public class ChessableSessionMoveServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ChessableSessionMoveService _svc;

    public ChessableSessionMoveServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _svc = new ChessableSessionMoveService(_db);
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
    public async Task Append_SameLineTwice_KeepsBothRows()
    {
        var u = await CreateUserAsync();

        var run1 = await _svc.AppendBatchAsync(u.Id, "228856", new()
        {
            new ChessableSessionMoveEntryDto { Oid = "36729913", Moves = Json("""[{"mid":0,"wrong":[]}]""") },
        });
        var run2 = await _svc.AppendBatchAsync(u.Id, "228856", new()
        {
            new ChessableSessionMoveEntryDto { Oid = "36729913", Moves = Json("""[{"mid":0,"wrong":["Nf3"]}]""") },
        });

        Assert.Equal(1, run1);
        Assert.Equal(1, run2);
        var rows = await _db.ChessableSessionMoves.OrderBy(s => s.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => { Assert.Equal(u.Id, r.UserId); Assert.Equal("228856", r.Bid); Assert.Equal("36729913", r.Oid); });
        Assert.Contains("Nf3", rows[1].MovesJson);
        Assert.DoesNotContain("Nf3", rows[0].MovesJson);
    }

    [Fact]
    public async Task Append_InvalidEntries_AreIgnored()
    {
        var u = await CreateUserAsync();

        var stored = await _svc.AppendBatchAsync(u.Id, "228856", new()
        {
            new ChessableSessionMoveEntryDto { Oid = "36729913" },                                  // moves fehlt
            new ChessableSessionMoveEntryDto { Oid = "36729914", Moves = Json("[]") },              // leeres Array
            new ChessableSessionMoveEntryDto { Oid = "36729915", Moves = Json("""{"mid":0}""") },   // Objekt statt Array
            new ChessableSessionMoveEntryDto { Oid = "not-a-number", Moves = Json("[1]") },         // kaputte oid
            new ChessableSessionMoveEntryDto { Oid = "36729916", Moves = Json($"[\"{new string('x', ChessableSessionMoveService.MaxJsonLength)}\"]") }, // zu groß
            new ChessableSessionMoveEntryDto { Oid = "36729917", Moves = Json("""[{"mid":0}]""") }, // gültig
        });

        Assert.Equal(1, stored);
        var row = await _db.ChessableSessionMoves.SingleAsync();
        Assert.Equal("36729917", row.Oid);
    }

    [Fact]
    public async Task Append_OverPerUserCap_TrimsOldestRows()
    {
        var u = await CreateUserAsync();
        var other = new AppUser { Username = "u2", PasswordHash = "x", CreatedAt = DateTime.UtcNow };
        _db.AppUsers.Add(other);
        var svc = new ChessableSessionMoveService(_db) { MaxRowsPerUser = 5 };
        // Bestand exakt am Deckel + eine Zeile eines ANDEREN Users (darf nicht angetastet werden).
        for (var i = 0; i < 5; i++)
            _db.ChessableSessionMoves.Add(new ChessableSessionMove { UserId = u.Id, Bid = "1", Oid = "1", MovesJson = "[1]" });
        _db.ChessableSessionMoves.Add(new ChessableSessionMove { User = other, Bid = "1", Oid = "1", MovesJson = "[1]" });
        await _db.SaveChangesAsync();

        await svc.AppendBatchAsync(u.Id, "228856", new()
        {
            new ChessableSessionMoveEntryDto { Oid = "36729913", Moves = Json("""[{"mid":0}]""") },
        });

        Assert.Equal(5, await _db.ChessableSessionMoves.CountAsync(s => s.UserId == u.Id));
        Assert.Equal(1, await _db.ChessableSessionMoves.CountAsync(s => s.UserId == other.Id));
        // Die neueste Zeile hat überlebt, getrimmt wurde am ALTEN Ende.
        Assert.True(await _db.ChessableSessionMoves.AnyAsync(s => s.UserId == u.Id && s.Oid == "36729913"));
    }
}
