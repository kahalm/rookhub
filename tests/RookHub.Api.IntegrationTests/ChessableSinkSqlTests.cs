using Microsoft.EntityFrameworkCore;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Die Byte-Deckel der Chessable-Roh-Senken gegen ECHTES MariaDB: gezählt wird per <c>LENGTH</c> (UTF-8-Bytes, so viel
/// liegt auf der Platte), und das als rohes SQL — InMemory rechnet dieselbe Summe in C# und sähe weder, ob die Abfrage
/// läuft, noch, ob sie Bytes statt Zeichen zählt.
/// </summary>
public class ChessableSinkSqlTests(ChessableSinkSqlFixture fixture)
    : IAsyncLifetime, IClassFixture<ChessableSinkSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [MySqlFact]
    public async Task AnonTotal_CountsUtf8BytesInSql()
    {
        await using (var db = fixture.Schema.NewContext())
            Assert.Equal(0, await new ChessableSinkBytes().AnonTotalAsync(db));   // leere Senke: COALESCE

        await using (var db = fixture.Schema.NewContext())
        {
            db.AnonymousChessableReviewLines.AddRange(
                new AnonymousChessableReviewLine { ChessableUid = "1", Bid = "1", Oid = "1", Json = "{\"x\":\"€€\"}" },   // 8 + 2 × 3
                new AnonymousChessableReviewLine { ChessableUid = "2", Bid = "1", Oid = "1", Json = "{}" });
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.Schema.NewContext())
            Assert.Equal(16, await new ChessableSinkBytes().AnonTotalAsync(db));
    }

    [MySqlFact]
    public async Task AnonUpsert_ByteCapHoldsAgainstMariaDb()
    {
        await using (var db = fixture.Schema.NewContext())
        {
            var svc = new ChessableReviewLineService(db, new PgnImportService(db)) { AnonBytesCap = 30 };
            // 14 Byte mit Mehrbyte-Zeichen, dann 16 Byte → genau am Deckel; die nächste Zeile passt nicht mehr.
            Assert.Equal(1, await svc.UpsertAnonBatchAsync("42", "1", new() { new ChessableReviewLineEntryDto { Oid = "1", Json = "{\"x\":\"€€\"}" } }));
            Assert.Equal(1, await svc.UpsertAnonBatchAsync("43", "1", new() { new ChessableReviewLineEntryDto { Oid = "1", Json = "{\"x\":\"aaaaaaaa\"}" } }));
            Assert.Equal(0, await svc.UpsertAnonBatchAsync("44", "1", new() { new ChessableReviewLineEntryDto { Oid = "1", Json = "{}" } }));
        }

        await using (var db = fixture.Schema.NewContext())
            Assert.Equal(2, await db.AnonymousChessableReviewLines.CountAsync());
    }
}

public sealed class ChessableSinkSqlFixture() : MariaDbClassFixture("csink", withApp: false);
