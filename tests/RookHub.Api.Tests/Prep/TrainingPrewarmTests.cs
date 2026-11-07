using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Engines;
using RookHub.Api.Services.Prep;
using RookHub.Api.Services.Repertoires;

namespace RookHub.Api.Tests.Prep;

/// <summary>Nächtliches Vorrechnen der Trainingslinien-Schätzung (0.884.0): welche Stellungen, welche Stufenkombinationen, Pause,
/// ohne lokalen Explorer nichts, Zeitfenster; dazu die einstellbare Parallelität der Fortsetzung.</summary>
public class TrainingPrewarmTests
{
    private const string WhiteRep = "[Event \"x\"]\n[Black \"Spanisch\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bb5 *\n\n"
                                    + "[Event \"x\"]\n[Black \"Spanisch\"]\n\n1. e4 c5 2. Nf3 *\n";
    private const string BlackRep = "[Event \"x\"]\n[Black \"Caro\"]\n\n1. e4 c6 2. d4 d5 *\n";

    private sealed class FakeExplorer(bool available = true) : ITrainingExplorer
    {
        public bool Available => available;
        public readonly List<(List<string> Keys, string Stages, int Parallelism)> Prewarms = new();
        public readonly HashSet<string> Stored = new();
        public Action? OnPrewarm;

        public Task<TrainingExplorerResult> StatsAsync(int userId, IReadOnlyList<RepertoireReach.Node> positions, int elo, TimeSpan budget,
            int maxNew, CancellationToken ct, int? parallelism = null) => throw new NotSupportedException();

        public Task<RepertoireExplorerService.PrewarmResult> PrewarmAsync(IReadOnlyList<RepertoireReach.Node> positions, IReadOnlyList<int> stages,
            int parallelism, CancellationToken ct)
        {
            OnPrewarm?.Invoke();
            var tag = string.Join(',', stages);
            Prewarms.Add((positions.Select(p => p.Key).ToList(), tag, parallelism));
            var present = positions.Count(p => Stored.Contains(tag + "|" + p.Key));
            foreach (var p in positions) Stored.Add(tag + "|" + p.Key);
            return Task.FromResult(new RepertoireExplorerService.PrewarmResult(present, positions.Count - present, 0));
        }
    }

