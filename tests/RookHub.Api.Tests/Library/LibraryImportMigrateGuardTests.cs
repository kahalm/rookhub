using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Api.Tests.Library;

/// <summary>R3-013: <c>LibraryImport migrate</c> verweigert echte Datenbanken und Ziele mit Nutzern.</summary>
public class LibraryImportMigrateGuardTests
{
    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase("migrate-guard-" + Guid.NewGuid().ToString("N")).Options);

    [Theory]
    [InlineData("Server=db;Port=3306;Database=rookhub;User=x;Password=y", "rookhub")]
    [InlineData("server=db; database = piratechess ;user=x", "piratechess")]
    [InlineData("Server=db;Initial Catalog=scratch_1", "scratch_1")]
    [InlineData("Server=db;User=x", null)]
    public void DatabaseName_FromConnectionString(string cs, string? expected) => Assert.Equal(expected, ScratchDbGuard.DatabaseName(cs));

    [Theory]
    [InlineData("rookhub")]
    [InlineData("ROOKHUB")]
    [InlineData("chessresults")]
    public async Task RealSchemaName_IsRefused_UnlessOverridden(string name)
    {
        await using var db = Db();
        var cs = $"Server=db;Database={name};User=x";
        Assert.NotNull(await ScratchDbGuard.RefusalAsync(db, cs, overridden: false));
        Assert.Null(await ScratchDbGuard.RefusalAsync(db, cs, overridden: true));
    }

    [Fact]
    public async Task TargetWithUsers_IsRefused_EmptyScratchIsAllowed()
    {
        await using var db = Db();
        const string cs = "Server=db;Database=rh_scratch;User=x";
        Assert.Null(await ScratchDbGuard.RefusalAsync(db, cs, overridden: false));

        db.AppUsers.Add(new AppUser { Username = "patrik", Email = "p@example.org", PasswordHash = "x" });
        await db.SaveChangesAsync();
        var refusal = await ScratchDbGuard.RefusalAsync(db, cs, overridden: false);
        Assert.NotNull(refusal);
        Assert.Contains(ScratchDbGuard.OverrideFlag, refusal);
        Assert.Null(await ScratchDbGuard.RefusalAsync(db, cs, overridden: true));
    }
}
