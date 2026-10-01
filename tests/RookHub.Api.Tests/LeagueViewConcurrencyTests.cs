using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>
/// Codereview 2026-09-29, W5 N4-007: drei Schreiber auf <see cref="LeagueView.Json"/> (Daten aktualisieren, Partienzahl nach
/// jeder Vereinspartie, Online-Konten) laden das ganze JSON, ändern es und schreiben es zurück. Ohne Concurrency-Token gewann
/// der letzte mit seinem ALTEN Stand. Die Tests schieben den anderen Schreiber genau zwischen Laden und Speichern
/// (Interceptor vor dem ersten SaveChanges, zweiter Kontext auf derselben InMemory-Datenbank).
/// </summary>
public class LeagueViewConcurrencyTests
{
    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _name = Guid.NewGuid().ToString();

    private AppDbContext Context(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(_name, _root).AddInterceptors(interceptors).Options);

    private const string OldView = "{\"fixtures\":{\"A\":{\"2\":{\"roster\":[{\"fide\":\"B1\",\"g\":0,\"acc\":[]}]}}}}";
    private const string RebuiltView = "{\"fixtures\":{\"A\":{\"2\":{\"roster\":[{\"fide\":\"B1\",\"g\":0,\"acc\":[]}]}}},\"prognose\":\"neu\"}";