    private static async Task<(AppDbContext Db, TrainingLinesService Lines)> SeedAsync(IConfiguration config, params (int User, string Pgn, bool Marked)[] reps)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        foreach (var user in reps.Select(r => r.User).Distinct())
            db.AppUsers.Add(new AppUser { Id = user, Username = "u" + user, PasswordHash = "x" });
        await db.SaveChangesAsync();
        foreach (var (user, pgn, marked) in reps)
        {
            var rep = new Repertoire { UserId = user, Name = "R" + db.Repertoires.Count(), UseForExtension = marked };
            db.Repertoires.Add(rep);
            await db.SaveChangesAsync();
            db.RepertoireFiles.Add(new RepertoireFile { RepertoireId = rep.Id, FileName = "a.pgn", PgnContent = pgn, FileSize = pgn.Length });
            await db.SaveChangesAsync();
        }
        return (db, new TrainingLinesService(db, TestServices.Repertoire(db), config));
    }

    private static IConfiguration Config(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? new()).Build();

    private static string KeyAfter(params string[] sans)
    {
        var board = new Chess.ChessBoard();
        foreach (var san in sans) Assert.True(board.Move(san), san);
        return RepertoireReach.Key(board.ToFen());
    }

    [Fact]
    public void StageSets_AllCombinationsTheBandCanYield_FromTheLocalRatings()
    {
        var sets = TrainingExplorer.StageSets(LocalExplorerClient.LocalRatings).Select(s => string.Join(',', s)).ToList();
        // Band 300 breit: 2000+2200+2500 zugleich geht nicht (2200–2499 ist selbst 300 breit)
        Assert.Equal(["1600", "1600,1800", "1600,1800,2000", "1800,2000", "1800,2000,2200", "2000,2200", "2200,2500", "2500"], sets);
        // jede Elo landet in einer davon
        for (var elo = 0; elo <= 3200; elo += 7)
            Assert.Contains(string.Join(',', TrainingExplorer.Stages(elo, LocalExplorerClient.LocalRatings)), sets);
    }

    [Fact]
    public async Task Positions_AreTheOpponentMoveNodesOfAllMarkedRepertoires_BothColors_LikeAnOpponentWithoutGames()
    {
        var (db, lines) = await SeedAsync(Config(), (1, WhiteRep, true), (1, BlackRep, true), (1, "[Event \"x\"]\n[Black \"K\"]\n\n1. d4 d5 *\n", false));
        using var _ = db;
        var keys = (await lines.PrewarmPositionsAsync(1, default)).Select(n => n.Key).ToHashSet();
        // Weiß: Gegner zieht nach 1.e4 und nach 2.Sf3 (Spanisch); Schwarz: Gegner zieht in der Grundstellung und nach 1...c6
        Assert.Contains(KeyAfter("e4"), keys);
        Assert.Contains(KeyAfter("e4", "e5", "Nf3"), keys);
        Assert.Contains(RepertoireReach.StandardStartKey, keys);
        Assert.Contains(KeyAfter("e4", "c6"), keys);
        Assert.DoesNotContain(KeyAfter("e4", "e5"), keys);                    // eigener Zug — keine Schätzung
        Assert.DoesNotContain(KeyAfter("d4"), keys);                          // nicht markiertes Repertoire
        Assert.Equal(keys.Count, (await lines.PrewarmPositionsAsync(1, default)).Count);   // ohne Doppel
    }

    [Fact]
    public async Task Run_EveryUserWithMarkedRepertoires_EveryCombination_SkipsWhatIsStored()
    {
        var config = Config(new() { [TrainingPrewarm.ParallelismKey] = "5" });
        var (db, lines) = await SeedAsync(config, (1, WhiteRep, true), (2, BlackRep, true), (3, WhiteRep, false));
        using var _ = db;
        var explorer = new FakeExplorer();
        var prewarm = new TrainingPrewarm(db, lines, explorer, config, NullLogger<TrainingPrewarm>.Instance);

        var first = await prewarm.RunAsync(default);
        Assert.Equal(TrainingPrewarm.Outcome.Complete, first.Outcome);
        Assert.Equal(2, first.Users);                                         // Nutzer 3 hat nichts markiert
        var combos = TrainingExplorer.StageSets(LocalExplorerClient.LocalRatings).Count;
        Assert.Equal(2 * combos, explorer.Prewarms.Count);
        Assert.All(explorer.Prewarms, c => Assert.Equal(5, c.Parallelism));
        Assert.Equal(0, first.Present);
        Assert.True(first.Fetched > 0);

        var second = await prewarm.RunAsync(default);                        // alles da: nichts Neues
        Assert.Equal(0, second.Fetched);
        Assert.Equal(first.Fetched, second.Present);
    }

    [Fact]
    public async Task Run_StopsWhenBackgroundJobsArePaused()
    {
        var config = Config(new() { [BackgroundPause.ConfigKey] = "true" });
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddLogging();
        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<BackgroundPause>();
        await using var sp = services.BuildServiceProvider();
        var (db, lines) = await SeedAsync(config, (1, WhiteRep, true));
        using var _ = db;
        var explorer = new FakeExplorer();
        var prewarm = new TrainingPrewarm(db, lines, explorer, config, NullLogger<TrainingPrewarm>.Instance, sp.GetRequiredService<BackgroundPause>());

        var r = await prewarm.RunAsync(default);
        Assert.Equal(TrainingPrewarm.Outcome.Paused, r.Outcome);
        Assert.Empty(explorer.Prewarms);
    }

    [Fact]
    public async Task Run_WithoutLocalExplorer_DoesNothing_AndTheSchedulerDoesNotRun()
    {
        var (db, lines) = await SeedAsync(Config(), (1, WhiteRep, true));
        using var _ = db;
        var explorer = new FakeExplorer(available: false);
        var r = await new TrainingPrewarm(db, lines, explorer, Config(), NullLogger<TrainingPrewarm>.Instance).RunAsync(default);
        Assert.Equal(TrainingPrewarm.Outcome.Unavailable, r.Outcome);
        Assert.Empty(explorer.Prewarms);

        Assert.False(TrainingPrewarmScheduler.ShouldRun(Config()));
        Assert.True(TrainingPrewarmScheduler.ShouldRun(Config(new() { [LocalExplorerClient.ConfigKey] = "http://rookhub-explorer:9002" })));
        Assert.False(TrainingPrewarmScheduler.ShouldRun(Config(new()
        {
            [LocalExplorerClient.ConfigKey] = "http://rookhub-explorer:9002", [TrainingPrewarmScheduler.EnabledKey] = "false",
        })));
    }

    [Fact]
    public async Task Run_WindowEnd_CancelsAndKeepsWhatWasDone()
    {
        var (db, lines) = await SeedAsync(Config(), (1, WhiteRep, true));
        using var _ = db;
        using var window = new CancellationTokenSource();
        var explorer = new FakeExplorer();
        explorer.OnPrewarm = () => { if (explorer.Prewarms.Count == 1) window.Cancel(); };   // während der zweiten Portion
        var prewarm = new TrainingPrewarm(db, lines, explorer, Config(), NullLogger<TrainingPrewarm>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prewarm.RunAsync(window.Token));
        Assert.Equal(2, explorer.Prewarms.Count);     // vor der nächsten Portion Schluss
    }

    [Theory]
    [InlineData("2026-11-07T02:29:00Z", false)]      // 03:29 Wien (Winterzeit, UTC+1)
    [InlineData("2026-11-07T02:30:00Z", true)]       // 03:30
    [InlineData("2026-11-07T04:29:00Z", true)]       // 05:29
    [InlineData("2026-11-07T04:30:00Z", false)]      // 05:30 — Ende ausschließlich
    [InlineData("2026-07-07T01:45:00Z", true)]       // 03:45 Sommerzeit (UTC+2)
    public void Window_NightInVienna(string utc, bool inside)
    {
        var w = TrainingPrewarmScheduler.WindowEnd(DateTimeOffset.Parse(utc), null, null);
        Assert.Equal(inside, w is not null);
    }

    [Fact]
    public void Window_EndIsTheLocalEnd_AndConfigurable()
    {
        var w = TrainingPrewarmScheduler.WindowEnd(DateTimeOffset.Parse("2026-11-07T02:45:00Z"), null, null)!.Value;
        Assert.Equal(DateTimeOffset.Parse("2026-11-07T04:30:00Z"), w.End);
        Assert.Equal(new DateOnly(2026, 11, 7), w.Night);
        Assert.Null(TrainingPrewarmScheduler.WindowEnd(DateTimeOffset.Parse("2026-11-07T02:45:00Z"), "01:00-02:00", "UTC"));
        Assert.NotNull(TrainingPrewarmScheduler.WindowEnd(DateTimeOffset.Parse("2026-11-07T01:30:00Z"), "01:00-02:00", "UTC"));
    }

    [Fact]
    public void Continuation_Parallelism_DefaultsTo16_Configurable()
    {
        var scopes = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        Assert.Equal(16, new TrainingLinesContinuation(scopes, NullLogger<TrainingLinesContinuation>.Instance).Parallelism);
        Assert.Equal(24, new TrainingLinesContinuation(scopes, NullLogger<TrainingLinesContinuation>.Instance,
            Config(new() { [TrainingLinesContinuation.ParallelismKey] = "24" })).Parallelism);
        Assert.Equal(64, new TrainingLinesContinuation(scopes, NullLogger<TrainingLinesContinuation>.Instance,
            Config(new() { [TrainingLinesContinuation.ParallelismKey] = "999" })).Parallelism);
    }
}
