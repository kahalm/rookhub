using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Der Deploy migriert automatisch. Eine Migration, die auf MariaDB nicht durchlaeuft, faellt
/// deshalb erst beim Ausrollen auf — und dann steht die API. Die Unit-Suite kann das nicht
/// sehen: sie laeuft gegen EF InMemory, wo es ueberhaupt keine Migrationen gibt.
/// </summary>
public class MigrationsTests
{
    [MySqlFact]
    public async Task AlleMigrationen_LaufenAufEinemLeerenSchemaDurch()
    {
        await using var schema = await MariaDbSchema.CreateAsync("mig");
        await using var db = schema.NewContext();

        var geplant = db.Database.GetMigrations().ToList();
        Assert.NotEmpty(geplant);

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(geplant.Count, (await db.Database.GetAppliedMigrationsAsync()).Count());
    }

    /// <summary>
    /// Eine Migration, die auf einem LEEREN Schema durchlaeuft, kann auf dem BESTAND scheitern.
    ///
    /// <para>Genau so passiert bei <c>AddDirectoryPublicId</c>: die Spalte kommt mit dem
    /// Vorgabewert „" hinzu, danach ein UNIQUE-Index darauf. Auf einem leeren Schema kein
    /// Problem, auf 4930 bestehenden Zeilen tausende Dubletten — die Migration braeche beim
    /// Start der API ab, und zwar bei JEDEM Start. Der Nachtrag zwischen Spalte und Index ist
    /// das, was dieser Test prueft.</para>
    ///
    /// <para>Der Weg dahin: bis zur VORHERGEHENDEN Migration migrieren, Zeilen einfuegen, dann
    /// den Rest laufen lassen. Ein Test mit leerem Schema kann das nicht sehen.</para>
    /// </summary>
    [MySqlFact]
    public async Task Migrationen_LaufenAuchUeberBestehendeZeilen()
    {
        await using var schema = await MariaDbSchema.CreateAsync("bestand");
        await using var db = schema.NewContext();

        var alle = db.Database.GetMigrations().ToList();
        var index = alle.FindIndex(m => m.EndsWith("AddDirectoryPublicId", StringComparison.Ordinal));
        Assert.True(index > 0, "Migration AddDirectoryPublicId nicht gefunden");

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(alle[index - 1]);

        // Zwei Zeilen mit chess-results-Nummer, wie sie auf Dev und Prod stehen.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO TournamentDirectoryEntries
                (ChessResultsId, Name, StartsOnWeekend, MissedSweeps, FirstSeenAt, LastSeenAt,
                 CreatedAt, UpdatedAt, Speed, GeoSource, Kind, IsLeague, AgeGroups, Gender)
            VALUES ('1457129', 'Open Braunau', 0, 0, NOW(), NOW(), NOW(), NOW(), 1, 2, 0, 0, 0, 0),
                   ('1405166', 'Frauenbundesliga', 0, 0, NOW(), NOW(), NOW(), NOW(), 1, 2, 0, 0, 0, 0)
            """);

        // Und ein Nutzer, der sich eines davon weggeklickt hat. Diese Entscheidung muss die
        // Umstellung ueberleben — ein Spaltentausch per DropColumn/AddColumn wuerde sie
        // stillschweigend wegwerfen.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO AppUsers (Username, PasswordHash, CreatedAt, IsAdmin)
            VALUES ('ausblender', 'x', NOW(), 0)
            """);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO TournamentDirectoryIgnores (UserId, ChessResultsId, CreatedAt)
            SELECT Id, '1405166', NOW() FROM AppUsers WHERE Username = 'ausblender'
            """);

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(["1405166"], await db.TournamentDirectoryIgnores
            .Select(i => i.PublicId).ToListAsync());
        // Und die Identitaet ist die chess-results-Nummer — bestehende Adressen, Abos und
        // Teilen-Links bleiben damit gueltig.
        var ids = await db.TournamentDirectoryEntries
            .OrderBy(e => e.PublicId)
            .Select(e => new { e.PublicId, e.ChessResultsId })
            .ToListAsync();
        Assert.Equal(["1405166", "1457129"], ids.Select(i => i.PublicId));
        Assert.All(ids, i => Assert.Equal(i.PublicId, i.ChessResultsId));
    }

    /// <summary>
    /// Faengt den Fall ab, dass jemand eine Entitaet aendert und die Migration vergisst: das
    /// Modell traegt dann Aenderungen, die in keiner Migration stehen, und Prod liefe mit einem
    /// Schema, das nicht zum Code passt.
    /// </summary>
    [MySqlFact]
    public async Task ModellUndMigrationen_LaufenNichtAuseinander()
    {
        await using var schema = await MariaDbSchema.CreateAsync("drift");
        await using var db = schema.NewContext();

        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot;
        Assert.NotNull(snapshot);

        // Der Snapshot ist ein Roh-Modell; ohne Finalisierung fehlen ihm die relationalen
        // Zusatzdaten und der Vergleich meldete Unterschiede, die es gar nicht gibt.
        var fertigerSnapshot = db.GetService<IModelRuntimeInitializer>()
            .Initialize((IModel)snapshot!.Model, designTime: true, validationLogger: null);

        var differ = db.GetService<IMigrationsModelDiffer>();
        var unterschiede = differ.GetDifferences(
            fertigerSnapshot.GetRelationalModel(),
            db.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        Assert.True(unterschiede.Count == 0,
            $"Modell und Migrations-Snapshot laufen auseinander ({unterschiede.Count} Unterschiede) — fehlt eine Migration?");
    }
}
