using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Die Import-Zustaende sind im Code Enums, in der Datenbank aber weiterhin Zeichenketten —
/// deshalb kam die Umstellung ohne Migration und ohne Datenaenderung aus. Diese Tests halten
/// genau das fest: was in der Spalte steht, MUSS die alte Zeichenkette sein. Sonst waeren
/// bestehende Zeilen nach einem Deploy nicht mehr lesbar, und das Frontend (das
/// `imp.status === 'completed'` woertlich vergleicht) faende nichts mehr.
/// </summary>
public class ImportStateWireFormatTests
{
    [MySqlFact]
    public async Task JederZustandLandetAlsDieAlteZeichenketteInDerSpalte()
    {
        await using var schema = await MariaDbSchema.CreateAsync("wire");
        await using var db = schema.NewContext();
        await db.Database.MigrateAsync();

        var user = new AppUser { Username = "w", Email = "w@t.local", PasswordHash = "x" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        var erwartet = new List<(int Id, string Status, string Phase)>();
        var stati = new[] { ChessableImportStatus.Running, ChessableImportStatus.Paused,
                            ChessableImportStatus.Completed, ChessableImportStatus.Failed,
                            ChessableImportStatus.Cancelled };
        var phasen = new[] { ChessableImportPhase.Queued, ChessableImportPhase.Claimed,
                             ChessableImportPhase.Fetching, ChessableImportPhase.Importing,
                             ChessableImportPhase.Done, ChessableImportPhase.BearerBlocked,
                             ChessableImportPhase.RateLimited };

        for (var i = 0; i < phasen.Length; i++)
        {
            var imp = new ChessableImport
            {
                UserId = user.Id, Bid = $"b{i}", CourseName = "C", Target = "book",
                Status = stati[i % stati.Length], Phase = phasen[i], CreatedAt = DateTime.UtcNow,
            };
            db.ChessableImports.Add(imp);
            await db.SaveChangesAsync();
            erwartet.Add((imp.Id, stati[i % stati.Length].ToWire(), phasen[i].ToWire()));
        }

        // ROH aus der Spalte lesen, am EF-Konverter vorbei.
        await using var conn = new MySqlConnector.MySqlConnection(schema.ConnectionString);
        await conn.OpenAsync();
        foreach (var (id, status, phase) in erwartet)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT Status, Phase FROM ChessableImports WHERE Id = {id}";
            await using var r = await cmd.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.Equal(status, r.GetString(0));
            Assert.Equal(phase, r.GetString(1));
        }
    }

    [MySqlFact]
    public async Task EinUnbekannterAltwertWirdNichtStillAlsRunningGelesen()
    {
        // Ein Fremdwert in der Spalte soll LAUT scheitern statt stumm auf einen gueltigen
        // Zustand gemappt zu werden — ein so verschluckter Job liefe als Geisterjob weiter mit.
        await using var schema = await MariaDbSchema.CreateAsync("bad");
        await using var db = schema.NewContext();
        await db.Database.MigrateAsync();
        var user = new AppUser { Username = "b", Email = "b@t.local", PasswordHash = "x" };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();   // erst speichern, sonst ist user.Id noch 0 (Fremdschluessel)
        db.ChessableImports.Add(new ChessableImport
        {
            UserId = user.Id, Bid = "b", CourseName = "C", Target = "book",
            Status = ChessableImportStatus.Running, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await using (var conn = new MySqlConnector.MySqlConnection(schema.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE ChessableImports SET Status = 'runnning'";   // Tippfehler
            await cmd.ExecuteNonQueryAsync();
        }

        db.ChangeTracker.Clear();
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => db.ChessableImports.ToListAsync());
        Assert.Contains("runnning", ex.ToString());
    }
}
