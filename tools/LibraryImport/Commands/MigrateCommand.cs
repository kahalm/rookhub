using Microsoft.EntityFrameworkCore;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary><c>migrate</c> — Schema per EF-Migrationen anlegen (nur fuer eine WEGWERF-MariaDB, z. B. zum Trainieren).
/// Verweigert (Rückgabe 2) echte Datenbanken und Ziele mit Nutzern, außer mit <c>--i-know-this-is-a-scratch-db</c>
/// (<see cref="ScratchDbGuard"/>, R3-013).</summary>
internal static class MigrateCommand
{
    public static async Task<int> RunAsync(ToolContext ctx)
    {
        await using var db = ctx.Db.New();
        var refusal = await ScratchDbGuard.RefusalAsync(db, ctx.Db.ConnectionString, ctx.Options.Has(ScratchDbGuard.OverrideFlag));
        if (refusal != null)
        {
            Console.Error.WriteLine(refusal);
            return 2;
        }
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        Console.WriteLine($"{pending.Count} Migrationen offen …");
        await db.Database.MigrateAsync();
        Console.WriteLine("Schema aktuell.");
        return 0;
    }
}
