using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services.Library;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary><c>rehash-results</c> — Altbestand mit nacktem „1/2" am Ende nachrechnen.</summary>
internal static class RehashResultsCommand
{
    // ===== 2a. Altbestand mit nacktem „1/2" nachrechnen ===========================
    //
    //   rehash-results [--dry-run]      danach: dedupe
    //
    // Bis 0.624.0 zaehlte LibraryGameReader ein nacktes „1/2" am Partieende als Halbzug (Codereview
    // 2026-09-29, N11-004): PlyCount eins zu hoch, „1/2" im MovesHash — dieselbe Partie mit „1/2-1/2"
    // aus einer anderen Quelle blieb als Dublette unerkannt. Der Import repariert das NICHT (siehe
    // ImportAsync), deshalb hier an Ort und Stelle: PlyCount, MovesHash, OpeningLine, CommentedPlies und
    // FirstCommentedPly aus dem gespeicherten PGN neu (LibraryGameReader.RehashBareResult). Angefasst wird
    // nur, was das „1/2" erklaert; andere Abweichungen und Partien, die ohne „1/2" keinen Zug mehr haetten,
    // werden nur gezaehlt und mit Id ausgegeben. Wiederholbar: eine nachgerechnete Zeile ist beim naechsten
    // Lauf „unveraendert".
    //
    // Vorher nachsehen, ob es ueberhaupt etwas zu tun gibt (erst Dev, dann Prod; teurer Volltext):
    //   SELECT COUNT(*) FROM LibraryGames WHERE Pgn REGEXP '[[:space:]]1/2[[:space:]]*$';
    // Ist das 0, braucht es den Lauf nicht. Auf Prod nur auf ausdrueckliche Anweisung, und zuerst --dry-run.
    public static async Task<int> RunAsync(ToolContext ctx)
    {
        var dryRun = ctx.Options.Has("--dry-run");
        var counts = new SortedDictionary<LibraryGameReader.BareResultRehash, int>();
        var started = DateTime.UtcNow;
        int examined = 0, lastId = 0;
        while (true)
        {
            await using var db = ctx.Db.New();
            // Grober Vorfilter in SQL (jede Partie mit „1/2" im Text, also auch alle „1/2-1/2"); ob das
            // „1/2" als eigenes Wort dasteht und die Spalten verfaelscht hat, entscheidet das Nachrechnen.
            // Ohne Verfolgung geholt: geschrieben wird nur die Handvoll geaenderter Zeilen, einzeln.
            var rows = await db.LibraryGames.AsNoTracking()
                .Where(g => g.Id > lastId && g.Pgn.Contains("1/2"))
                .OrderBy(g => g.Id)
                .Take(1000)
                .Select(g => new LibraryGame
                {
                    Id = g.Id, Pgn = g.Pgn, StartFen = g.StartFen, PlyCount = g.PlyCount, MovesHash = g.MovesHash,
                    OpeningLine = g.OpeningLine, CommentedPlies = g.CommentedPlies, FirstCommentedPly = g.FirstCommentedPly,
                })
                .ToListAsync();
            if (rows.Count == 0) break;
            lastId = rows[^1].Id;

            foreach (var row in rows)
            {
                var before = row.PlyCount;
                var outcome = LibraryGameReader.RehashBareResult(row);
                counts[outcome] = counts.GetValueOrDefault(outcome) + 1;
                examined++;
                if (outcome == LibraryGameReader.BareResultRehash.Unchanged) continue;
                Console.WriteLine($"  #{row.Id,-7} {outcome,-11} Halbzuege {before} -> {row.PlyCount}");
                if (outcome != LibraryGameReader.BareResultRehash.Rehashed || dryRun) continue;
                await db.LibraryGames.Where(g => g.Id == row.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(g => g.PlyCount, row.PlyCount)
                        .SetProperty(g => g.MovesHash, row.MovesHash)
                        .SetProperty(g => g.OpeningLine, row.OpeningLine)
                        .SetProperty(g => g.CommentedPlies, row.CommentedPlies)
                        .SetProperty(g => g.FirstCommentedPly, row.FirstCommentedPly)
                        .SetProperty(g => g.UpdatedAt, row.UpdatedAt));
            }
            if (examined % 20000 < rows.Count) Console.WriteLine($"  … {examined:N0} Partien mit 1/2 im Text geprueft");
        }
        var rehashed = counts.GetValueOrDefault(LibraryGameReader.BareResultRehash.Rehashed);
        Console.WriteLine((dryRun ? "PROBELAUF, nichts geschrieben. " : "")
            + $"Geprueft {examined:N0}: " + string.Join(" · ", counts.Select(k => $"{k.Key} {k.Value:N0}"))
            + $" · Dauer {DateTime.UtcNow - started:hh\\:mm\\:ss}");
        if (rehashed > 0 && !dryRun)
            Console.WriteLine("Jetzt `dedupe` laufen lassen — erst dann finden die nachgerechneten Partien ihre Dubletten.");
        return 0;
    }
}
