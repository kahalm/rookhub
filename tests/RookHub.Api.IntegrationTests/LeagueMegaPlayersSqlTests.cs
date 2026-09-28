using Microsoft.EntityFrameworkCore;
using RookHub.Api.Services.League;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Das Megabase-Spielerverzeichnis gegen ECHTES MariaDB (0.576.0): die Suche („jedes Wort irgendwo im Namen", als
/// <c>LIKE '%wort%'</c>) und das Nachschlagen einer ganzen Übersicht (<c>NameKey IN (…)</c>, <c>FideId IN (…)</c>).
/// InMemory rechnet beides in C# und sieht nicht, ob der Anbieter die Abfrage übersetzt.
/// </summary>
public class LeagueMegaPlayersSqlTests(LeagueMegaPlayersSqlFixture fixture)
    : IAsyncLifetime, IClassFixture<LeagueMegaPlayersSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [MySqlFact]
    public async Task SearchAndLookup_TranslateToSql()
    {
        await using (var db = fixture.Schema.NewContext())
            await new LeagueMegaPlayers(db).ReplaceAsync(new StringReader(
                "Bertl, Rudolf\t111\t40\t2024\t1900\nAngerer, Helmut\t1607162\t98\t2023\t2195\n" +
                "Angerer, Helmut\t\t3\t1980\t\nHuber, Franz\t1\t10\t2020\t\nHuber, Franz\t2\t20\t2021\t\n" +
                "Hoecher, Michael\t1271145\t83\t2025\t2157\n"), default);

        await using (var db = fixture.Schema.NewContext())
        {
            var mega = new LeagueMegaPlayers(db);
            Assert.Equal("Bertl, Rudolf", (await mega.SearchAsync("ert rud", 10, default)).Single().Name);
            Assert.Equal("Hoecher, Michael", (await mega.SearchAsync("Höcher mich", 10, default)).Single().Name);   // OR aus LIKEs
            Assert.Equal("Hoecher, Michael", (await mega.SearchAsync("1271145", 10, default)).Single().Name);

            var names = new[] { "Helmut Angerer", "Huber, Franz", "Niemand" }.Concat(Enumerable.Range(0, 600).Select(i => $"Name{i}, X"));
            var l = await mega.LookupAsync(names, new[] { "111", null }, default);   // mehr als eine Portion à 500
            Assert.Equal(new LeagueMegaPlayers.Hit("Angerer, Helmut", "1607162"), l.ByName("Helmut Angerer"));
            Assert.Null(l.ByName("Huber, Franz"));
            Assert.Equal("Bertl, Rudolf", l.ByFide("111")?.Name);
            Assert.Null(l.ByName("Niemand"));
        }
    }

    /// <summary>Gemerkte Namens-Zuordnungen (0.579.0): anlegen, überschreiben, nachschlagen — eindeutiger Index auf NameKey.</summary>
    [MySqlFact]
    public async Task NameAliases_SaveOverwriteAndLoad()
    {
        var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        await using (var db = fixture.Schema.NewContext())
        {
            var aliases = new LeagueNameAliases(db);
            Assert.Equal(2, await aliases.SaveAsync(new[] { ("FM Andi S.", new LeagueNameAliases.Entry("333", "Schnabl, Andreas")),
                ("Kostic", new LeagueNameAliases.Entry(null, "Kostic, Milan")) }, now, default));
        }
        await using (var db = fixture.Schema.NewContext())
        {
            var aliases = new LeagueNameAliases(db);
            Assert.Equal(1, await aliases.SaveAsync(new[] { ("andi s.", new LeagueNameAliases.Entry("222", "Hengl, Philip")),
                ("Kostic", new LeagueNameAliases.Entry(null, "Kostic, Milan")) }, now, default));     // nur eine ändert sich
            var loaded = await aliases.LoadAsync(new[] { "Andi S.", "KOSTIC", "Niemand" }, default);
            Assert.Equal(new LeagueNameAliases.Entry("222", "Hengl, Philip"), loaded["andi s."]);
            Assert.Equal(2, loaded.Count);
            Assert.Equal(2, await db.LeagueNameAliases.CountAsync());
        }
    }
}

public sealed class LeagueMegaPlayersSqlFixture() : MariaDbClassFixture("megapl", withApp: false);
