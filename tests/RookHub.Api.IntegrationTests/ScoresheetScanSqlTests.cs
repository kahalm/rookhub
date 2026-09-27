using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Partie löschen gegen ECHTES MariaDB (0.568.1): die Einlesung bleibt ohne Foto fürs Tageskontingent stehen
/// (<see cref="ScoresheetScanService.DetachWithoutLoading"/>). Das hängt an der Reihenfolge der Befehle in EINEM
/// SaveChanges — das UPDATE der Einlesung muss VOR dem DELETE der Partie laufen, sonst nimmt der Fremdschlüssel
/// (ON DELETE CASCADE) die Zeile mit, und das UPDATE träfe nichts mehr (Concurrency-Fehler, Partie nicht gelöscht).
/// InMemory kennt weder Cascade in der Datenbank noch eine Befehlsreihenfolge.
/// </summary>
public class ScoresheetScanSqlTests(ScoresheetScanSqlFixture fixture)
    : IAsyncLifetime, IClassFixture<ScoresheetScanSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [MySqlFact]
    public async Task DeleteGame_KeepsTheScanWithoutPhoto_InsteadOfLettingTheCascadeTakeIt()
    {
        int userId, gameId, scanId;
        await using (var db = fixture.Schema.NewContext())
        {
            var u = new AppUser { Username = "scan-del", Email = "scan-del@t.local", PasswordHash = "x" };
            db.AppUsers.Add(u);
            await db.SaveChangesAsync();
            var g = new SavedGame { UserId = u.Id, Source = SavedGameService.ScoresheetSource, Pgn = "1. e4 *", ShareToken = "scan-del-1" };
            db.SavedGames.Add(g);
            await db.SaveChangesAsync();
            var s = new ScoresheetScan
            {
                UserId = u.Id, SavedGameId = g.Id, Photo = new byte[] { 1, 2, 3 }, ContentType = "image/jpeg",
                FileName = "a.jpg", NotationLanguage = "de", Status = ScoresheetScanStatus.Done,
                TranscriptionJson = "{}", ResolutionJson = "{}", CostMicroUsd = 80_000,
            };
            db.ScoresheetScans.Add(s);
            await db.SaveChangesAsync();
            (userId, gameId, scanId) = (u.Id, g.Id, s.Id);
        }

        // Derselbe Weg wie SavedGameService.DeleteAsync: Partie geladen, Einlesung nur über ihre Schlüssel.
        await using (var db = fixture.Schema.NewContext())
        {
            var g = await db.SavedGames.SingleAsync(x => x.Id == gameId);
            ScoresheetScanService.DetachWithoutLoading(db,
                await ScoresheetScanService.KeysAsync(db.ScoresheetScans.Where(x => x.SavedGameId == gameId)));
            db.SavedGames.Remove(g);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.Schema.NewContext())
        {
            Assert.False(await db.SavedGames.AnyAsync(x => x.Id == gameId));
            var row = await db.ScoresheetScans.SingleAsync(x => x.Id == scanId);
            Assert.Null(row.SavedGameId);
            Assert.Empty(row.Photo);
            Assert.Null(row.FileName);
            Assert.Null(row.TranscriptionJson);
            Assert.Null(row.ResolutionJson);
            Assert.Equal(userId, row.UserId);
            Assert.Equal(80_000, row.CostMicroUsd);
            Assert.Equal(ScoresheetScanStatus.Done, row.Status);
        }
    }
}

public sealed class ScoresheetScanSqlFixture() : MariaDbClassFixture("scan", withApp: false);
