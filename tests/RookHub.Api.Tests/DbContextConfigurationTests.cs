using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Data.Configurations;
using RookHub.Api.Models;

namespace RookHub.Api.Tests;

/// <summary>
/// A9-005: die ganze Fluent-Konfiguration stand in EINER Methode (AppDbContext.OnModelCreating, rund 1 700 Zeilen für
/// 138 Entitäten), Book war an zwei Stellen konfiguriert, und jede neue Tabelle hängte ihre Zeilen irgendwo an. Seit der
/// Zerlegung hat jede Entität ihre <see cref="IEntityTypeConfiguration{TEntity}"/> in <c>Data/Configurations/</c>, in der
/// Datei ihrer Domäne. Dass das Modell dabei gleich geblieben ist, belegt
/// MigrationsTests.ModellUndMigrationen_LaufenNichtAuseinander (MariaDB); die Wächter hier halten die Form fest.
/// </summary>
public class DbContextConfigurationTests
{
    private static readonly Type[] ConfigTypes = typeof(AppDbContext).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && ConfiguredEntity(t) is not null)
        .ToArray();

    private static Type? ConfiguredEntity(Type t) => t.GetInterfaces()
        .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>))
        ?.GenericTypeArguments[0];

    private static AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public void Konfigurationen_LiegenInDataConfigurations_UndGreifen()
    {
        Assert.NotEmpty(ConfigTypes);
        Assert.All(ConfigTypes, t => Assert.Equal(typeof(AppUserConfiguration).Namespace, t.Namespace));

        using var db = NewDb();
        // Stichproben aus drei Domänen: ohne ApplyConfigurationsFromAssembly fehlten sie.
        Assert.Equal(1000, db.Model.FindEntityType(typeof(Book))!.FindProperty(nameof(Book.KidsTitles))!.GetMaxLength());
        Assert.Contains(db.Model.FindEntityType(typeof(AppUser))!.GetIndexes(),
            i => i.IsUnique && i.Properties.Single().Name == nameof(AppUser.Username));
        Assert.Equal([nameof(LeagueTournament.Tnr)],
            db.Model.FindEntityType(typeof(LeagueTournament))!.FindPrimaryKey()!.Properties.Select(p => p.Name));
    }

    [Fact]
    public void JedeEntitaet_WirdAnGenauEinerStelleKonfiguriert()
    {
        var doppelt = ConfigTypes.GroupBy(t => ConfiguredEntity(t)!).Where(g => g.Count() > 1)
            .Select(g => g.Key.Name).ToList();
        Assert.True(doppelt.Count == 0, "Mehrfach konfiguriert: " + string.Join(", ", doppelt));
    }

    /// <summary>Neue Konfiguration gehört nach Data/Configurations/, nicht zurück in den Kontext.</summary>
    [Fact]
    public void AppDbContext_KonfiguriertSelbstKeineEntitaet()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var dataDir = Path.Combine(dir!.FullName, "src", "api", "RookHub.Api", "Data");
        foreach (var file in Directory.GetFiles(dataDir, "AppDbContext*.cs"))
            Assert.DoesNotContain(".Entity<", File.ReadAllText(file));
    }
}
