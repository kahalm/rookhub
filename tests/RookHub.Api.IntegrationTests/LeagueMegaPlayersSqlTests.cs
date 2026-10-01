using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
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

    /// <summary>
    /// W5 N4-005: Löschen und Neubefüllen liegen in EINER Transaktion — bricht der Upload nach zwei vollen Portionen ab,
    /// steht das alte Verzeichnis vollständig, und ein paralleler Abgleich hat währenddessen nur den alten Stand gesehen
    /// (vorher: gelöscht, 10 000 neue Zeilen festgeschrieben, Rest fehlt). Mit EnableRetryOnFailure wie in Program.cs,
    /// denn die Strategie verweigert eine Transaktion außerhalb von <c>ExecuteAsync</c>.
    /// </summary>
    [MySqlFact]
    public async Task Replace_AbortedMidway_KeepsOldDirectory_ParallelReaderSeesOldState()
    {
        await using (var db = RetryingContext())
            Assert.Equal(2, await new LeagueMegaPlayers(db).ReplaceAsync(
                new StringReader("Alt, Anna\t11\t5\t2020\t\nAlt, Berta\t12\t6\t2021\t\n"), default));

        List<string>? seenDuring = null;
        var tsv = new AbortingReader(LeagueMegaPlayers.BatchSize * 2 + 10, () =>
        {
            using var other = fixture.Schema.NewContext();
            seenDuring = other.LeagueMegaPlayers.OrderBy(p => p.Name).Select(p => p.Name).Take(5).ToList();
        });
        await using (var db = RetryingContext())
            await Assert.ThrowsAsync<IOException>(() => new LeagueMegaPlayers(db).ReplaceAsync(tsv, default));

        Assert.Equal(new[] { "Alt, Anna", "Alt, Berta" }, seenDuring);
        await using (var db = fixture.Schema.NewContext())
            Assert.Equal(new[] { "Alt, Anna", "Alt, Berta" },
                await db.LeagueMegaPlayers.OrderBy(p => p.Name).Select(p => p.Name).ToListAsync());
    }

    private AppDbContext RetryingContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseMySql(fixture.Schema.ConnectionString, DbServerVersion.Current,
            o => o.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null))
        .Options);

    /// <summary>Liefert <paramref name="lines"/> Zeilen „Neu, Spieler N", ruft dann <paramref name="beforeAbort"/> und
    /// wirft wie ein abgerissener Upload.</summary>
    private sealed class AbortingReader(int lines, Action beforeAbort) : TextReader
    {
        private int _i;

        public override string? ReadLine()
        {
            if (_i < lines) return $"Neu, Spieler {_i++}\t{_i}\t1\t2025\t";
            beforeAbort();
            throw new IOException("Upload abgerissen");
        }

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => new(ReadLine());
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
