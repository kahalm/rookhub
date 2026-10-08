using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Models;
using RookHub.Api.Services.League;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Feste Ligapaarung gegen ECHTES MariaDB (0.716.1, gemeldet 2026-10-07: die Zuordnung ging bei jedem Aktualisieren verloren).
/// Geprüft wird, was InMemory nicht sieht: die Migration <c>LeagueGameLinkKey</c> (Spalten + Indizes), das Zusammenführen der
/// Brettpaarungen in einer echten Transaktion (Ids bleiben), die Übersetzung der Abfragen von <see cref="LeagueGameLinks"/>
/// und <see cref="LeagueFixtureGames"/> und die Heilung des Bestands so, wie er auf Prod liegt (Zuordnungen ohne Schlüssel,
/// eine davon mit toter Id).
/// </summary>
public class LeagueGameLinksSqlTests(LeagueGameLinksSqlFixture fixture) : IAsyncLifetime, IClassFixture<LeagueGameLinksSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const int Tnr = 1479345;
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static LeagueRefresh.Pages Pages() => new(Tnr,
        new() { new(2, 2, "Schach Ohne Grenzen", "Schwaz", 3, 3, "04.10.2026", null, null) },
        new()
        {
            new(2, 2, 1, "Schach Ohne Grenzen", "Schwaz", "Hess, Max", "Binder, Moriz", null, null, "w", "½ - ½", .5, .5, 0, null),
            new(2, 2, 2, "Schach Ohne Grenzen", "Schwaz", "Kruckenhauser, Arthur", "Tafertshofer, Matthias", null, null, "s", "½ - ½", .5, .5, 0, null),
        },
        new() { [2] = "04.10.2026" },
        new()
        {
            new(1, null, "Hess, Max", "24656666", 1900, null, "AUT", "Schach Ohne Grenzen", 1),
            new(2, null, "Kruckenhauser, Arthur", "1642812", 1850, null, "AUT", "Schach Ohne Grenzen", 2),
            new(3, null, "Binder, Moriz", "1616951", 2000, null, "AUT", "Schwaz", 1),
            new(4, null, "Tafertshofer, Matthias", "1270012", 1950, null, "AUT", "Schwaz", 2),
        },
        new());

    private static readonly LeagueClub Schwaz = new() { Id = 1, Name = "SK Schwaz", TeamPrefix = "Schwaz", AnonName = "Schwaz" };

    [MySqlFact]
    public async Task Refresh_keepsIds_healRepairsTheProdShapedRows_fixtureFindsThem()
    {
        await using (var db = fixture.Schema.NewContext())
        {
            if (!await db.LeagueClubs.AnyAsync(c => c.Id == 1))   // ResetAsync leert auch die Vereine der Migration
                db.LeagueClubs.Add(new LeagueClub { Id = 1, Name = "SK Schwaz", TeamPrefix = "Schwaz", AnonName = "Schwaz" });
            db.LeagueTournaments.Add(new LeagueTournament { Tnr = Tnr, Name = "LL", Season = "2026/27", Level = 3, League = "Landesliga", Stage = "Liga" });
            await db.SaveChangesAsync();
            await LeagueRefresh.ReplaceAsync(db, Pages(), Now, default);
        }
        int board1, board2;
        await using (var db = fixture.Schema.NewContext())
        {
            board1 = (await db.LeagueGames.SingleAsync(g => g.Board == 1)).Id;
            board2 = (await db.LeagueGames.SingleAsync(g => g.Board == 2)).Id;
            // Bestand wie auf Prod: Zuordnungen nur als Id — eine gültige, eine tote (die Runde wurde seither neu angelegt)
            db.LeagueClubGames.AddRange(
                new LeagueClubGame { ClubId = 1, Year = 2026, White = "Hess, Max", WhiteFide = "24656666", Black = "Schwaz",
                    BlackRealFide = "1616951", Result = "1/2-1/2", Pgn = "1. e4 e5 1/2-1/2", MovesHash = "valid", LeagueGameId = board1 },
                new LeagueClubGame { ClubId = 1, Year = 2026, White = "Schwaz", WhiteRealFide = "1270012", Black = "Kruckenhauser, Arthur",
                    BlackFide = "1642812", Result = "1/2-1/2", Pgn = "1. d4 d5 1/2-1/2", MovesHash = "dead", LeagueGameId = board2 + 10_000 });
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.Schema.NewContext())
        {
            var r = await LeagueGameLinks.HealAsync(db, default, NullLogger.Instance);
            Assert.Equal((1, 1, 0), (r.Keyed, r.Repaired, r.Cleared));
        }

        // „Daten aktualisieren": dieselben Paarungen, die Ids bleiben, die Zuordnungen auch
        await using (var db = fixture.Schema.NewContext())
            await LeagueRefresh.ReplaceAsync(db, Pages(), Now, default);
        await using (var db = fixture.Schema.NewContext())
        {
            Assert.Equal(new[] { board1, board2 }, await db.LeagueGames.OrderBy(g => g.Board).Select(g => g.Id).ToListAsync());
            var rows = await db.LeagueClubGames.AsNoTracking().ToDictionaryAsync(g => g.MovesHash);
            Assert.Equal((board1, Tnr, 2, 2, 1), (rows["valid"].LeagueGameId!.Value, rows["valid"].LeagueTnr!.Value,
                rows["valid"].LeagueRound!.Value, rows["valid"].LeagueMatchNo!.Value, rows["valid"].LeagueBoard!.Value));
            Assert.Equal(board2, rows["dead"].LeagueGameId);

            var list = await new LeagueFixtureGames(db).ForFixtureAsync(Schwaz, Tnr, 2, "Schwaz", default);
            Assert.Equal(new[] { rows["valid"].Id, rows["dead"].Id }, list.OrderBy(p => p.Board).Select(p => p.ClubGameId!.Value));
        }

        // Sicherheitsnetz: tote Id MIT Schlüssel löst ResolveAsync auf, RelinkAsync repariert sie
        await using (var db = fixture.Schema.NewContext())
        {
            await db.LeagueClubGames.Where(g => g.MovesHash == "dead").ExecuteUpdateAsync(s => s.SetProperty(g => g.LeagueGameId, 77_777));
            var list = await new LeagueFixtureGames(db).ForFixtureAsync(Schwaz, Tnr, 2, "Schwaz", default);
            Assert.Contains(list, p => p.Board == 2 && p.Source == "club");
            Assert.Equal(1, await LeagueGameLinks.RelinkAsync(db, Tnr, default));
            Assert.Equal(board2, (await db.LeagueClubGames.AsNoTracking().SingleAsync(g => g.MovesHash == "dead")).LeagueGameId);
        }
    }
}

public sealed class LeagueGameLinksSqlFixture() : MariaDbClassFixture("lglinks", withApp: false);
