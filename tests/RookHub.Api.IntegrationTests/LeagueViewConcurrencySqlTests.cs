using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// W5 N4-007 gegen ECHTES MariaDB: <see cref="LeagueView.Json"/> ist Concurrency-Token, das UPDATE vergleicht den geladenen
/// Inhalt (<c>WHERE Json = @alt</c>) und meldet 0 Zeilen, wenn ein anderer Schreiber dazwischen war. InMemory prüft das in
/// C# und sieht weder das SQL noch die gezählten Zeilen des Anbieters. Kontext mit EnableRetryOnFailure wie Program.cs.
/// </summary>
public class LeagueViewConcurrencySqlTests(LeagueViewConcurrencySqlFixture fixture)
    : IAsyncLifetime, IClassFixture<LeagueViewConcurrencySqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string OldView = "{\"fixtures\":{\"A\":{\"2\":{\"roster\":[{\"fide\":\"B1\",\"g\":0}]}}}}";
    private const string RebuiltView = "{\"fixtures\":{\"A\":{\"2\":{\"roster\":[{\"fide\":\"B1\",\"g\":0}]}}},\"prognose\":\"neu\"}";

    [MySqlFact]
    public async Task CountPatch_RebuildInBetween_ReloadsAndKeepsTheRebuild()
    {
        await using (var seed = fixture.Schema.NewContext())
        {
            seed.LeagueViews.Add(new LeagueView { Tnr = 1, Json = OldView, GeneratedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) });
            seed.LeaguePlayerProfiles.Add(new LeaguePlayerProfile { FideId = "B1", Name = "Bspieler, Nr1", GameCount = 3 });
            await seed.SaveChangesAsync();
        }
        var once = new BeforeFirstSave(() =>
        {
            using var other = fixture.Schema.NewContext();
            var v = other.LeagueViews.Single();
            v.Json = RebuiltView;
            other.SaveChanges();
        });
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                         .UseMySql(fixture.Schema.ConnectionString, new MySqlServerVersion(new Version(11, 0, 0)),
                             o => o.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null))
                         .AddInterceptors(once).Options))
            await new LeagueProfileStore(db).PatchViewCountsAsync(new[] { "B1" }, default);

        Assert.True(once.Fired);
        await using var check = fixture.Schema.NewContext();
        var root = JsonNode.Parse((await check.LeagueViews.SingleAsync()).Json)!;
        Assert.Equal("neu", root["prognose"]?.GetValue<string>());
        Assert.Equal(3, root["fixtures"]!["A"]!["2"]!["roster"]![0]!["g"]!.GetValue<int>());
    }

    private sealed class BeforeFirstSave(Action action) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired && eventData.Context!.ChangeTracker.Entries<LeagueView>().Any(e => e.State == EntityState.Modified))
            {
                Fired = true;
                action();
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

public sealed class LeagueViewConcurrencySqlFixture() : MariaDbClassFixture("lgview", withApp: false);
