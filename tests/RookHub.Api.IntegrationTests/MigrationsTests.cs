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
