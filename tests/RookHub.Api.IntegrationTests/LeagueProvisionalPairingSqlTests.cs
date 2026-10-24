using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.DTOs.League;
using RookHub.Api.Models;
using RookHub.Api.Services.League;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Vorläufige Zuordnung zur laufenden Runde gegen ECHTES MariaDB (0.832.0): die Migration <c>LeaguePairingProvisional</c>,
/// die Abfragen von <see cref="LeagueProvisionalPairing"/> und <see cref="LeaguePairingFinder"/> (InMemory prüft die
/// SQL-Übersetzung nicht) und der ganze Weg: Formular übernehmen bei leeren Brettern → vorläufig, „Daten aktualisieren“ mit
/// Aufstellung → bestätigt.
/// </summary>
public class LeagueProvisionalPairingSqlTests(LeagueProvisionalPairingSqlFixture fixture) : IAsyncLifetime, IClassFixture<LeagueProvisionalPairingSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const int Tnr = 1479342;
    private static readonly DateTime Now = new(2026, 10, 24, 18, 0, 0, DateTimeKind.Utc);
    private static readonly LeagueClub Schwaz = new() { Id = 1, Name = "SK Schwaz", TeamPrefix = "Schwaz", AnonName = "Schwaz" };

    /// <summary>Runde 3 Schwaz – Sparkasse Jenbach 2 (zwei Bretter); <paramref name="lineup"/> = chess-results hat die Aufstellung.</summary>
    private static LeagueRefresh.Pages Pages(bool lineup) => new(Tnr,
        new() { new(3, 3, "Schwaz", "Sparkasse Jenbach 2", null, null, "24.10.2026", null, null) },
        new()
        {
            new(3, 3, 1, "Schwaz", "Sparkasse Jenbach 2", lineup ? "Ober, David" : "", lineup ? "Rupprechter, Alfred" : "", null, null, "w", "", null, null, 0, null),
            new(3, 3, 2, "Schwaz", "Sparkasse Jenbach 2", lineup ? "Katkic, Samuel" : "", lineup ? "Fercher, Alois" : "", null, null, "s", "", null, null, 0, null),
        },
        new() { [3] = "24.10.2026" },
        new()
        {
            new(1, null, "Ober, David", "530014190", 1500, null, "AUT", "Schwaz", 12),
            new(2, null, "Katkic, Samuel", "530005108", 1400, null, "AUT", "Schwaz", 11),
            new(3, null, "Fercher, Alois", "1621947", 1600, null, "AUT", "Sparkasse Jenbach 2", 10),
            new(4, null, "Rupprechter, Alfred", "1682679", 1550, null, "AUT", "Sparkasse Jenbach 2", 11),
        },
        new());

    [MySqlFact]
    public async Task Formular_beiLeerenBrettern_vorlaeufig_nachDemAktualisieren_bestaetigt()
    {
        await using (var db = fixture.Schema.NewContext())
        {
            if (!await db.LeagueClubs.AnyAsync(c => c.Id == 1))
                db.LeagueClubs.Add(new LeagueClub { Id = 1, Name = "SK Schwaz", TeamPrefix = "Schwaz", AnonName = "Schwaz" });
            db.LeagueTournaments.Add(new LeagueTournament { Tnr = Tnr, Name = "GK", Season = "2026/27", Level = 6, League = "Gebietsklasse", Stage = "Liga" });
            await db.SaveChangesAsync();
            await LeagueRefresh.ReplaceAsync(db, Pages(lineup: false), Now, default);
        }
        int id, board2;
        await using (var db = fixture.Schema.NewContext())
        {
            board2 = (await db.LeagueGames.SingleAsync(g => g.Board == 2)).Id;
            var club = new LeagueClubService(db, NullLogger<LeagueClubService>.Instance, () => Now);
            var (game, reason, msg) = await club.AddGameAsync(Schwaz, null, new LeagueClubGameRequest
            {
                Moves = new() { "e4", "e5", "Nf3", "Nc6" }, White = "Fercher, Alois", WhiteFide = "1621947", Black = "Katkic, Samuel",
                BlackFide = "530005108", BlackReplace = true, Result = "1-0", Year = 2026, Date = "2026.10.24",
            });
            Assert.True(reason == null, $"{reason}: {msg}");
            id = game!.Id;
        }
        await using (var db = fixture.Schema.NewContext())
        {
            var row = await db.LeagueClubGames.AsNoTracking().SingleAsync(g => g.Id == id);
            Assert.Equal((board2, true), (row.LeagueGameId!.Value, row.LeaguePairingProvisional));   // Gast Weiß → Brett 2
            Assert.Equal(1, await db.LeagueClubGames.CountAsync(g => g.LeaguePairingProvisional));
        }

        // „Daten aktualisieren“: chess-results hat jetzt die Aufstellung — Katkic–Fercher an Brett 2
        await using (var db = fixture.Schema.NewContext())
            await LeagueRefresh.ReplaceAsync(db, Pages(lineup: true), Now, default);
        await using (var db = fixture.Schema.NewContext())
        {
            var row = await db.LeagueClubGames.AsNoTracking().SingleAsync(g => g.Id == id);
            Assert.Equal((board2, false), (row.LeagueGameId!.Value, row.LeaguePairingProvisional));
        }
    }
}

public sealed class LeagueProvisionalPairingSqlFixture() : MariaDbClassFixture("lgprov", withApp: false);
