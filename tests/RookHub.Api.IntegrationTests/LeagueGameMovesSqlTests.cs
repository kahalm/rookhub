using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services.League;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Erste Züge je Ligapartie gegen ECHTES MariaDB (2026-10-08): die Migration <c>LeagueGameMoves</c> (Tabelle, eindeutiger
/// natürlicher Schlüssel), die Übersetzung der Abfragen von <see cref="LeagueGameMoves"/> und dass ein zweiter Eintrag für
/// dieselbe Paarung an der Datenbank scheitert. Namen erfunden.
/// </summary>
public class LeagueGameMovesSqlTests(LeagueGameMovesSqlFixture fixture) : IAsyncLifetime, IClassFixture<LeagueGameMovesSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const int Tnr = 4711;
    private static readonly LeagueClub Club = new() { Id = 1, Name = "SK Testdorf", TeamPrefix = "Testdorf", AnonName = "Testdorf" };

    [MySqlFact]
    public async Task Migration_SaveAndRead_UniqueNaturalKey()
    {
        await using (var db = fixture.Schema.NewContext())
        {
            if (!await db.LeagueClubs.AnyAsync(c => c.Id == 1)) db.LeagueClubs.Add(new LeagueClub { Id = 1, Name = "SK Testdorf", TeamPrefix = "Testdorf", AnonName = "Testdorf" });
            db.LeagueRounds.Add(new LeagueRound { Tnr = Tnr, Round = 1, Date = new DateOnly(2026, 10, 11) });
            db.LeagueMatches.Add(new LeagueMatch { Tnr = Tnr, Round = 1, MatchNo = 1, Home = "Testdorf", Away = "Bergheim" });
            db.LeagueGames.Add(new LeagueGame { Tnr = Tnr, Round = 1, MatchNo = 1, Board = 1, HomeTeam = "Testdorf", AwayTeam = "Bergheim",
                HomePlayer = "Ackermann, Anna", AwayPlayer = "Brunner, Bert", HomeColor = "w", Result = "1 - 0" });
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.Schema.NewContext())
        {
            var r = await new LeagueGameMoves(db).SaveAsync(Club, 7, false, Tnr, 1, 1, 1, "1.e4 c5 2.Sf3 d6");
            Assert.Equal((null, "e4 c5 Nf3 d6"), (r.Reason, r.Moves));
            // derselbe Eintragende überschreibt — keine zweite Zeile
            r = await new LeagueGameMoves(db).SaveAsync(Club, 7, false, Tnr, 1, 1, 1, "e4 e5");
            Assert.Null(r.Reason);
        }
        await using (var db = fixture.Schema.NewContext())
        {
            var l = await new LeagueGameMoves(db).LineupsAsync(Club, Tnr, 1, 7, true, false);
            Assert.Equal("e4 e5", l!.Matches.Single().Boards.Single().Moves);
            Assert.True(l.Matches[0].Boards[0].CanEditMoves);
            Assert.Equal(1, await db.LeagueGameMoves.CountAsync());
            db.LeagueGameMoves.Add(new LeagueGameMove { Tnr = Tnr, Round = 1, MatchNo = 1, Board = 1, Moves = "d4", ClubId = 1, UpdatedAt = DateTime.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }
}

public sealed class LeagueGameMovesSqlFixture() : MariaDbClassFixture("lgmoves", withApp: false);
