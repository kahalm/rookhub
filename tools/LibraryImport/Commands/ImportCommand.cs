using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services.Library;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary><c>import</c> — eine PGN-Sammlung in den Rohbestand einlesen.</summary>
internal static class ImportCommand
{
    // ===== 1. Einlesen ==========================================================

    public static async Task<int> RunAsync(ToolContext ctx)
    {
        var path = ctx.Options.Positional(1);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Console.Error.WriteLine("Aufruf: import <pfad-zur-pgn> [--limit n]");
            return 1;
        }
        var limit = ctx.Options.Int("--limit") ?? int.MaxValue;
        var sourceFile = Path.GetFileName(path);
        const int batchSize = 500;

        // Wiederholbar: was aus DIESER Datei schon drinsteht, kommt nicht ein zweites Mal hinein.
        // Der Griff ist der Zug-Hash zusammen mit dem Kommentator — dieselbe Partie von zwei Leuten
        // kommentiert sind zwei Zeilen, dieselbe Partie zweimal eingelesen ist eine.
        //
        // ACHTUNG: der Griff ist der GESPEICHERTE Hash, und bestehende Zeilen fasst der Import nie an.
        // Rechnet LibraryGameReader eine Partie heute anders als beim ersten Einlesen (z. B. das nackte
        // „1/2" am Ende, bis 0.624.0 als Halbzug gezaehlt — Codereview 2026-09-29, N11-004), passt der
        // neue Hash nicht zum alten: die Partie kaeme ein ZWEITES Mal hinein, und dedupe ordnete die alte
        // Zeile nie zu. Eine Datei deshalb NIE zur Reparatur neu einlesen — erst `rehash-results`, dann
        // `dedupe`; danach ist ein erneutes Einlesen wieder harmlos.
        HashSet<string> known;
        await using (var db = ctx.Db.New())
        {
            known = (await db.LibraryGames
                    .Where(g => g.SourceFile == sourceFile)
                    .Select(g => g.MovesHash + "" + (g.Annotator ?? ""))
                    .ToListAsync())
                .ToHashSet(StringComparer.Ordinal);
        }
        if (known.Count > 0)
            Console.WriteLine($"{known.Count:N0} Partien aus {sourceFile} liegen schon im Bestand — die werden uebersprungen.");

        var started = DateTime.UtcNow;
        int read = 0, written = 0, skippedEmpty = 0, skippedKnown = 0;
        var batch = new List<LibraryGame>(batchSize);
        var db2 = ctx.Db.New();

        try
        {
            foreach (var game in PgnFileReader.Read(path))
            {
                if (read >= limit) break;
                read++;

                var row = LibraryGameReader.From(game.Pgn, game.Headers, game.MoveText, sourceFile);
                if (row is null) { skippedEmpty++; continue; }

                var key = row.MovesHash + "" + (row.Annotator ?? "");
                if (!known.Add(key)) { skippedKnown++; continue; }

                batch.Add(row);
                if (batch.Count >= batchSize)
                {
                    written += await FlushAsync(batch);
                    batch.Clear();
                    await db2.DisposeAsync();
                    db2 = ctx.Db.New();
                    if (written % 10000 == 0)
                        Console.WriteLine($"  {written:N0} geschrieben ({Throughput.Rate(started, written)})");
                }
            }
            if (batch.Count > 0) written += await FlushAsync(batch);
        }
        finally { await db2.DisposeAsync(); }

        Console.WriteLine($"Gelesen {read:N0} · geschrieben {written:N0} · ohne Zuege {skippedEmpty:N0} · schon bekannt {skippedKnown:N0}");
        Console.WriteLine($"Dauer {DateTime.UtcNow - started:hh\\:mm\\:ss}");
        return 0;

        async Task<int> FlushAsync(List<LibraryGame> rows)
        {
            db2.LibraryGames.AddRange(rows);
            await db2.SaveChangesAsync();
            return rows.Count;
        }
    }
}
