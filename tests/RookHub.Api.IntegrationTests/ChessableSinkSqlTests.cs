using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
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

    [MySqlFact]
    public async Task UserTotal_SumsAllThreeSinksInBytes()
    {
        int userId;
        await using (var db = fixture.Schema.NewContext())
        {
            var user = new AppUser { Username = "sink", PasswordHash = "x" };
            var other = new AppUser { Username = "sink2", PasswordHash = "x" };
            db.AppUsers.AddRange(user, other);
            await db.SaveChangesAsync();
            userId = user.Id;
            db.ChessableReviewLines.Add(new ChessableReviewLine { UserId = user.Id, Bid = "1", Oid = "1", Json = "{\"x\":\"€€\"}" });
            db.ChessableSessionMoves.Add(new ChessableSessionMove { UserId = user.Id, Bid = "1", Oid = "1", MovesJson = "[1]" });
            db.ChessableProblemMoves.Add(new ChessableProblemMove { UserId = user.Id, Bid = "1", Oid = "1" });   // ohne Zug-Detail
            db.ChessableSessionMoves.Add(new ChessableSessionMove { UserId = other.Id, Bid = "1", Oid = "1", MovesJson = "[1,2,3]" });
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.Schema.NewContext())
        {
            Assert.Equal(14 + 3 + 3 * ChessableSinkBytes.RowOverheadBytes, await new ChessableSinkBytes().UserTotalAsync(db, userId));
            Assert.Equal(0, await new ChessableSinkBytes().UserTotalAsync(db, 999_999));
        }
    }

    // ── Löschen, Retention, Übernahme und Merge ohne das JSON voll zu laden (A3-010) ──────────────────────────────
    // Geprüft am abgesetzten SQL: eine Spalte, die als `alias`.`Spalte` in einem SELECT steht, wurde geladen.

    private AppDbContext Recorded(CommandTextRecorder recorder) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(fixture.Schema.ConnectionString, DbServerVersion.Current)
            .AddInterceptors(recorder)
            .Options);

    /// <summary>Die Befehle, die <paramref name="column"/> aus <paramref name="table"/> LADEN.</summary>
    private static List<string> Reads(CommandTextRecorder recorder, string table, string column) =>
        recorder.Commands
            .Where(c => c.Contains($"FROM `{table}`", StringComparison.Ordinal)
                        && Regex.IsMatch(c, $@"SELECT\b[^;]*`\w+`\.`{column}`[^;]*FROM `{table}`", RegexOptions.Singleline))
            .ToList();

    private static string Dump(IEnumerable<string> commands) => string.Join("\n---\n", commands);

    private sealed class NoEmail : IEmailSender
    {
        public bool IsEnabled => false;
        public Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    [MySqlFact]
    public async Task DeleteAccount_RemovesChessableSinks_WithoutLoadingTheirJson()
    {
        int userId, otherId;
        await using (var db = fixture.Schema.NewContext())
        {
            var user = new AppUser { Username = "gone", PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw") };
            var other = new AppUser { Username = "stays", PasswordHash = "x" };
            db.AppUsers.AddRange(user, other);
            await db.SaveChangesAsync();
            userId = user.Id;
            otherId = other.Id;
            foreach (var id in new[] { userId, otherId })
            {
                db.ChessableReviewLines.Add(new ChessableReviewLine { UserId = id, Bid = "1", Oid = "1", Json = "{}" });
                db.ChessableSessionMoves.Add(new ChessableSessionMove { UserId = id, Bid = "1", Oid = "1", MovesJson = "[1]" });
                db.ChessableProblemMoves.Add(new ChessableProblemMove { UserId = id, Bid = "1", Oid = "1", ProblemMovesJson = "{}" });
            }
            await db.SaveChangesAsync();
        }

        var recorder = new CommandTextRecorder();
        await using (var db = Recorded(recorder))
            await new ProfileService(db, new BackgroundTaskQueue(), NullLogger<ProfileService>.Instance,
                new BookAdminService(db), new NoEmail(), new DiscordLinkService(new ConfigurationBuilder().Build()))
                .DeleteAccountAsync(userId, "pw");

        Assert.Empty(Reads(recorder, "ChessableReviewLines", "Json"));
        Assert.Empty(Reads(recorder, "ChessableSessionMoves", "MovesJson"));
        Assert.Empty(Reads(recorder, "ChessableProblemMoves", "ProblemMovesJson"));
        await using (var db = fixture.Schema.NewContext())
        {
            Assert.False(await db.ChessableReviewLines.AnyAsync(r => r.UserId == userId));
            Assert.False(await db.ChessableSessionMoves.AnyAsync(r => r.UserId == userId));
            Assert.False(await db.ChessableProblemMoves.AnyAsync(r => r.UserId == userId));
            Assert.Equal(1, await db.ChessableReviewLines.CountAsync(r => r.UserId == otherId));
            Assert.Equal(1, await db.ChessableSessionMoves.CountAsync(r => r.UserId == otherId));
            Assert.Equal(1, await db.ChessableProblemMoves.CountAsync(r => r.UserId == otherId));
        }
    }

    [MySqlFact]
    public async Task PruneAnon_DeletesInPortions_WithoutLoadingJson()
    {
        await using (var db = fixture.Schema.NewContext())
        {
            for (var i = 1; i <= 5; i++)
                db.AnonymousChessableReviewLines.Add(new AnonymousChessableReviewLine
                { ChessableUid = "1", Bid = "1", Oid = i.ToString(), Json = "{}", UpdatedAt = DateTime.UtcNow.AddDays(-120) });
            db.AnonymousChessableReviewLines.Add(new AnonymousChessableReviewLine
            { ChessableUid = "2", Bid = "1", Oid = "1", Json = "{}", UpdatedAt = DateTime.UtcNow.AddDays(-30) });
            db.AnonymousChessableReviewLines.Add(new AnonymousChessableReviewLine
            { ChessableUid = "3", Bid = "1", Oid = "1", Json = "{}", UpdatedAt = DateTime.UtcNow.AddDays(-30) });
            var user = new AppUser { Username = "linked", PasswordHash = "x" };
            db.AppUsers.Add(user);
            await db.SaveChangesAsync();
            db.ChessableCredentials.Add(new ChessableCredential { UserId = user.Id, EncryptedBearer = "enc", ChessableUid = "3" });
            await db.SaveChangesAsync();
        }

        var recorder = new CommandTextRecorder();
        await using (var db = Recorded(recorder))
        {
            var svc = new ChessableReviewLineService(db, new PgnImportService(db)) { DeleteChunkSize = 2 };
            Assert.Equal(5, await svc.PruneAnonOlderThanAsync(TimeSpan.FromDays(90)));
            Assert.Equal(1, await svc.PruneUnlinkedAnonOlderThanAsync(TimeSpan.FromDays(14)));   // uid 2, nicht die verknüpfte 3
        }

        Assert.True(Reads(recorder, "AnonymousChessableReviewLines", "Json").Count == 0, Dump(recorder.Commands));
        await using (var db = fixture.Schema.NewContext())
            Assert.Equal("3", (await db.AnonymousChessableReviewLines.SingleAsync()).ChessableUid);
    }

    [MySqlFact]
    public async Task ClaimAnon_ReadsKeysFirst_ThenJsonOnlyByIdInPortions()
    {
        int userId;
        await using (var db = fixture.Schema.NewContext())
        {
            var user = new AppUser { Username = "claimer", PasswordHash = "x" };
            db.AppUsers.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
            db.ChessableCredentials.Add(new ChessableCredential
            {
                UserId = user.Id, EncryptedBearer = "enc",
                CachedCoursesJson = "[{\"bid\":\"100\",\"name\":\"Kurs\"}]", CoursesCachedAt = DateTime.UtcNow,
            });
            for (var i = 1; i <= 5; i++)
                db.AnonymousChessableReviewLines.Add(new AnonymousChessableReviewLine
                { ChessableUid = "790927", Bid = "100", Oid = i.ToString(), Json = "{}" });
            await db.SaveChangesAsync();
        }

        var recorder = new CommandTextRecorder();
        await using (var db = Recorded(recorder))
        {
            var svc = new ChessableReviewLineService(db, new PgnImportService(db)) { JsonChunkSize = 2 };
            Assert.Equal(5, await svc.ClaimAnonForUidAsync(userId, "790927"));
        }

        var jsonReads = Reads(recorder, "AnonymousChessableReviewLines", "Json");
        Assert.Equal(3, jsonReads.Count);   // 2 + 2 + 1
        Assert.All(jsonReads, c => Assert.Matches(@"WHERE[^;]*`\w+`\.`Id`", c));
        await using (var db = fixture.Schema.NewContext())
        {
            Assert.Equal(0, await db.AnonymousChessableReviewLines.CountAsync());
            Assert.Equal(5, await db.ChessableReviewLines.CountAsync(r => r.UserId == userId));
        }
    }

    [MySqlFact]
    public async Task Merge_LoadsJsonOnlyForTheGaps()
    {
        int userId;
        await using (var db = fixture.Schema.NewContext())
        {
            var user = new AppUser { Username = "merger", PasswordHash = "x" };
            db.AppUsers.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
            var file = $"chessable-u{userId}-100.pgn";
            for (var i = 1; i <= 4; i++)
                db.ChessableReviewLines.Add(new ChessableReviewLine { UserId = userId, Bid = "100", Oid = i.ToString(), Json = "{}" });
            // oid 1 und 2 stehen schon im Buch — deren JSON braucht der Merge nicht.
            foreach (var oid in new[] { "1", "2" })
                db.BookPuzzles.Add(new BookPuzzle
                {
                    LineId = $"{file}:{oid}", BookFileName = file, ChessableOid = oid,
                    Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", Moves = "e2e4",
                });
            await db.SaveChangesAsync();
        }

        var recorder = new CommandTextRecorder();
        await using (var db = Recorded(recorder))
            Assert.Equal(0, await new ChessableReviewLineService(db, new PgnImportService(db)).MergeIntoCourseAsync(userId, "100"));

        var jsonReads = Reads(recorder, "ChessableReviewLines", "Json");
        Assert.True(jsonReads.Count == 1, Dump(recorder.Commands));
        Assert.Matches(@"WHERE[^;]*`\w+`\.`Oid`", jsonReads[0]);
    }
}

public sealed class ChessableSinkSqlFixture() : MariaDbClassFixture("csink", withApp: false);
