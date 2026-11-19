using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Authorization;
using RookHub.Api.Controllers.Prep;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Repertoires;
using RookHub.Api.Models;
using RookHub.Api.Services.Prep;

namespace RookHub.Api.Tests.Prep;

/// <summary>
/// „Frühere Vorbereitungen" (<c>GET /api/prep/history</c>, Wunsch 2026-11-19): die eigenen Trainings-Repertoires „Prep: …" als
/// Liste, zum Spieler des Bestands aufgelöst — über <c>prep:&lt;id&gt;</c>, <c>league:&lt;FIDE&gt;</c> oder (ältere ohne
/// <c>OpponentJson</c>) den Namen, wenn genau EIN Spieler so heißt. Je Gegner ein Eintrag, jüngste zuerst. FIDE-IDs im
/// 99xxxx-Bereich; jede Testinstanz hat ihre eigene Datenbank.
/// </summary>
public class PrepHistoryTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private const int Me = 7, Other = 8;
    private static readonly DateTime Day = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private long _hash = 1;

    public void Dispose() => _db.Dispose();

    private PrepHistoryService Service() => new(_db);

    private async Task<List<PrepHistoryService.Item>> List(int user = Me) => await Service().ListAsync(user, default);

    /// <summary>Ein Spieler des Bestands, Schlüssel wie beim Einlesen.</summary>
    private async Task<PrepPlayer> PlayerAsync(string name, string? fide = null, int games = 10, short? first = 2015, short? last = 2025,
        short? elo = 2100)
    {
        var p = new PrepPlayer
        {
            Name = name, NameKey = PrepPgn.NameKey(name), FideId = fide, KeyHash = _hash++, Games = games, FirstYear = first, LastYear = last,
            MaxElo = elo,
        };
        _db.PrepPlayers.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    /// <summary>Ein Repertoire; <paramref name="day"/> = Tage nach <see cref="Day"/> (größer = jünger).</summary>
    private async Task<Repertoire> RepertoireAsync(string name, int day, string? opponentJson = null, int user = Me)
    {
        var r = new Repertoire { UserId = user, Name = name, OpponentJson = opponentJson, CreatedAt = Day, UpdatedAt = Day.AddDays(day) };
        _db.Repertoires.Add(r);
        await _db.SaveChangesAsync();
        return r;
    }

    private static string Opponent(string key, string? name = null, string? color = null) =>
        new RepertoireOpponentDto { Opponent = key, Name = name, Color = color }.ToJson();

    private static string Prep(string opponent, int year = 2026) => TrainingLinesService.RepertoireName(opponent, year);

    // ── Name des Repertoires ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Huber, Franz")]
    [InlineData("Moser, Christian Mag.")]
    [InlineData("O'Kelly de Galway, Alberic")]
    [InlineData("Carlsen")]
    public void OpponentOfRepertoireName_InvertsRepertoireName(string opponent)
    {
        Assert.Equal(opponent, TrainingLinesService.OpponentOfRepertoireName(TrainingLinesService.RepertoireName(opponent, 2026)));
        Assert.Equal(opponent, TrainingLinesService.OpponentOfRepertoireName(TrainingLinesService.RepertoireName($"  {opponent} ", 1999)));
    }

    [Fact]
    public void OpponentOfRepertoireName_NotAPrepName_OrNoOpponent_Null()
    {
        Assert.StartsWith(TrainingLinesService.RepertoirePrefix, TrainingLinesService.RepertoireName("Huber, Franz", 2026));
        Assert.Null(TrainingLinesService.OpponentOfRepertoireName(null));
        Assert.Null(TrainingLinesService.OpponentOfRepertoireName("Sizilianisch 2026"));
        Assert.Null(TrainingLinesService.OpponentOfRepertoireName("prep: Huber, Franz 2026"));          // genau die vergebene Schreibweise
        Assert.Null(TrainingLinesService.OpponentOfRepertoireName(TrainingLinesService.RepertoireName(" ", 2026)));   // „Prep: ? 2026"
        // umbenannt: was hinter dem Präfix steht, bleibt der Name — nur ein angehängtes Jahr fällt weg
        Assert.Equal("Huber, Franz (alt)", TrainingLinesService.OpponentOfRepertoireName("Prep: Huber, Franz (alt)"));
        Assert.Equal("Huber 1960, Franz", TrainingLinesService.OpponentOfRepertoireName("Prep: Huber 1960, Franz 2026"));
    }

    // ── Auswahl und Reihenfolge ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_OnlyOwnTrainingRepertoires_NewestFirst()
    {
        var huber = await PlayerAsync("Huber, Franz", "990001");
        var mair = await PlayerAsync("Mair, Josef", "990002");
        var old = await RepertoireAsync(Prep("Huber, Franz"), 1, Opponent($"prep:{huber.Id}", "Huber", "w"));
        var young = await RepertoireAsync(Prep("Mair, Josef"), 5, Opponent($"prep:{mair.Id}", "Mair", "b"));
        await RepertoireAsync("Sizilianisch", 9);                                                        // kein Trainings-Repertoire
        await RepertoireAsync(Prep("Gruber, Karl"), 8, Opponent("prep:999999", "Gruber"), user: Other);   // fremd
        await RepertoireAsync(Prep("Pichler, Anna"), 7, user: Other);

        var items = await List();
        Assert.Equal(new[] { young.Id, old.Id }, items.Select(i => i.RepertoireId));
        Assert.Equal(new[] { "Mair, Josef", "Huber, Franz" }, items.Select(i => i.Name));
        Assert.Equal(Day.AddDays(5), items[0].PreparedAt);
        Assert.Empty(await List(user: 12345));
    }

    [Fact]
    public async Task List_PrepKey_ResolvesThePlayer_WithTheFieldsOfASearchHit()
    {
        var huber = await PlayerAsync("Huber, Franz", "990001", games: 321, first: 2001, last: 2026, elo: 2234);
        var rep = await RepertoireAsync(Prep("Huber, Franz"), 1, Opponent($"prep:{huber.Id}", "Huber", "b"));

        var item = Assert.Single(await List());
        Assert.Equal(new PrepHistoryService.Item(huber.Id, "Huber, Franz", "990001", 321, 2001, 2026, 2234, rep.Id, "Prep: Huber, Franz 2026",
            Day.AddDays(1), "b", "Huber, Franz"), item);
    }

    [Fact]
    public async Task List_PrepKeyOfAMissingPlayer_StaysUnresolved_EvenIfTheNameIsUnique()
    {
        await PlayerAsync("Huber, Franz", "990001");
        await RepertoireAsync(Prep("Huber, Franz"), 1, Opponent("prep:424242", "Huber", "w"));

        var item = Assert.Single(await List());
        Assert.Null(item.PlayerId);
        Assert.Null(item.Games);
        Assert.Equal(("Huber, Franz", "Huber, Franz", "w"), (item.Name, item.Query, item.Color));
    }

    [Fact]
    public async Task List_LeagueKey_ResolvesByFideId_OtherwiseUnresolvedWithNameAndFide()
    {
        var inStock = await PlayerAsync("Wegscheider, Olaf", "990010", games: 44);
        await PlayerAsync("Maurer, Hannes");                                                 // heißt so, aber ohne diese FIDE-ID
        await RepertoireAsync(Prep("Wegscheider, Olaf Dr."), 3, Opponent("league:990010", "Wegscheider", "w"));
        await RepertoireAsync(Prep("Maurer, Hannes"), 2, Opponent("league:990011", "Maurer", "b"));
        await RepertoireAsync(Prep("Ohnefide, Kim"), 1, Opponent("league:n-0123456789abcd", "Ohnefide"));   // Ligaspieler ohne FIDE-ID

        var items = await List();
        Assert.Equal(new int?[] { inStock.Id, null, null }, items.Select(i => i.PlayerId));
        Assert.Equal(("Wegscheider, Olaf", "990010", (int?)44), (items[0].Name, items[0].Fide, items[0].Games));   // Name des Bestands
        Assert.Equal(("Maurer, Hannes", "990011", "Maurer, Hannes", "b"), (items[1].Name, items[1].Fide, items[1].Query, items[1].Color));
        Assert.Null(items[1].Games);
        Assert.Equal("Ohnefide, Kim", items[2].Name);
        Assert.Null(items[2].Fide);                                                          // „n-…" ist keine FIDE-ID
        Assert.Null(items[2].Color);
    }

    [Fact]
    public async Task List_LeagueKey_SeveralPlayersWithThatFideId_TheOneWithMostGames()
    {
        await PlayerAsync("Doppelt, Dora", "990020", games: 3);
        var main = await PlayerAsync("Doppelt, Dora Maria", "990020", games: 80);
        await RepertoireAsync(Prep("Doppelt, Dora"), 1, Opponent("league:990020", "Doppelt"));

        Assert.Equal(main.Id, Assert.Single(await List()).PlayerId);
    }

    // ── ohne OpponentJson: der Name ────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_NameOnly_ExactlyOnePlayerWithThatKey_Resolved_TitlesIgnored()
    {
        var moser = await PlayerAsync("Moser, Christian", "990030", games: 12);
        await PlayerAsync("Moser, Christa");                                                 // anderer Schlüssel
        await RepertoireAsync(Prep("Moser, Christian Mag."), 1);

        var item = Assert.Single(await List());
        Assert.Equal(((int?)moser.Id, "Moser, Christian", "990030", (int?)12), (item.PlayerId, item.Name, item.Fide, item.Games));
        Assert.Null(item.Color);
    }

    [Fact]
    public async Task List_NameOnly_TwoNamesakes_OrNobody_Unresolved_QueryWithoutTitles()
    {
        await PlayerAsync("Berger, Armin", "990040");
        await PlayerAsync("Berger, Armin");                                                  // Zwilling ohne FIDE-ID: wer ist gemeint?
        await RepertoireAsync(Prep("Berger, Armin Mag."), 2);
        await RepertoireAsync(Prep("Niemand, Nora Dr."), 1);

        var items = await List();
        Assert.All(items, i => Assert.Null(i.PlayerId));
        Assert.Equal(new[] { "Berger, Armin Mag.", "Niemand, Nora Dr." }, items.Select(i => i.Name));   // voller Name aus dem Repertoire
        Assert.Equal(new[] { "Berger, Armin", "Niemand, Nora" }, items.Select(i => i.Query));
        Assert.All(items, i => Assert.Null(i.Fide));
    }

    [Fact]
    public async Task List_NameOnly_Umlaut_MatchesEitherSpellingOfTheStock_ButNotBoth()
    {
        // Megabase schreibt „Hoecher" (Schlüssel „hoecher, …"), Lumbra „Höcher" (Schlüssel „hocher, …")
        var mega = await PlayerAsync("Hoecher, Michael", "990050");
        var lumbra = await PlayerAsync("Stöttner, Moritz");
        await PlayerAsync("Kaeser, Uwe", "990051");
        await PlayerAsync("Käser, Uwe");
        await RepertoireAsync(Prep("Höcher, Michael"), 3);
        await RepertoireAsync(Prep("Stöttner, Moritz"), 2);
        await RepertoireAsync(Prep("Käser, Uwe"), 1);                                          // beide Schreibweisen im Bestand → zwei Spieler

        var items = await List();
        Assert.Equal(new int?[] { mega.Id, lumbra.Id, null }, items.Select(i => i.PlayerId));
        // die Rückschreibweise der Suche (ae → a) zählt nicht: „Michael" ist nicht „Michal"
        await PlayerAsync("Kofler, Michal");
        await RepertoireAsync(Prep("Kofler, Michael"), 9);
        Assert.Null((await List())[0].PlayerId);
    }

    [Fact]
    public async Task Query_OfAnUnresolvedName_FindsThePlayersInTheSearch()
    {
        var a = await PlayerAsync("Berger, Armin", "990040");
        var b = await PlayerAsync("Berger, Armin");
        await RepertoireAsync(Prep("DI Berger, Armin Mag."), 1);

        var query = Assert.Single(await List()).Query;
        Assert.Equal("Berger, Armin", query);
        var hits = await new PrepPlayerSearch(_db).SearchAsync(query, 25, default);
        Assert.Equal(new[] { a.Id, b.Id }.Order(), hits.Select(h => h.Id).Order());
        // mit dem Titel fände die Suche niemanden — „Mag" gälte als weiterer Vorname
        Assert.Empty(await new PrepPlayerSearch(_db).SearchAsync("Berger, Armin Mag.", 25, default));
    }

    [Theory]
    [InlineData("Moser, Christian Mag.", "Moser, Christian")]
    [InlineData("FM Humer, Wolfgang", "Humer, Wolfgang")]
    [InlineData("Dr. Huber, Franz J.", "Huber, Franz")]
    [InlineData("Carlsen, M.", "Carlsen, M.")]
    [InlineData("Carlsen", "Carlsen")]
    public void SearchText_DropsTitles_KeepsTheKey(string name, string expected)
    {
        Assert.Equal(expected, PrepHistoryService.SearchText(name));
        Assert.Equal(PrepPgn.NameKey(name), PrepPgn.NameKey(PrepHistoryService.SearchText(name)));
    }

    // ── je Gegner ein Eintrag ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_SameResolvedPlayer_OnlyTheNewest_WhateverTheForm()
    {
        var huber = await PlayerAsync("Huber, Franz", "990001");
        await RepertoireAsync(Prep("Huber, Franz", 2024), 1);                                                   // nur der Name
        await RepertoireAsync(Prep("Huber, Franz", 2025), 2, Opponent("league:990001", "Huber", "w"));
        var newest = await RepertoireAsync(Prep("Huber, Franz", 2026), 3, Opponent($"prep:{huber.Id}", "Huber", "b"));

        var item = Assert.Single(await List());
        Assert.Equal((newest.Id, (int?)huber.Id, "b"), (item.RepertoireId, item.PlayerId, item.Color));
    }

    [Fact]
    public async Task List_UnresolvedSameKeyOrSameName_OnlyTheNewest_ButNamesakesWithOwnIdsStay()
    {
        // gleicher Schlüssel, nicht im Bestand
        await RepertoireAsync(Prep("Maurer, Hannes", 2025), 1, Opponent("league:990011", "Maurer"));
        var maurer = await RepertoireAsync(Prep("Maurer, Hannes", 2026), 6, Opponent("league:990011", "Maurer"));
        // gleicher Name: der ältere ohne Schlüssel (zwei Namensvettern → unaufgelöst) hinter dem jüngeren mit Schlüssel
        var first = await PlayerAsync("Berger, Armin", "990040");
        var second = await PlayerAsync("Berger, Armin", "990041");
        await RepertoireAsync(Prep("Berger, Armin Mag.", 2025), 2);
        var berger = await RepertoireAsync(Prep("Berger, Armin", 2026), 5, Opponent($"prep:{first.Id}", "Berger"));
        // zwei verschiedene Spieler gleichen Namens bleiben zwei Einträge
        var namesake = await RepertoireAsync("Prep: Berger, Armin (Wien) 2026", 4, Opponent($"prep:{second.Id}", "Berger"));

        var items = await List();
        Assert.Equal(new[] { maurer.Id, berger.Id, namesake.Id }, items.Select(i => i.RepertoireId));
        Assert.Equal(new int?[] { null, first.Id, second.Id }, items.Select(i => i.PlayerId));
    }

    // ── Randfälle ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_BrokenOpponentJson_FallsBackToTheName_OrIsLeftOut()
    {
        var huber = await PlayerAsync("Huber, Franz", "990001");
        var byName = await RepertoireAsync(Prep("Huber, Franz"), 3, "{kaputt");
        await RepertoireAsync("Mein Franzose", 2, "{\"opponent\":\"sonstwas:1\"}");            // kein gültiger Schlüssel, kein „Prep: …"
        var noKey = await RepertoireAsync(Prep("Pichler, Anna"), 1, "{\"opponent\":\"\",\"name\":\"Pichler\"}");

        var items = await List();
        Assert.Equal(new[] { byName.Id, noKey.Id }, items.Select(i => i.RepertoireId));
        Assert.Equal(new int?[] { huber.Id, null }, items.Select(i => i.PlayerId));
        Assert.All(items, i => Assert.Null(i.Color));
    }

    [Fact]
    public async Task List_RenamedRepertoireWithKey_StillListed_LowercasePrefixWithoutKeyIsNot()
    {
        var huber = await PlayerAsync("Huber, Franz", "990001");
        var renamed = await RepertoireAsync("Gegen Huber am Sonntag", 2, Opponent($"prep:{huber.Id}", "Huber", "w"));
        var unresolved = await RepertoireAsync("Liga Runde 3", 1, Opponent("league:990099", "Gruber", "b"));
        await RepertoireAsync("prep: klein geschrieben 2026", 5);                              // so vergibt es das Anlegen nie

        var items = await List();
        Assert.Equal(new[] { renamed.Id, unresolved.Id }, items.Select(i => i.RepertoireId));
        Assert.Equal("Huber, Franz", items[0].Name);
        Assert.Equal(("Gruber", "Gruber"), (items[1].Name, items[1].Query));                    // nur der Kurzname aus dem Schlüssel
    }

    [Fact]
    public async Task List_ManyOpponents_CappedAtMaxItems_NewestKept()
    {
        for (var i = 0; i < PrepHistoryService.MaxItems + 5; i++)
            _db.Repertoires.Add(new Repertoire { UserId = Me, Name = Prep($"Gegner{i:000}, X"), CreatedAt = Day, UpdatedAt = Day.AddMinutes(i) });
        await _db.SaveChangesAsync();

        var items = await List();
        Assert.Equal(PrepHistoryService.MaxItems, items.Count);
        Assert.Equal($"Gegner{PrepHistoryService.MaxItems + 4:000}, X", items[0].Name);
        Assert.Equal("Gegner005, X", items[^1].Name);
    }

    // ── Controller ─────────────────────────────────────────────────────────────────────────────

    private PrepController Controller(int userId) => new(new PrepImportService(_db))
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "t")),
            },
        },
    };

    [Fact]
    public async Task History_OwnItems_InTheDocumentedShape()
    {
        var huber = await PlayerAsync("Huber, Franz", "990001", games: 321, first: 2001, last: 2026, elo: 2234);
        var rep = await RepertoireAsync(Prep("Huber, Franz"), 2, Opponent($"prep:{huber.Id}", "Huber", "w"));
        var open = await RepertoireAsync(Prep("Niemand, Nora Dr."), 1);
        await RepertoireAsync(Prep("Fremd, Fritz"), 9, user: Other);

        var ok = Assert.IsType<OkObjectResult>(await Controller(Me).History(Service(), default));
        var json = JsonNode.Parse(JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
        var items = json["items"]!.AsArray();
        Assert.Equal(2, items.Count);
        var expected = new JsonObject
        {
            ["playerId"] = huber.Id, ["name"] = "Huber, Franz", ["fide"] = "990001", ["games"] = 321, ["firstYear"] = 2001,
            ["lastYear"] = 2026, ["maxElo"] = 2234, ["repertoireId"] = rep.Id, ["repertoireName"] = "Prep: Huber, Franz 2026",
            ["preparedAt"] = "2026-11-03T12:00:00Z", ["color"] = "w", ["query"] = "Huber, Franz",
        };
        Assert.Equal(expected.ToJsonString(), items[0]!.ToJsonString());
        Assert.Equal(new JsonObject
        {
            ["playerId"] = null, ["name"] = "Niemand, Nora Dr.", ["fide"] = null, ["games"] = null, ["firstYear"] = null,
            ["lastYear"] = null, ["maxElo"] = null, ["repertoireId"] = open.Id, ["repertoireName"] = "Prep: Niemand, Nora Dr. 2026",
            ["preparedAt"] = "2026-11-02T12:00:00Z", ["color"] = null, ["query"] = "Niemand, Nora",
        }.ToJsonString(), items[1]!.ToJsonString());

        var other = Assert.IsType<OkObjectResult>(await Controller(Other).History(Service(), default));
        var foreign = JsonNode.Parse(JsonSerializer.Serialize(other.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!["items"]!.AsArray();
        Assert.Equal("Fremd, Fritz", Assert.Single(foreign)!["name"]!.GetValue<string>());
    }

    [Fact]
    public void History_IsALiteralRouteBehindPrepView()
    {
        var action = typeof(PrepController).GetMethod(nameof(PrepController.History))!;
        Assert.Equal("history", action.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal(PermissionPolicyProvider.Prefix + Permissions.PrepView, action.GetCustomAttribute<HasPermissionAttribute>()!.Policy);
    }
}
