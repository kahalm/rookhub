using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>
/// Online-Konten für Ligaspieler OHNE FIDE-ID (0.730.0, Wunsch 2026-10-10: „die Verbindung von Onlinekonto zu Ligakonto soll
/// nicht nur über FIDE gehen, um auch die ohne FIDE zu fangen"). Schlüssel ist <see cref="LeagueNames.AccountKey"/>.
/// </summary>
public class LeagueNoFideAccountsTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private const string Name = "Hofer, Patrick";
    private static readonly string Key = LeagueNames.AccountKey(null, LeagueNames.NameKey(Name));

    private async Task SeedAsync()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 1, Name = "TMM Landesliga 2026/2027", Season = "2026/27", Level = 3, League = "Landesliga", Stage = "Liga" });
        _db.LeaguePlayers.AddRange(
            new LeaguePlayer { Tnr = 1, Team = "Schwaz", Name = Name, NameKey = LeagueNames.NameKey(Name), FideId = null, EloN = 1650 },
            new LeaguePlayer { Tnr = 1, Team = "Schwaz", Name = "Muster, Max", NameKey = LeagueNames.NameKey("Muster, Max"), FideId = "222" });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public void AccountKey_FitsTheFideColumn_AndKeepsAFideId()
    {
        Assert.Equal("222", LeagueNames.AccountKey("222", "egal"));
        Assert.Equal(16, Key.Length);                                           // FideId-Spalten sind 16 Zeichen lang
        Assert.StartsWith("n-", Key);
        Assert.True(LeagueNames.IsNoFideKey(Key));
        Assert.False(LeagueNames.IsNoFideKey("222"));
        Assert.Equal(Key, LeagueNames.AccountKey("", LeagueNames.NameKey(Name)));   // leere FIDE-ID = keine
        Assert.Equal(Key, LeagueNames.AccountKeyOfPid(LeagueNames.Pid(null, LeagueNames.NameKey(Name))));
        Assert.Equal("222", LeagueNames.AccountKeyOfPid(LeagueNames.Pid("222", "x")));
    }

    [Fact]
    public async Task Create_ForAPlayerWithoutFide_UsesHisKey()
    {
        await SeedAsync();
        var svc = new LeagueOnlineAccountService(_db);
        var (acc, reason) = await svc.CreateAsync(Key, new("chess.com", "phofer1003", false, "lusr"), default);
        Assert.Null(reason);
        Assert.Equal((Key, "wahrscheinlich"), (acc!.FideId, acc.Confidence));
        Assert.Equal("unknownPlayer", (await svc.CreateAsync("n-0000000000000a", new("lichess", "x_y", false, null), default)).Reason);
        Assert.True(await LeagueOnlineAccountService.LeagueKnowsAsync(_db, Key, default));
        Assert.Equal((Name, 1650), ((await LeagueAccountFinder.PlayerAsync(_db, Key, default))!.Name,
            (await LeagueAccountFinder.PlayerAsync(_db, Key, default))!.Elo));
    }

    [Fact]
    public async Task Card_ForAPlayerWithoutFide_EvenWithoutAccounts()
    {
        await SeedAsync();
        var league = new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
        var card = await league.CardAsync(Key, onlySure: false, default);
        Assert.NotNull(card);
        Assert.Equal(("", Key, Name), (card!["fide"]!.GetValue<string>(), card["key"]!.GetValue<string>(), card["name"]!.GetValue<string>()));
        Assert.Empty(card["accounts"]!.AsArray());

        await new LeagueOnlineAccountService(_db).CreateAsync(Key, new("chess.com", "phofer1003", true, null), default);
        card = await league.CardAsync(Key, onlySure: true, default);
        Assert.Equal("phofer1003", card!["accounts"]!.AsArray().Single()!["user"]!.GetValue<string>());
        Assert.Null(await league.CardAsync("n-0000000000000a", onlySure: false, default));
    }

    [Fact]
    public void View_RosterAndCandidates_CarryTheKey_AndItsAccounts()
    {
        var w = LeagueEngineTests.TinyWorld(withoutFide: "C1");
        var key = LeagueNames.AccountKey(null, "cspieler, nr1");
        var v = new LeagueViewBuilder(w, LeagueModel.FromEmbedded(), new Dictionary<string, int>(),
            new Dictionary<string, List<LeagueOnlineAccount>>
            {
                [key] = new() { new() { FideId = key, Site = "chess.com", UserName = "cnr1", Url = "u", Confidence = "wahrscheinlich" } },
            }).Build(1, w.Games);
        var fx = v["fixtures"]!["A"]!["3"]!;                                    // Runde 3: A gegen C
        var row = fx["roster"]!.AsArray().Single(r => r!["n"]!.GetValue<string>() == "Cspieler, Nr1")!;
        Assert.Null(row["fide"]);
        Assert.Equal(key, row["key"]!.GetValue<string>());
        Assert.Equal("cnr1", row["acc"]!.AsArray().Single()!["user"]!.GetValue<string>());
        var other = fx["roster"]!.AsArray().First(r => r!["fide"] is not null)!;
        Assert.Null(other["key"]);
        Assert.Contains(fx["boards"]!.AsArray().SelectMany(b => b!["cand"]!.AsArray()),
            c => c!["key"]?.GetValue<string>() == key);
    }

    [Fact]
    public async Task Patch_ReachesTheRosterRowWithTheKey()
    {
        await SeedAsync();
        _db.LeagueViews.Add(new LeagueView { Tnr = 1, GeneratedAt = DateTime.UtcNow,
            Json = """{"fixtures":{"Schwaz":{"3":{"roster":[{"fide":null,"key":"KEY","acc":[]},{"fide":"222","acc":[]}]}}}}""".Replace("KEY", Key) });
        await _db.SaveChangesAsync();
        await new LeagueOnlineAccountService(_db).CreateAsync(Key, new("chess.com", "phofer1003", false, null), default);
        var roster = JsonNode.Parse((await _db.LeagueViews.AsNoTracking().SingleAsync()).Json)!["fixtures"]!["Schwaz"]!["3"]!["roster"]!.AsArray();
        Assert.Equal("phofer1003", roster[0]!["acc"]!.AsArray().Single()!["user"]!.GetValue<string>());
        Assert.Empty(roster[1]!["acc"]!.AsArray());
    }
}
