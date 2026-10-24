using Microsoft.EntityFrameworkCore;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary><c>migrate</c> — Schema per EF-Migrationen anlegen (nur fuer eine WEGWERF-MariaDB, z. B. zum Trainieren).</summary>
internal static class MigrateCommand
{
    public static async Task<int> RunAsync(ToolContext ctx)
    {
        await using var db = ctx.Db.New();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        Console.WriteLine($"{pending.Count} Migrationen offen …");
        await db.Database.MigrateAsync();
        Console.WriteLine("Schema aktuell.");
        return 0;
    }
}
