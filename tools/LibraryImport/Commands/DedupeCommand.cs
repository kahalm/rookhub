using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary><c>dedupe</c> — gleiche Zugfolgen zusammenfassen (DuplicateOfId, Status).</summary>
internal static class DedupeCommand
{
    // ===== 2. Dubletten =========================================================

    public static async Task<int> RunAsync(ToolContext ctx)
    {
        // Dieselbe Zugfolge kommt in einer Zeitschriften-Sammlung mehrfach vor, jedes Mal von
        // jemand anderem kommentiert. Behalten wird die Fassung mit den MEISTEN kommentierten
        // Halbzuegen — das ist die, die fuer die Punktepartie etwas hergibt. Die anderen bleiben
        // stehen und zeigen auf sie; geloescht wird nichts, die zweite Meinung kann die bessere sein.
        await using var db = ctx.Db.New();
        var groups = await db.LibraryGames
            .Where(g => g.MovesHash != null && g.Status != LibraryGameStatus.Imported)
            .GroupBy(g => g.MovesHash)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToListAsync();

        Console.WriteLine($"{groups.Count:N0} Zugfolgen kommen mehrfach vor.");
        var marked = 0;
        foreach (var chunk in groups.Chunk(200))
        {
            await using var scope = ctx.Db.New();
            var rows = await scope.LibraryGames
                .Where(g => chunk.Contains(g.MovesHash) && g.Status != LibraryGameStatus.Imported)
                .ToListAsync();

            foreach (var group in rows.GroupBy(r => r.MovesHash))
            {
                var keeper = group
                    .OrderByDescending(r => r.CommentedPlies ?? 0)
                    .ThenByDescending(r => r.CommentChars ?? 0)
                    .ThenBy(r => r.Id)
                    .First();
                foreach (var other in group.Where(r => r.Id != keeper.Id))
                {
                    if (other.Status == LibraryGameStatus.Duplicate && other.DuplicateOfId == keeper.Id) continue;
                    other.Status = LibraryGameStatus.Duplicate;
                    other.DuplicateOfId = keeper.Id;
                    other.UpdatedAt = DateTime.UtcNow;
                    marked++;
                }
                // Der Behalter koennte aus einem frueheren Lauf als Dublette markiert sein.
                if (keeper.Status == LibraryGameStatus.Duplicate)
                {
                    keeper.Status = LibraryGameStatus.New;
                    keeper.DuplicateOfId = null;
                    keeper.UpdatedAt = DateTime.UtcNow;
                }
            }
            scope.ChangeTracker.DetectChanges();
            await scope.SaveChangesAsync();
        }
        Console.WriteLine($"{marked:N0} Zeilen als Dublette markiert.");
        return 0;
    }
}
