using Microsoft.EntityFrameworkCore;
using RookHub.Api.Services.Library;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary><c>score</c> — Eignungsnote fuer die Punktepartie berechnen.</summary>
internal static class ScoreCommand
{
    // ===== 4. Eignungsnote ======================================================

    public static async Task<int> RunAsync(ToolContext ctx)
    {
        // Auch hier gruppiert geschrieben: die Note ist eine Zahl von 0 bis 100, je Seite gibt es also
        // hoechstens hundert verschiedene Werte — und damit hoechstens hundert UPDATEs statt fuenftausend.
        var done = 0;
        var lastId = 0;
        while (true)
        {
            await using var db = ctx.Db.New();
            var rows = await db.LibraryGames
                .Where(g => g.Id > lastId)
                .OrderBy(g => g.Id)
                .Take(5000)
                .Select(g => new
                {
                    g.Id, g.PlyCount, g.CommentedPlies, g.CommentChars, g.VariationCount,
                    g.Result, g.WhiteElo, g.BlackElo,
                })
                .ToListAsync();
            if (rows.Count == 0) break;
            lastId = rows[^1].Id;

            var byScore = new Dictionary<int, List<int>>();
            foreach (var r in rows)
            {
                var score = GuessSuitability.Score(r.PlyCount, r.CommentedPlies, r.CommentChars,
                    r.VariationCount, r.Result, r.WhiteElo, r.BlackElo);
                if (!byScore.TryGetValue(score, out var ids)) byScore[score] = ids = new List<int>();
                ids.Add(r.Id);
            }

            foreach (var (score, ids) in byScore)
                await db.LibraryGames.Where(g => ids.Contains(g.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(g => g.Score, score));

            done += rows.Count;
            if (done % 25000 == 0) Console.WriteLine($"  {done:N0} bewertet");
        }
        Console.WriteLine($"Note berechnet fuer {done:N0} Partien.");
        return 0;
    }
}
