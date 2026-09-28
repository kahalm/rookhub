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
                "Angerer, Helmut\t\t3\t1980\t\nHuber, Franz\t1\t10\t2020\t\nHuber, Franz\t2\t20\t2021\t\n"), default);

        await using (var db = fixture.Schema.NewContext())
        {
            var mega = new LeagueMegaPlayers(db);
            Assert.Equal("Bertl, Rudolf", (await mega.SearchAsync("ert rud", 10, default)).Single().Name);

            var names = new[] { "Helmut Angerer", "Huber, Franz", "Niemand" }.Concat(Enumerable.Range(0, 600).Select(i => $"Name{i}, X"));
            var l = await mega.LookupAsync(names, new[] { "111", null }, default);   // mehr als eine Portion à 500
            Assert.Equal(new LeagueMegaPlayers.Hit("Angerer, Helmut", "1607162"), l.ByName("Helmut Angerer"));
            Assert.Null(l.ByName("Huber, Franz"));
            Assert.Equal("Bertl, Rudolf", l.ByFide("111")?.Name);
            Assert.Null(l.ByName("Niemand"));
        }
    }
}

public sealed class LeagueMegaPlayersSqlFixture() : MariaDbClassFixture("megapl", withApp: false);
