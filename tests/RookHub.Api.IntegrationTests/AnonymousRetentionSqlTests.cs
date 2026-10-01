using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Retention der anonymen Endless-Spielstände gegen ECHTES MariaDB (A8-002): gelöscht wird portionsweise über die Ids,
/// ohne die LONGTEXT-/TEXT-Spalten zu laden. InMemory kennt kein ExecuteDelete und liefe über den Tracker — ob das SQL
/// den Spielstand liest, sieht man nur hier.
/// </summary>
public class AnonymousRetentionSqlTests(AnonymousRetentionSqlFixture fixture)
    : IAsyncLifetime, IClassFixture<AnonymousRetentionSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

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

    [MySqlFact]
    public async Task PruneEndless_DeletesInPortions_WithoutLoadingTheGameState()
    {
        var old = DateTime.UtcNow.AddDays(-90);
        int userId;
        await using (var db = fixture.Schema.NewContext())
        {
            var user = new AppUser { Username = "keeps", PasswordHash = "x" };
            db.AppUsers.Add(user);
            await db.SaveChangesAsync();
            userId = user.Id;
            for (var i = 0; i < 5; i++)
                db.EndlessProgresses.Add(new EndlessProgress
                { AnonymousSessionId = $"alt{i}", UpdatedAt = old, ActiveGameState = new string('x', 10_000) });
            for (var i = 0; i < 3; i++)
                db.EndlessSessions.Add(new EndlessSession
                { AnonymousSessionId = $"alt{i}", CreatedAt = old, Timestamp = i, ConfigJson = "{}", PuzzleAttemptsJson = "[]" });
            db.EndlessProgresses.Add(new EndlessProgress { AnonymousSessionId = "neu", UpdatedAt = DateTime.UtcNow });
            db.EndlessProgresses.Add(new EndlessProgress { UserId = userId, UpdatedAt = old, ActiveGameState = "{}" });
            db.EndlessSessions.Add(new EndlessSession { UserId = userId, CreatedAt = old, Timestamp = 9, ConfigJson = "{}" });
            await db.SaveChangesAsync();
        }

        var recorder = new CommandTextRecorder();
        await using (var db = Recorded(recorder))
            Assert.Equal(8, await AnonymousDataRetentionService.PruneAsync(
                db, DateTime.UtcNow - AnonymousDataRetentionService.AnonymousEndlessMaxAge, chunkSize: 2));

        var dump = string.Join("\n---\n", recorder.Commands);
        Assert.True(Reads(recorder, "EndlessProgresses", "ActiveGameState").Count == 0, dump);
        Assert.True(Reads(recorder, "EndlessSessions", "ConfigJson").Count == 0, dump);
        Assert.True(Reads(recorder, "EndlessSessions", "PuzzleAttemptsJson").Count == 0, dump);
        // Mehrere Portionen = mehrere DELETEs (5 Spielstände bei Portion 2 → 3, 3 Läufe → 2).
        Assert.Equal(5, recorder.Commands.Count(c => c.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)));

        await using (var check = fixture.Schema.NewContext())
        {
            Assert.Equal(new[] { "neu" }, await check.EndlessProgresses
                .Where(p => p.UserId == null).Select(p => p.AnonymousSessionId).ToArrayAsync());
            Assert.Equal(1, await check.EndlessProgresses.CountAsync(p => p.UserId == userId));
            Assert.Equal(userId, (await check.EndlessSessions.SingleAsync()).UserId);
        }
    }
}

public sealed class AnonymousRetentionSqlFixture() : MariaDbClassFixture("aret", withApp: false);
