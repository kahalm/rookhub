using Microsoft.EntityFrameworkCore;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary><c>stats</c> — zeigen, was im Rohbestand steht.</summary>
internal static class StatsCommand
{
    // ===== Uebersicht ===========================================================

    public static async Task<int> RunAsync(ToolContext ctx)
    {
        await using var db = ctx.Db.New();
        var total = await db.LibraryGames.CountAsync();
        Console.WriteLine($"Partien im Bestand: {total:N0}");
        if (total == 0) return 0;

        foreach (var g in await db.LibraryGames.GroupBy(x => x.Status)
                     .Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync())
            Console.WriteLine($"  {g.Status,-12} {g.Count,8:N0}");

        Console.WriteLine($"mit Kommentaren : {await db.LibraryGames.CountAsync(g => g.CommentedPlies > 0):N0}");
        Console.WriteLine($"mit Eroeffnung  : {await db.LibraryGames.CountAsync(g => g.OpeningLine != null):N0}");
        Console.WriteLine($"mit 1. Kommentar: {await db.LibraryGames.CountAsync(g => g.FirstCommentedPly != null):N0}");
        Console.WriteLine($"mit Sprache     : {await db.LibraryGames.CountAsync(g => g.Languages != null):N0}");
        Console.WriteLine($"mit Note        : {await db.LibraryGames.CountAsync(g => g.Score != null):N0}");
        return 0;
    }
}