    private void SeedView()
    {
        using var db = Context();
        db.LeagueViews.Add(new LeagueView { Tnr = 1, Json = OldView, GeneratedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) });
        db.LeaguePlayerProfiles.Add(new LeaguePlayerProfile { FideId = "B1", Name = "Bspieler, Nr1", GameCount = 3 });
        db.SaveChanges();
    }

    /// <summary>„Daten aktualisieren" schreibt zwischen Laden und Speichern des Patches: dessen Stand bleibt, die
    /// Partienzahl kommt trotzdem dazu (vorher: das gepatchte ALTE JSON überschrieb die neue Prognose).</summary>
    [Fact]
    public async Task CountPatch_RebuildInBetween_KeepsTheRebuildAndAddsTheCount()
    {
        SeedView();
        var rebuiltAt = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var once = new BeforeFirstSave(() =>
        {
            using var other = Context();
            var v = other.LeagueViews.Single();
            v.Json = RebuiltView;
            v.GeneratedAt = rebuiltAt;
            other.SaveChanges();
        });
        await using (var db = Context(once))
            await new LeagueProfileStore(db).PatchViewCountsAsync(new[] { "B1" }, default);

        Assert.True(once.Fired);
        await using var check = Context();
        var view = await check.LeagueViews.SingleAsync();
        var root = JsonNode.Parse(view.Json)!;
        Assert.Equal("neu", root["prognose"]?.GetValue<string>());                       // Rebuild-Stand erhalten
        Assert.Equal(3, root["fixtures"]!["A"]!["2"]!["roster"]![0]!["g"]!.GetValue<int>());   // Patch neu angewandt
        Assert.Equal(rebuiltAt, view.GeneratedAt);
    }

    /// <summary>Zwei Patches gleichzeitig (Partienzahl und Konto): keiner verliert die Änderung des anderen.</summary>
    [Fact]
    public async Task AccountPatch_CountPatchInBetween_KeepsBoth()
    {
        SeedView();
        await using (var seed = Context())
        {
            seed.LeagueOnlineAccounts.Add(new LeagueOnlineAccount { FideId = "B1", Site = "lichess", UserName = "bspieler", Url = "u", Confidence = "sicher" });
            await seed.SaveChangesAsync();
        }
        var once = new BeforeFirstSave(() =>
        {
            using var other = Context();
            new LeagueProfileStore(other).PatchViewCountsAsync(new[] { "B1" }, default).GetAwaiter().GetResult();
        });
        await using (var db = Context(once))
            await new LeagueOnlineAccountService(db).PatchViewsAsync("B1", default);

        Assert.True(once.Fired);
        await using var check = Context();
        var r = JsonNode.Parse((await check.LeagueViews.SingleAsync()).Json)!["fixtures"]!["A"]!["2"]!["roster"]![0]!;
        Assert.Equal(3, r["g"]!.GetValue<int>());                                         // Partienzahl des anderen
        Assert.Single(r["acc"]!.AsArray());                                               // eigenes Konto
    }

    /// <summary>Umgekehrt: eine Partie kommt herein, während „Daten aktualisieren" rechnet. Der Rebuild hat die Zählwerte
    /// vorher gelesen — er rechnet neu, statt den nachgezogenen Wert mit dem alten zu überschreiben.</summary>
    [Fact]
    public async Task Rebuild_CountPatchInBetween_RecomputesWithTheNewCount()
    {
        await using (var seed = Context())
        {
            SeedWorld(seed);
            await seed.SaveChangesAsync();
        }
        await using (var first = Context())
            Assert.Equal(1, await Service(first).RebuildViewsAsync(default));
        string fide, other2;
        await using (var read = Context())
        {
            var fides = Rosters(JsonNode.Parse((await read.LeagueViews.SingleAsync()).Json)!)
                .Select(r => r["fide"]!.GetValue<string>()).Distinct().ToList();
            (fide, other2) = (fides[0], fides[1]);
            // Seit dem letzten Lauf hat sich etwas geändert — sonst wäre das neue JSON gleich dem alten, EF schriebe nur
            // GeneratedAt, und der Patch überlebte zufällig.
            read.LeaguePlayerProfiles.Add(new LeaguePlayerProfile { FideId = other2, Name = "Y", GameCount = 2 });
            await read.SaveChangesAsync();
        }

        var once = new BeforeFirstSave(() =>
        {
            // Wie ein Upload: Karte gespeichert, dann die Ansichten nachgezogen.
            using var other = Context();
            other.LeaguePlayerProfiles.Add(new LeaguePlayerProfile { FideId = fide, Name = "X", GameCount = 7 });
            other.SaveChanges();
            new LeagueProfileStore(other).PatchViewCountsAsync(new[] { fide }, default).GetAwaiter().GetResult();
        });
        await using (var db = Context(once))
            Assert.Equal(1, await Service(db).RebuildViewsAsync(default));

        Assert.True(once.Fired);
        await using var check = Context();
        var rosters = Rosters(JsonNode.Parse((await check.LeagueViews.SingleAsync()).Json)!).ToList();
        int[] G(string f) => rosters.Where(r => r["fide"]!.GetValue<string>() == f).Select(r => r["g"]!.GetValue<int>()).Distinct().ToArray();
        Assert.Equal(new[] { 7 }, G(fide));                                               // nachgezogener Wert nicht überschrieben
        Assert.Equal(new[] { 2 }, G(other2));                                             // und der Rebuild ist durch
    }

    private static LeagueService Service(AppDbContext db) =>
        new(db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);

    private static IEnumerable<JsonNode> Rosters(JsonNode view) =>
        view["fixtures"]!.AsObject().SelectMany(t => t.Value!.AsObject())
            .SelectMany(fx => fx.Value?["roster"] as JsonArray ?? new JsonArray()).Where(r => r?["fide"] is not null)!;

    /// <summary>Eine kleine Liga wie <c>LeagueEngineTests.TinyWorld</c>: Runde 1 gespielt, Runden 2 und 3 offen.</summary>
    private static void SeedWorld(AppDbContext db)
    {
        db.LeagueTournaments.Add(new LeagueTournament { Tnr = 1, Name = "TMM Landesliga 2026/2027", Season = "2026/27", Level = 1, League = "Landesliga", Stage = "Liga" });
        db.LeagueRounds.AddRange(
            new LeagueRound { Tnr = 1, Round = 1, Date = new DateOnly(2026, 10, 3) },
            new LeagueRound { Tnr = 1, Round = 2, Date = new DateOnly(2026, 10, 4) },
            new LeagueRound { Tnr = 1, Round = 3, Date = new DateOnly(2026, 11, 7) });
        db.LeagueMatches.AddRange(
            new LeagueMatch { Id = 1, Tnr = 1, Round = 1, Home = "A", Away = "B", HomePts = 1, AwayPts = 1 },
            new LeagueMatch { Id = 2, Tnr = 1, Round = 1, Home = "C", Away = "D", HomePts = 2, AwayPts = 0 },
            new LeagueMatch { Id = 3, Tnr = 1, Round = 2, Home = "B", Away = "C" },
            new LeagueMatch { Id = 4, Tnr = 1, Round = 2, Home = "D", Away = "A" },
            new LeagueMatch { Id = 5, Tnr = 1, Round = 3, Home = "A", Away = "C" },
            new LeagueMatch { Id = 6, Tnr = 1, Round = 3, Home = "D", Away = "B" });
        var id = 1;
        foreach (var team in new[] { "A", "B", "C", "D" })
            for (var rb = 1; rb <= 4; rb++)
                db.LeaguePlayers.Add(new LeaguePlayer
                {
                    Id = id++, Tnr = 1, Team = team, RosterBoard = rb, Name = $"{team}spieler, Nr{rb}",
                    NameKey = $"{team.ToLowerInvariant()}spieler, nr{rb}", FideId = $"{team}{rb}", EloI = 2000 - rb * 50,
                });
        var gid = 1;
        void Match(int rnd, int no, string h, string a, bool played)
        {
            for (var b = 1; b <= 2; b++)
                db.LeagueGames.Add(new LeagueGame
                {
                    Id = gid++, Tnr = 1, Round = rnd, MatchNo = no, Board = b, HomeTeam = h, AwayTeam = a,
                    HomePlayer = played ? $"{h}spieler, Nr{b + 1}" : null, AwayPlayer = played ? $"{a}spieler, Nr{b}" : null,
                    HomeFide = played ? $"{h}{b + 1}" : null, AwayFide = played ? $"{a}{b}" : null,
                    Result = played ? "½ - ½" : "", HomeScore = played ? .5 : null, AwayScore = played ? .5 : null,
                    HomeColor = b % 2 == 1 ? "w" : "s",
                });
        }
        Match(1, 1, "A", "B", true);
        Match(1, 2, "C", "D", true);
        Match(2, 1, "B", "C", false);
        Match(2, 2, "D", "A", false);
        Match(3, 1, "A", "C", false);
        Match(3, 2, "D", "B", false);
    }

    /// <summary>Führt <paramref name="action"/> genau einmal aus — vor dem ERSTEN SaveChanges des Kontexts, also nachdem der
    /// Schreiber die Ansicht geladen und geändert hat und bevor er sie speichert.</summary>
    private sealed class BeforeFirstSave(Action action) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired && eventData.Context!.ChangeTracker.Entries<LeagueView>().Any(e => e.State != EntityState.Unchanged))
            {
                Fired = true;
                action();
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
